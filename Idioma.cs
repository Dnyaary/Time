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

// Idioma da interface: português quando o Windows está em português; inglês nos demais.
static class L
{
    public static readonly bool Pt =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase);

    public static string T(string pt, string en) => Pt ? pt : en;
}
