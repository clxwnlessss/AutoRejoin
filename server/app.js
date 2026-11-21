'use strict';
// AutoRejoin Server by clxwnlesss — аккаунты и топ АФК-стендеров за месяц.
// База закрытая: её видит только этот сервер. Пароли хранятся как scrypt-хеш с солью,
// токены входа — как sha256 (сам токен знает только программа пользователя).
const express = require('express');
const crypto = require('crypto');
const { createDiscord, DISCORD_SCHEMA } = require('./discord');

const HEARTBEAT_MAX_GAP = 150; // сек: дольше между пульсами — промежуток не засчитывается (ПК спал, интернет пропал)
const ONLINE_WINDOW = 180;     // сек: пульс свежее — человек «сейчас стоит»
const NICK_RE = /^[A-Za-z0-9_]{3,20}$/;

const SCHEMA = [
  `CREATE TABLE IF NOT EXISTS users (
     id SERIAL PRIMARY KEY,
     nickname TEXT NOT NULL,
     nickname_lc TEXT NOT NULL UNIQUE,
     pass_hash TEXT NOT NULL,
     pass_salt TEXT NOT NULL,
     created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
     last_beat TIMESTAMPTZ,
     last_in_game INTEGER NOT NULL DEFAULT 0
   )`,
  `CREATE TABLE IF NOT EXISTS sessions (
     token_hash TEXT PRIMARY KEY,
     user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
     created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
   )`,
  `CREATE TABLE IF NOT EXISTS afk_monthly (
     user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
     month TEXT NOT NULL,
     seconds BIGINT NOT NULL DEFAULT 0,
     account_seconds BIGINT NOT NULL DEFAULT 0,
     PRIMARY KEY (user_id, month)
   )`
];

function monthKey(date) {
  return date.toISOString().slice(0, 7); // YYYY-MM по UTC
}

function sha256(text) {
  return crypto.createHash('sha256').update(text).digest('hex');
}

function hashPassword(password, salt) {
  return crypto.scryptSync(password, salt, 64).toString('hex');
}

// Простое ограничение частоты по IP: регистрация и вход — не чаще 10 раз за 10 минут.
function rateLimiter(limit, windowMs) {
  const hits = new Map();
  return (req, res, next) => {
    const now = Date.now();
    const key = req.ip || 'unknown';
    let entry = hits.get(key);
    if (!entry || entry.reset < now) {
      entry = { count: 0, reset: now + windowMs };
      hits.set(key, entry);
    }
    entry.count++;
    if (hits.size > 10000) {
      for (const [k, v] of hits) if (v.reset < now) hits.delete(k);
    }
    if (entry.count > limit) return res.status(429).json({ error: 'Слишком много попыток. Подожди 10 минут.' });
    next();
  };
}

async function initDb(pool) {
  for (const sql of SCHEMA.concat(DISCORD_SCHEMA)) await pool.query(sql);
}

function createApp(pool, options = {}) {
  const now = options.now || (() => new Date());
  const app = express();
  app.set('trust proxy', 1);
  app.disable('x-powered-by');
  const discord = createDiscord(pool, { now, ...(options.discord || {}) });
  app.locals.discord = discord;
  // Discord interactions need the raw body (signature check), so this route goes before the JSON parser.
  app.post('/discord/interactions', express.raw({ type: '*/*', limit: '64kb' }), discord.interactions);
  app.use(express.json({ limit: '32kb' }));

  // The website (React, on GitHub Pages) calls this API from the browser. Auth is a Bearer token, no cookies,
  // so allowing these origins does not expose anything to other sites.
  const allowedOrigins = new Set(['https://clxwnlessss.github.io', 'http://localhost:5173', 'http://127.0.0.1:5173', ...(options.origins || [])]);
  app.use((req, res, next) => {
    const origin = req.get('origin');
    if (origin && allowedOrigins.has(origin)) {
      res.set('Access-Control-Allow-Origin', origin);
      res.set('Vary', 'Origin');
      res.set('Access-Control-Allow-Headers', 'authorization, content-type');
      res.set('Access-Control-Allow-Methods', 'GET, POST, DELETE, OPTIONS');
      res.set('Access-Control-Max-Age', '86400');
    }
    if (req.method === 'OPTIONS') return res.status(204).end();
    next();
  });

  const authLimit = rateLimiter(options.authLimit || 10, 10 * 60 * 1000);

  async function auth(req, res, next) {
    try {
      const header = req.get('authorization') || '';
      const token = header.startsWith('Bearer ') ? header.slice(7).trim() : '';
      if (!token) return res.status(401).json({ error: 'Нужно войти в аккаунт.' });
      const { rows } = await pool.query(
        'SELECT u.id, u.nickname, u.last_beat FROM sessions s JOIN users u ON u.id = s.user_id WHERE s.token_hash = $1',
        [sha256(token)]
      );
      if (!rows.length) return res.status(401).json({ error: 'Сессия устарела — войди заново.' });
      req.user = rows[0];
      req.tokenHash = sha256(token);
      next();
    } catch (e) { next(e); }
  }

  async function issueToken(userId) {
    const token = crypto.randomBytes(32).toString('hex');
    await pool.query('INSERT INTO sessions (token_hash, user_id) VALUES ($1, $2)', [sha256(token), userId]);
    return token;
  }

  async function stats(userId, month) {
    const mine = await pool.query('SELECT seconds FROM afk_monthly WHERE user_id = $1 AND month = $2', [userId, month]);
    const seconds = mine.rows.length ? Number(mine.rows[0].seconds) : 0;
    const above = await pool.query('SELECT COUNT(*) AS c FROM afk_monthly WHERE month = $1 AND seconds > $2', [month, seconds]);
    return { seconds, rank: seconds > 0 ? Number(above.rows[0].c) + 1 : null };
  }

  app.get('/', (req, res) => res.json({ name: 'AutoRejoin Server', owner: 'clxwnlesss', ok: true }));
  app.get('/health', (req, res) => res.json({ ok: true }));

  app.post('/api/register', authLimit, async (req, res, next) => {
    try {
      const nickname = String((req.body && req.body.nickname) || '').trim();
      const password = String((req.body && req.body.password) || '');
      if (!NICK_RE.test(nickname)) return res.status(400).json({ error: 'Ник: 3–20 символов, латиница, цифры и _.' });
      if (password.length < 6 || password.length > 100) return res.status(400).json({ error: 'Пароль: от 6 до 100 символов.' });
      const salt = crypto.randomBytes(16).toString('hex');
      let id;
      try {
        const { rows } = await pool.query(
          'INSERT INTO users (nickname, nickname_lc, pass_hash, pass_salt) VALUES ($1, $2, $3, $4) RETURNING id',
          [nickname, nickname.toLowerCase(), hashPassword(password, salt), salt]
        );
        id = rows[0].id;
      } catch (e) {
        if (e.code === '23505' || /unique|duplicate/i.test(e.message)) return res.status(409).json({ error: 'Такой ник уже занят.' });
        throw e;
      }
      res.json({ token: await issueToken(id), nickname });
    } catch (e) { next(e); }
  });

  app.post('/api/login', authLimit, async (req, res, next) => {
    try {
      const nickname = String((req.body && req.body.nickname) || '').trim();
      const password = String((req.body && req.body.password) || '');
      const { rows } = await pool.query('SELECT id, nickname, pass_hash, pass_salt FROM users WHERE nickname_lc = $1', [nickname.toLowerCase()]);
      const user = rows[0];
      const ok = user && crypto.timingSafeEqual(Buffer.from(hashPassword(password, user.pass_salt), 'hex'), Buffer.from(user.pass_hash, 'hex'));
      if (!ok) return res.status(401).json({ error: 'Неверный ник или пароль.' });
      res.json({ token: await issueToken(user.id), nickname: user.nickname });
    } catch (e) { next(e); }
  });

  app.post('/api/logout', auth, async (req, res, next) => {
    try {
      sessionCache.delete(req.tokenHash);
      await pool.query('DELETE FROM sessions WHERE token_hash = $1', [req.tokenHash]);
      res.json({ ok: true });
    } catch (e) { next(e); }
  });

  // Пульс раз в минуту: сколько аккаунтов сейчас в игре. Время считает сервер — по своим часам между пульсами,
  // поэтому программа не может «прислать» лишние часы.
  app.post('/api/heartbeat', auth, async (req, res, next) => {
    try {
      const inGame = Math.max(0, Math.min(50, parseInt(req.body && req.body.inGame, 10) || 0));
      const at = now();
      const last = req.user.last_beat ? new Date(req.user.last_beat) : null;
      const gap = last ? (at - last) / 1000 : 0;
      const credit = gap > 0 && gap <= HEARTBEAT_MAX_GAP ? Math.round(gap) : 0;
      await pool.query('UPDATE users SET last_beat = $1, last_in_game = $2 WHERE id = $3', [at, inGame, req.user.id]);
      const month = monthKey(at);
      if (credit > 0 && inGame > 0) {
        await pool.query(
          `INSERT INTO afk_monthly (user_id, month, seconds, account_seconds) VALUES ($1, $2, $3, $4)
           ON CONFLICT (user_id, month) DO UPDATE SET seconds = afk_monthly.seconds + $3, account_seconds = afk_monthly.account_seconds + $4`,
          [req.user.id, month, credit, credit * inGame]
        );
      }
      res.json({ ok: true, credited: inGame > 0 ? credit : 0, ...(await stats(req.user.id, month)) });
    } catch (e) { next(e); }
  });

  app.get('/api/me', auth, async (req, res, next) => {
    try {
      const month = monthKey(now());
      res.json({ nickname: req.user.nickname, month, ...(await stats(req.user.id, month)) });
    } catch (e) { next(e); }
  });

  // Топ за месяц (по умолчанию текущий). Открытый: только ники и часы, ничего больше.
  app.get('/api/top', async (req, res, next) => {
    try {
      const month = /^\d{4}-\d{2}$/.test(String(req.query.month || '')) ? String(req.query.month) : monthKey(now());
      const limit = Math.max(1, Math.min(100, parseInt(req.query.limit, 10) || 50));
      const { rows } = await pool.query(
        `SELECT u.nickname, m.seconds, m.account_seconds, u.last_beat, u.last_in_game
           FROM afk_monthly m JOIN users u ON u.id = m.user_id
          WHERE m.month = $1 AND m.seconds > 0
          ORDER BY m.seconds DESC, u.id ASC
          LIMIT $2`,
        [month, limit]
      );
      const at = now();
      res.json({
        month,
        top: rows.map((r, i) => ({
          rank: i + 1,
          nickname: r.nickname,
          seconds: Number(r.seconds),
          accountSeconds: Number(r.account_seconds),
          online: !!(r.last_beat && (at - new Date(r.last_beat)) / 1000 <= ONLINE_WINDOW && r.last_in_game > 0)
        }))
      });
    } catch (e) { next(e); }
  });

  // Public numbers for the website: registered users, who is AFK right now, hours this month.
  let statsCache = null;
  app.get('/api/stats', async (req, res, next) => {
    try {
      if (statsCache && statsCache.exp > Date.now()) return res.json(statsCache.data);
      const at = now();
      const users = await pool.query('SELECT COUNT(*) AS c FROM users');
      const online = await pool.query('SELECT COUNT(*) AS c, COALESCE(SUM(last_in_game), 0) AS a FROM users WHERE last_beat > $1 AND last_in_game > 0',
        [new Date(at.getTime() - ONLINE_WINDOW * 1000)]);
      const month = await pool.query('SELECT COALESCE(SUM(seconds), 0) AS s, COALESCE(SUM(account_seconds), 0) AS a FROM afk_monthly WHERE month = $1', [monthKey(at)]);
      const data = { users: Number(users.rows[0].c), onlineUsers: Number(online.rows[0].c), onlineAccounts: Number(online.rows[0].a),
        monthHours: Math.round(Number(month.rows[0].s) / 3600), monthAccountHours: Math.round(Number(month.rows[0].a) / 3600) };
      statsCache = { data, exp: Date.now() + 60 * 1000 };
      res.json(data);
    } catch (e) { next(e); }
  });

  // Website dashboard: accounts the app reported last (needs the Discord bot linked — see discord.webState).
  app.get('/api/agent/state', auth, async (req, res, next) => {
    try { res.json(await discord.webState(req.user.id)); } catch (e) { next(e); }
  });
  // Website dashboard buttons: queued for the app like the Discord buttons, picked up on its next sync (≤10 s).
  const WEB_COMMANDS = new Set(['start', 'stop', 'rejoin-all', 'rejoin']);
  app.post('/api/agent/command', auth, async (req, res, next) => {
    try {
      const cmd = String((req.body && req.body.cmd) || '');
      const arg = String((req.body && req.body.arg) || '').slice(0, 64);
      if (!WEB_COMMANDS.has(cmd) || (cmd === 'rejoin' && !arg)) return res.status(400).json({ error: 'Неизвестная команда.' });
      if (!(await discord.linkedForUser(req.user.id))) return res.status(409).json({ error: 'Сначала привяжи Discord-бота в программе: Настройки → Привязать бота.' });
      discord.enqueue(req.user.id, cmd, arg);
      res.json({ ok: true });
    } catch (e) { next(e); }
  });

  // ---------- Discord bot: link code, link state, agent sync ----------
  const inviteUrl = () => (options.discord && options.discord.appId)
    ? `https://discord.com/oauth2/authorize?client_id=${options.discord.appId}&scope=applications.commands%20bot&permissions=1099780156486`
    : null;

  app.post('/api/discord/code', auth, async (req, res, next) => {
    try { res.json({ ...discord.newCode(req.user.id), invite: inviteUrl() }); } catch (e) { next(e); }
  });
  app.get('/api/discord/link', auth, async (req, res, next) => {
    try { res.json({ ...(await discord.linkInfo(req.user.id)), invite: inviteUrl() }); } catch (e) { next(e); }
  });
  app.delete('/api/discord/link', auth, async (req, res, next) => {
    try { await discord.unlinkUser(req.user.id); res.json({ ok: true }); } catch (e) { next(e); }
  });
  // The app reports its accounts and receives queued commands. Session lookups are cached so that
  // frequent syncs do not hit the database.
  const sessionCache = new Map(); // tokenHash -> { user, exp }
  async function cachedAuth(req, res, next) {
    const header = req.get('authorization') || '';
    const token = header.startsWith('Bearer ') ? header.slice(7).trim() : '';
    const hit = token && sessionCache.get(sha256(token));
    if (hit && hit.exp > Date.now()) { req.user = hit.user; return next(); }
    return auth(req, res, () => {
      if (sessionCache.size > 5000) sessionCache.clear();
      sessionCache.set(req.tokenHash, { user: { id: req.user.id, nickname: req.user.nickname }, exp: Date.now() + 10 * 60 * 1000 });
      next();
    });
  }
  app.post('/api/agent/sync', cachedAuth, async (req, res, next) => {
    try { res.json(await discord.sync(req.user.id, req.body || {})); } catch (e) { next(e); }
  });

  app.use((req, res) => res.status(404).json({ error: 'Не найдено.' }));
  // eslint-disable-next-line no-unused-vars
  app.use((err, req, res, next) => {
    console.error(err);
    res.status(500).json({ error: 'Ошибка сервера.' });
  });
  return app;
}

module.exports = { createApp, initDb, monthKey };
