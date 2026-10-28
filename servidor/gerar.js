// Time — servidor opcional
// Copyright (c) 2026 Daniel 'dnyaary' Carvalho — https://instagram.com/dnyaary
// SPDX-License-Identifier: MIT
//
// Copia a página do painel, o manifesto e o ícone de ../Api.cs para src/, para o
// servidor mostrar exatamente o mesmo painel do Time. Roda sozinho antes do deploy.
const fs = require('fs');
const path = require('path');

const api = fs.readFileSync(path.join(__dirname, '..', 'Api.cs'), 'utf8').replace(/\r\n/g, '\n');

function constante(nome) {
  const inicio = api.indexOf(`const string ${nome} =\n        """\n`);
  if (inicio < 0) throw new Error(`Não achei ${nome} no Api.cs`);
  const abre = api.indexOf('"""\n', inicio) + 4;
  const fecha = api.indexOf('\n        """;', abre);
  return api.slice(abre, fecha).split('\n')
    .map(l => (l.startsWith('        ') ? l.slice(8) : l.trimStart()))
    .join('\n') + '\n';
}

const saida = path.join(__dirname, 'src');
fs.writeFileSync(path.join(saida, 'pagina.html'), constante('Pagina'));
fs.writeFileSync(path.join(saida, 'manifesto.json.txt'), constante('Manifesto'));
fs.writeFileSync(path.join(saida, 'icone.svg.txt'), constante('Icone'));
console.log('Página, manifesto e ícone copiados do Api.cs.');
