'use strict';
// Email: linking with a code, forgot/reset password by a code from the letter, limits and edge cases.
const assert = require('assert');
const { newDb } = require('pg-mem');
const { createApp, initDb } = require('./app');

(async () => {
  const db = newDb();
  const { Pool } = db.adapters.createPg();
  const pool = new Pool();
  await initDb(pool);
  let clock = new Date('2026-11-22T12:00:00Z');
  const sent = [];
  let failMail = false;
  const mailer = { configured: true, async send(to, subject, text) { if (failMail) throw new Error('boom'); sent.push({ to, subject, text }); } };
  const app = createApp(pool, { now: () => clock, authLimit: 1000, mailer, allowPlainRegister: true });
  const server = app.listen(0);
  const base = `http://127.0.0.1:${server.address().port}`;
  const call = async (method, path, body, token) => {
    const res = await fetch(base + path, { method, headers: { 'content-type': 'application/json', ...(token ? { authorization: 'Bearer ' + token } : {}) }, body: body ? JSON.stringify(body) : undefined });
    return { status: res.status, body: await res.json() };
  };
  const tick = (sec) => { clock = new Date(clock.getTime() + sec * 1000); };
  const lastCode = () => /(\d{6})/.exec(sent[sent.length - 1].text)[1];
  let passed = 0;
  const ok = (cond, msg) => { assert.ok(cond, msg); passed++; console.log('  ✓ ' + msg); };

  let r = await call('POST', '/api/register', { nickname: 'puzan777', password: 'oldpass1' });
  const tok = r.body.token;
  r = await call('POST', '/api/register', { nickname: 'other_bee', password: 'otherpw1' });
  const tok2 = r.body.token;

  r = await call('GET', '/api/email', null, tok);
  ok(r.status === 200 && r.body.email === null && r.body.mail === true, 'почта не привязана, отправка настроена');
  r = await call('POST', '/api/email/start', { email: 'not-an-email' }, tok);
  ok(r.status === 400, 'кривая почта отклонена');
  r = await call('POST', '/api/email/start', { email: 'Puzan@Example.com' }, tok);
  ok(r.status === 200 && sent.length === 1 && sent[0].to === 'Puzan@Example.com', 'код привязки отправлен на почту');
  r = await call('POST', '/api/email/start', { email: 'Puzan@Example.com' }, tok);
  ok(r.status === 429, 'повторный код раньше минуты не отправляется');
  r = await call('POST', '/api/email/confirm', { code: '000000' === lastCode() ? '111111' : '000000' }, tok);
  ok(r.status === 400, 'неверный код не подходит');
  r = await call('POST', '/api/email/confirm', { code: lastCode() }, tok);
  ok(r.status === 200 && r.body.email === 'Puzan@Example.com', 'почта привязана по коду');
  r = await call('GET', '/api/me', null, tok);
  ok(r.body.email === 'Puzan@Example.com', '/api/me показывает почту');
  r = await call('POST', '/api/email/confirm', { code: lastCode() }, tok);
  ok(r.status === 400, 'код одноразовый');

  tick(61);
  r = await call('POST', '/api/email/start', { email: 'puzan@example.com' }, tok2);
  ok(r.status === 409, 'чужую почту (без учёта регистра) не привязать');

  // forgot / reset
  r = await call('POST', '/api/password/forgot', { email: 'nobody@example.com' });
  ok(r.status === 200 && sent.length === 1, 'на неизвестную почту письма нет, ответ тот же');
  r = await call('POST', '/api/password/forgot', { email: 'PUZAN@example.com' });
  ok(r.status === 200 && sent.length === 2 && /сброса пароля/.test(sent[1].text), 'код сброса ушёл на привязанную почту');
  const code = lastCode();
  for (let i = 0; i < 5; i++) await call('POST', '/api/password/reset', { email: 'puzan@example.com', code: code === '123456' ? '654321' : '123456', newPassword: 'newpass1' });
  r = await call('POST', '/api/password/reset', { email: 'puzan@example.com', code, newPassword: 'newpass1' });
  ok(r.status === 400, 'после 5 ошибок код сгорает');
  tick(61);
  r = await call('POST', '/api/password/forgot', { email: 'puzan@example.com' });
  const code2 = lastCode();
  r = await call('POST', '/api/password/reset', { email: 'puzan@example.com', code: code2, newPassword: '123' });
  ok(r.status === 400, 'короткий новый пароль отклонён');
  r = await call('POST', '/api/password/reset', { email: 'puzan@example.com', code: code2, newPassword: 'newpass1' });
  ok(r.status === 200 && r.body.token && r.body.nickname === 'puzan777', 'пароль сброшен, сразу вход');
  r = await call('GET', '/api/me', null, tok);
  ok(r.status === 401, 'старые сессии закрыты');
  r = await call('POST', '/api/login', { nickname: 'puzan777', password: 'newpass1' });
  ok(r.status === 200, 'вход с новым паролем');
  const tok3 = r.body.token;
  r = await call('POST', '/api/login', { nickname: 'puzan777', password: 'oldpass1' });
  ok(r.status === 401, 'старый пароль больше не подходит');

  tick(61);
  await call('POST', '/api/password/forgot', { email: 'puzan@example.com' });
  const code3 = lastCode();
  tick(16 * 60);
  r = await call('POST', '/api/password/reset', { email: 'puzan@example.com', code: code3, newPassword: 'another1' });
  ok(r.status === 400, 'код через 15 минут просрочен');

  failMail = true; tick(61);
  r = await call('POST', '/api/password/forgot', { email: 'puzan@example.com' });
  ok(r.status === 502, 'ошибка почты → 502');
  failMail = false;

  r = await call('DELETE', '/api/email', null, tok3);
  r = await call('GET', '/api/email', null, tok3);
  ok(r.body.email === null, 'почту можно отвязать');

  const app2 = createApp(pool, { now: () => clock, authLimit: 1000, mail: {} });
  const s2 = app2.listen(0);
  const res = await fetch(`http://127.0.0.1:${s2.address().port}/api/password/forgot`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email: 'a@b.cd' }) });
  ok(res.status === 503, 'без BREVO_API_KEY — понятная 503');
  s2.close();

  // ---------- sign-up with an email code ----------
  const app3 = createApp(pool, { now: () => clock, authLimit: 1000, mailer });
  const s3 = app3.listen(0);
  const base3 = `http://127.0.0.1:${s3.address().port}`;
  const call3 = async (method, path, body, token) => {
    const res = await fetch(base3 + path, { method, headers: { 'content-type': 'application/json', ...(token ? { authorization: 'Bearer ' + token } : {}) }, body: body ? JSON.stringify(body) : undefined });
    return { status: res.status, body: await res.json() };
  };
  r = await call3('POST', '/api/register', { nickname: 'old_client', password: 'secret12' });
  ok(r.status === 400 && /обнови/i.test(r.body.error), 'старая регистрация без почты закрыта, когда почта настроена');
  r = await call3('POST', '/api/register/start', { nickname: 'new_bee', email: 'bad', password: 'secret12' });
  ok(r.status === 400, 'регистрация: кривая почта отклонена');
  r = await call3('POST', '/api/register/start', { nickname: 'puzan777', email: 'x@example.com', password: 'secret12' });
  ok(r.status === 409, 'регистрация: занятый ник');
  failMail = false; tick(61);
  await call('POST', '/api/email/start', { email: 'Puzan@Example.com' }, tok3);
  await call('POST', '/api/email/confirm', { code: lastCode() }, tok3);
  r = await call3('POST', '/api/register/start', { nickname: 'new_bee', email: 'puzan@example.com', password: 'secret12' });
  ok(r.status === 409, 'регистрация: занятая почта');
  const before = sent.length;
  r = await call3('POST', '/api/register/start', { nickname: 'New_Bee', email: 'NewBee@Example.com', password: 'secret12' });
  ok(r.status === 200 && sent.length === before + 1 && /регистрации/.test(sent[sent.length - 1].text), 'регистрация: код ушёл на почту');
  const scode = lastCode();
  r = await call3('POST', '/api/login', { nickname: 'New_Bee', password: 'secret12' });
  ok(r.status === 401, 'до кода аккаунта ещё нет');
  r = await call3('POST', '/api/register/start', { nickname: 'New_Bee', email: 'newbee@example.com', password: 'secret12' });
  ok(r.status === 429, 'повторный код регистрации раньше минуты не отправляется');
  r = await call3('POST', '/api/register/confirm', { email: 'newbee@example.com', code: scode === '000000' ? '111111' : '000000' });
  ok(r.status === 400, 'неверный код регистрации не подходит');
  r = await call3('POST', '/api/register/confirm', { email: 'newbee@example.com', code: scode });
  ok(r.status === 200 && r.body.token && r.body.nickname === 'New_Bee' && r.body.email === 'NewBee@Example.com', 'код подошёл: аккаунт создан и вход');
  r = await call3('GET', '/api/email', null, r.body.token);
  ok(r.body.email === 'NewBee@Example.com', 'почта сразу привязана');
  r = await call3('POST', '/api/register/confirm', { email: 'newbee@example.com', code: scode });
  ok(r.status === 400, 'код регистрации одноразовый');
  r = await call3('POST', '/api/login', { nickname: 'NEWBEE@example.com', password: 'secret12' });
  ok(r.status === 200 && r.body.nickname === 'New_Bee', 'вход по почте');
  r = await call3('POST', '/api/login', { nickname: 'newbee@example.com', password: 'wrong123' });
  ok(r.status === 401, 'вход по почте с неверным паролем отклонён');
  r = await call3('POST', '/api/login', { nickname: 'new_bee', password: 'secret12' });
  ok(r.status === 200, 'вход по нику тоже работает');
  tick(61);
  await call3('POST', '/api/register/start', { nickname: 'late_bee', email: 'late@example.com', password: 'secret12' });
  const lcode = lastCode();
  tick(16 * 60);
  r = await call3('POST', '/api/register/confirm', { email: 'late@example.com', code: lcode });
  ok(r.status === 400, 'код регистрации через 15 минут просрочен');
  s3.close();

  server.close();
  console.log(`email: ${passed} проверок пройдено`);
  process.exit(0);
})().catch((e) => { console.error(e); process.exit(1); });
