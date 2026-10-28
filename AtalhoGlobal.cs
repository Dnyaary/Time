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

// Atalho global em dois tempos: segure Ctrl+Shift+Alt, aperte X e, em até 2 s, aperte 5.
// As teclas continuam funcionando nos outros programas; só o 5 do atalho é consumido.
static class AtalhoGlobal
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100;
    const int WM_SYSKEYDOWN = 0x0104;
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
    const int VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    const int VK_X = 0x58, VK_5 = 0x35, VK_NUMPAD5 = 0x65;
    const int VK_F2 = 0x71, VK_1 = 0x31, VK_NUMPAD1 = 0x61;
    static readonly TimeSpan ESPERA = TimeSpan.FromSeconds(4);

    static IntPtr gancho;
    static HOOKPROC? retorno;   // mantém o delegate vivo: se ele for coletado, o Windows chama memória inválida
    static DateTime xEm;
    static DateTime f2Em;
    static Action? disparado;
    static Action? disparadoInfo;

    public static void Instalar(Action aoDisparar, Action aoPedirInfo)
    {
        disparado = aoDisparar;
        disparadoInfo = aoPedirInfo;
        retorno = AoTeclar;
        gancho = SetWindowsHookEx(WH_KEYBOARD_LL, retorno, IntPtr.Zero, 0);
    }

    public static void Remover()
    {
        if (gancho != IntPtr.Zero) UnhookWindowsHookEx(gancho);
        gancho = IntPtr.Zero;
        retorno = null;
        disparado = null;
        disparadoInfo = null;
    }

    // Atalhos de dois tempos: segure Ctrl+Shift+Alt e aperte a primeira tecla;
    // depois (até 4 s) aperte a segunda. Soltar Ctrl/Shift/Alt entre as duas não
    // atrapalha: o que vale é a primeira tecla ter vindo com os três. Qualquer
    // outra tecla cancela a sequência.
    static IntPtr AoTeclar(int codigo, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (codigo >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int tecla = Marshal.ReadInt32(lParam);

                if (!EhModificador(tecla))
                {
                    bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                    bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                    bool comOsTres = ctrl && shift && alt;
                    DateTime agora = DateTime.UtcNow;

                    if (comOsTres && tecla == VK_X)
                    {
                        xEm = agora;                         // 1º tempo: X
                    }
                    else if (comOsTres && tecla == VK_F2)
                    {
                        f2Em = agora;                        // 1º tempo: F2
                    }
                    else if ((tecla == VK_5 || tecla == VK_NUMPAD5) && xEm != default && agora - xEm <= ESPERA)
                    {
                        xEm = f2Em = default;
                        disparado?.Invoke();                 // X e depois 5: janela mínima
                        return (IntPtr)1;                    // consome o 5 do atalho
                    }
                    else if ((tecla == VK_1 || tecla == VK_NUMPAD1) && f2Em != default && agora - f2Em <= ESPERA)
                    {
                        xEm = f2Em = default;
                        disparadoInfo?.Invoke();             // F2 e depois 1: informações
                        return (IntPtr)1;                    // consome o 1 do atalho
                    }
                    else
                    {
                        xEm = f2Em = default;                // outra tecla: esquece a sequência
                    }
                }
            }
        }
        catch { }
        return CallNextHookEx(gancho, codigo, wParam, lParam);
    }

    static bool EhModificador(int tecla) =>
        tecla == VK_SHIFT || tecla == VK_LSHIFT || tecla == VK_RSHIFT ||
        tecla == VK_CONTROL || tecla == VK_LCONTROL || tecla == VK_RCONTROL ||
        tecla == VK_MENU || tecla == VK_LMENU || tecla == VK_RMENU;

    delegate IntPtr HOOKPROC(int codigo, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HOOKPROC lpfn, IntPtr hMod, uint idThread);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int tecla);
}
