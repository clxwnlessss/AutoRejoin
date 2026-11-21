'use strict';
// Server-management checks: automod rules, durations, templates, and slash commands against a fake Discord API.
const assert = require('assert');
const { newDb } = require('pg-mem');
const { initDb } = require('./app');
const { createGuildBot, parseDuration, fillTemplate, detectViolation, defaultConfig, parseDisboard, parseDiscadia, dueBumps } = require('./guild');

(async () => {
  let passed = 0;
  const ok = (cond, msg) => { assert.ok(cond, msg); passed++; console.log('  ✓ ' + msg); };

  // ---------- pure helpers ----------
  ok(parseDuration('10m') === 600000 && parseDuration('1h30m') === 5400000 && parseDuration('2d') === 172800000, 'время: 10m, 1h30m, 2d');
  ok(parseDuration('15') === 900000 && parseDuration('1ч') === 3600000 && parseDuration('abc') === null && parseDuration('5x') === null, 'время: число = минуты, русские единицы, мусор отклонён');
  ok(fillTemplate('Привет {user} на {server}! Ты {count}-й', { user: '<@1>', username: 'a', server: 'AR', count: 42 }) === 'Привет <@1> на AR! Ты 42-й', 'шаблон приветствия');

  const cfg = defaultConfig();
  cfg.automod.enabled = true; cfg.automod.links = true; cfg.automod.words = ['дурак', 'scam*'];
  const t = 100000;
  ok(detectViolation(cfg, 'join discord.gg/abc123', [], t) === 'invites', 'автомод: инвайт');
  ok(detectViolation(cfg, 'see https://evil.example/x', [], t) === 'links', 'автомод: чужая ссылка');
  ok(detectViolation(cfg, 'code: https://github.com/clxwnlessss/AutoRejoin', [], t) === null, 'автомод: разрешённый домен');
  ok(detectViolation(cfg, 'ЭТО ОЧЕНЬ ГРОМКОЕ СООБЩЕНИЕ', [], t) === 'caps', 'автомод: капс (кириллица)');
  ok(detectViolation(cfg, 'ОК', [], t) === null, 'автомод: короткий капс не трогает');
  ok(detectViolation(cfg, '<@1> <@2> <@3> <@4> <@5>', [], t) === 'mentions', 'автомод: 5 упоминаний');
  ok(detectViolation(cfg, 'ты дурак', [], t) === 'badwords' && detectViolation(cfg, 'дураковатый', [], t) === null, 'автомод: слово целиком, не часть');
  ok(detectViolation(cfg, 'free scammers here', [], t) === 'badwords', 'автомод: слово со * — часть слова');
  const flood = Array.from({ length: 6 }, (_, k) => ({ t: t - k * 500, content: 'a' + k }));
  ok(detectViolation(cfg, 'a0', flood, t) === 'spam', 'автомод: 6 сообщений за 7 сек — флуд');
  const dup = [{ t: t - 2000, content: 'buy' }, { t: t - 1000, content: 'buy' }, { t, content: 'buy' }];
  ok(detectViolation(cfg, 'buy', dup, t) === 'spam', 'автомод: 3 одинаковых — флуд');

  // ---------- commands against a fake API ----------
  const db = newDb();
  const { Pool } = db.adapters.createPg();
  const pool = new Pool();
  await initDb(pool);
  const calls = [];
  const fake = async (method, path, body, reason) => {
    calls.push({ method, path, body, reason });
    if (method === 'GET' && /^\/guilds\/G1(\?|$)/.test(path)) return { id: 'G1', name: 'AutoRejoin', owner_id: 'OWNER', approximate_member_count: 10,
      roles: [{ id: 'G1', position: 0 }, { id: 'MOD', position: 5 }, { id: 'ADMIN', position: 9 }, { id: 'MEMBER', position: 1 }] };
    if (method === 'POST' && path === '/users/@me/channels') return { id: 'DM' };
    if (method === 'POST' && /\/messages$/.test(path)) return { id: 'MSG' };
    if (method === 'GET' && /\/messages\?limit=100/.test(path)) return [
      { id: 'm1', author: { id: 'U2' }, timestamp: new Date().toISOString() }, { id: 'm2', author: { id: 'U3' }, timestamp: new Date().toISOString() },
      { id: 'm3', author: { id: 'U2' }, timestamp: new Date().toISOString() }];
    return null;
  };
  const bot = createGuildBot(pool, { api: fake });
  const mod = { user: { id: 'MODU', username: 'mod' }, roles: ['MOD'], permissions: String(1099511627776n | 4n | 2n | 8192n | 268435456n | 32n) };
  const cmd = (name, options, extra = {}) => bot.handleCommand({ type: 2, guild_id: 'G1', channel_id: 'C1', member: mod, data: { name, options, resolved: extra.resolved }, ...extra.i }, true);
  const sub = (name, s, options = [], extra) => cmd(name, [{ type: 1, name: s, options }], extra);
  const opt = (name, value) => ({ name, value });
  const last = (method, re) => calls.filter((c) => c.method === method && re.test(c.path)).pop();

  // Autorole
  let r = await sub('autorole', 'add', [opt('role', 'MEMBER')]);
  ok(/будет выдаваться/.test(r.data.content) && (await bot.getConfig('G1')).autoroles[0] === 'MEMBER', 'автороль добавлена');
  r = await sub('autorole', 'list');
  ok(/<@&MEMBER>/.test(r.data.content), 'список авторолей');
  const saved = await pool.query("SELECT data FROM guild_config WHERE guild_id = 'G1'");
  ok(JSON.parse(saved.rows[0].data).autoroles[0] === 'MEMBER', 'настройки сохранены в базе');

  // Role panel + buttons
  r = await cmd('rolepanel', [opt('channel', 'C9'), opt('title', 'Pick'), opt('role1', 'R1'), opt('role2', 'R2'), opt('unique', true)],
    { resolved: { roles: { R1: { name: 'EU' }, R2: { name: 'NA' } } } });
  const panel = last('POST', /\/channels\/C9\/messages$/);
  ok(panel && panel.body.components[0].components.map((b) => b.custom_id).join() === 'rr:u:R1,rr:u:R2' && panel.body.components[0].components[0].label === 'EU', 'панель ролей с кнопками отправлена');
  const press = (roles, id) => bot.handleComponent({ type: 3, guild_id: 'G1', member: { user: { id: 'U5' }, roles }, message: { components: panel.body.components }, data: { custom_id: id } }, true);
  r = await press(['R1'], 'rr:u:R2');
  ok(last('DELETE', /roles\/R1$/) && last('PUT', /roles\/R2$/) && /выдана/.test(r.data.content), 'уникальная панель: старая роль снята, новая выдана');
  r = await press(['R2'], 'rr:u:R2');
  ok(/снята/.test(r.data.content), 'повторное нажатие снимает роль');

  // Reaction role
  r = await sub('reactionrole', 'add', [opt('message_link', 'https://discord.com/channels/G1/C2/M2'), opt('emoji', '🐝'), opt('role', 'R3')]);
  ok(last('PUT', /\/channels\/C2\/messages\/M2\/reactions\/.+\/@me$/) && (await bot.getConfig('G1')).reactionRoles[0].key === '🐝', 'роль за реакцию: бот поставил реакцию и сохранил');
  r = await sub('reactionrole', 'add', [opt('message_link', 'https://discord.com/channels/OTHER/C2/M2'), opt('emoji', '🐝'), opt('role', 'R3')]);
  ok(/с этого сервера/.test(r.data.content), 'ссылка на чужой сервер отклонена');

  // Automod config
  r = await sub('automod', 'toggle', [opt('rule', 'enabled'), opt('enabled', true)]);
  ok((await bot.getConfig('G1')).automod.enabled === true && r.data.embeds[0].title.includes('🟢'), 'автомод включён');
  await sub('automod', 'addword', [opt('words', 'Плохое, ещё одно ')]);
  ok((await bot.getConfig('G1')).automod.words.join('|') === 'плохое|ещё одно', 'запрещённые слова добавлены');
  await sub('automod', 'settings', [opt('timeout_after', 2), opt('ignore_role', 'VIP')]);
  ok((await bot.getConfig('G1')).automod.timeoutAfter === 2 && (await bot.getConfig('G1')).automod.ignoreRoles[0] === 'VIP', 'настройки автомода');

  // Moderation + hierarchy
  const target = (id, roles) => ({ resolved: { members: { [id]: { roles } }, users: { [id]: { id } } } });
  r = await cmd('ban', [opt('user', 'ADMINU'), opt('reason', 'x')], target('ADMINU', ['ADMIN']));
  ok(/не ниже твоей/.test(r.data.content) && !last('PUT', /bans\/ADMINU/), 'нельзя забанить того, кто выше');
  r = await cmd('ban', [opt('user', 'MODU')], target('MODU', ['MOD']));
  ok(/к себе/.test(r.data.content), 'нельзя наказать себя');
  r = await cmd('ban', [opt('user', 'OWNER')], target('OWNER', []));
  ok(/владелец/.test(r.data.content), 'нельзя тронуть владельца');
  r = await cmd('ban', [opt('user', 'U2'), opt('reason', 'spam'), opt('delete_days', 1)], target('U2', ['MEMBER']));
  const ban = last('PUT', /\/guilds\/G1\/bans\/U2$/);
  ok(ban && ban.body.delete_message_seconds === 86400 && /mod: spam/.test(ban.reason) && /случай #1/.test(r.data.content), 'бан: запрос, причина в журнале аудита, случай #1');
  r = await cmd('timeout', [opt('user', 'U3'), opt('duration', '1h'), opt('reason', 'flood')], target('U3', ['MEMBER']));
  const to = last('PATCH', /\/members\/U3$/);
  ok(to && Math.abs(new Date(to.body.communication_disabled_until) - Date.now() - 3600000) < 5000, 'мут на 1 час');
  r = await cmd('timeout', [opt('user', 'U3'), opt('duration', '40d')], target('U3', ['MEMBER']));
  ok(/до 28 дней/.test(r.data.content), 'мут больше 28 дней отклонён');
  await cmd('warn', [opt('user', 'U3'), opt('reason', 'caps')], target('U3', ['MEMBER']));
  r = await cmd('warn', [opt('user', 'U3'), opt('reason', 'caps again')], target('U3', ['MEMBER']));
  ok(/Всего предупреждений: \*\*2\*\*/.test(r.data.content) && last('POST', /\/channels\/DM\/messages$/), 'предупреждения считаются, участник получил ЛС');
  r = await cmd('warnings', [opt('user', 'U3')]);
  ok(r.data.embeds[0].description.split('\n').filter((l) => l.startsWith('**#')).length === 3, 'история: мут + 2 предупреждения');
  r = await cmd('clearwarns', [opt('user', 'U3')]);
  ok(/Удалено предупреждений: 2/.test(r.data.content), 'предупреждения очищены');

  // Purge
  r = await cmd('purge', [opt('count', 10), opt('user', 'U2')]);
  const bulk = last('POST', /bulk-delete$/);
  ok(bulk && bulk.body.messages.join() === 'm1,m3', 'очистка: только сообщения нужного участника');

  // Logs, welcome, tags, embed, suggestions, remind
  await sub('log', 'channel', [opt('channel', 'LOG')]);
  ok(last('POST', /\/channels\/LOG\/messages$/), 'канал логов настроен (тестовое сообщение)');
  await cmd('kick', [opt('user', 'U4'), opt('reason', 'bye')], target('U4', ['MEMBER']));
  ok(calls.filter((c) => c.path === '/channels/LOG/messages').some((c) => /Кик/.test(c.body.embeds[0].title)), 'наказание попало в логи');
  await sub('welcome', 'join', [opt('channel', 'WEL'), opt('message', 'Привет {user}! Нас {count}')]);
  r = await sub('welcome', 'test');
  ok(/Привет <@MODU>! Нас 10/.test(r.data.content), 'приветствие: предпросмотр с подстановкой');
  r = await sub('tag', 'create', [opt('name', 'Download'), opt('content', 'Скачать: github\\nСервер: discord')], { i: { member: { ...mod, permissions: '0' } } });
  ok(/Управлять сообщениями/.test(r.data.content), 'тег без прав не создаётся');
  await sub('tag', 'create', [opt('name', 'Download'), opt('content', 'Скачать: github\\nСервер: discord')]);
  r = await sub('tag', 'show', [opt('name', 'download')]);
  ok(r.data.content === 'Скачать: github\nСервер: discord' && !r.data.flags, 'тег показан всем, с переносом строки');
  r = await bot.handleAutocomplete({ guild_id: 'G1', data: { name: 'tag', options: [{ type: 1, name: 'show', options: [{ name: 'name', value: 'down', focused: true }] }] } });
  ok(r.data.choices[0].value === 'download', 'автодополнение тегов');
  await cmd('embed', [opt('channel', 'ANN'), opt('title', 'Update'), opt('description', 'a\\nb'), opt('color', '#30A46C')]);
  const emb = last('POST', /\/channels\/ANN\/messages$/);
  ok(emb.body.embeds[0].color === 0x30A46C && emb.body.embeds[0].description === 'a\nb', 'embed с цветом и переносом');
  r = await cmd('suggest', [opt('text', 'dark theme')]);
  ok(/не выбрал канал/.test(r.data.content), 'предложение без канала — подсказка');
  await cmd('suggestions', [opt('channel', 'SUG')]);
  r = await cmd('suggest', [opt('text', 'dark theme')]);
  ok(last('POST', /\/channels\/SUG\/messages$/) && calls.filter((c) => /SUG\/messages\/MSG\/reactions/.test(c.path)).length === 2, 'предложение отправлено с 👍/👎');
  r = await cmd('remind', [opt('in', '1h'), opt('text', 'check farm')]);
  const rem = await pool.query('SELECT text FROM reminders');
  ok(/Напомню/.test(r.data.content) && rem.rows[0].text === 'check farm', 'напоминание сохранено');
  r = await cmd('remind', [opt('in', 'soon'), opt('text', 'x')]);
  ok(/10m, 1h30m/.test(r.data.content), 'неверное время напоминания — подсказка');

  // Bump reminders (DISBOARD + Discadia)
  ok(parseDisboard('Bump done! :thumbsup:\nCheck it out on DISBOARD.').ok && parseDisboard('Сервер поднят! 👍').ok, 'DISBOARD: успешный бамп (en/ru)');
  ok(parseDisboard('<@1>, Please wait another 87 minutes until the server can be bumped').waitMinutes === 87 && parseDisboard('Подождите ещё 15 минут').waitMinutes === 15, 'DISBOARD: «подожди N минут»');
  ok(parseDisboard('Welcome to DISBOARD') === null, 'DISBOARD: посторонний ответ не считается');
  const d1 = parseDiscadia('Auto Rejoin bumped. +1 Boost earned. 1 Boost this month · Next bump <t:1795300000:R>');
  const d2 = parseDiscadia('Already bumped. You can bump again <t:1795310000:R>.');
  ok(d1.ok && d1.nextAt === 1795300000000 && !d2.ok && d2.nextAt === 1795310000000 && parseDiscadia('Invite changed') === null, 'Discadia: бамп и «уже бампнут» со временем следующего');
  let clock = new Date('2026-11-21T10:00:00Z').getTime();
  const bb = createGuildBot(pool, { api: fake, now: () => new Date(clock) });
  const bcmd = (s, options = []) => bb.handleCommand({ type: 2, guild_id: 'G1', channel_id: 'C1', member: mod, data: { name: 'bumps', options: [{ type: 1, name: s, options }] } }, true);
  r = await bcmd('setup', [opt('channel', 'BUMP'), opt('role', 'BUMPER')]);
  ok(/<#BUMP>/.test(r.data.content) && dueBumps(await bb.getConfig('G1'), clock).join() === 'disboard,discadia', 'бамп: настроен, первые напоминания сразу');
  calls.length = 0;
  await bb._bumpTick();
  const rem1 = calls.filter((c) => c.method === 'POST' && c.path === '/channels/BUMP/messages');
  ok(rem1.length === 2 && /<@&BUMPER>/.test(rem1[0].body.content) && /\/bump/.test(rem1[0].body.content) && /DISBOARD/.test(rem1[0].body.content) && /Discadia/.test(rem1[1].body.content) && rem1[0].body.allowed_mentions.roles[0] === 'BUMPER', 'бамп: напоминания DISBOARD и Discadia с пингом роли');
  calls.length = 0;
  await bb._bumpTick();
  ok(calls.length === 0, 'бамп: повторно не напоминает');
  const sentMsgs = [];
  const botMsg = (botId, text, user) => ({ guild: { id: 'G1' }, author: { id: botId, bot: true }, content: '', embeds: [{ description: text }],
    interactionMetadata: user ? { user: { id: user } } : null, channel: { send: async (m) => { sentMsgs.push(m); } } });
  const DB = '302050872383242240', DC = '1222548162741538938';
  await bb._onBumpBot(botMsg(DB, 'Bump done! :thumbsup:', 'U7'));
  let bc = await bb.getConfig('G1');
  ok(bc.bump.counts.U7 === 1 && Date.parse(bc.bump.disboardNext) === clock + 2 * 3600000 && /<@U7>.*DISBOARD.*Бампов: \*\*1\*\*/.test(sentMsgs[0].content), 'бамп: успешный /bump DISBOARD — спасибо, счёт, следующий через 2 ч');
  ok(calls.some((c) => c.method === 'DELETE' && c.path === '/channels/BUMP/messages/MSG'), 'бамп: старое напоминание удалено');
  await bb._onBumpBot(botMsg(DB, 'Please wait another 30 minutes until the server can be bumped', 'U8'));
  bc = await bb.getConfig('G1');
  ok(Date.parse(bc.bump.disboardNext) === clock + 30 * 60000 && !bc.bump.counts.U8, 'бамп: «подожди 30 минут» — напоминание переносится, не засчитывается');
  clock += 31 * 60000;
  calls.length = 0;
  await bb._bumpTick();
  ok(calls.filter((c) => c.path === '/channels/BUMP/messages').length === 1, 'бамп: через 30 мин снова напоминает о DISBOARD');
  const nextTs = Math.floor(clock / 1000) + 86400;
  await bb._onBumpBot(botMsg(DC, `Auto Rejoin bumped. +1 Boost earned. · Next bump <t:${nextTs}:R>`, 'U9'));
  bc = await bb.getConfig('G1');
  ok(bc.bump.counts.U9 === 1 && Date.parse(bc.bump.discadiaNext) === nextTs * 1000 && /Discadia/.test(sentMsgs[1].content), 'бамп: Discadia — спасибо, счёт, следующий по времени из ответа');
  await bb._onBumpBot(botMsg(DC, `Already bumped. You can bump again <t:${nextTs - 3600}:R>.`, 'U9'));
  bc = await bb.getConfig('G1');
  ok(bc.bump.counts.U9 === 1 && Date.parse(bc.bump.discadiaNext) === (nextTs - 3600) * 1000, 'бамп: Discadia «уже бампнут» — время уточнено, не засчитывается');
  r = await bcmd('status');
  ok(/1\. <@U7> — 1/.test(r.data.content) && /Discadia: следующий/.test(r.data.content), 'бамп: статус с топом бамперов');
  await bcmd('off');
  ok(dueBumps(await bb.getConfig('G1'), clock + 99 * 3600000).length === 0, 'бамп: выключен — напоминаний нет');
  console.log(`\nВсе проверки управления сервером пройдены: ${passed}`);
  process.exit(0);
})().catch((e) => { console.error('ПРОВАЛ:', e); process.exit(1); });
