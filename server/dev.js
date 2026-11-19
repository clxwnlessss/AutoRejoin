'use strict';
// Локальный сервер для проверки программы: PostgreSQL в памяти (pg-mem) и тестовые игроки в топе.
// node dev.js [порт] — печатает токен тестового аккаунта clxwnlesss (пароль secret123).
const { newDb } = require('pg-mem');
const crypto = require('crypto');
const { createApp, initDb, monthKey } = require('./app');

(async () => {
  const db = newDb();
  const { Pool } = db.adapters.createPg();
  const pool = new Pool();
  await initDb(pool);
  const port = parseInt(process.argv[2], 10) || 3999;
  createApp(pool).listen(port, '127.0.0.1');
  const base = `http://127.0.0.1:${port}`;
  const post = async (path, body, token) => (await fetch(base + path, {
    method: 'POST',
    headers: { 'content-type': 'application/json', ...(token ? { authorization: 'Bearer ' + token } : {}) },
    body: JSON.stringify(body || {})
  })).json();
  const month = monthKey(new Date());
  const players = [
    ['BeeQueen_77', 412 * 3600 + 1200], ['clxwnlesss', 356 * 3600 + 2400], ['HiveMaster', 301 * 3600],
    ['AFK_Legend', 255 * 3600 + 900], ['honeyfarm', 190 * 3600], ['Stinger_X', 144 * 3600 + 1800],
    ['buzzbuzz', 98 * 3600], ['PollenPro', 51 * 3600 + 600]
  ];
  let ownerToken = null;
  for (const [nick, seconds] of players) {
    const r = await post('/api/register', { nickname: nick, password: 'secret123' });
    const { rows } = await pool.query('SELECT id FROM users WHERE nickname = $1', [nick]);
    await pool.query('INSERT INTO afk_monthly (user_id, month, seconds, account_seconds) VALUES ($1, $2, $3, $4)', [rows[0].id, month, seconds, seconds * 3]);
    if (['BeeQueen_77', 'HiveMaster', 'clxwnlesss'].includes(nick)) {
      await pool.query('UPDATE users SET last_beat = NOW(), last_in_game = 3 WHERE id = $1', [rows[0].id]);
    }
    if (nick === 'clxwnlesss') ownerToken = r.token;
  }
  console.log('TOKEN=' + ownerToken);
  console.log(`dev server on ${base}`);
})();
