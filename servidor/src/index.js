// Time — servidor opcional (Cloudflare Worker + D1)
// Copyright (c) 2026 Daniel 'dnyaary' Carvalho — https://instagram.com/dnyaary
// SPDX-License-Identifier: MIT
//
// Cada PC com o Time envia o resumo dos seus dias para POST /api/enviar (com a
// CHAVE_ENVIO). O painel é a mesma página do Time, servida aqui, e lê os dados com
// o CODIGO de leitura — as mesmas rotas /api/... do painel da rede local, então a
// página não muda. Assim dá para abrir o painel de qualquer lugar, e o histórico de
// um PC continua aparecendo mesmo com ele desligado.
//
// Segredos (wrangler secret put): CHAVE_ENVIO e CODIGO.
// Variável (wrangler.toml): FUSO, o fuso horário de "hoje" (ex.: America/Sao_Paulo).

import PAGINA from './pagina.html';
import MANIFESTO from './manifesto.json.txt';
import ICONE from './icone.svg.txt';

const ONLINE_MS = 150 * 1000;   // PC que enviou nos últimos 2,5 min está online

export default {
  async fetch(pedido, env) {
    try {
      return await atender(pedido, env);
    } catch (e) {
      return json({ erro: 'servidor' }, 500);
    }
  }
};

async function atender(pedido, env) {
  const url = new URL(pedido.url);
  const rota = url.pathname;
  const q = url.searchParams;

  if (pedido.method === 'GET') {
    if (rota === '/' || rota === '/index.html') return resposta(PAGINA, 'text/html; charset=utf-8');
    if (rota === '/manifest.webmanifest') return resposta(MANIFESTO, 'application/manifest+json; charset=utf-8');
    if (rota === '/icone.svg') return resposta(ICONE, 'image/svg+xml');
  }

  if (!rota.startsWith('/api/')) return resposta('Não encontrado', 'text/plain; charset=utf-8', 404);

  // Envio dos PCs: só com a chave de envio.
  if (rota === '/api/enviar') {
    if (pedido.method !== 'POST') return json({ erro: 'metodo' }, 405);
    if (!confere(portador(pedido), env.CHAVE_ENVIO)) return json({ erro: 'chave' }, 401);
    return receber(await pedido.json(), env);
  }

  // Leitura: o mesmo código de acesso do painel (?k= ou Authorization: Bearer).
  if (!confere(q.get('k') || portador(pedido), env.CODIGO)) return json({ erro: 'codigo' }, 401);

  const hoje = hojeNoFuso(env.FUSO);
  const id = q.get('id') || '';

  switch (rota) {
    case '/api/resumo': return json(await resumo(env, hoje, id, url));
    case '/api/dias': return json(await diasLista(env, faixa(q.get('n'), 30, 90), id));
    case '/api/pc': {
      const r = await resumo(env, hoje, id, url);
      if (r.erro) return json(r);
      return json({ resumo: r, dias: await diasLista(env, faixa(q.get('n'), 14, 90), id) });
    }
    case '/api/dia': return json(await dia(env, q.get('data') || '', id));
    case '/api/app': return json(await app(env, q.get('nome') || '', id, faixa(q.get('n'), 70, 90)));
    case '/api/grupos': return json(await grupos(env));
    case '/api/semana': return json(await periodo(env, q.get('inicio') || '', faixa(q.get('n'), 7, 31), id));
    case '/api/csv': return csv(env, q.get('tipo') || '', id, hoje, pedido);
    case '/api/descobertos': return json([]);
    case '/api/pcs':
    case '/api/pcs/remover': return json({ erro: 'servidor' });
    default: return json({ erro: 'rota' }, 404);
  }
}

// ---------- envio dos PCs ----------

async function receber(corpo, env) {
  const maquina = texto(corpo && corpo.maquina, 64);
  if (!maquina) return json({ erro: 'maquina' }, 400);
  const agora = corpo.agora || {};
  const dias = Array.isArray(corpo.dias) ? corpo.dias.slice(0, 62) : [];

  const comandos = [
    env.DB.prepare(
      `INSERT INTO pcs (maquina, usuario, visto, atual, contando, pausado, pausa_ate, grupos, foco)
       VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)
       ON CONFLICT (maquina) DO UPDATE SET usuario = ?2, visto = ?3, atual = ?4, contando = ?5,
         pausado = ?6, pausa_ate = ?7, grupos = ?8, foco = ?9`
    ).bind(
      maquina, texto(corpo.usuario, 64), Date.now(), texto(agora.atual, 120),
      agora.contando ? 1 : 0, agora.pausado ? 1 : 0, texto(agora.pausaAte, 5),
      JSON.stringify(objeto(corpo.grupos && corpo.grupos.mapa)),
      JSON.stringify(objeto(corpo.grupos && corpo.grupos.foco))
    )
  ];

  let aceitos = 0;
  for (const d of dias) {
    if (!dataValida(d && d.data)) continue;
    const apps = {};
    for (const [nome, s] of Object.entries(objeto(d.apps)).slice(0, 500))
      if (Number(s) > 0) apps[texto(nome, 120)] = Math.round(Number(s));
    const horas = Array.from({ length: 24 }, (_, h) => Math.max(0, Math.round(Number((d.horas || [])[h]) || 0)));
    // O PC manda sempre o total do dia: gravar de novo só substitui, nunca soma em dobro.
    comandos.push(env.DB.prepare(
      `INSERT INTO dias (maquina, data, segundos, parado, inicio, ultima, apps, horas)
       VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)
       ON CONFLICT (maquina, data) DO UPDATE SET segundos = ?3, parado = ?4, inicio = ?5,
         ultima = ?6, apps = ?7, horas = ?8`
    ).bind(
      maquina, d.data, inteiro(d.segundos), inteiro(d.parado), hora(d.inicio), hora(d.ultima),
      JSON.stringify(apps), JSON.stringify(horas)
    ));
    aceitos++;
  }

  await env.DB.batch(comandos);
  return json({ ok: true, dias: aceitos });
}

// ---------- leitura ----------

async function pcsTodos(env) {
  const { results } = await env.DB.prepare('SELECT * FROM pcs ORDER BY maquina').all();
  return results.map(p => ({ ...p, online: Date.now() - (p.visto || 0) < ONLINE_MS }));
}

// Linhas de "dias" de uma faixa de datas, de todos os PCs ou de um só.
async function linhasDias(env, de, ate, id) {
  const filtro = id ? ' AND maquina = ?3' : '';
  const consulta = env.DB.prepare(`SELECT * FROM dias WHERE data >= ?1 AND data <= ?2${filtro} ORDER BY data`);
  const { results } = await (id ? consulta.bind(de, ate, id) : consulta.bind(de, ate)).all();
  return results.map(r => ({ ...r, apps: lerJson(r.apps, {}), horas: lerJson(r.horas, []) }));
}

// Soma várias linhas (de PCs diferentes) num dia só.
function somar(linhas) {
  const t = { segundos: 0, parado: 0, inicio: '--:--', ultima: '--:--', apps: {}, horas: new Array(24).fill(0) };
  for (const r of linhas) {
    t.segundos += r.segundos;
    t.parado += r.parado;
    if (r.inicio && r.inicio !== '--:--' && (t.inicio === '--:--' || r.inicio < t.inicio)) t.inicio = r.inicio;
    if (r.ultima && r.ultima !== '--:--' && (t.ultima === '--:--' || r.ultima > t.ultima)) t.ultima = r.ultima;
    for (const [nome, s] of Object.entries(r.apps)) t.apps[nome] = (t.apps[nome] || 0) + s;
    for (let h = 0; h < 24; h++) t.horas[h] += Number(r.horas[h]) || 0;
  }
  return t;
}

function listaApps(apps) {
  return Object.entries(apps).map(([nome, segundos]) => ({ nome, segundos }))
    .sort((a, b) => b.segundos - a.segundos);
}

async function resumo(env, hoje, id, url) {
  let pcs = await pcsTodos(env);
  if (id) {
    pcs = pcs.filter(p => p.maquina === id);
    if (!pcs.length) return { erro: 'pc' };
  }
  const linhas = await linhasDias(env, hoje, hoje, id);
  const t = somar(linhas);
  const porPc = pcs.map(p => {
    const r = linhas.find(l => l.maquina === p.maquina);
    return {
      id: p.maquina, nome: p.maquina, segundos: r ? r.segundos : 0,
      atual: p.online ? p.atual : '', contando: p.online && !!p.contando, pausado: p.online && !!p.pausado,
      online: p.online, guardado: !p.online, este: false
    };
  });

  // Estado do topo: o PC que está contando e enviou por último.
  const ativos = pcs.filter(p => p.online && p.contando).sort((a, b) => b.visto - a.visto);
  const pausados = pcs.filter(p => p.online && p.pausado);
  const umPc = pcs.length === 1 ? pcs[0] : null;

  return {
    maquina: id || (umPc ? umPc.maquina : url.hostname),
    usuario: umPc ? umPc.usuario || '' : '',
    data: hoje,
    segundos: t.segundos,
    parado: t.parado,
    inicio: t.inicio,
    ultima: t.ultima,
    contando: ativos.length > 0,
    pausado: !ativos.length && pausados.length > 0,
    pausaAte: !ativos.length && pausados.length ? pausados[0].pausa_ate || '' : '',
    atual: ativos.length ? ativos[0].atual : '',
    pcs: id ? [] : porPc,
    apps: listaApps(t.apps),
    horas: t.horas,
    servidor: true
  };
}

async function diasLista(env, n, id) {
  const ate = hojeNoFuso(env.FUSO);
  const de = somaDias(ate, -(n + 30));   // folga: dias sem uso não entram
  const linhas = await linhasDias(env, de, ate, id);
  const porData = new Map();
  for (const r of linhas) {
    if (!porData.has(r.data)) porData.set(r.data, []);
    porData.get(r.data).push(r);
  }
  return [...porData.entries()]
    .map(([data, ls]) => { const t = somar(ls); return { data, segundos: t.segundos, parado: t.parado, inicio: t.inicio, ultima: t.ultima }; })
    .filter(d => d.segundos > 0)
    .sort((a, b) => (a.data < b.data ? 1 : -1))
    .slice(0, n);
}

async function dia(env, data, id) {
  if (!dataValida(data)) return { erro: 'data' };
  const t = somar(await linhasDias(env, data, data, id));
  return { data, segundos: t.segundos, parado: t.parado, inicio: t.inicio, ultima: t.ultima, horas: t.horas, apps: listaApps(t.apps) };
}

async function app(env, nome, id, n) {
  nome = (nome || '').trim();
  if (!nome) return { erro: 'app' };
  const ate = hojeNoFuso(env.FUSO);
  const linhas = await linhasDias(env, somaDias(ate, -(n + 30)), ate, id);
  const porData = {};
  const alvo = nome.toLowerCase();
  for (const r of linhas)
    for (const [item, s] of Object.entries(r.apps))
      if (item.toLowerCase() === alvo) porData[r.data] = (porData[r.data] || 0) + s;
  const mapa = (await grupos(env)).mapa;
  return {
    nome,
    grupo: mapa[nome] || '',
    dias: Object.entries(porData).map(([data, segundos]) => ({ data, segundos }))
      .sort((a, b) => (a.data < b.data ? 1 : -1)).slice(0, n)
  };
}

// Grupos e foco: junta o categorias.txt de cada PC (o que enviou por último vale primeiro).
async function grupos(env) {
  const pcs = (await pcsTodos(env)).sort((a, b) => b.visto - a.visto);
  const mapa = {}, foco = {};
  for (const p of pcs) {
    for (const [k, v] of Object.entries(lerJson(p.grupos, {}))) if (!(k in mapa)) mapa[k] = v;
    for (const [k, v] of Object.entries(lerJson(p.foco, {}))) if (!(k in foco)) foco[k] = v;
  }
  return { mapa, foco };
}

async function periodo(env, inicio, n, id) {
  if (!dataValida(inicio)) return { erro: 'data' };
  const fim = somaDias(inicio, n - 1);
  const linhas = await linhasDias(env, inicio, fim, id);
  const dias = [];
  for (let i = 0; i < n; i++) {
    const data = somaDias(inicio, i);
    const t = somar(linhas.filter(r => r.data === data));
    dias.push({ data, segundos: t.segundos, parado: t.parado, apps: listaApps(t.apps) });
  }
  return { inicio, dias };
}

// Planilhas iguais às do Time (com as colunas Usuario e Maquina dizendo de quem é cada linha).
async function csv(env, tipo, id, hoje, pedido) {
  if (!['dias', 'apps', 'horas'].includes(tipo)) return json({ erro: 'csv' }, 404);
  const pt = /^pt/i.test(pedido.headers.get('Accept-Language') || '');
  const usuarios = Object.fromEntries((await pcsTodos(env)).map(p => [p.maquina, p.usuario || '']));
  const linhas = await linhasDias(env, '0000-01-01', '9999-12-31', id);
  const saida = [];
  if (tipo === 'dias') {
    saida.push(pt ? 'Data;Usuario;Maquina;Primeira atividade;Ultima atividade;Tempo ativo;Segundos;Parado (s)'
                  : 'Date;User;Machine;First activity;Last activity;Active time;Seconds;Idle (s)');
    for (const r of linhas)
      saida.push([r.data, usuarios[r.maquina], r.maquina, r.inicio, r.ultima, hms(r.segundos), r.segundos, r.parado].join(';'));
  } else if (tipo === 'apps') {
    saida.push(pt ? 'Data;Usuario;Maquina;Item;Tempo;Segundos' : 'Date;User;Machine;Item;Time;Seconds');
    for (const r of linhas)
      for (const [nome, s] of Object.entries(r.apps).sort((a, b) => b[1] - a[1]))
        saida.push([r.data, usuarios[r.maquina], r.maquina, nome.replace(/;/g, ','), hms(s), s].join(';'));
  } else {
    saida.push(pt ? 'Data;Usuario;Maquina;Hora;Segundos' : 'Date;User;Machine;Hour;Seconds');
    for (const r of linhas)
      for (let h = 0; h < 24; h++)
        if (r.horas[h] >= 1) saida.push([r.data, usuarios[r.maquina], r.maquina, h, r.horas[h]].join(';'));
  }
  return new Response('﻿' + saida.join('\r\n') + '\r\n', {
    headers: {
      'Content-Type': 'text/csv; charset=utf-8',
      'Content-Disposition': `attachment; filename="time-${tipo}-${hoje}.csv"`,
      'Cache-Control': 'no-store'
    }
  });
}

// ---------- utilidades ----------

function resposta(corpo, tipo, status = 200) {
  return new Response(corpo, { status, headers: { 'Content-Type': tipo, 'Cache-Control': 'no-store' } });
}

function json(dados, status = 200) {
  return resposta(JSON.stringify(dados), 'application/json; charset=utf-8', status);
}

function portador(pedido) {
  const a = pedido.headers.get('Authorization') || '';
  return a.startsWith('Bearer ') ? a.slice(7).trim() : '';
}

// Compara sem diferenciar maiúsculas (o código aparece em maiúsculas, mas no celular
// é comum digitar em minúsculas) e sem atalho que vaze o tamanho por tempo de resposta.
function confere(dado, segredo) {
  if (!segredo || !dado) return false;
  const a = String(dado).trim().toUpperCase(), b = String(segredo).trim().toUpperCase();
  let dif = a.length ^ b.length;
  for (let i = 0; i < Math.max(a.length, b.length); i++) dif |= (a.charCodeAt(i) || 0) ^ (b.charCodeAt(i) || 0);
  return dif === 0;
}

function hojeNoFuso(fuso) {
  try {
    return new Intl.DateTimeFormat('en-CA', { timeZone: fuso || 'UTC', year: 'numeric', month: '2-digit', day: '2-digit' })
      .format(new Date());
  } catch (e) {
    return new Date().toISOString().slice(0, 10);
  }
}

function somaDias(data, n) {
  const d = new Date(data + 'T12:00:00Z');
  d.setUTCDate(d.getUTCDate() + n);
  return d.toISOString().slice(0, 10);
}

function dataValida(s) { return typeof s === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(s) && !isNaN(Date.parse(s + 'T00:00:00Z')); }
function hora(s) { return typeof s === 'string' && /^\d{2}:\d{2}$/.test(s) ? s : '--:--'; }
function texto(s, max) { return typeof s === 'string' ? s.slice(0, max) : ''; }
function inteiro(n) { const v = Math.round(Number(n)); return Number.isFinite(v) && v > 0 ? v : 0; }
function objeto(o) { return o && typeof o === 'object' && !Array.isArray(o) ? o : {}; }
function faixa(v, padrao, max) { const n = parseInt(v, 10); return Number.isFinite(n) ? Math.min(Math.max(n, 1), max) : padrao; }
function lerJson(s, padrao) { try { return s ? JSON.parse(s) : padrao; } catch (e) { return padrao; } }

function hms(s) {
  const d2 = n => String(n).padStart(2, '0');
  return d2(Math.floor(s / 3600)) + ':' + d2(Math.floor(s % 3600 / 60)) + ':' + d2(s % 60);
}
