'use strict';
// The farm: every PC running AutoRejoin under one Top AFK account. Shared by the Discord bot, the Telegram bot and
// the website dashboard.
//  • agents: userId -> pcId -> last report (accounts, running…) + queued commands for that PC;
//  • screenshots: a request goes to the PC that runs the account, the app uploads a JPEG, waiting deliverers get it;
//  • events (drop, back, error, stuck, alert) are fanned out to listeners (Telegram) and counted for analytics;
//  • analytics: seconds in game, drops and honey per account per hour, kept in farm_hourly.
const crypto = require('crypto');

const STATE_STALE = 60 * 1000;       // a PC silent longer than this = offline
const COMMAND_TTL = 2 * 60 * 1000;   // commands not picked up in time are dropped
const SCREEN_TTL = 3 * 60 * 1000;    // screenshot requests expire
const MAX_ACCOUNTS = 50;
const MAX_PCS = 10;
const SAMPLE_GAP = 75;               // s: longer between two reports → the gap is not counted as time in game

const FARM_SCHEMA = [
  `CREATE TABLE IF NOT EXISTS farm_hourly (
     user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
     hour TIMESTAMPTZ NOT NULL,
     account_id TEXT NOT NULL,
     account_name TEXT,
     pc_name TEXT,
     seconds INTEGER NOT NULL DEFAULT 0,
     drops INTEGER NOT NULL DEFAULT 0,
     honey BIGINT NOT NULL DEFAULT 0,
     PRIMARY KEY (user_id, hour, account_id)
   )`,
  `CREATE TABLE IF NOT EXISTS farm_events (
     id SERIAL PRIMARY KEY,
     user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
     at TIMESTAMPTZ NOT NULL,
     kind TEXT NOT NULL,
     account_name TEXT,
     pc_name TEXT,
     text TEXT
   )`
];

function clip(s, n) { s = String(s == null ? '' : s); return s.length > n ? s.slice(0, n - 1) + '…' : s; }
const hourStart = (t) => new Date(Math.floor(t / 3600000) * 3600000);
const inGameState = (s) => s === 'ingame' || s === 'script';

function sanitizeState(body) {
  const accounts = (Array.isArray(body.accounts) ? body.accounts : []).slice(0, MAX_ACCOUNTS).map((a) => ({
    id: clip(a && a.id, 64), name: clip(a && a.name, 40) || '?', main: !!(a && a.main), enabled: !!(a && a.enabled),
    state: clip(a && a.state, 20), text: clip(a && a.text, 60), uptime: clip(a && a.uptime, 20),
    honey: Number.isFinite(Number(a && a.honey)) && Number(a.honey) >= 0 ? Math.floor(Number(a.honey)) : null
  })).filter((a) => a.id);
  return { running: !!body.running, phase: clip(body.phase, 20), version: clip(body.version, 20), lang: body.lang === 'en' ? 'en' : 'ru', accounts };
}

function createFarm(pool, options = {}) {
  const now = options.now || (() => new Date());
  const agents = new Map();     // userId -> Map(pcId -> { name, state, at, queue })
  const screens = new Map();    // requestId -> { userId, accountId, pcId, accountName, exp, waiters: [fn], image }
  const listeners = [];         // (userId, event) => void
  const hourly = new Map();     // `${userId}|${hourIso}|${accountId}` -> { userId, hour, accountId, name, pc, seconds, drops, honey }
  let cmdSeq = 0;

  function pcsOf(userId) {
    if (!agents.has(userId)) agents.set(userId, new Map());
    return agents.get(userId);
  }

  // ---------- reports from the app ----------
  function sync(userId, body) {
    const t = now().getTime();
    const pc = (body && body.pc) || {};
    const pcId = clip(pc.id, 64) || 'default';
    const pcs = pcsOf(userId);
    if (!pcs.has(pcId) && pcs.size >= MAX_PCS) {
      // forget the PC that has been silent the longest
      const oldest = [...pcs.entries()].sort((a, b) => a[1].at - b[1].at)[0];
      pcs.delete(oldest[0]);
    }
    const agent = pcs.get(pcId) || { queue: [] };
    agent.kind = pc.kind === 'phone' ? 'phone' : 'pc';   // AutoRejoin Mobile reports kind: 'phone'
    const state = sanitizeState(body || {});
    sample(userId, agent, state, clip(pc.name, 40) || 'PC', t);
    agent.state = state;
    agent.name = clip(pc.name, 40) || agent.name || 'PC';
    agent.at = t;
    const commands = agent.queue.filter((c) => t - c.at <= COMMAND_TTL).map(({ id, cmd, arg }) => ({ id, cmd, arg }));
    agent.queue = [];
    pcs.set(pcId, agent);
    return commands;
  }

  // Seconds in game and honey gained since the previous report from this PC.
  function sample(userId, agent, state, pcName, t) {
    const prev = agent.state, prevAt = agent.at;
    if (!prev || !prevAt) return;
    const gap = (t - prevAt) / 1000;
    if (gap <= 0 || gap > SAMPLE_GAP) return;
    for (const a of state.accounts) {
      const before = prev.accounts.find((x) => x.id === a.id);
      if (!before) continue;
      const secs = inGameState(a.state) && inGameState(before.state) && state.running ? Math.round(gap) : 0;
      // Honey only goes up while farming; a drop means it was spent — skip it, do not count it as loss.
      const gain = a.honey != null && before.honey != null && a.honey > before.honey && a.honey - before.honey < 5e9 ? a.honey - before.honey : 0;
      if (secs || gain) bump(userId, t, a, pcName, { seconds: secs, honey: gain });
    }
  }

  function bump(userId, t, account, pcName, add) {
    const hour = hourStart(t);
    const key = `${userId}|${hour.toISOString()}|${account.id}`;
    const row = hourly.get(key) || { userId, hour, accountId: account.id, name: account.name, pc: pcName, seconds: 0, drops: 0, honey: 0 };
    row.seconds += add.seconds || 0;
    row.drops += add.drops || 0;
    row.honey += add.honey || 0;
    row.name = account.name || row.name;
    row.pc = pcName || row.pc;
    row.dirty = true;
    hourly.set(key, row);
  }

  async function flush() {
    const rows = [...hourly.values()].filter((r) => r.dirty);
    for (const r of rows) {
      r.dirty = false;
      try {
        await pool.query(
          `INSERT INTO farm_hourly (user_id, hour, account_id, account_name, pc_name, seconds, drops, honey) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
           ON CONFLICT (user_id, hour, account_id) DO UPDATE SET seconds = $6, drops = $7, honey = $8, account_name = $4, pc_name = $5`,
          [r.userId, r.hour, r.accountId, r.name, r.pc, r.seconds, r.drops, r.honey]);
      } catch (e) { r.dirty = true; console.error('farm: flush failed:', e.message); break; }
    }
    // keep only the current and previous hour in memory
    const keep = hourStart(now().getTime()).getTime() - 3600000;
    for (const [k, r] of hourly) if (!r.dirty && r.hour.getTime() < keep) hourly.delete(k);
  }

  // Rows for the current hours live in memory first; on the first touch after a restart, continue from the DB.
  async function load(userId) {
    const since = new Date(hourStart(now().getTime()).getTime() - 3600000);
    const { rows } = await pool.query('SELECT hour, account_id, account_name, pc_name, seconds, drops, honey FROM farm_hourly WHERE user_id = $1 AND hour >= $2', [userId, since]);
    for (const r of rows) {
      const hour = new Date(r.hour);
      const key = `${userId}|${hour.toISOString()}|${r.account_id}`;
      if (!hourly.has(key)) hourly.set(key, { userId, hour, accountId: r.account_id, name: r.account_name, pc: r.pc_name, seconds: Number(r.seconds), drops: Number(r.drops), honey: Number(r.honey) });
    }
  }
  const loaded = new Set();
  async function ensureLoaded(userId) {
    if (loaded.has(userId)) return;
    loaded.add(userId);
    try { await load(userId); } catch (e) { loaded.delete(userId); }
  }

  // ---------- views ----------
  // kind: 'pc' / 'phone' — only devices of that kind; anything else — all of them.
  const ofKind = (kind) => (p) => !(kind === 'pc' || kind === 'phone') || p.kind === kind;
  function view(userId, kind) {
    const t = now().getTime();
    const pcs = [...pcsOf(userId).entries()].map(([id, a]) => ({
      id, name: a.name || 'PC', kind: a.kind || 'pc', online: !!(a.at && t - a.at <= STATE_STALE), lastSeen: a.at ? new Date(a.at).toISOString() : null,
      state: a.state || null
    })).filter(ofKind(kind)).sort((a, b) => (b.online - a.online) || a.name.localeCompare(b.name));
    return pcs;
  }
  // Every account of every online device, with its device.
  function accounts(userId, onlyOnline = true, kind) {
    const out = [];
    for (const pc of view(userId, kind)) {
      if (onlyOnline && !pc.online) continue;
      for (const a of (pc.state ? pc.state.accounts : [])) out.push({ ...a, pcId: pc.id, pcName: pc.name, kind: pc.kind, running: pc.state.running, phase: pc.state.phase });
    }
    return out;
  }
  function findAccount(userId, accountId) {
    return accounts(userId).find((a) => a.id === accountId) || null;
  }

  // ---------- commands ----------
  // pcId: one device; null: start/stop/rejoin-all go to every online device (of `kind`, when given);
  // rejoin/screen go to the device with that account.
  function enqueue(userId, cmd, arg, pcId, kind) {
    const pcs = pcsOf(userId);
    let targets;
    if (pcId) targets = [pcId];
    else if ((cmd === 'rejoin' || cmd === 'screen') && arg) {
      const accountId = cmd === 'screen' ? String(arg).split(':').slice(1).join(':') : arg;
      const acc = findAccount(userId, accountId);
      targets = acc ? [acc.pcId] : [];
    } else targets = view(userId, kind).filter((p) => p.online).map((p) => p.id);
    if (!targets.length && pcs.size === 1 && view(userId, kind).length === 1) targets = [...pcs.keys()];
    for (const id of targets) {
      const agent = pcs.get(id) || { queue: [] };
      agent.queue = agent.queue.slice(-9);
      agent.queue.push({ id: ++cmdSeq, cmd, arg: arg || '', at: now().getTime() });
      pcs.set(id, agent);
    }
    return targets.length;
  }

  // ---------- screenshots ----------
  function requestScreen(userId, accountId, waiter) {
    const acc = findAccount(userId, accountId);
    if (!acc) return null;
    const t = now().getTime();
    for (const [k, s] of screens) if (s.exp < t) screens.delete(k);
    const id = crypto.randomBytes(6).toString('hex');
    screens.set(id, { userId, accountId, pcId: acc.pcId, accountName: acc.name, pcName: acc.pcName, exp: t + SCREEN_TTL, waiters: waiter ? [waiter] : [], image: null });
    enqueue(userId, 'screen', `${id}:${accountId}`, acc.pcId);
    return { id, accountName: acc.name, pcName: acc.pcName };
  }
  function screenInfo(userId, id) {
    const s = screens.get(id);
    if (!s || s.userId !== userId) return null;
    return { accountName: s.accountName, pcName: s.pcName, ready: !!s.image, error: s.error || null, image: s.image };
  }
  // The app uploaded the picture (or an error text when the window could not be captured).
  function deliverScreen(userId, id, image, error) {
    const s = screens.get(id);
    if (!s || s.userId !== userId) return false;
    s.image = image || null;
    s.error = error ? clip(error, 200) : null;
    s.exp = now().getTime() + SCREEN_TTL;
    for (const w of s.waiters) Promise.resolve().then(() => w(s)).catch((e) => console.error('farm: screen delivery failed:', e.message));
    s.waiters = [];
    return true;
  }

  // ---------- events ----------
  function onEvent(fn) { listeners.push(fn); }
  async function event(userId, body) {
    const t = now().getTime();
    const e = {
      kind: ['drop', 'back', 'error', 'stuck', 'alert', 'info'].includes(body.kind) ? body.kind : 'info',
      text: clip(body.text, 1500), account: clip(body.account, 40) || null, accountId: clip(body.accountId, 64) || null,
      pcName: clip(body.pcName, 40) || null, mention: !!body.mention, image: Buffer.isBuffer(body.image) ? body.image : null, at: new Date(t)
    };
    if (e.kind === 'drop' && e.accountId) bump(userId, t, { id: e.accountId, name: e.account }, e.pcName, { drops: 1 });
    try {
      await pool.query('INSERT INTO farm_events (user_id, at, kind, account_name, pc_name, text) VALUES ($1, $2, $3, $4, $5, $6)', [userId, e.at, e.kind, e.account, e.pcName, e.text]);
    } catch (err) { console.error('farm: event not saved:', err.message); }
    for (const fn of listeners) Promise.resolve().then(() => fn(userId, e)).catch((err) => console.error('farm: listener failed:', err.message));
    return e;
  }

  // ---------- analytics ----------
  async function analytics(userId, days = 7) {
    await ensureLoaded(userId);
    await flush();
    const since = new Date(now().getTime() - days * 86400000);
    const { rows } = await pool.query(
      'SELECT hour, account_id, account_name, pc_name, seconds, drops, honey FROM farm_hourly WHERE user_id = $1 AND hour >= $2 ORDER BY hour ASC', [userId, since]);
    const ev = await pool.query(
      "SELECT at, kind, account_name, pc_name, text FROM farm_events WHERE user_id = $1 AND at >= $2 AND kind IN ('drop', 'stuck', 'error', 'alert') ORDER BY at DESC LIMIT 60", [userId, since]);
    const accountsMap = new Map();
    const dayKey = (d) => new Date(d).toISOString().slice(0, 10);
    for (const r of rows) {
      const a = accountsMap.get(r.account_id) || { id: r.account_id, name: r.account_name, pc: r.pc_name, seconds: 0, drops: 0, honey: 0, days: {}, hours: {} };
      a.name = r.account_name || a.name; a.pc = r.pc_name || a.pc;
      a.seconds += Number(r.seconds); a.drops += Number(r.drops); a.honey += Number(r.honey);
      const dk = dayKey(r.hour);
      const d = a.days[dk] || { seconds: 0, drops: 0, honey: 0 };
      d.seconds += Number(r.seconds); d.drops += Number(r.drops); d.honey += Number(r.honey);
      a.days[dk] = d;
      a.hours[new Date(r.hour).toISOString()] = { seconds: Number(r.seconds), drops: Number(r.drops), honey: Number(r.honey) };
      accountsMap.set(r.account_id, a);
    }
    const dayList = [];
    for (let i = days - 1; i >= 0; i--) dayList.push(dayKey(now().getTime() - i * 86400000));
    return {
      days: dayList,
      accounts: [...accountsMap.values()].sort((a, b) => b.seconds - a.seconds),
      events: ev.rows.map((r) => ({ at: new Date(r.at).toISOString(), kind: r.kind, account: r.account_name, pc: r.pc_name, text: r.text }))
    };
  }

  // Last 24 h per account — the daily report (Telegram, and the app posts it to the Discord webhook).
  async function report(userId) {
    await ensureLoaded(userId);
    await flush();
    const since = new Date(now().getTime() - 86400000);
    const { rows } = await pool.query(
      `SELECT account_id, MAX(account_name) AS name, SUM(seconds) AS seconds, SUM(drops) AS drops, SUM(honey) AS honey
         FROM farm_hourly WHERE user_id = $1 AND hour >= $2 GROUP BY account_id ORDER BY SUM(seconds) DESC`, [userId, since]);
    const causes = await pool.query(
      "SELECT text FROM farm_events WHERE user_id = $1 AND at >= $2 AND kind = 'drop'", [userId, since]);
    const causeCount = {};
    for (const r of causes.rows) {
      const m = String(r.text || '').match(/(?:вылетел|dropped):?\s*(.+)$/i);
      const c = clip((m ? m[1] : r.text || '?').replace(/[*_`~|]/g, '').trim(), 60);
      causeCount[c] = (causeCount[c] || 0) + 1;
    }
    return {
      accounts: rows.map((r) => ({ id: r.account_id, name: r.name, seconds: Number(r.seconds), drops: Number(r.drops), honey: Number(r.honey) })),
      causes: Object.entries(causeCount).sort((a, b) => b[1] - a[1]).slice(0, 5).map(([text, n]) => ({ text, n }))
    };
  }

  function forget(userId) { agents.delete(userId); }

  return { sync, view, accounts, findAccount, enqueue, requestScreen, screenInfo, deliverScreen, onEvent, event, analytics, report, flush, forget, ensureLoaded, STATE_STALE };
}

module.exports = { createFarm, FARM_SCHEMA };
