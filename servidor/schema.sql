-- Time — banco do servidor (Cloudflare D1)
-- Uma linha por PC e uma linha por PC por dia. Os programas e as horas de cada dia
-- ficam em JSON na própria linha do dia: o painel sempre lê o dia inteiro de uma vez.

CREATE TABLE IF NOT EXISTS pcs (
  maquina   TEXT PRIMARY KEY,   -- nome do PC no Windows
  usuario   TEXT,
  visto     INTEGER,            -- último envio (ms desde 1970)
  atual     TEXT,               -- programa em foco no último envio
  contando  INTEGER,
  pausado   INTEGER,
  pausa_ate TEXT,
  grupos    TEXT,               -- JSON: programa -> grupo, do categorias.txt daquele PC
  foco      TEXT                -- JSON: grupo -> produtivo/distracao
);

CREATE TABLE IF NOT EXISTS dias (
  maquina  TEXT NOT NULL,
  data     TEXT NOT NULL,       -- AAAA-MM-DD no fuso do PC
  segundos INTEGER NOT NULL,    -- tempo de uso
  parado   INTEGER NOT NULL,    -- ligado sem uso
  inicio   TEXT,                -- HH:MM
  ultima   TEXT,
  apps     TEXT,                -- JSON: programa -> segundos
  horas    TEXT,                -- JSON: 24 números (segundos em cada hora)
  PRIMARY KEY (maquina, data)
);

CREATE INDEX IF NOT EXISTS dias_por_data ON dias (data);
