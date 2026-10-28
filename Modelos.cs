// Time — contador de tempo de uso real do PC
// Copyright (c) 2026 Daniel 'dnyaary' Carvalho — https://instagram.com/dnyaary
// SPDX-License-Identifier: MIT
// Distribuído sob a licença MIT. Veja o arquivo LICENSE na raiz do projeto.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

// Os dados que o Time guarda e troca entre os PCs.

class RegistroDia
{
    public long Ms;                 // tempo ativo em milissegundos
    public TimeSpan? Primeira;      // hora da primeira atividade do dia
    public TimeSpan? Ultima;        // hora da última atividade do dia
    public int Limite;              // limite de inatividade (s) usado; 0 = desconhecido, -1 = dia medido com limites diferentes
    public long MsParado;           // PC ligado com o Time rodando, mas sem ninguém usando (pausa não entra)
}

// Tipo: "site" (título, só em navegador), "titulo" (título, qualquer janela) ou "programa" (nome do processo).
// Tolerancia: segundos sem mexer que ainda contam para esse item (0 = usa o padrão LIMITE_SEGUNDOS).
// Grupo: o [Grupo] do categorias.txt em que a regra está ("" = sem grupo).
record Regra(string Tipo, string Texto, string Nome, int Tolerancia, string Grupo);

// Um outro PC que aparece no painel (lido do pcs.txt).
// Maquina é o nome que o outro PC anuncia na rede (identidade); Nome é o rótulo
// escolhido por você. Endereco é o último IP conhecido — a descoberta atualiza.
sealed record Remoto(string Nome, string Codigo, string Maquina, Uri Endereco);

// O que o outro PC respondeu em /api/tudo, já no formato que o painel usa.
// Guardado = o PC não respondeu agora, e estes são os últimos dados que ele mandou.
sealed record RemotoDados
{
    public required Remoto Remoto { get; init; }
    public bool Online { get; init; }
    public bool Guardado { get; init; }
    public bool TemDados => Online || Guardado;
    public string Data { get; init; } = "";      // dia (AAAA-MM-DD) a que Segundos/Apps/Horas se referem
    public long Parado { get; init; }
    public string Usuario { get; init; } = "";
    public long Segundos { get; init; }
    public string Inicio { get; init; } = "--:--";
    public string Ultima { get; init; } = "--:--";
    public bool Contando { get; init; }
    public bool Pausado { get; init; }
    public string PausaAte { get; init; } = "";   // "14:30"; vazio = pausado até retomar
    public string Atual { get; init; } = "";
    public Dictionary<string, long> Apps { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> Dias { get; init; } = new();
    public long[] Horas { get; init; } = new long[24];   // segundos em cada hora de hoje
}
