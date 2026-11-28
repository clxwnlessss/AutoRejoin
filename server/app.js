'use strict';
// AutoRejoin Server by clxwnlesss — аккаунты и топ АФК-стендеров за месяц.
// База закрытая: её видит только этот сервер. Пароли хранятся как scrypt-хеш с солью,
// токены входа — как sha256 (сам токен знает только программа пользователя).
const express = require('express');
const crypto = require('crypto');
const { createDiscord, DISCORD_SCHEMA } = require('./discord');
const { createTelegram, TELEGRAM_SCHEMA } = require('./telegram');
const { mountEmail, EMAIL_SCHEMA } = require('./email');
const { createMailer } = require('./mailer');

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

// AutoRejoin Mobile: accounts and settings backup (one per account).
const MOBILE_SCHEMA = [
  `CREATE TABLE IF NOT EXISTS mobile_backups (
     user_id INTEGER PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
     data TEXT NOT NULL,
     updated_at TIMESTAMPTZ NOT NULL
   )`
];

async function initDb(pool) {
  for (const sql of SCHEMA.concat(DISCORD_SCHEMA, TELEGRAM_SCHEMA, EMAIL_SCHEMA, MOBILE_SCHEMA)) await pool.query(sql);
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
  const farm = discord.farm;
  const telegram = createTelegram(pool, { now, farm, discord, ...(options.telegram || {}) });
  app.locals.telegram = telegram;
  app.locals.farm = farm;
  const flushTimer = setInterval(() => farm.flush().catch(() => {}), 60 * 1000);
  if (flushTimer.unref) flushTimer.unref();
  // A link (Discord or Telegram) is what lets the app send account names and receive commands.
  async function anyLink(userId) {
    return (await discord.linkedForUser(userId)) || (await telegram.linkedForUser(userId));
  }

  // Bigger bodies (screenshots, drop storyboards) — these routes go before the 32 KB JSON parser.
  const bigJson = express.json({ limit: '4mb' });
  const image = (b64) => {
    if (!b64) return null;
    const buf = Buffer.from(String(b64).replace(/^data:image\/\w+;base64,/, ''), 'base64');
    // JPEG or PNG only, and not empty
    return buf.length > 100 && ((buf[0] === 0xFF && buf[1] === 0xD8) || (buf[0] === 0x89 && buf[1] === 0x50)) ? buf : null;
  };
  // The app uploads the screenshot it was asked for (or the reason it could not take it).
  app.post('/api/agent/screen', bigJson, cachedAuth, async (req, res, next) => {
    try {
      const b = req.body || {};
      const ok = farm.deliverScreen(req.user.id, String(b.id || ''), image(b.image), b.error ? String(b.error) : (b.image ? null : 'нет картинки'));
      res.status(ok ? 200 : 404).json({ ok });
    } catch (e) { next(e); }
  });
  // Instant events from the app (drop, back, error, stuck, alert) → Telegram + analytics.
  app.post('/api/agent/event', bigJson, cachedAuth, async (req, res, next) => {
    try {
      if (!(await anyLink(req.user.id))) return res.json({ ok: false });
      const b = req.body || {};
      await farm.event(req.user.id, { ...b, image: image(b.image) });
      res.json({ ok: true });
    } catch (e) { next(e); }
  });
  app.post('/telegram/:secret', express.json({ limit: '1mb' }), telegram.webhook);
  app.use(express.json({ limit: '32kb' }));

  // The website (React, on GitHub Pages) calls this API from the browser. Auth is a Bearer token, no cookies,
  // so allowing these origins does not expose anything to other sites.
  const allowedOrigins = new Set(['https://clxwnlessss.github.io', 'https://autorejoin.is-a.bot', 'http://localhost:5173', 'http://127.0.0.1:5173', ...(options.origins || [])]);
  app.use((req, res, next) => {
    const origin = req.get('origin');
    if (origin && allowedOrigins.has(origin)) {
      res.set('Access-Control-Allow-Origin', origin);
      res.set('Vary', 'Origin');
      res.set('Access-Control-Allow-Headers', 'authorization, content-type');
      res.set('Access-Control-Allow-Methods', 'GET, POST, PUT, DELETE, OPTIONS');
      res.set('Access-Control-Max-Age', '86400');
    }
    if (req.method === 'OPTIONS') return res.status(204).end();
    next();
  });

  const authLimit = rateLimiter(options.authLimit || 10, 10 * 60 * 1000);
  // Admins by nickname (unique, case-insensitive). ADMIN_NICKS="a,b" on Render overrides the default owner.
  const adminNicks = new Set(String(options.adminNicks || process.env.ADMIN_NICKS || 'clxwnless')
    .split(',').map((s) => s.trim().toLowerCase()).filter(Boolean));
  const isAdmin = (user) => !!user && adminNicks.has(String(user.nickname).toLowerCase());
  function adminOnly(req, res, next) {
    if (!isAdmin(req.user)) return res.status(403).json({ error: 'Только для администратора.' });
    next();
  }
  const checkPassword = (password) => (password.length < 6 || password.length > 100) ? 'Пароль: от 6 до 100 символов.' : null;
  async function setPassword(userId, password) {
    const salt = crypto.randomBytes(16).toString('hex');
    await pool.query('UPDATE users SET pass_hash = $1, pass_salt = $2 WHERE id = $3', [hashPassword(password, salt), salt, userId]);
  }
  async function dropSessions(userId, keepHash) {
    for (const [k, v] of sessionCache) if (v.user.id === userId && k !== keepHash) sessionCache.delete(k);
    await pool.query('DELETE FROM sessions WHERE user_id = $1 AND token_hash <> $2', [userId, keepHash || '']);
  }

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

  // Почта: регистрация с кодом, привязка почты и сброс забытого пароля по коду из письма.
  const mailer = options.mailer || createMailer(options.mail || {});
  const emailLimit = rateLimiter(options.authLimit || 20, 10 * 60 * 1000);
  mountEmail(app, pool, { now, auth, authLimit: emailLimit, mailer, checkPassword, setPassword, dropSessions, issueToken,
    nickRe: NICK_RE, hashPassword, log: (...a) => console.log(...a) });

  // Регистрация без почты — только пока отправка писем не настроена (и для тестов).
  // Когда почта работает, новые аккаунты создаются через /api/register/start + /api/register/confirm.
  app.post('/api/register', authLimit, async (req, res, next) => {
    try {
      if (mailer.configured && !options.allowPlainRegister) {
        return res.status(400).json({ error: 'Регистрация теперь с подтверждением почты — обнови AutoRejoin или зарегистрируйся на сайте.' });
      }
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
      // Вход по нику или по привязанной почте.
      const nickname = String((req.body && req.body.nickname) || '').trim();
      const password = String((req.body && req.body.password) || '');
      const { rows } = nickname.includes('@')
        ? await pool.query('SELECT u.id, u.nickname, u.pass_hash, u.pass_salt FROM user_emails e JOIN users u ON u.id = e.user_id WHERE e.email_lc = $1', [nickname.toLowerCase()])
        : await pool.query('SELECT id, nickname, pass_hash, pass_salt FROM users WHERE nickname_lc = $1', [nickname.toLowerCase()]);
      const user = rows[0];
      const ok = user && crypto.timingSafeEqual(Buffer.from(hashPassword(password, user.pass_salt), 'hex'), Buffer.from(user.pass_hash, 'hex'));
      if (!ok) return res.status(401).json({ error: 'Неверный ник/почта или пароль.' });
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
      const em = await pool.query('SELECT email FROM user_emails WHERE user_id = $1', [req.user.id]);
      res.json({ nickname: req.user.nickname, month, admin: isAdmin(req.user), email: em.rows.length ? em.rows[0].email : null, ...(await stats(req.user.id, month)) });
    } catch (e) { next(e); }
  });

  // Смена пароля. Достаточно быть в аккаунте (сессия в программе или на сайте) — так можно сменить и забытый пароль.
  // Если старый пароль прислали, он должен подойти. Все остальные сессии, кроме текущей, закрываются.
  app.post('/api/password', authLimit, auth, async (req, res, next) => {
    try {
      const password = String((req.body && req.body.newPassword) || '');
      const bad = checkPassword(password);
      if (bad) return res.status(400).json({ error: bad });
      if (req.body && req.body.oldPassword) {
        const { rows } = await pool.query('SELECT pass_hash, pass_salt FROM users WHERE id = $1', [req.user.id]);
        const ok = crypto.timingSafeEqual(Buffer.from(hashPassword(String(req.body.oldPassword), rows[0].pass_salt), 'hex'), Buffer.from(rows[0].pass_hash, 'hex'));
        if (!ok) return res.status(401).json({ error: 'Старый пароль не подходит.' });
      }
      await setPassword(req.user.id, password);
      if (req.body && req.body.signOutOthers) await dropSessions(req.user.id, req.tokenHash);
      res.json({ ok: true });
    } catch (e) { next(e); }
  });

  // ---------- админка ----------
  app.get('/api/admin/users', auth, adminOnly, async (req, res, next) => {
    try {
      const month = monthKey(now());
      const q = String(req.query.q || '').trim().toLowerCase().slice(0, 20);
      const { rows } = await pool.query(
        `SELECT u.id, u.nickname, u.created_at, u.last_beat, u.last_in_game, COALESCE(m.seconds, 0) AS seconds, COALESCE(s.c, 0) AS sessions, l.discord_name AS discord
           FROM users u LEFT JOIN afk_monthly m ON m.user_id = u.id AND m.month = $1
           LEFT JOIN (SELECT user_id, COUNT(*) AS c FROM sessions GROUP BY user_id) s ON s.user_id = u.id
           LEFT JOIN discord_links l ON l.user_id = u.id
          WHERE ($2 = '' OR u.nickname_lc LIKE $3)
          ORDER BY u.id ASC LIMIT 500`,
        [month, q, `%${q}%`]
      );
      const at = now();
      res.json({ month, users: rows.map((r) => ({
        id: r.id, nickname: r.nickname, createdAt: r.created_at, lastBeat: r.last_beat, seconds: Number(r.seconds),
        sessions: Number(r.sessions), discord: r.discord || null, admin: adminNicks.has(r.nickname.toLowerCase()),
        online: !!(r.last_beat && (at - new Date(r.last_beat)) / 1000 <= ONLINE_WINDOW && r.last_in_game > 0), inGame: r.last_in_game
      })) });
    } catch (e) { next(e); }
  });
  // Новый пароль пользователю (забыл пароль). signOut — закрыть все его сессии (программа тоже выйдет из аккаунта).
  app.post('/api/admin/users/:id/password', auth, adminOnly, async (req, res, next) => {
    try {
      const id = parseInt(req.params.id, 10);
      const password = String((req.body && req.body.newPassword) || '');
      const bad = checkPassword(password);
      if (bad) return res.status(400).json({ error: bad });
      const { rows } = await pool.query('SELECT id, nickname FROM users WHERE id = $1', [id]);
      if (!rows.length) return res.status(404).json({ error: 'Пользователь не найден.' });
      await setPassword(id, password);
      if (req.body && req.body.signOut) await dropSessions(id, id === req.user.id ? req.tokenHash : null);
      console.log(`admin ${req.user.nickname}: new password for ${rows[0].nickname}${req.body && req.body.signOut ? ' (signed out)' : ''}`);
      res.json({ ok: true, nickname: rows[0].nickname });
    } catch (e) { next(e); }
  });
  app.post('/api/admin/users/:id/signout', auth, adminOnly, async (req, res, next) => {
    try {
      const id = parseInt(req.params.id, 10);
      await dropSessions(id, id === req.user.id ? req.tokenHash : null);
      res.json({ ok: true });
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
    try {
      const st = await discord.webState(req.user.id);
      const tg = await telegram.linkedForUser(req.user.id);
      res.json({ ...st, linked: st.linked || !!tg, telegram: tg ? { name: tg.name || null } : null });
    } catch (e) { next(e); }
  });
  // Website: screenshot button → request id; the page polls until the picture is there.
  app.post('/api/agent/screen-request', auth, async (req, res, next) => {
    try {
      if (!(await anyLink(req.user.id))) return res.status(409).json({ error: 'Сначала привяжи Discord-бота в программе: Настройки → Привязать бота.' });
      const r = farm.requestScreen(req.user.id, String((req.body && req.body.accountId) || ''));
      if (!r) return res.status(404).json({ error: 'Этот аккаунт сейчас не на связи.' });
      setTimeout(() => {
        const info = farm.screenInfo(req.user.id, r.id);
        if (info && !info.ready && !info.error) farm.deliverScreen(req.user.id, r.id, null, 'программа не ответила за минуту');
      }, 60000).unref?.();
      res.json(r);
    } catch (e) { next(e); }
  });
  app.get('/api/agent/screen/:id', auth, async (req, res, next) => {
    try {
      const info = farm.screenInfo(req.user.id, String(req.params.id));
      if (!info) return res.status(404).json({ error: 'Не найдено.' });
      res.json({ ready: info.ready, error: info.error, accountName: info.accountName, pcName: info.pcName,
        image: info.image ? 'data:image/jpeg;base64,' + info.image.toString('base64') : null });
    } catch (e) { next(e); }
  });
  // Farm analytics for the website: time in game, drops and honey per account per day + recent drops.
  app.get('/api/farm/analytics', auth, async (req, res, next) => {
    try {
      const days = Math.max(1, Math.min(30, parseInt(req.query.days, 10) || 7));
      res.json(await farm.analytics(req.user.id, days));
    } catch (e) { next(e); }
  });
  // End of day: the app asks for the report — it posts it to the Discord webhook, the server sends it to Telegram.
  app.post('/api/agent/report', cachedAuth, async (req, res, next) => {
    try {
      const report = await farm.report(req.user.id);
      const sent = await telegram.sendReport(req.user.id).catch(() => false);
      res.json({ ...report, telegram: sent });
    } catch (e) { next(e); }
  });
  // AutoRejoin Mobile: accounts and settings backup, so a reinstall only needs signing in. One copy per account.
  app.get('/api/mobile/backup', auth, async (req, res, next) => {
    try {
      const { rows } = await pool.query('SELECT data, updated_at FROM mobile_backups WHERE user_id = $1', [req.user.id]);
      res.json(rows.length ? { data: rows[0].data, updatedAt: new Date(rows[0].updated_at).toISOString() } : { data: null });
    } catch (e) { next(e); }
  });
  app.put('/api/mobile/backup', auth, async (req, res, next) => {
    try {
      const data = String((req.body && req.body.data) || '');
      let parsed = null;
      try { parsed = JSON.parse(data); } catch { /* checked below */ }
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed) || data.length > 30000) return res.status(400).json({ error: 'Bad backup.' });
      await pool.query(
        `INSERT INTO mobile_backups (user_id, data, updated_at) VALUES ($1, $2, $3)
         ON CONFLICT (user_id) DO UPDATE SET data = $2, updated_at = $3`, [req.user.id, data, now()]);
      res.json({ ok: true });
    } catch (e) { next(e); }
  });
  app.delete('/api/telegram/link', auth, async (req, res, next) => {
    try { await telegram.unlinkUser(req.user.id); res.json({ ok: true }); } catch (e) { next(e); }
  });
  // Website dashboard buttons: queued for the app like the Discord buttons, picked up on its next sync (≤10 s).
  const WEB_COMMANDS = new Set(['start', 'stop', 'rejoin-all', 'rejoin']);
  app.post('/api/agent/command', auth, async (req, res, next) => {
    try {
      const cmd = String((req.body && req.body.cmd) || '');
      const arg = String((req.body && req.body.arg) || '').slice(0, 64);
      const pcId = req.body && req.body.pcId ? String(req.body.pcId).slice(0, 64) : null;
      if (!WEB_COMMANDS.has(cmd) || (cmd === 'rejoin' && !arg)) return res.status(400).json({ error: 'Неизвестная команда.' });
      if (!(await anyLink(req.user.id))) return res.status(409).json({ error: 'Сначала привяжи Discord-бота в программе: Настройки → Привязать бота.' });
      farm.enqueue(req.user.id, cmd, arg, pcId);
      res.json({ ok: true });
    } catch (e) { next(e); }
  });

  // ---------- Discord bot: link code, link state, agent sync ----------
  const inviteUrl = () => (options.discord && options.discord.appId)
    ? `https://discord.com/oauth2/authorize?client_id=${options.discord.appId}&scope=applications.commands%20bot&permissions=1099780156486`
    : null;

  app.post('/api/discord/code', auth, async (req, res, next) => {
    try { res.json({ ...discord.newCode(req.user.id), invite: inviteUrl(), telegram: telegram.username ? 'https://t.me/' + telegram.username : null }); } catch (e) { next(e); }
  });
  app.get('/api/discord/link', auth, async (req, res, next) => {
    try {
      const tg = await telegram.linkedForUser(req.user.id);
      res.json({ ...(await discord.linkInfo(req.user.id)), invite: inviteUrl(), telegram: tg ? { linked: true, name: tg.name || null } : { linked: false },
        telegramBot: telegram.username ? 'https://t.me/' + telegram.username : null });
    } catch (e) { next(e); }
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
    try {
      if (!(await anyLink(req.user.id))) return res.json({ linked: false, commands: [] });
      res.json({ linked: true, commands: farm.sync(req.user.id, req.body || {}) });
    } catch (e) { next(e); }
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
