'use strict';
// Farm (several PCs), screenshots, events, analytics, the Telegram bot and the /screen command — on in-memory PostgreSQL
// with fake Discord/Telegram APIs.
const assert = require('assert');
const crypto = require('crypto');
const { newDb } = require('pg-mem');
const { createApp, initDb } = require('./app');

(async () => {
  const db = newDb();
  const { Pool } = db.adapters.createPg();
  const pool = new Pool();
  await initDb(pool);

  let clock = new Date('2026-11-21T10:00:00Z');
  const tgCalls = [];
  const tgApi = async (method, body, file) => { tgCalls.push({ method, body, file }); return method === 'getMe' ? { username: 'AutoRejoinTestBot' } : { message_id: 1 }; };
  const { publicKey, privateKey } = crypto.generateKeyPairSync('ed25519');
  const pubHex = publicKey.export({ format: 'der', type: 'spki' }).subarray(12).toString('hex');
  const app = createApp(pool, { now: () => clock, authLimit: 1000, discord: { publicKey: pubHex, appId: '111', screenTimeoutMs: 999999 }, telegram: { token: 'TEST:TOKEN', api: tgApi } });
  const server = app.listen(0);
  const base = `http://127.0.0.1:${server.address().port}`;
  const call = async (method, path, body, token, headers = {}) => {
    const res = await fetch(base + path, { method, headers: { 'content-type': 'application/json', ...(token ? { authorization: 'Bearer ' + token } : {}), ...headers }, body: body ? JSON.stringify(body) : undefined });
    const text = await res.text();
    let json = null; try { json = JSON.parse(text); } catch { json = text; }
    return { status: res.status, body: json };
  };
  const interact = async (payload) => {
    const body = JSON.stringify(payload), ts = String(Math.floor(Date.now() / 1000));
    const sig = crypto.sign(null, Buffer.from(ts + body), privateKey).toString('hex');
    const res = await fetch(base + '/discord/interactions', { method: 'POST', headers: { 'content-type': 'application/json', 'x-signature-ed25519': sig, 'x-signature-timestamp': ts }, body });
    return { status: res.status, body: await res.json() };
  };
  const tg = app.locals.telegram;
  const tgUpdate = (u) => call('POST', `/telegram/${tg.secret}`, u, null, { 'x-telegram-bot-api-secret-token': tg.secret });
  const tick = (sec) => { clock = new Date(clock.getTime() + sec * 1000); };
  const settle = () => new Promise((r) => setTimeout(r, 30));
  let passed = 0;
  const ok = (cond, msg) => { assert.ok(cond, msg); passed++; console.log('  ✓ ' + msg); };
  const JPEG = Buffer.concat([Buffer.from([0xFF, 0xD8, 0xFF, 0xE0]), crypto.randomBytes(400)]).toString('base64');

  const owner = (await call('POST', '/api/register', { nickname: 'farmer', password: 'secret123' })).body.token;
  const acc = (id, name, state, honey, main) => ({ id, name, main: !!main, enabled: true, state, text: state === 'ingame' ? 'в игре' : 'вылетел', uptime: '1 ч', honey });
  const syncPc = (pcId, pcName, accounts, running = true) => call('POST', '/api/agent/sync', { running, phase: 'running', version: '3.1', lang: 'ru', pc: { id: pcId, name: pcName }, accounts }, owner);

  // ---------- AutoRejoin Mobile: accounts and settings backup ----------
  let b = await call('GET', '/api/mobile/backup', null, owner);
  ok(b.status === 200 && b.body.data === null, 'бэкап телефона: пока пусто');
  const backup = JSON.stringify({ app: 'AutoRejoin Mobile', savedAt: 1, prefs: { accounts: '[{"pkg":"com.roblox.client"}]', link: '1537690962' } });
  b = await call('PUT', '/api/mobile/backup', { data: backup }, owner);
  ok(b.body.ok === true, 'бэкап телефона сохранён');
  b = await call('GET', '/api/mobile/backup', null, owner);
  ok(b.body.data === backup && b.body.updatedAt, 'бэкап телефона отдаётся после переустановки (тот же аккаунт)');
  b = await call('PUT', '/api/mobile/backup', { data: 'not json' }, owner);
  ok(b.status === 400, 'битый бэкап отклонён');
  b = await call('GET', '/api/mobile/backup', null, null);
  ok(b.status === 401, 'без входа бэкап не отдаётся');

  // ---------- not linked: nothing is stored ----------
  let r = await syncPc('pc1', 'MAIN-PC', [acc('a1', 'Main', 'ingame', 1000, true)]);
  ok(r.body.linked === false && r.body.commands.length === 0, 'без привязки программа не отдаёт аккаунты');

  // ---------- Telegram: link with the app code ----------
  const code = (await call('POST', '/api/discord/code', null, owner)).body;
  ok(/^\d{6}$/.test(code.code) && code.telegram === null, 'код привязки выдан (ник бота появится после setup)');
  await tg.setup('https://example.test');
  ok(tgCalls.some((c) => c.method === 'setWebhook' && c.body.url === `https://example.test/telegram/${tg.secret}` && c.body.secret_token === tg.secret), 'Telegram: вебхук с секретом настроен');
  r = await call('POST', `/telegram/${tg.secret}`, { message: {} });
  ok(r.status === 401, 'Telegram: запрос без секретного заголовка отклонён');
  await tgUpdate({ message: { chat: { id: 555, type: 'private' }, from: { first_name: 'Den' }, text: '000000' } });
  await settle();
  ok(/неверный/.test(tgCalls.pop().body.text), 'Telegram: неверный код — отказ');
  await tgUpdate({ message: { chat: { id: 555, type: 'private' }, from: { first_name: 'Den' }, text: code.code } });
  await settle();
  ok(/Привязано к аккаунту <b>farmer<\/b>/.test(tgCalls.pop().body.text), 'Telegram: привязан кодом из программы');
  r = await call('GET', '/api/discord/link', null, owner);
  ok(r.body.telegram.linked === true && r.body.telegramBot === 'https://t.me/AutoRejoinTestBot', 'программа видит привязку Telegram и ссылку на бота');

  // ---------- two PCs ----------
  r = await syncPc('pc1', 'MAIN-PC', [acc('a1', 'Main', 'ingame', 1000, true), acc('a2', 'Alt1', 'ingame', 500)]);
  ok(r.body.linked === true, 'с привязкой Telegram синхронизация работает');
  await syncPc('pc2', 'LAPTOP', [acc('b1', 'Alt2', 'ingame', 0)]);
  r = await call('GET', '/api/agent/state', null, owner);
  ok(r.body.pcs.length === 2 && r.body.pcs.every((p) => p.online) && r.body.telegram.name === 'Den', 'сайт видит два ПК и привязку Telegram');
  await call('POST', '/api/agent/command', { cmd: 'stop' }, owner);
  const c1 = (await syncPc('pc1', 'MAIN-PC', [acc('a1', 'Main', 'ingame', 1000, true), acc('a2', 'Alt1', 'ingame', 500)])).body.commands;
  const c2 = (await syncPc('pc2', 'LAPTOP', [acc('b1', 'Alt2', 'ingame', 0)])).body.commands;
  ok(c1.length === 1 && c2.length === 1 && c1[0].cmd === 'stop', 'общая команда «стоп» ушла на оба ПК');
  await call('POST', '/api/agent/command', { cmd: 'rejoin', arg: 'b1' }, owner);
  ok((await syncPc('pc1', 'MAIN-PC', [acc('a1', 'Main', 'ingame', 1000, true), acc('a2', 'Alt1', 'ingame', 500)])).body.commands.length === 0
    && (await syncPc('pc2', 'LAPTOP', [acc('b1', 'Alt2', 'ingame', 0)])).body.commands[0].arg === 'b1', 'перезаход аккаунта ушёл только на его ПК');
  await call('POST', '/api/agent/command', { cmd: 'start', pcId: 'pc2' }, owner);
  ok((await syncPc('pc2', 'LAPTOP', [acc('b1', 'Alt2', 'ingame', 0)])).body.commands[0].cmd === 'start', 'команда для одного выбранного ПК');

  // ---------- analytics: time in game and honey ----------
  tick(30);
  await syncPc('pc1', 'MAIN-PC', [acc('a1', 'Main', 'ingame', 4000, true), acc('a2', 'Alt1', 'ingame', 500)]);
  tick(30);
  await syncPc('pc1', 'MAIN-PC', [acc('a1', 'Main', 'ingame', 9000, true), acc('a2', 'Alt1', 'offline', 200)]);
  tick(600);
  await syncPc('pc1', 'MAIN-PC', [acc('a1', 'Main', 'ingame', 99000, true), acc('a2', 'Alt1', 'ingame', 200)]); // 10-min gap: not counted
  r = await call('GET', '/api/farm/analytics?days=1', null, owner);
  const main = r.body.accounts.find((a) => a.id === 'a1'), alt = r.body.accounts.find((a) => a.id === 'a2');
  ok(main.seconds === 60 && main.honey === 8000, 'аналитика: мейн — 60 сек в игре и 8000 мёда (разрыв 10 мин не засчитан)');
  ok(alt.seconds === 30 && alt.honey === 0, 'аналитика: альт — время только пока в игре, траты мёда не считаются убытком');

  // ---------- events → Telegram, drops counted ----------
  tgCalls.length = 0;
  await call('POST', '/api/agent/event', { kind: 'drop', text: '🔴 **Alt1** вылетел: кикнули с сервера (267)', account: 'Alt1', accountId: 'a2', pcName: 'MAIN-PC', image: JPEG }, owner);
  await settle();
  const photo = tgCalls.find((c) => c.method === 'sendPhoto');
  ok(photo && photo.file.data.length > 100 && /<b>Alt1<\/b> вылетел/.test(photo.body.caption) && /MAIN-PC/.test(photo.body.caption), 'вылет с раскадровкой ушёл в Telegram фото с подписью');
  await call('POST', '/api/agent/event', { kind: 'back', text: '🟢 **Alt1** снова в игре', account: 'Alt1', accountId: 'a2' }, owner);
  await settle();
  ok(tgCalls.some((c) => c.method === 'sendMessage' && /снова в игре/.test(c.body.text)), 'возврат в игру — сообщение в Telegram');
  r = await call('GET', '/api/farm/analytics?days=1', null, owner);
  ok(r.body.accounts.find((a) => a.id === 'a2').drops === 1 && r.body.events[0].kind === 'drop', 'вылет посчитан в аналитике и попал в список событий');
  r = await call('POST', '/api/agent/report', null, owner);
  ok(r.body.accounts.length >= 2 && r.body.causes[0].text.includes('кикнули с сервера') && r.body.telegram === true, 'отчёт за сутки: аккаунты, причины вылетов, отправлен в Telegram');

  // ---------- screenshots: Telegram ----------
  await syncPc('pc2', 'LAPTOP', [acc('b1', 'Alt2', 'ingame', 0)]); // the laptop went quiet during the 10-min gap above
  tgCalls.length = 0;
  await tgUpdate({ message: { chat: { id: 555, type: 'private' }, from: {}, text: '/screen' } });
  await settle();
  const kb = tgCalls.pop().body.reply_markup.inline_keyboard.flat();
  ok(kb.some((b) => b.callback_data === 'sc:a1' && /Main/.test(b.text)) && kb.some((b) => /LAPTOP/.test(b.text)), 'Telegram /screen: выбор аккаунта с ПК');
  await tgUpdate({ callback_query: { id: 'q1', data: 'sc:a1', message: { chat: { id: 555 }, message_id: 7 } } });
  await settle();
  const cmds = (await syncPc('pc1', 'MAIN-PC', [acc('a1', 'Main', 'ingame', 99000, true), acc('a2', 'Alt1', 'ingame', 200)])).body.commands;
  const screenCmd = cmds.find((c) => c.cmd === 'screen');
  ok(screenCmd && screenCmd.arg.endsWith(':a1'), 'команда скриншота ушла на ПК с этим аккаунтом');
  r = await call('POST', '/api/agent/screen', { id: screenCmd.arg.split(':')[0], image: JPEG }, owner);
  await settle();
  ok(r.body.ok && tgCalls.some((c) => c.method === 'sendPhoto' && /Main/.test(c.body.caption)), 'скриншот пришёл в Telegram фото');
  r = await call('POST', '/api/agent/screen', { id: 'nope', image: JPEG }, owner);
  ok(r.status === 404, 'чужой/несуществующий запрос скриншота отклонён');

  // ---------- screenshots: website ----------
  r = await call('POST', '/api/agent/screen-request', { accountId: 'b1' }, owner);
  const sid = r.body.id;
  ok(sid && r.body.pcName === 'LAPTOP', 'сайт: запрос скриншота');
  r = await call('GET', `/api/agent/screen/${sid}`, null, owner);
  ok(r.body.ready === false, 'сайт: пока картинки нет — ждём');
  const cmd2 = (await syncPc('pc2', 'LAPTOP', [acc('b1', 'Alt2', 'ingame', 0)])).body.commands.find((c) => c.cmd === 'screen');
  await call('POST', '/api/agent/screen', { id: cmd2.arg.split(':')[0], error: 'окно свёрнуто' }, owner);
  r = await call('GET', `/api/agent/screen/${sid}`, null, owner);
  ok(r.body.ready === false && r.body.error === 'окно свёрнуто', 'сайт: причина, если окно не снялось');

  // ---------- Discord /screen ----------
  const userD = { id: '7001', username: 'dan' };
  const code2 = (await call('POST', '/api/discord/code', null, owner)).body.code;
  await interact({ type: 5, locale: 'ru', user: userD, data: { custom_id: 'ar:linkmodal', components: [{ type: 1, components: [{ type: 4, custom_id: 'code', value: code2 }] }] } });
  r = await interact({ type: 2, locale: 'ru', member: { user: userD }, data: { name: 'screen' } });
  const sel = r.body.data.components[0].components[0];
  ok(sel.custom_id === 'ar:screen' && sel.options.length === 3 && sel.options.some((o) => /⭐/.test(o.label) && /MAIN-PC/.test(o.label)), 'Discord /screen: меню выбора аккаунта (мейн со звездой, с ПК)');
  r = await interact({ type: 3, locale: 'ru', token: 'tok123', member: { user: userD }, data: { custom_id: 'ar:screen', values: ['a1'] } });
  ok(r.body.type === 7 && /Делаю скриншот \*\*Main\*\*/.test(r.body.data.content), 'Discord: выбрал мейна — «делаю скриншот»');
  r = await interact({ type: 2, locale: 'ru', member: { user: userD }, data: { name: 'status' } });
  ok(/MAIN-PC/.test(r.body.data.embeds[0].description) && /LAPTOP/.test(r.body.data.embeds[0].description)
    && r.body.data.components[0].components.some((b) => b.custom_id === 'ar:screenpick'), 'Discord /status: аккаунты по ПК и кнопка «Скриншот»');

  // ---------- phones: AutoRejoin Mobile, the PCs / Phones choice in both bots ----------
  await call('POST', '/api/agent/sync', { running: true, phase: 'running', version: 'm1.7', lang: 'en', pc: { id: 'ph1', name: 'Pixel 7', kind: 'phone' },
    accounts: [acc('ph1/com.roblox.client', 'PhoneMain', 'ingame', null)] }, owner);
  await syncPc('pc1', 'MAIN-PC', [acc('a1', 'Main', 'ingame', 99000, true), acc('a2', 'Alt1', 'ingame', 200)]);
  r = await interact({ type: 3, locale: 'ru', member: { user: userD }, data: { custom_id: 'ar:kind:phone' } });
  const phoneDesc = r.body.data.embeds[0].description;
  ok(r.body.type === 7 && /Телефоны/.test(r.body.data.embeds[0].title) && /PhoneMain/.test(phoneDesc) && !/MAIN-PC/.test(phoneDesc)
    && r.body.data.components.at(-1).components.find((b) => b.custom_id === 'ar:kind:phone').style === 1, 'Discord: «Телефоны» — только телефоны, кнопка выбрана');
  r = await interact({ type: 3, locale: 'ru', member: { user: userD }, data: { custom_id: 'ar:stop' } });
  let phoneCmds = (await call('POST', '/api/agent/sync', { running: true, phase: 'running', pc: { id: 'ph1', name: 'Pixel 7', kind: 'phone' }, accounts: [] }, owner)).body.commands;
  let pcCmds = (await syncPc('pc1', 'MAIN-PC', [acc('a1', 'Main', 'ingame', 99000, true)])).body.commands;
  ok(phoneCmds.some((c) => c.cmd === 'stop') && !pcCmds.some((c) => c.cmd === 'stop'), 'Discord: «Остановить» в режиме телефонов — только на телефон');
  r = await interact({ type: 3, locale: 'ru', member: { user: userD }, data: { custom_id: 'ar:kind:pc' } });
  ok(/MAIN-PC/.test(r.body.data.embeds[0].description) && !/Pixel 7/.test(r.body.data.embeds[0].description), 'Discord: «ПК» — только ПК');
  tgCalls.length = 0;
  await tgUpdate({ callback_query: { id: 'q9', data: 'k:phone', message: { chat: { id: 555 }, message_id: 8 } } });
  await settle();
  await settle();
  const tgEdit = tgCalls.find((c) => c.method === 'editMessageText');
  ok(tgEdit && /📱 Телефоны/.test(tgEdit.body.text) && !/MAIN-PC/.test(tgEdit.body.text)
    && tgEdit.body.reply_markup.inline_keyboard.flat().some((b) => b.callback_data === 'k:phone' && /✅/.test(b.text)), 'Telegram: выбор «Телефоны» — только телефоны');
  await tgUpdate({ callback_query: { id: 'q10', data: 'start', message: { chat: { id: 555 }, message_id: 8 } } });
  await settle();
  phoneCmds = (await call('POST', '/api/agent/sync', { running: true, phase: 'running', pc: { id: 'ph1', name: 'Pixel 7', kind: 'phone' }, accounts: [] }, owner)).body.commands;
  pcCmds = (await syncPc('pc1', 'MAIN-PC', [acc('a1', 'Main', 'ingame', 99000, true)])).body.commands;
  ok(phoneCmds.some((c) => c.cmd === 'start') && !pcCmds.some((c) => c.cmd === 'start'), 'Telegram: «Запустить» в режиме телефонов — только на телефон');

  // ---------- notify off ----------
  tgCalls.length = 0;
  await tgUpdate({ message: { chat: { id: 555, type: 'private' }, from: {}, text: '/notify off' } });
  await settle();
  await call('POST', '/api/agent/event', { kind: 'drop', text: 'x', account: 'Alt1', accountId: 'a2' }, owner);
  await settle();
  ok(tgCalls.length === 1 && /выключены/.test(tgCalls[0].body.text), 'Telegram: /notify off — уведомления больше не приходят');

  server.close();
  console.log(`\nВсе проверки фермы и Telegram пройдены: ${passed}`);
  process.exit(0);
})().catch((e) => { console.error('ПРОВАЛ:', e); process.exit(1); });
