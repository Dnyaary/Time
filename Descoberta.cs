// Time — contador de tempo de uso real do PC
// Copyright (c) 2026 Daniel 'dnyaary' Carvalho — https://instagram.com/dnyaary
// SPDX-License-Identifier: MIT
// Distribuído sob a licença MIT. Veja o arquivo LICENSE na raiz do projeto.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Descoberta dos outros Time na mesma rede local, sem IP configurado.
//
// Cada Time com o painel ligado manda, a cada 10 s, um aviso UDP para o
// broadcast da rede (porta 8788) e escuta os avisos dos outros. O aviso leva só
// o nome da máquina e a porta — o código não vai para a rede, nem em forma de
// hash (com 8 caracteres, um hash dele seria quebrado por força bruta em pouco
// tempo). Assim o hub acha os PCs pelo nome, e o IP pode mudar à vontade.
//
// Para trocar o IP de um PC sem abrir a porta para um impostor, o hub manda um
// desafio (número aleatório) e o outro PC responde com um HMAC do código. Só
// quem tem o código consegue responder — e o código não trafega.
//
// O aviso é a única coisa que o Time manda para fora; nada é exibido na tela
// de nenhum PC. O que for descoberto aparece só no painel (localhost/celular).

static class Descoberta
{
    public const int PORTA = 8788;
    const int INTERVALO_MS = 10_000;
    const int VALIDADE_MS = 35_000;
    const int ESPERA_DESAFIO_MS = 1500;

    public sealed record Vizinho(string Nome, string Ip, int Porta, DateTime Visto);

    static readonly object trava = new();
    static readonly Dictionary<string, Vizinho> vistos = new(StringComparer.Ordinal);
    static readonly Dictionary<string, TaskCompletionSource<string>> desafios = new(StringComparer.Ordinal);

    static UdpClient? escuta;     // só existe quando conseguiu abrir a porta
    static UdpClient? envio;      // usado quando a porta já está ocupada (2ª instância)
    static volatile bool ativo;
    static string nome = "", codigo = "";
    static int porta;

    // Identifica esta execução: o PC ignora os próprios avisos por este número,
    // mesmo que existam duas instâncias no mesmo computador.
    public static string Instancia { get; } = Guid.NewGuid().ToString("N");

    public static void Iniciar(string nomeMaquina, int portaApi, string codigoApi)
    {
        if (ativo) return;

        nome = nomeMaquina;
        porta = portaApi;
        codigo = (codigoApi ?? "").ToUpperInvariant();
        ativo = true;

        try
        {
            var s = new UdpClient();
            s.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            s.Client.EnableBroadcast = true;
            s.Client.Bind(new IPEndPoint(IPAddress.Any, PORTA));
            escuta = s;
            new Thread(Escutar) { IsBackground = true, Name = "Time - descoberta" }.Start();
        }
        catch { escuta = null; /* porta ocupada: segue só anunciando */ }

        try { envio = new UdpClient { EnableBroadcast = true }; } catch { envio = null; }

        new Thread(Anunciar) { IsBackground = true, Name = "Time - aviso" }.Start();
    }

    public static void Parar()
    {
        ativo = false;
        try { escuta?.Close(); } catch { }
        try { envio?.Close(); } catch { }

        lock (desafios)
        {
            foreach (var espera in desafios.Values) espera.TrySetResult("");
            desafios.Clear();
        }
    }

    // PCs vistos nos últimos 35 s (o aviso chega a cada 10 s).
    public static IReadOnlyList<Vizinho> Vizinhos()
    {
        var limite = DateTime.UtcNow.AddMilliseconds(-VALIDADE_MS);
        lock (trava)
        {
            foreach (string chave in vistos.Where(x => x.Value.Visto < limite).Select(x => x.Key).ToList())
                vistos.Remove(chave);
            return vistos.Values.ToList();
        }
    }

    public static Vizinho? ProcurarPorMaquina(string maquina)
    {
        if (string.IsNullOrWhiteSpace(maquina)) return null;
        return Vizinhos()
            .Where(v => v.Nome.Equals(maquina, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.Visto)
            .FirstOrDefault();
    }

    public static Vizinho? ProcurarPorEndereco(string ip, int portaApi) =>
        Vizinhos().FirstOrDefault(v => v.Ip == ip && v.Porta == portaApi);

    public enum Verificacao { Ok, SemResposta, CodigoErrado }

    // Confirma que quem está naquele IP realmente tem o código.
    // O desafio vai para a porta da descoberta (8788), não para a do painel.
    public static Verificacao Verificar(string ip, string codigoTeste)
    {
        if (escuta == null) return Verificacao.SemResposta;
        if (!IPAddress.TryParse(ip, out var endereco)) return Verificacao.SemResposta;

        string numero = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        var espera = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (desafios) desafios[numero] = espera;

        try
        {
            byte[] pacote = JsonSerializer.SerializeToUtf8Bytes(new { t = "d", app = "time", i = Instancia, x = numero });
            (escuta ?? envio)?.Send(pacote, pacote.Length, new IPEndPoint(endereco, PORTA));

            if (!espera.Task.Wait(ESPERA_DESAFIO_MS)) return Verificacao.SemResposta;

            string recebido = espera.Task.Result;
            if (recebido.Length == 0) return Verificacao.SemResposta;

            string esperado = Hmac(codigoTeste, numero);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(recebido.ToUpperInvariant()),
                Encoding.ASCII.GetBytes(esperado))
                ? Verificacao.Ok
                : Verificacao.CodigoErrado;
        }
        catch { return Verificacao.SemResposta; }
        finally { lock (desafios) desafios.Remove(numero); }
    }

    // ---------- rede ----------

    static void Anunciar()
    {
        while (ativo)
        {
            byte[] pacote = JsonSerializer.SerializeToUtf8Bytes(new
            {
                t = "a", app = "time", v = 1, i = Instancia, n = nome, p = porta
            });

            foreach (var destino in Destinos())
            {
                try { (escuta ?? envio)?.Send(pacote, pacote.Length, destino); } catch { }
            }

            for (int i = 0; i < INTERVALO_MS / 500 && ativo; i++)
                Thread.Sleep(500);
        }
    }

    // 255.255.255.255 mais o broadcast de cada placa (alguns roteadores só
    // repassam o endereço dirigido da sub-rede).
    static List<IPEndPoint> Destinos()
    {
        var lista = new List<IPEndPoint> { new(IPAddress.Broadcast, PORTA) };

        try
        {
            foreach (var placa in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (placa.OperationalStatus != OperationalStatus.Up) continue;
                if (placa.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                foreach (var unico in placa.GetIPProperties().UnicastAddresses)
                {
                    if (unico.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (unico.IPv4Mask == null) continue;

                    byte[] ip = unico.Address.GetAddressBytes();
                    byte[] mascara = unico.IPv4Mask.GetAddressBytes();
                    if (ip.Length != 4 || mascara.Length != 4) continue;

                    var alvo = new byte[4];
                    for (int i = 0; i < 4; i++) alvo[i] = (byte)(ip[i] | ~mascara[i]);

                    var broadcast = new IPAddress(alvo);
                    if (!broadcast.Equals(IPAddress.Broadcast))
                        lista.Add(new IPEndPoint(broadcast, PORTA));
                }
            }
        }
        catch { /* placa sem permissão: fica só o broadcast geral */ }

        return lista;
    }

    static void Escutar()
    {
        var socket = escuta;
        if (socket == null) return;

        while (ativo)
        {
            byte[] dados;
            var origem = new IPEndPoint(IPAddress.Any, 0);
            try { dados = socket.Receive(ref origem); }
            catch { return; }   // soquete fechado no Parar()

            try { Ler(dados, origem); } catch { /* pacote estranho: ignora */ }
        }
    }

    static void Ler(byte[] dados, IPEndPoint origem)
    {
        using var documento = JsonDocument.Parse(dados);
        var raiz = documento.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object) return;
        if (!raiz.TryGetProperty("app", out var app) || app.GetString() != "time") return;
        if (!raiz.TryGetProperty("t", out var tipo)) return;

        switch (tipo.GetString())
        {
            case "a": Aviso(raiz, origem); break;
            case "d": ResponderDesafio(raiz, origem); break;
            case "r": RespostaDesafio(raiz); break;
        }
    }

    static void Aviso(JsonElement raiz, IPEndPoint origem)
    {
        string id = Texto(raiz, "i");
        if (id.Length == 0 || id == Instancia) return;

        string maquina = Texto(raiz, "n");
        if (maquina.Length == 0) return;

        if (!raiz.TryGetProperty("p", out var p) || p.ValueKind != JsonValueKind.Number) return;
        int portaApi = p.GetInt32();
        if (portaApi < 1 || portaApi > 65535) return;

        // O IP que vale é o de origem do pacote: o campo do JSON poderia ser forjado.
        var vizinho = new Vizinho(maquina, origem.Address.ToString(), portaApi, DateTime.UtcNow);

        lock (trava) vistos[id] = vizinho;
    }

    static void ResponderDesafio(JsonElement raiz, IPEndPoint origem)
    {
        string numero = Texto(raiz, "x");
        if (numero.Length == 0) return;

        byte[] pacote = JsonSerializer.SerializeToUtf8Bytes(new
        {
            t = "r", app = "time", i = Instancia, x = numero, m = Hmac(codigo, numero)
        });

        try { (escuta ?? envio)?.Send(pacote, pacote.Length, new IPEndPoint(origem.Address, PORTA)); } catch { }
    }

    static void RespostaDesafio(JsonElement raiz)
    {
        string numero = Texto(raiz, "x");
        if (numero.Length == 0) return;

        TaskCompletionSource<string>? espera;
        lock (desafios)
        {
            if (!desafios.TryGetValue(numero, out espera)) return;
            desafios.Remove(numero);
        }

        espera.TrySetResult(Texto(raiz, "m"));
    }

    static string Texto(JsonElement raiz, string nome)
    {
        if (!raiz.TryGetProperty(nome, out var valor)) return "";
        return valor.ValueKind == JsonValueKind.String ? (valor.GetString() ?? "") : "";
    }

    // ---------- criptografia ----------

    static string Hmac(string codigoApi, string texto)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes((codigoApi ?? "").ToUpperInvariant()));
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(texto)));
    }
}
