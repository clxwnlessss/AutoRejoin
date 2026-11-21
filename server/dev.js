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
  // --agent: pretend clxwnlesss linked the Discord bot and the app is online with three accounts (site dashboard demo).
  if (process.argv.includes('--agent')) {
    const { rows } = await pool.query("SELECT id FROM users WHERE nickname = 'clxwnlesss'");
    await pool.query("INSERT INTO discord_links (discord_id, user_id, discord_name) VALUES ('100', $1, 'clxwnlesss')", [rows[0].id]);
    let running = true;
    const report = async () => {
      const r = await post('/api/agent/sync', { running, phase: running ? 'running' : 'stopped', version: '2.5.2', lang: 'ru', accounts: [
        { id: 'a1', name: 'Orelplayy', main: true, enabled: true, state: running ? 'ingame' : 'off', text: running ? 'в игре, скрипт работает' : 'AutoRejoin остановлен', uptime: running ? '3 ч 12 мин' : '' },
        { id: 'a2', name: 'zxcghoul21x', main: false, enabled: false, state: '', text: '', uptime: '' },
        { id: 'a3', name: 'Orelplayy_Alt3', main: false, enabled: true, state: running ? 'script' : 'off', text: running ? 'в игре, ждёт запуска скрипта' : 'AutoRejoin остановлен', uptime: running ? '50 сек' : '' }
      ] }, ownerToken);
      for (const c of r.commands || []) { console.log('command from site:', c.cmd, c.arg); if (c.cmd === 'stop') running = false; if (c.cmd === 'start') running = true; }
    };
    await report();
    setInterval(() => report().catch(() => {}), 3000);
  }
  console.log('TOKEN=' + ownerToken);
  console.log(`dev server on ${base}`);
})();
