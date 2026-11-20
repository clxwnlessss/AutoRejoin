'use strict';
// Discord bot checks on in-memory PostgreSQL: signatures, link flow, one-to-one links, brute-force guard, sync and commands.
const assert = require('assert');
const crypto = require('crypto');
const { newDb } = require('pg-mem');
const { createApp, initDb } = require('./app');

(async () => {
  const db = newDb();
  const { Pool } = db.adapters.createPg();
  const pool = new Pool();
  await initDb(pool);

  const { publicKey, privateKey } = crypto.generateKeyPairSync('ed25519');
  const pubHex = publicKey.export({ format: 'der', type: 'spki' }).subarray(12).toString('hex');
  const app = createApp(pool, { authLimit: 1000, discord: { publicKey: pubHex, appId: '111' } });
  const server = app.listen(0);
  const base = `http://127.0.0.1:${server.address().port}`;
  const call = async (method, path, body, token) => {
    const res = await fetch(base + path, { method, headers: { 'content-type': 'application/json', ...(token ? { authorization: 'Bearer ' + token } : {}) }, body: body ? JSON.stringify(body) : undefined });
    return { status: res.status, body: await res.json() };
  };
  const interact = async (payload, { badSig } = {}) => {
    const body = JSON.stringify(payload), ts = String(Math.floor(Date.now() / 1000));
    let sig = crypto.sign(null, Buffer.from(ts + body), privateKey).toString('hex');
    if (badSig) sig = sig.replace(/^./, (c) => (c === 'a' ? 'b' : 'a'));
    const res = await fetch(base + '/discord/interactions', { method: 'POST', headers: { 'content-type': 'application/json', 'x-signature-ed25519': sig, 'x-signature-timestamp': ts }, body });
    return { status: res.status, body: res.headers.get('content-type')?.includes('json') ? await res.json() : await res.text() };
  };
  const userA = { id: '9001', username: 'alice', global_name: 'Alice' };
  const userB = { id: '9002', username: 'bob' };
  const userC = { id: '9003', username: 'carol' };
  const slash = (user, name, options) => ({ type: 2, locale: 'ru', member: { user }, data: { name, options } });
  const button = (user, custom_id, values) => ({ type: 3, locale: 'ru', member: { user }, data: { custom_id, values } });
  const modal = (user, code) => ({ type: 5, locale: 'ru', user, data: { custom_id: 'ar:linkmodal', components: [{ type: 1, components: [{ type: 4, custom_id: 'code', value: code }] }] } });
  let passed = 0;
  const ok = (cond, msg) => { assert.ok(cond, msg); passed++; console.log('  ✓ ' + msg); };

  // Signatures
  let r = await interact({ type: 1 });
  ok(r.status === 200 && r.body.type === 1, 'PING с верной подписью → PONG');
  r = await interact({ type: 1 }, { badSig: true });
  ok(r.status === 401, 'неверная подпись отклонена');

  // Two app accounts
  const t1 = (await call('POST', '/api/register', { nickname: 'clxwnlesss', password: 'secret123' })).body.token;
  const t2 = (await call('POST', '/api/register', { nickname: 'OtherGuy', password: 'secret456' })).body.token;

  // Panel and not-linked state
  r = await interact(slash(userA, 'panel'));
  ok(r.body.type === 4 && r.body.data.components[0].components[0].custom_id === 'ar:link', '/panel — кнопка «Привязать аккаунт»');
  r = await interact(slash(userA, 'status'));
  ok(r.body.data.flags === 64 && /не привязан/.test(r.body.data.content) && r.body.data.components[0].components[0].custom_id === 'ar:link', 'без привязки /status предлагает привязать (видно только тебе)');
  r = await interact(button(userA, 'ar:link'));
  ok(r.body.type === 9 && r.body.data.custom_id === 'ar:linkmodal', 'кнопка открывает окно ввода кода');

  // Code is tied to the requesting account
  r = await call('POST', '/api/discord/code', null, t1);
  ok(/^\d{6}$/.test(r.body.code) && r.body.invite.includes('client_id=111'), 'программа получила код и ссылку-приглашение');
  const code1 = r.body.code;
  r = await call('POST', '/api/discord/code', null);
  ok(r.status === 401, 'без входа код не выдаётся');

  // Brute-force guard
  for (let i = 0; i < 5; i++) await interact(modal(userB, code1 === '000000' ? '111111' : '000000'));
  r = await interact(modal(userB, code1));
  ok(/Слишком много/.test(r.body.data.content), 'после 5 неверных кодов ввод заблокирован (даже верный)');

  r = await interact(modal(userA, code1));
  ok(/Привязано к аккаунту \*\*clxwnlesss\*\*/.test(r.body.data.content), 'верный код привязал именно запросивший аккаунт');
  r = await interact(modal(userA, code1));
  ok(/неверный или устарел/.test(r.body.data.content), 'код одноразовый');
  r = await call('GET', '/api/discord/link', null, t1);
  ok(r.body.linked === true && r.body.name === 'Alice', 'программа видит привязку');

  // One Discord user <-> one account: linking OtherGuy from the same Discord moves the link
  const code2 = (await call('POST', '/api/discord/code', null, t2)).body.code;
  r = await interact(modal(userA, code2));
  ok(/OtherGuy/.test(r.body.data.content), 'тот же Discord привязан к другому аккаунту…');
  ok((await call('GET', '/api/discord/link', null, t1)).body.linked === false, '…а старая привязка снята (не два аккаунта на один Discord)');
  // And one account cannot be linked to two Discord users
  const code3 = (await call('POST', '/api/discord/code', null, t1)).body.code;
  await interact(modal(userA, code3));
  const code4 = (await call('POST', '/api/discord/code', null, t1)).body.code;
  await interact(modal(userC, code4));
  r = await interact(slash(userA, 'status'));
  ok(/не привязан/.test(r.body.data.content || ''), 'аккаунт перепривязан к Carol — у Alice доступа больше нет');
  const linkedB = await interact(slash(userC, 'status'));
  ok(linkedB.body.data.embeds && /clxwnlesss/.test(linkedB.body.data.embeds[0].title), 'у Carol — аккаунт clxwnlesss');

  // Sync + status + commands
  r = await call('POST', '/api/agent/sync', { running: true, phase: 'running', accounts: [
    { id: 'a1', name: 'BeeMain', main: true, enabled: true, state: 'ingame', text: 'в игре', uptime: '2 ч 10 мин' },
    { id: 'a2', name: 'Helper_1', enabled: true, state: 'problem', text: 'вылетел' },
    { id: 'a3', name: 'Off', enabled: false, state: 'idle' }] }, t1);
  ok(r.body.linked === true && r.body.commands.length === 0, 'синхронизация: привязан, команд нет');
  r = await interact(slash(userC, 'status'));
  const desc = r.body.data.embeds[0].description;
  ok(/в игре \*\*1 из 2\*\*/.test(desc) && /BeeMain/.test(desc) && !/Off/.test(desc), 'статус: 1 из 2 в игре, выключенный аккаунт скрыт');
  ok(r.body.data.components[1].components[0].options.length === 2, 'меню «перезайти одним» со списком аккаунтов');
  ok(r.body.data.components[0].components[1].disabled === true && r.body.data.components[0].components[2].disabled === false, 'работает → «Запустить» неактивна, «Остановить» активна');

  r = await interact(button(userC, 'ar:rejoin', ['a2']));
  ok(r.body.type === 7 && /Helper_1/.test(r.body.data.embeds[0].footer.text), 'перезаход одного аккаунта поставлен в очередь');
  await interact(button(userC, 'ar:stop'));
  r = await interact({ type: 4, locale: 'en', member: { user: userC }, data: { name: 'rejoin', options: [{ name: 'account', value: 'help', focused: true }] } });
  ok(r.body.type === 8 && r.body.data.choices.length === 1 && r.body.data.choices[0].value === 'a2', 'автодополнение /rejoin ищет по имени');
  r = await call('POST', '/api/agent/sync', { running: true, phase: 'running', accounts: [] }, t1);
  ok(r.body.commands.length === 2 && r.body.commands[0].cmd === 'rejoin' && r.body.commands[0].arg === 'a2' && r.body.commands[1].cmd === 'stop', 'программа получила команды по порядку');
  r = await call('POST', '/api/agent/sync', { running: false }, t1);
  ok(r.body.commands.length === 0, 'команды выдаются один раз');

  // Strangers cannot control someone else's account
  r = await interact(button(userA, 'ar:start'));
  ok(/не привязан/.test(r.body.data.content || ''), 'чужой Discord не может управлять аккаунтом');
  r = await call('POST', '/api/agent/sync', { running: false }, t2);
  ok(r.body.linked === false, 'непривязанный аккаунт ничего не хранит на сервере');

  // Unlink from the app
  await call('DELETE', '/api/discord/link', null, t1);
  r = await interact(slash(userC, 'status'));
  ok(/не привязан/.test(r.body.data.content || ''), 'после отвязки в программе бот доступ закрыл');

  server.close();
  console.log(`\nВсе проверки бота пройдены: ${passed}`);
})().catch((e) => { console.error('ПРОВАЛ:', e); process.exit(1); });
