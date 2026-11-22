'use strict';
// Email for accounts: link an email (confirmed with a 6-digit code) and reset a forgotten password by a code
// sent to that email. Codes are stored only as a hash, live 15 minutes and allow 5 tries.
const crypto = require('crypto');
const { codeEmail } = require('./mailer');

const EMAIL_SCHEMA = [
  `CREATE TABLE IF NOT EXISTS user_emails (
     user_id INTEGER PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
     email TEXT NOT NULL,
     email_lc TEXT NOT NULL UNIQUE,
     verified_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
   )`,
  `CREATE TABLE IF NOT EXISTS email_codes (
     id SERIAL PRIMARY KEY,
     user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
     purpose TEXT NOT NULL,
     email TEXT NOT NULL,
     code_hash TEXT NOT NULL,
     attempts INTEGER NOT NULL DEFAULT 0,
     created_at TIMESTAMPTZ NOT NULL,
     expires_at TIMESTAMPTZ NOT NULL
   )`
];

const EMAIL_RE = /^[^\s@]{1,64}@[^\s@]{1,190}\.[^\s@]{2,24}$/;
const CODE_TTL = 15 * 60 * 1000;
const RESEND_GAP = 60 * 1000;      // one code per minute
const MAX_PER_HOUR = 5;            // codes per user per hour
const MAX_TRIES = 5;

const normEmail = (s) => String(s || '').trim().slice(0, 254);
const sha = (s) => crypto.createHash('sha256').update(s).digest('hex');
const codeHash = (id, code) => sha(`${id}:${code}`);

function mountEmail(app, pool, h) {
  const { now, auth, authLimit, mailer, checkPassword, setPassword, dropSessions, issueToken, log } = h;

  async function sendCode(user, purpose, email) {
    const at = now();
    const recent = await pool.query('SELECT created_at FROM email_codes WHERE user_id = $1 AND created_at > $2 ORDER BY created_at DESC',
      [user.id, new Date(at.getTime() - 60 * 60 * 1000)]);
    if (recent.rows.length && at - new Date(recent.rows[0].created_at) < RESEND_GAP) return { wait: true };
    if (recent.rows.length >= MAX_PER_HOUR) return { wait: true };
    const code = String(crypto.randomInt(0, 1000000)).padStart(6, '0');
    await pool.query('DELETE FROM email_codes WHERE user_id = $1 AND purpose = $2', [user.id, purpose]);
    const { rows } = await pool.query(
      'INSERT INTO email_codes (user_id, purpose, email, code_hash, created_at, expires_at) VALUES ($1, $2, $3, $4, $5, $6) RETURNING id',
      [user.id, purpose, email, 'pending', at, new Date(at.getTime() + CODE_TTL)]);
    await pool.query('UPDATE email_codes SET code_hash = $1 WHERE id = $2', [codeHash(rows[0].id, code), rows[0].id]);
    const m = codeEmail(code, purpose, user.nickname);
    try {
      await mailer.send(email, m.subject, m.text, m.html);
    } catch (e) {
      await pool.query('DELETE FROM email_codes WHERE id = $1', [rows[0].id]);
      throw e;
    }
    return { sent: true };
  }

  // Checks a code; returns the code row when it fits. Wrong codes count as tries.
  async function useCode(userId, purpose, code) {
    const { rows } = await pool.query('SELECT * FROM email_codes WHERE user_id = $1 AND purpose = $2 ORDER BY id DESC LIMIT 1', [userId, purpose]);
    const row = rows[0];
    if (!row || new Date(row.expires_at) < now() || row.attempts >= MAX_TRIES) return null;
    const good = /^\d{6}$/.test(code) && crypto.timingSafeEqual(Buffer.from(codeHash(row.id, code)), Buffer.from(row.code_hash));
    if (!good) { await pool.query('UPDATE email_codes SET attempts = attempts + 1 WHERE id = $1', [row.id]); return null; }
    await pool.query('DELETE FROM email_codes WHERE id = $1', [row.id]);
    return row;
  }

  const mailError = (res, e) => {
    if (e && e.message === 'mail not configured') return res.status(503).json({ error: 'Отправка писем на сервере ещё не настроена.' });
    log('mail error:', e && e.message);
    return res.status(502).json({ error: 'Не удалось отправить письмо. Попробуй позже.' });
  };
  const WAIT = 'Код уже отправлен. Новый можно запросить через минуту.';
  const BAD_CODE = 'Неверный или просроченный код.';

  app.get('/api/email', auth, async (req, res, next) => {
    try {
      const { rows } = await pool.query('SELECT email, verified_at FROM user_emails WHERE user_id = $1', [req.user.id]);
      res.json({ email: rows.length ? rows[0].email : null, verifiedAt: rows.length ? rows[0].verified_at : null, mail: mailer.configured });
    } catch (e) { next(e); }
  });

  // Step 1 of linking: a code goes to the new address.
  app.post('/api/email/start', authLimit, auth, async (req, res, next) => {
    try {
      const email = normEmail(req.body && req.body.email);
      if (!EMAIL_RE.test(email)) return res.status(400).json({ error: 'Похоже, это не почта.' });
      const taken = await pool.query('SELECT user_id FROM user_emails WHERE email_lc = $1', [email.toLowerCase()]);
      if (taken.rows.length && taken.rows[0].user_id !== req.user.id) return res.status(409).json({ error: 'Эта почта уже привязана к другому аккаунту.' });
      let r;
      try { r = await sendCode(req.user, 'verify', email); } catch (e) { return mailError(res, e); }
      if (r.wait) return res.status(429).json({ error: WAIT });
      res.json({ ok: true, email });
    } catch (e) { next(e); }
  });

  // Step 2: the code from the letter → the email is linked.
  app.post('/api/email/confirm', authLimit, auth, async (req, res, next) => {
    try {
      const row = await useCode(req.user.id, 'verify', String((req.body && req.body.code) || '').trim());
      if (!row) return res.status(400).json({ error: BAD_CODE });
      try {
        await pool.query('DELETE FROM user_emails WHERE user_id = $1', [req.user.id]);
        await pool.query('INSERT INTO user_emails (user_id, email, email_lc, verified_at) VALUES ($1, $2, $3, $4)', [req.user.id, row.email, row.email.toLowerCase(), now()]);
      } catch (e) {
        if (e.code === '23505' || /unique|duplicate/i.test(e.message)) return res.status(409).json({ error: 'Эта почта уже привязана к другому аккаунту.' });
        throw e;
      }
      res.json({ ok: true, email: row.email });
    } catch (e) { next(e); }
  });

  app.delete('/api/email', auth, async (req, res, next) => {
    try {
      await pool.query('DELETE FROM user_emails WHERE user_id = $1', [req.user.id]);
      await pool.query('DELETE FROM email_codes WHERE user_id = $1', [req.user.id]);
      res.json({ ok: true });
    } catch (e) { next(e); }
  });

  // Forgot password: the code goes to the linked email. The answer is the same whether or not the address
  // is known, so nobody can check whose email is registered.
  app.post('/api/password/forgot', authLimit, async (req, res, next) => {
    try {
      const email = normEmail(req.body && req.body.email);
      if (!EMAIL_RE.test(email)) return res.status(400).json({ error: 'Похоже, это не почта.' });
      if (!mailer.configured) return mailError(res, new Error('mail not configured'));
      const { rows } = await pool.query('SELECT u.id, u.nickname, e.email FROM user_emails e JOIN users u ON u.id = e.user_id WHERE e.email_lc = $1', [email.toLowerCase()]);
      if (rows.length) {
        try {
          const r = await sendCode(rows[0], 'reset', rows[0].email);
          if (r.wait) return res.status(429).json({ error: WAIT });
        } catch (e) { return mailError(res, e); }
      }
      res.json({ ok: true });
    } catch (e) { next(e); }
  });

  // Code + new password → password changed, every session closed, and this browser is signed in.
  app.post('/api/password/reset', authLimit, async (req, res, next) => {
    try {
      const email = normEmail(req.body && req.body.email);
      const password = String((req.body && req.body.newPassword) || '');
      const bad = checkPassword(password);
      if (bad) return res.status(400).json({ error: bad });
      const { rows } = await pool.query('SELECT u.id, u.nickname FROM user_emails e JOIN users u ON u.id = e.user_id WHERE e.email_lc = $1', [email.toLowerCase()]);
      const user = rows[0];
      if (!user || !(await useCode(user.id, 'reset', String((req.body && req.body.code) || '').trim()))) return res.status(400).json({ error: BAD_CODE });
      await setPassword(user.id, password);
      await dropSessions(user.id, null);
      log(`password reset by email for ${user.nickname}`);
      res.json({ ok: true, token: await issueToken(user.id), nickname: user.nickname });
    } catch (e) { next(e); }
  });
}

module.exports = { mountEmail, EMAIL_SCHEMA };
