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
  // Local mail: codes are printed here instead of being emailed.
  const mailer = { configured: true, async send(to, subject, text) { console.log(`[mail to ${to}] ${subject}
${text}`); } };
  createApp(pool, { adminNicks: 'clxwnlesss', mailer }).listen(port, '127.0.0.1');
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
  // --agent: pretend clxwnlesss linked the bot and runs the app on two PCs (site dashboard demo): accounts, honey,
  // a week of analytics, drops, and screenshot answers.
  if (process.argv.includes('--agent')) {
    const { rows } = await pool.query("SELECT id FROM users WHERE nickname = 'clxwnlesss'");
    const uid = rows[0].id;
    await pool.query("INSERT INTO discord_links (discord_id, user_id, discord_name) VALUES ('100', $1, 'clxwnlesss')", [uid]);
    // a week of history
    const names = { a1: ['BeeMain', 'MAIN-PC'], a3: ['Helper_1', 'MAIN-PC'], b1: ['Helper_2', 'LAPTOP'] };
    const hourMs = 3600000, nowH = Math.floor(Date.now() / hourMs) * hourMs;
    for (let h = 1; h < 24 * 7; h++) {
      for (const [id, [name, pc]] of Object.entries(names)) {
        const on = (h * 7 + id.charCodeAt(1)) % 11 !== 0;
        const seconds = on ? 3000 + ((h * 37 + id.charCodeAt(0)) % 600) : 900;
        const honey = id === 'b1' ? 0 : Math.round(seconds / 3600 * (id === 'a1' ? 2.4e6 : 1.1e6));
        const drops = on ? 0 : 1;
        await pool.query('INSERT INTO farm_hourly (user_id, hour, account_id, account_name, pc_name, seconds, drops, honey) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)',
          [uid, new Date(nowH - h * hourMs), id, name, pc, seconds, drops, honey]);
      }
    }
    const causes = ['кикнули с сервера (267)', 'окно не отвечает уже 60 сек', 'отключило от сервера (277)', 'Роблокс не принял вход в окне'];
    for (let i = 0; i < 6; i++) {
      const [name, pc] = Object.values(names)[i % 3];
      await pool.query("INSERT INTO farm_events (user_id, at, kind, account_name, pc_name, text) VALUES ($1, $2, 'drop', $3, $4, $5)",
        [uid, new Date(Date.now() - (i + 1) * 5.3 * hourMs), name, pc, `🔴 **${name}** вылетел: ${causes[i % causes.length]}`]);
    }
    const shot = require('fs').readFileSync(require('path').join(__dirname, '..', 'docs', 'screenshot.png')).toString('base64');
    let running = true, honey1 = 1.23e9, honey2 = 4.1e8;
    const pcs = {
      'pc-main': { name: 'MAIN-PC', accounts: () => [
        { id: 'a1', name: 'BeeMain', main: true, enabled: true, state: running ? 'ingame' : 'off', text: running ? 'в игре, скрипт работает' : 'AutoRejoin остановлен', uptime: running ? '3 ч 12 мин' : '', honey: Math.round(honey1) },
        { id: 'a3', name: 'Helper_1', main: false, enabled: true, state: running ? 'script' : 'off', text: running ? 'в игре, ждёт запуска скрипта' : 'AutoRejoin остановлен', uptime: running ? '50 сек' : '', honey: Math.round(honey2) }
      ] },
      'pc-laptop': { name: 'LAPTOP', accounts: () => [
        { id: 'b1', name: 'Helper_2', main: false, enabled: true, state: 'ingame', text: 'в игре', uptime: '7 ч 02 мин' }
      ] }
    };
    const report = async () => {
      honey1 += 2000; honey2 += 900;
      for (const [id, pc] of Object.entries(pcs)) {
        const r = await post('/api/agent/sync', { running: id === 'pc-laptop' || running, phase: 'running', version: '3.1', lang: 'ru', pc: { id, name: pc.name }, accounts: pc.accounts() }, ownerToken);
        for (const c of r.commands || []) {
          console.log('command from site:', id, c.cmd, c.arg);
          if (c.cmd === 'stop' && id === 'pc-main') running = false;
          if (c.cmd === 'start' && id === 'pc-main') running = true;
          if (c.cmd === 'screen') setTimeout(() => post('/api/agent/screen', { id: c.arg.split(':')[0], image: shot }, ownerToken), 1500);
        }
      }
    };
    await report();
    setInterval(() => report().catch(() => {}), 3000);
  }
  console.log('TOKEN=' + ownerToken);
  console.log(`dev server on ${base}`);
})();
