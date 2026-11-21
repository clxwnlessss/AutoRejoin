'use strict';
// Проверка сервера на PostgreSQL в памяти (pg-mem): регистрация, вход, пульсы, топ, защита от накрутки.
const assert = require('assert');
const { newDb } = require('pg-mem');
const { createApp, initDb } = require('./app');

(async () => {
  const db = newDb();
  const { Pool } = db.adapters.createPg();
  const pool = new Pool();
  await initDb(pool);

  let clock = new Date('2026-11-19T12:00:00Z');
  const app = createApp(pool, { now: () => clock, authLimit: 1000 });
  const server = app.listen(0);
  const base = `http://127.0.0.1:${server.address().port}`;
  const call = async (method, path, body, token) => {
    const res = await fetch(base + path, {
      method,
      headers: { 'content-type': 'application/json', ...(token ? { authorization: 'Bearer ' + token } : {}) },
      body: body ? JSON.stringify(body) : undefined
    });
    return { status: res.status, body: await res.json() };
  };
  const tick = (sec) => { clock = new Date(clock.getTime() + sec * 1000); };
  let passed = 0;
  const ok = (cond, msg) => { assert.ok(cond, msg); passed++; console.log('  ✓ ' + msg); };

  // Регистрация и вход
  let r = await call('POST', '/api/register', { nickname: 'ab', password: '123456' });
  ok(r.status === 400, 'короткий ник отклонён');
  r = await call('POST', '/api/register', { nickname: 'clxwnlesss', password: '123' });
  ok(r.status === 400, 'короткий пароль отклонён');
  r = await call('POST', '/api/register', { nickname: 'clxwnlesss', password: 'secret123' });
  ok(r.status === 200 && r.body.token, 'регистрация clxwnlesss');
  const owner = r.body.token;
  r = await call('POST', '/api/register', { nickname: 'CLXWNLESSS', password: 'other123' });
  ok(r.status === 409, 'занятый ник (без учёта регистра) отклонён');
  r = await call('POST', '/api/login', { nickname: 'clxwnlesss', password: 'wrong' });
  ok(r.status === 401, 'неверный пароль отклонён');
  r = await call('POST', '/api/login', { nickname: 'Clxwnlesss', password: 'secret123' });
  ok(r.status === 200 && r.body.nickname === 'clxwnlesss', 'вход по нику в любом регистре');
  r = await call('GET', '/api/me');
  ok(r.status === 401, 'без токена /api/me закрыт');

  r = await call('POST', '/api/register', { nickname: 'Helper_Bee', password: 'secret456' });
  const other = r.body.token;

  // Пульсы: первый ничего не начисляет, дальше — по часам сервера
  r = await call('POST', '/api/heartbeat', { inGame: 3 }, owner);
  ok(r.body.credited === 0, 'первый пульс — без начисления');
  await call('POST', '/api/heartbeat', { inGame: 1 }, other);
  for (let i = 0; i < 60; i++) {
    tick(60);
    await call('POST', '/api/heartbeat', { inGame: 3 }, owner);
    if (i < 30) await call('POST', '/api/heartbeat', { inGame: 1 }, other);
  }
  r = await call('GET', '/api/me', null, owner);
  ok(r.body.seconds === 3600 && r.body.rank === 1, 'час в АФК засчитан, место #1');

  // Защита: большой перерыв не засчитывается, inGame=0 не засчитывается
  tick(3 * 3600);
  r = await call('POST', '/api/heartbeat', { inGame: 3 }, owner);
  ok(r.body.credited === 0, 'перерыв 3 ч не засчитан');
  tick(60);
  r = await call('POST', '/api/heartbeat', { inGame: 0 }, owner);
  ok(r.body.credited === 0, 'без аккаунтов в игре — не засчитано');
  r = await call('POST', '/api/heartbeat', { inGame: 999 }, owner);
  ok(r.body.credited === 0, 'повтор без паузы — 0 сек');

  // Топ
  r = await call('GET', '/api/top');
  ok(r.body.top.length === 2 && r.body.top[0].nickname === 'clxwnlesss' && r.body.top[0].seconds === 3600, 'топ: clxwnlesss #1, 1 ч');
  ok(r.body.top[1].nickname === 'Helper_Bee' && r.body.top[1].seconds === 1800, 'топ: Helper_Bee #2, 30 мин');
  ok(r.body.top[0].accountSeconds === 3600 * 3, 'аккаунто-часы считаются отдельно');
  ok(r.body.top[0].online === true, 'clxwnlesss «сейчас стоит» — свежий пульс с аккаунтами в игре');
  ok(r.body.top[1].online === false, 'Helper_Bee не в сети — пульс давно');
  ok(!('pass_hash' in r.body.top[0]) && !('id' in r.body.top[0]), 'в топе нет закрытых данных');

  // Новый месяц — новый топ
  clock = new Date('2026-12-01T00:00:30Z');
  r = await call('GET', '/api/top');
  ok(r.body.month === '2026-12' && r.body.top.length === 0, 'в новом месяце топ пустой');
  r = await call('GET', '/api/top?month=2026-11');
  ok(r.body.top.length === 2, 'прошлый месяц доступен по ?month=');

  // Сайт: CORS, общая статистика, панель аккаунтов
  let cr = await fetch(base + '/api/top', { method: 'OPTIONS', headers: { origin: 'https://clxwnlessss.github.io', 'access-control-request-method': 'GET' } });
  ok(cr.status === 204 && cr.headers.get('access-control-allow-origin') === 'https://clxwnlessss.github.io' && /authorization/.test(cr.headers.get('access-control-allow-headers')), 'сайт: CORS для GitHub Pages');
  cr = await fetch(base + '/api/top', { headers: { origin: 'https://evil.example' } });
  ok(!cr.headers.get('access-control-allow-origin'), 'сайт: чужой сайт CORS не получает');
  r = await call('GET', '/api/stats');
  ok(r.status === 200 && r.body.users >= 2 && typeof r.body.monthHours === 'number' && typeof r.body.onlineAccounts === 'number', 'сайт: общая статистика');
  const site = await call('POST', '/api/login', { nickname: 'clxwnlesss', password: 'secret123' });
  r = await call('GET', '/api/agent/state', null, site.body.token);
  ok(r.status === 200 && r.body.linked === false, 'сайт: без привязанного бота аккаунтов нет');
  r = await call('POST', '/api/agent/command', { cmd: 'start' }, site.body.token);
  ok(r.status === 409, 'сайт: команда без привязки — подсказка');
  r = await call('POST', '/api/agent/command', { cmd: 'format-c' }, site.body.token);
  ok(r.status === 400, 'сайт: неизвестная команда отклонена');
  r = await call('GET', '/api/agent/state');
  ok(r.status === 401, 'сайт: панель без входа закрыта');

  // Выход
  r = await call('POST', '/api/logout', null, owner);
  r = await call('GET', '/api/me', null, owner);
  ok(r.status === 401, 'после выхода токен не работает');

  server.close();
  console.log(`\nВсе проверки пройдены: ${passed}`);
})().catch((e) => { console.error('ПРОВАЛ:', e); process.exit(1); });
