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

// Pausa: para de contar por um tempo, mesmo com o PC em uso.

partial class Contador
{
    // ---------- Pausa ----------
    // Botão "Pausar" da janela: 15 minutos, 1 hora ou até retomar. Serve para um
    // intervalo, ou quando outra pessoa vai usar o PC. Não fica gravada: fechar e
    // abrir o Time de novo volta a contar.

    long pausaAte;   // 0 = sem pausa; long.MaxValue = até retomar; senão, o fim da pausa (ticks UTC)
    ContextMenuStrip? menuPausa;

    bool Pausado()
    {
        long fim = Interlocked.Read(ref pausaAte);
        if (fim == 0) return false;
        if (fim != long.MaxValue && DateTime.UtcNow.Ticks >= fim)
        {
            Interlocked.CompareExchange(ref pausaAte, 0, fim);   // acabou o tempo: volta a contar
            return false;
        }
        return true;
    }

    // "14:30" quando a pausa tem hora para acabar; "" quando é até retomar (ou não há pausa).
    string FimDaPausa()
    {
        long fim = Interlocked.Read(ref pausaAte);
        if (fim == 0 || fim == long.MaxValue) return "";
        return new DateTime(fim, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    void Pausar(TimeSpan? duracao)
    {
        long fim = duracao is TimeSpan d ? DateTime.UtcNow.Add(d).Ticks : long.MaxValue;
        Interlocked.Exchange(ref pausaAte, fim);
        ativo = false;
        AtualizarTela();
    }

    void Retomar()
    {
        Interlocked.Exchange(ref pausaAte, 0);
        AtualizarTela();
    }

    // Pausado: o botão retoma. Contando: abre o menu com quanto tempo pausar.
    void ClicouPausar()
    {
        if (Pausado())
        {
            Retomar();
            return;
        }

        if (menuPausa == null)
        {
            menuPausa = new ContextMenuStrip { ShowImageMargin = false };
            menuPausa.Items.Add(L.T("Por 15 minutos", "For 15 minutes"), null, (_, _) => Pausar(TimeSpan.FromMinutes(15)));
            menuPausa.Items.Add(L.T("Por 1 hora", "For 1 hour"), null, (_, _) => Pausar(TimeSpan.FromHours(1)));
            menuPausa.Items.Add(L.T("Até eu retomar", "Until I resume"), null, (_, _) => Pausar(null));
        }
        menuPausa.Show(janela.Pausar, new Point(0, janela.Pausar.Height));
    }
}
