'use strict';
// Discord bot for AutoRejoin — HTTP interactions only (no gateway connection), so it runs on the free Render plan.
// Link: Settings → Link bot in the app gives a one-time 6-digit code; in Discord the "Link account" button opens a
// form for that code. The code links exactly the account that requested it; one Discord user <-> one account.
// Status and control: the app syncs its accounts every ~10 s and picks up queued commands (start/stop/rejoin).
// Agent state and the command queue live in memory on purpose: nothing about accounts is written to the database,
// and frequent syncs do not keep the free database awake.
const crypto = require('crypto');
const { createGuildBot, GUILD_SCHEMA, GUILD_COMMANDS } = require('./guild');
const { createFarm, FARM_SCHEMA } = require('./farm');

const CODE_TTL = 10 * 60 * 1000;

const DISCORD_SCHEMA = [
  `CREATE TABLE IF NOT EXISTS discord_links (
     discord_id TEXT PRIMARY KEY,
     user_id INTEGER NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
     discord_name TEXT,
     created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
   )`
].concat(GUILD_SCHEMA, FARM_SCHEMA);

// ---------- slash command definitions ----------
const CTX = { integration_types: [0, 1], contexts: [0, 1, 2] };
const COMMANDS = [
  { name: 'panel', description: 'Post the AutoRejoin panel with the "Link account" button',
    description_localizations: { ru: 'Отправить панель AutoRejoin с кнопкой «Привязать аккаунт»', uk: 'Надіслати панель AutoRejoin з кнопкою «Прив’язати акаунт»' }, ...CTX },
  { name: 'status', description: 'Show your accounts and control AutoRejoin',
    description_localizations: { ru: 'Показать аккаунты и управлять AutoRejoin', uk: 'Показати акаунти й керувати AutoRejoin' }, ...CTX },
  { name: 'start', description: 'Start AutoRejoin on your PC', description_localizations: { ru: 'Запустить AutoRejoin на ПК', uk: 'Запустити AutoRejoin на ПК' }, ...CTX },
  { name: 'stop', description: 'Stop AutoRejoin and close Roblox windows', description_localizations: { ru: 'Остановить AutoRejoin и закрыть окна Роблокса', uk: 'Зупинити AutoRejoin і закрити вікна Роблокса' }, ...CTX },
  { name: 'rejoin', description: 'Rejoin one account or all of them',
    description_localizations: { ru: 'Перезайти одним аккаунтом или всеми', uk: 'Перезайти одним акаунтом або всіма' },
    options: [{ type: 3, name: 'account', description: 'Account (or "all")', required: true, autocomplete: true,
      description_localizations: { ru: 'Аккаунт (или «все»)', uk: 'Акаунт (або «усі»)' } }], ...CTX },
  { name: 'screen', description: 'Screenshot of one account\'s Roblox window',
    description_localizations: { ru: 'Скриншот окна Roblox одного аккаунта', uk: 'Скриншот вікна Roblox одного акаунта' }, ...CTX },
  { name: 'unlink', description: 'Unlink Discord from AutoRejoin', description_localizations: { ru: 'Отвязать Discord от AutoRejoin', uk: 'Відв’язати Discord від AutoRejoin' }, ...CTX }
];

function verifier(publicKeyHex) {
  if (!publicKeyHex) return null;
  const key = crypto.createPublicKey({ key: Buffer.concat([Buffer.from('302a300506032b6570032100', 'hex'), Buffer.from(publicKeyHex, 'hex')]), format: 'der', type: 'spki' });
  return (signature, timestamp, body) => {
    try { return crypto.verify(null, Buffer.concat([Buffer.from(timestamp), body]), key, Buffer.from(signature, 'hex')); }
    catch { return false; }
  };
}

function ago(ms, ru) {
  const m = Math.max(0, Math.round(ms / 60000));
  if (m < 1) return ru ? 'только что' : 'just now';
  if (m < 60) return ru ? `${m} мин назад` : `${m} min ago`;
  const h = Math.floor(m / 60);
  return ru ? `${h} ч назад` : `${h} h ago`;
}

function clip(s, n) { s = String(s == null ? '' : s); return s.length > n ? s.slice(0, n - 1) + '…' : s; }
function esc(s) { return String(s).replace(/([\\*_`~|>])/g, '\\$1'); }

function createDiscord(pool, options = {}) {
  const now = options.now || (() => new Date());
  const verify = options.verify || verifier(options.publicKey);
  const codes = new Map();   // code -> { userId, exp }
  const farm = options.farm || createFarm(pool, { now });
  // All links are held in memory (loaded once; every change goes through this process), so Discord replies
  // never wait for the database — a sleeping free database takes seconds to wake, Discord allows 3.
  const links = new Map();   // discordId -> { userId, nickname, name }
  let warmed = null;
  const guild = options.guildBot || createGuildBot(pool, { token: options.token, now, api: options.guildApi });

  function warm() {
    if (!warmed) {
      warmed = pool.query('SELECT l.discord_id, l.user_id, l.discord_name, u.nickname FROM discord_links l JOIN users u ON u.id = l.user_id')
        .then(({ rows }) => { for (const r of rows) links.set(r.discord_id, { userId: r.user_id, nickname: r.nickname, name: r.discord_name }); })
        .catch((e) => { warmed = null; throw e; });
    }
    return warmed;
  }
  async function linkOf(discordId) {
    await warm();
    return links.get(discordId) || null;
  }
  async function linkedForUser(userId) {
    await warm();
    for (const v of links.values()) if (v.userId === userId) return v;
    return null;
  }
  function forgetUser(userId) {
    for (const [k, v] of links) if (v.userId === userId) links.delete(k);
  }

  // ---------- app side (called with an authenticated user) ----------
  function newCode(userId) {
    const t = now().getTime();
    for (const [c, v] of codes) if (v.exp < t || v.userId === userId) codes.delete(c);
    let code;
    do { code = String(crypto.randomInt(0, 1000000)).padStart(6, '0'); } while (codes.has(code));
    codes.set(code, { userId, exp: t + CODE_TTL });
    return { code, expiresIn: CODE_TTL / 1000 };
  }

  async function linkInfo(userId) {
    const v = await linkedForUser(userId);
    return v ? { linked: true, name: v.name } : { linked: false };
  }

  async function unlinkUser(userId) {
    await warm();
    await pool.query('DELETE FROM discord_links WHERE user_id = $1', [userId]);
    forgetUser(userId);
  }

  // The app's reports go to the farm (several PCs per account); kept here for the old single-PC callers.
  async function sync(userId, body) {
    const linked = await linkedForUser(userId);
    if (!linked) return { linked: false, commands: [] };
    return { linked: true, commands: farm.sync(userId, body || {}) };
  }

  // For the website dashboard: every PC with its last report.
  async function webState(userId) {
    const linked = await linkedForUser(userId);
    const pcs = farm.view(userId);
    const online = pcs.find((p) => p.online);
    return { linked: !!linked, discordName: linked ? linked.name || null : null, online: !!online,
      lastSeen: pcs[0] && pcs[0].lastSeen, state: online ? online.state : null, pcs };
  }

  function enqueue(userId, cmd, arg, pcId) { return farm.enqueue(userId, cmd, arg, pcId); }

  // ---------- Discord side ----------
  function statusMessage(link, ru, note) {
    const t = now().getTime();
    const pcs = farm.view(link.userId);
    const online = pcs.filter((p) => p.online);
    const multi = pcs.length > 1;
    let color = 0x6E6D85, head;
    const all = farm.accounts(link.userId).filter((a) => a.enabled);
    const inGame = all.filter((a) => a.state === 'ingame' || a.state === 'script').length;
    const running = online.some((p) => p.state && p.state.running);
    if (!online.length) {
      const last = pcs[0] && pcs[0].lastSeen ? Date.parse(pcs[0].lastSeen) : null;
      head = last
        ? (ru ? `⚫ Программа не на связи (была ${ago(t - last, true)}). Открой AutoRejoin на ПК.` : `⚫ The app is offline (last seen ${ago(t - last, false)}). Open AutoRejoin on your PC.`)
        : (ru ? '⚫ Программа не на связи. Открой AutoRejoin на ПК и войди в аккаунт Топа АФК.' : '⚫ The app is offline. Open AutoRejoin on your PC and sign in to Top AFK.');
    } else if (!running) {
      head = ru ? '⏸️ AutoRejoin остановлен' : '⏸️ AutoRejoin is stopped'; color = 0xE5484D;
    } else {
      head = ru ? `🟢 Работает · в игре **${inGame} из ${all.length}**${multi ? ` · ПК: ${online.length}` : ''}` : `🟢 Running · in game **${inGame} of ${all.length}**${multi ? ` · PCs: ${online.length}` : ''}`;
      color = inGame === all.length && all.length ? 0x30A46C : 0xF5A524;
    }
    const lines = [];
    for (const pc of (multi ? pcs : online)) {
      const accs = pc.online && pc.state ? pc.state.accounts.filter((a) => a.enabled) : [];
      if (multi) {
        const pcHead = !pc.online ? (ru ? 'не на связи' : 'offline') : !pc.state.running ? (ru ? 'остановлен' : 'stopped') : (ru ? 'работает' : 'running');
        lines.push(`🖥️ **${esc(pc.name)}** — ${pcHead}`);
      }
      for (const a of accs.slice(0, 20)) {
        const ok = a.state === 'ingame' || a.state === 'script';
        lines.push(`${multi ? '> ' : ''}${ok ? '🟢' : a.state === 'joining' || a.state === 'launching' || a.state === 'loading' ? '🟡' : '🔴'} **${esc(a.name)}**${a.main ? ' ⭐' : ''} — ${esc(a.text || a.state)}${ok && a.uptime ? ` · ${esc(a.uptime)}` : ''}`);
      }
      if (pc.online && !accs.length) lines.push(ru ? '_Нет включённых аккаунтов._' : '_No enabled accounts._');
    }
    const embed = {
      title: `AutoRejoin · ${link.nickname}`,
      description: [head, '', ...lines].join('\n').slice(0, 4000),
      color,
      footer: { text: note || (ru ? 'Команды выполняются в течение ~10 сек' : 'Commands run within ~10 s') },
      timestamp: new Date(t).toISOString()
    };
    const anyOnline = online.length > 0;
    const allPhaseRunning = online.some((p) => p.state && p.state.running && p.state.phase === 'running');
    const components = [{ type: 1, components: [
      { type: 2, style: 2, custom_id: 'ar:refresh', emoji: { name: '🔄' }, label: ru ? 'Обновить' : 'Refresh' },
      { type: 2, style: 3, custom_id: 'ar:start', emoji: { name: '▶️' }, label: ru ? 'Запустить' : 'Start', disabled: !anyOnline || online.every((p) => p.state.running) },
      { type: 2, style: 4, custom_id: 'ar:stop', emoji: { name: '⏹️' }, label: ru ? 'Остановить' : 'Stop', disabled: !anyOnline || !running },
      { type: 2, style: 1, custom_id: 'ar:rejoinall', emoji: { name: '🔁' }, label: ru ? 'Перезайти всеми' : 'Rejoin all', disabled: !allPhaseRunning },
      { type: 2, style: 2, custom_id: 'ar:screenpick', emoji: { name: '📸' }, label: ru ? 'Скриншот' : 'Screenshot', disabled: !all.length }
    ] }];
    if (allPhaseRunning && all.length) {
      components.push({ type: 1, components: [{ type: 3, custom_id: 'ar:rejoin', placeholder: ru ? 'Перезайти одним аккаунтом…' : 'Rejoin one account…',
        options: all.slice(0, 25).map((a) => ({ label: clip(a.name + (multi ? ` · ${a.pcName}` : ''), 100), value: a.id, description: clip(a.text || a.state, 100) || undefined })) }] });
    }
    return { embeds: [embed], components, flags: 64 };
  }

  // /screen: pick an account → the app captures its window → the picture replaces the "taking a screenshot…" message.
  function screenPicker(link, ru) {
    const accs = farm.accounts(link.userId).filter((a) => a.enabled);
    if (!accs.length) return { content: ru ? '⚫ Нет аккаунтов на связи. Открой AutoRejoin на ПК.' : '⚫ No accounts online. Open AutoRejoin on your PC.', components: [], flags: 64 };
    const multi = new Set(accs.map((a) => a.pcId)).size > 1;
    return {
      content: ru ? '📸 **Скриншот какого аккаунта?**' : '📸 **Screenshot of which account?**', embeds: [], flags: 64,
      components: [{ type: 1, components: [{ type: 3, custom_id: 'ar:screen', placeholder: ru ? 'Выбери аккаунт…' : 'Pick an account…',
        options: accs.slice(0, 25).map((a) => ({ label: clip(`${a.name}${a.main ? ' ⭐' : ''}${multi ? ` · ${a.pcName}` : ''}`, 100), value: a.id,
          description: clip(a.text || a.state, 100) || undefined, emoji: { name: a.main ? '⭐' : '🐝' } })) }] }]
    };
  }

  async function editOriginal(token, payload, file) {
    if (!options.appId) return;
    const url = `https://discord.com/api/v10/webhooks/${options.appId}/${token}/messages/@original`;
    if (!file) {
      await fetch(url, { method: 'PATCH', headers: { 'content-type': 'application/json' }, body: JSON.stringify(payload) });
      return;
    }
    const form = new FormData();
    form.append('payload_json', JSON.stringify({ ...payload, attachments: [{ id: 0, filename: file.name }] }));
    form.append('files[0]', new Blob([file.data], { type: 'image/jpeg' }), file.name);
    const res = await fetch(url, { method: 'PATCH', body: form });
    if (!res.ok) console.error('Discord: screenshot upload failed:', res.status, (await res.text()).slice(0, 200));
  }

  function takeScreen(link, accountId, token, ru) {
    const req = farm.requestScreen(link.userId, accountId, async (s) => {
      if (s.error || !s.image) {
        await editOriginal(token, { content: (ru ? '⚠️ Не получилось сделать скриншот: ' : '⚠️ Could not take the screenshot: ') + esc(s.error || '?'), embeds: [], components: [] });
        return;
      }
      await editOriginal(token, {
        content: '', components: [],
        embeds: [{ title: `📸 ${s.accountName}${s.pcName ? ` · ${s.pcName}` : ''}`, color: 0xA855F7, image: { url: 'attachment://screen.jpg' }, timestamp: now().toISOString() }]
      }, { name: 'screen.jpg', data: s.image });
    });
    if (!req) return { content: ru ? '❌ Этот аккаунт сейчас не на связи.' : '❌ This account is offline right now.', components: [], embeds: [] };
    // No answer within a minute: say so instead of leaving "taking a screenshot…" forever.
    setTimeout(() => {
      const info = farm.screenInfo(link.userId, req.id);
      if (info && !info.ready && !info.error) farm.deliverScreen(link.userId, req.id, null, ru ? 'программа не ответила за минуту' : 'the app did not answer within a minute');
    }, options.screenTimeoutMs || 60000).unref?.();
    return { content: ru ? `📸 Делаю скриншот **${esc(req.accountName)}**… (обычно до 15 сек)` : `📸 Taking a screenshot of **${esc(req.accountName)}**… (usually under 15 s)`, components: [], embeds: [] };
  }

  const ephemeral = (content, components) => ({ type: 4, data: { content, flags: 64, ...(components ? { components } : {}) } });
  const linkButton = (ru) => ({ type: 2, style: 1, custom_id: 'ar:link', emoji: { name: '🔗' }, label: ru ? 'Привязать аккаунт' : 'Link account' });
  const notLinked = (ru) => ephemeral(ru
    ? '🔗 Discord ещё не привязан. В AutoRejoin открой **Настройки → Привязать бота**, получи код и нажми кнопку ниже.'
    : '🔗 Discord is not linked yet. In AutoRejoin open **Settings → Link bot**, get a code and press the button below.', [{ type: 1, components: [linkButton(ru)] }]);
  const linkModal = (ru) => ({ type: 9, data: { custom_id: 'ar:linkmodal', title: ru ? 'Привязка AutoRejoin' : 'Link AutoRejoin',
    components: [{ type: 1, components: [{ type: 4, custom_id: 'code', style: 1, min_length: 6, max_length: 6, required: true,
      label: ru ? 'Код из AutoRejoin' : 'Code from AutoRejoin', placeholder: ru ? 'Настройки → Привязать бота → 6 цифр' : 'Settings → Link bot → 6 digits' }] }] } });
  const panel = (ru) => ({ type: 4, data: {
    embeds: [{ title: '🐝 AutoRejoin', color: 0x8B5CF6,
      description: ru
        ? 'Управляй AutoRejoin прямо из Discord: смотри, какие аккаунты стоят, запускай, останавливай и перезаходи.\n\n**Как привязать:**\n1. В программе: **Настройки → Привязать бота** — появится код.\n2. Здесь: кнопка **«Привязать аккаунт»** → введи код.\n\nКод одноразовый, живёт 10 минут и привязывает именно тот аккаунт, который его запросил. Все ответы бота видишь только ты.'
        : 'Control AutoRejoin right from Discord: see which accounts are in game, start, stop and rejoin.\n\n**How to link:**\n1. In the app: **Settings → Link bot** — you get a code.\n2. Here: press **"Link account"** and enter the code.\n\nThe code works once, for 10 minutes, and links exactly the account that requested it. Only you see the bot\'s replies.' }],
    components: [{ type: 1, components: [linkButton(ru), { type: 2, style: 2, custom_id: 'ar:status', emoji: { name: '📋' }, label: ru ? 'Мои аккаунты' : 'My accounts' }] }] } });

  const attempts = new Map(); // discordId -> { n, reset } — brute-force guard for codes
  async function tryLink(user, code, ru) {
    const t = now().getTime();
    let a = attempts.get(user.id);
    if (!a || a.reset < t) { a = { n: 0, reset: t + CODE_TTL }; attempts.set(user.id, a); }
    if (a.n >= 5) return ephemeral(ru ? '⛔ Слишком много неверных кодов. Подожди 10 минут.' : '⛔ Too many wrong codes. Wait 10 minutes.');
    const entry = codes.get(String(code || '').trim());
    if (!entry || entry.exp < t) {
      a.n++;
      return ephemeral(ru ? `❌ Код неверный или устарел (осталось попыток: ${5 - a.n}). Получи новый в AutoRejoin → Настройки → Привязать бота.` : `❌ Wrong or expired code (${5 - a.n} tries left). Get a new one in AutoRejoin → Settings → Link bot.`);
    }
    codes.delete(String(code).trim());
    attempts.delete(user.id);
    // One Discord user <-> one AutoRejoin account: drop any previous link on either side.
    await warm();
    const name = clip(user.global_name || user.username || '', 64);
    await pool.query('DELETE FROM discord_links WHERE discord_id = $1 OR user_id = $2', [user.id, entry.userId]);
    await pool.query('INSERT INTO discord_links (discord_id, user_id, discord_name) VALUES ($1, $2, $3)', [user.id, entry.userId, name]);
    const nick = (await pool.query('SELECT nickname FROM users WHERE id = $1', [entry.userId])).rows[0].nickname;
    forgetUser(entry.userId);
    links.delete(user.id);
    links.set(user.id, { userId: entry.userId, nickname: nick, name });
    const link = links.get(user.id);
    return ephemeral(ru ? `✅ Привязано к аккаунту **${esc(link.nickname)}**. Жми «Мои аккаунты» или пиши \`/status\`.` : `✅ Linked to **${esc(link.nickname)}**. Press "My accounts" or type \`/status\`.`,
      [{ type: 1, components: [{ type: 2, style: 2, custom_id: 'ar:status', emoji: { name: '📋' }, label: ru ? 'Мои аккаунты' : 'My accounts' }] }]);
  }
  const cmdNames = { start: ['Запустить', 'Start'], stop: ['Остановить', 'Stop'], 'rejoin-all': ['Перезайти всеми', 'Rejoin all'], rejoin: ['Перезайти', 'Rejoin'] };
  const queuedNote = (ru, cmd, extra) => (ru ? `⏳ Команда «${cmdNames[cmd][0]}${extra ? ' ' + extra : ''}» отправлена — выполнится в течение ~10 сек` : `⏳ "${cmdNames[cmd][1]}${extra ? ' ' + extra : ''}" sent — runs within ~10 s`);

  function accountName(userId, id) {
    const acc = farm.findAccount(userId, id);
    return acc ? acc.name : null;
  }

  async function handle(i) {
    const ru = /^(ru|uk)/.test(String(i.locale || ''));
    const user = (i.member && i.member.user) || i.user || {};
    if (i.type === 1) return { type: 1 };

    // Server-management commands (roles, automod, moderation, logs, welcome, tags…)
    if (i.type === 4 && guild.isCommand(i.data.name)) return guild.handleAutocomplete(i);
    if (i.type === 2 && guild.isCommand(i.data.name)) return guild.handleCommand(i, ru);
    if (i.type === 3 && String(i.data.custom_id).startsWith('rr:')) return guild.handleComponent(i, ru);

    // Autocomplete for /rejoin account
    if (i.type === 4) {
      const link = await linkOf(user.id);
      const focused = ((i.data.options || []).find((o) => o.focused) || {}).value || '';
      const accs = link ? farm.accounts(link.userId).filter((a) => a.enabled) : [];
      const choices = [{ name: ru ? '🔁 Все аккаунты' : '🔁 All accounts', value: '*' }]
        .concat(accs.map((a) => ({ name: clip(`${a.name}${a.main ? ' ⭐' : ''} — ${a.text || a.state}`, 100), value: a.id })))
        .filter((c) => !focused || c.name.toLowerCase().includes(String(focused).toLowerCase()))
        .slice(0, 25);
      return { type: 8, data: { choices } };
    }

    if (i.type === 2) {
      const name = i.data.name;
      if (name === 'panel') return panel(ru);
      const link = await linkOf(user.id);
      if (!link) return notLinked(ru);
      if (name === 'unlink') {
        await unlinkUser(link.userId);
        return ephemeral(ru ? '✅ Discord отвязан.' : '✅ Discord unlinked.');
      }
      if (name === 'status') return { type: 4, data: statusMessage(link, ru) };
      if (name === 'screen') return { type: 4, data: screenPicker(link, ru) };
      if (name === 'start' || name === 'stop') {
        enqueue(link.userId, name);
        return { type: 4, data: statusMessage(link, ru, queuedNote(ru, name)) };
      }
      if (name === 'rejoin') {
        const value = String(((i.data.options || [])[0] || {}).value || '');
        if (value === '*' || /^(all|все|усі)$/i.test(value)) { enqueue(link.userId, 'rejoin-all'); return { type: 4, data: statusMessage(link, ru, queuedNote(ru, 'rejoin-all')) }; }
        const accName = accountName(link.userId, value);
        if (!accName) return ephemeral(ru ? '❌ Такого аккаунта нет. Выбери из списка подсказок.' : '❌ No such account. Pick one from the suggestions.');
        enqueue(link.userId, 'rejoin', value);
        return { type: 4, data: statusMessage(link, ru, queuedNote(ru, 'rejoin', accName)) };
      }
      return ephemeral('?');
    }

    // Modal submit: the code typed after pressing "Link account"
    if (i.type === 5 && i.data.custom_id === 'ar:linkmodal') {
      const field = ((i.data.components || [])[0] || {}).components || [];
      return tryLink(user, (field[0] || {}).value, ru);
    }

    if (i.type === 3) {
      const id = i.data.custom_id;
      if (id === 'ar:link') return linkModal(ru);
      const link = await linkOf(user.id);
      if (!link) return notLinked(ru);
      if (id === 'ar:status') return { type: 4, data: statusMessage(link, ru) };
      if (id === 'ar:refresh') return { type: 7, data: statusMessage(link, ru) };
      if (id === 'ar:screenpick') return { type: 4, data: screenPicker(link, ru) };
      if (id === 'ar:screen') return { type: 7, data: takeScreen(link, String((i.data.values || [])[0] || ''), i.token, ru) };
      if (id === 'ar:start' || id === 'ar:stop') { const cmd = id.slice(3); enqueue(link.userId, cmd); return { type: 7, data: statusMessage(link, ru, queuedNote(ru, cmd)) }; }
      if (id === 'ar:rejoinall') { enqueue(link.userId, 'rejoin-all'); return { type: 7, data: statusMessage(link, ru, queuedNote(ru, 'rejoin-all')) }; }
      if (id === 'ar:rejoin') {
        const value = String((i.data.values || [])[0] || '');
        const accName = accountName(link.userId, value);
        if (accName) enqueue(link.userId, 'rejoin', value);
        return { type: 7, data: statusMessage(link, ru, accName ? queuedNote(ru, 'rejoin', accName) : undefined) };
      }
    }
    return ephemeral('?');
  }

  // Express handler; needs the raw body for signature checks.
  async function interactions(req, res, next) {
    try {
      if (!verify) return res.status(503).json({ error: 'Discord bot is not configured.' });
      const sig = req.get('x-signature-ed25519') || '', ts = req.get('x-signature-timestamp') || '';
      const raw = Buffer.isBuffer(req.body) ? req.body : Buffer.from('');
      if (!sig || !ts || !verify(sig, ts, raw)) return res.status(401).send('invalid request signature');
      let i;
      try { i = JSON.parse(raw.toString('utf8')); } catch { return res.status(400).send('bad json'); }
      // Discord gives 3 s. If the reply is not ready in ~2 s (database waking up), acknowledge now
      // ("thinking…") and put the real reply in place when it is ready.
      const work = handle(i);
      const late = Symbol('late');
      const first = await Promise.race([work, new Promise((r) => setTimeout(() => r(late), options.deferAfterMs || 2000))]);
      if (first !== late) return res.json(first);
      if (i.type === 4 || !options.appId) return res.json(await work); // autocomplete cannot be deferred
      res.json(i.type === 3 ? { type: 6 } : { type: 5, data: { flags: 64 } });
      let result;
      try { result = await work; } catch (e) { console.error('Discord: deferred reply failed:', e.message); result = ephemeral('⚠️ Server error, try again.'); }
      if (!result || !result.data || result.type === 9) return;
      const { flags, ...data } = result.data;
      await fetch(`https://discord.com/api/v10/webhooks/${options.appId}/${i.token}/messages/@original`, {
        method: 'PATCH', headers: { 'content-type': 'application/json' }, body: JSON.stringify(data)
      }).catch((e) => console.error('Discord: follow-up failed:', e.message));
    } catch (e) { next(e); }
  }

  async function registerCommands() {
    if (!options.appId || !options.token) return false;
    const res = await fetch(`https://discord.com/api/v10/applications/${options.appId}/commands`, {
      method: 'PUT',
      headers: { authorization: `Bot ${options.token}`, 'content-type': 'application/json' },
      body: JSON.stringify(COMMANDS.concat(GUILD_COMMANDS))
    });
    if (!res.ok) throw new Error(`Discord ${res.status}: ${(await res.text()).slice(0, 300)}`);
    return true;
  }

  // A link code from the app works for Telegram too: telegram.js consumes it here.
  function consumeCode(code) {
    const t = now().getTime();
    const entry = codes.get(String(code || '').trim());
    if (!entry || entry.exp < t) return null;
    codes.delete(String(code).trim());
    return entry.userId;
  }

  return { interactions, handle, newCode, consumeCode, linkInfo, unlinkUser, sync, registerCommands, enqueue, webState, linkedForUser, warm, guild, farm };
}

module.exports = { createDiscord, DISCORD_SCHEMA, COMMANDS };
