'use strict';
// Server-management features of the AutoRejoin bot (Carl-bot style):
// roles (autorole, button panels, reaction roles), automod, moderation with cases, logs, welcome/leave,
// tags, embeds, suggestions, reminders and bump reminders (DISBOARD every 2 h, Discadia every 24 h).
// Bots can't run another bot's /bump (and DISBOARD bans automated bumping), so the bot does everything around it:
// sees every /bump reply of DISBOARD and Discadia, thanks and counts the bumper, and pings the bump role the moment
// the next bump is possible.
// Slash commands and buttons arrive over HTTP interactions (discord.js handles the rest: member joins,
// messages, reactions — these need a gateway connection). Guild settings are cached in memory so events
// never wait for the (sleepy, free) database.

const API = 'https://discord.com/api/v10';

const GUILD_SCHEMA = [
  `CREATE TABLE IF NOT EXISTS guild_config (guild_id TEXT PRIMARY KEY, data TEXT NOT NULL)`,
  `CREATE TABLE IF NOT EXISTS mod_cases (
     id SERIAL PRIMARY KEY, guild_id TEXT NOT NULL, user_id TEXT NOT NULL, mod_id TEXT NOT NULL,
     action TEXT NOT NULL, reason TEXT, created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())`,
  `CREATE TABLE IF NOT EXISTS reminders (
     id SERIAL PRIMARY KEY, user_id TEXT NOT NULL, channel_id TEXT NOT NULL, text TEXT NOT NULL, due_at TIMESTAMPTZ NOT NULL)`
];

const PERM = { KICK: 2n, BAN: 4n, ADMIN: 8n, MANAGE_GUILD: 32n, MANAGE_MESSAGES: 8192n, MANAGE_ROLES: 268435456n, MODERATE: 1099511627776n };
const COLOR = { brand: 0x8B5CF6, green: 0x30A46C, red: 0xE5484D, yellow: 0xF5A524, gray: 0x6E6D85 };
const AUTOMOD_RULES = ['spam', 'invites', 'links', 'caps', 'mentions', 'badwords'];
const DEFAULT_ALLOWED = ['github.com', 'roblox.com', 'youtube.com', 'youtu.be', 'tenor.com', 'giphy.com', 'discord.com', 'cdn.discordapp.com', 'media.discordapp.net'];

function defaultConfig() {
  return {
    autoroles: [],
    reactionRoles: [],
    automod: { enabled: false, spam: true, invites: true, links: false, caps: true, mentions: true, badwords: true,
      words: [], maxMentions: 5, timeoutAfter: 3, timeoutMinutes: 10, ignoreRoles: [], allowDomains: DEFAULT_ALLOWED.slice() },
    logChannel: null,
    welcome: { channel: null, message: null, leaveChannel: null, leaveMessage: null, dm: null },
    tags: {},
    suggestChannel: null,
    bump: { channel: null, role: null, disboardNext: null, disboardReminded: false, disboardMsg: null,
      discadiaNext: null, discadiaReminded: false, discadiaMsg: null, counts: {} }
  };
}

const DISBOARD_ID = '302050872383242240';
const DISCADIA_ID = '1222548162741538938';
const DISBOARD_COOLDOWN = 2 * 3600 * 1000;
const DISCADIA_COOLDOWN = 24 * 3600 * 1000;

// What a DISBOARD reply says: { ok: true } for a successful bump, { waitMinutes } when it's too early, null otherwise.
function parseDisboard(text) {
  const t = String(text || '');
  const wait = t.match(/(?:wait|подожд|ещё|еще|осталось)[^\d]{0,40}(\d+)\s*(?:minutes?|mins?|минут|мин)/i);
  if (wait) return { waitMinutes: Number(wait[1]) };
  if (/bump done|:thumbsup:|👍|поднят|выполнен/i.test(t)) return { ok: true };
  return null;
}

// What a Discadia /bump reply says: "Auto Rejoin bumped. … Next bump <t:…:R>" → { ok, nextAt },
// "Already bumped. You can bump again <t:…:R>." → { nextAt }. nextAt is ms or null.
function parseDiscadia(text) {
  const t = String(text || '');
  const ts = t.match(/<t:(\d+)(?::\w)?>/);
  const nextAt = ts ? Number(ts[1]) * 1000 : null;
  if (/already bumped|уже/i.test(t)) return { nextAt };
  if (/bumped|бампнут|поднят/i.test(t)) return { ok: true, nextAt };
  return null;
}

// Which bump reminders are due now: ['disboard', 'discadia'].
function dueBumps(cfg, t) {
  const b = cfg && cfg.bump;
  if (!b || !b.channel) return [];
  const due = [];
  if (b.disboardNext && !b.disboardReminded && t >= Date.parse(b.disboardNext)) due.push('disboard');
  if (b.discadiaNext && !b.discadiaReminded && t >= Date.parse(b.discadiaNext)) due.push('discadia');
  return due;
}

// ---------- pure helpers (exported for tests) ----------

function parseDuration(text) {
  const s = String(text || '').trim().toLowerCase().replace(/\s+/g, '');
  if (!s) return null;
  if (/^\d+$/.test(s)) return parseInt(s, 10) * 60 * 1000; // bare number = minutes
  const re = /(\d+)(d|д|h|ч|m|м|мин|min|s|с|сек|sec)/g;
  let total = 0, m, used = '';
  while ((m = re.exec(s))) {
    const n = parseInt(m[1], 10), u = m[2];
    total += n * ({ d: 86400, 'д': 86400, h: 3600, 'ч': 3600, m: 60, 'м': 60, 'мин': 60, min: 60, s: 1, 'с': 1, 'сек': 1, sec: 1 }[u]) * 1000;
    used += m[0];
  }
  return used === s && total > 0 ? total : null;
}

function fillTemplate(tpl, { user, username, server, count }) {
  return String(tpl || '')
    .replace(/\{user\}/g, user).replace(/\{username\}/g, username)
    .replace(/\{server\}/g, server).replace(/\{count\}/g, String(count))
    .replace(/\\n/g, '\n');
}

// Returns the name of the broken rule, or null. history = this user's recent messages [{t, content}].
function detectViolation(cfg, content, history, now) {
  const a = cfg.automod;
  const text = String(content || '');
  const lower = text.toLowerCase();
  if (a.invites && /(discord\.gg|discord(?:app)?\.com\/invite)\/[\w-]+/i.test(text)) return 'invites';
  if (a.links) {
    const urls = text.match(/https?:\/\/[^\s<>]+/gi) || [];
    for (const u of urls) {
      let host = '';
      try { host = new URL(u).hostname.toLowerCase(); } catch { host = ''; }
      if (!(a.allowDomains || []).some((d) => host === d || host.endsWith('.' + d))) return 'links';
    }
  }
  if (a.mentions) {
    const n = (text.match(/<@[!&]?\d+>/g) || []).length + (/@(everyone|here)/.test(text) ? a.maxMentions : 0);
    if (n >= a.maxMentions) return 'mentions';
  }
  if (a.caps) {
    const letters = text.replace(/<[^>]+>/g, '').match(/\p{L}/gu) || [];
    const upper = letters.filter((c) => c !== c.toLowerCase()).length;
    if (letters.length >= 12 && upper / letters.length >= 0.7) return 'caps';
  }
  if (a.badwords && (a.words || []).length) {
    for (const w of a.words) {
      const word = String(w).toLowerCase().trim();
      if (!word) continue;
      if (word.includes('*')) { if (lower.includes(word.replace(/\*/g, ''))) return 'badwords'; continue; }
      const re = new RegExp(`(^|[^\\p{L}\\p{N}])${word.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}($|[^\\p{L}\\p{N}])`, 'u');
      if (re.test(lower)) return 'badwords';
    }
  }
  if (a.spam && history) {
    const recent = history.filter((h) => now - h.t <= 7000);
    if (recent.length >= 6) return 'spam';
    const same = history.filter((h) => now - h.t <= 15000 && h.content && h.content === text);
    if (text.length > 0 && same.length >= 3) return 'spam';
  }
  return null;
}

function hasPerm(member, bit) {
  try {
    const p = BigInt((member && member.permissions) || '0');
    return (p & PERM.ADMIN) === PERM.ADMIN || (p & bit) === bit;
  } catch { return false; }
}

function emojiKey(e) {
  const s = String(e || '').trim();
  const m = s.match(/^<a?:(\w+):(\d+)>$/);
  return m ? m[2] : s; // custom emoji -> id, unicode -> itself
}
function emojiPath(e) {
  const s = String(e || '').trim();
  const m = s.match(/^<a?:(\w+):(\d+)>$/);
  return encodeURIComponent(m ? `${m[1]}:${m[2]}` : s);
}
function parseMessageLink(link) {
  const m = String(link || '').match(/channels\/([\w@]+)\/(\w+)\/(\w+)/);
  return m ? { guildId: m[1], channelId: m[2], messageId: m[3] } : null;
}

// ---------- slash commands ----------
const SUB = 1, STR = 3, INT = 4, BOOL = 5, USER = 6, CHANNEL = 7, ROLE = 8;
const G = { integration_types: [0], contexts: [0] };
const ru = (d) => ({ ru: d });
const o = (type, name, description, extra = {}) => ({ type, name, description, ...extra });
const textChannel = { channel_types: [0, 5] };

const GUILD_COMMANDS = [
  { name: 'autorole', description: 'Roles given to everyone who joins', description_localizations: ru('Роли, которые выдаются всем при входе'),
    default_member_permissions: String(PERM.MANAGE_ROLES), ...G, options: [
      o(SUB, 'add', 'Add an autorole', { options: [o(ROLE, 'role', 'Role', { required: true })] }),
      o(SUB, 'remove', 'Remove an autorole', { options: [o(ROLE, 'role', 'Role', { required: true })] }),
      o(SUB, 'list', 'Show autoroles')] },
  { name: 'rolepanel', description: 'Post a message with buttons that give roles', description_localizations: ru('Сообщение с кнопками для выдачи ролей'),
    default_member_permissions: String(PERM.MANAGE_ROLES), ...G, options: [
      o(CHANNEL, 'channel', 'Where to post', { required: true, ...textChannel }),
      o(STR, 'title', 'Title', { required: true, max_length: 200 }),
      o(ROLE, 'role1', 'Role 1', { required: true }), o(ROLE, 'role2', 'Role 2'), o(ROLE, 'role3', 'Role 3'),
      o(ROLE, 'role4', 'Role 4'), o(ROLE, 'role5', 'Role 5'),
      o(STR, 'description', 'Text under the title', { max_length: 1000 }),
      o(BOOL, 'unique', 'Only one role from this panel at a time')] },
  { name: 'reactionrole', description: 'Give a role for a reaction on a message', description_localizations: ru('Роль за реакцию на сообщение'),
    default_member_permissions: String(PERM.MANAGE_ROLES), ...G, options: [
      o(SUB, 'add', 'Add a reaction role', { options: [o(STR, 'message_link', 'Message link (right click → Copy link)', { required: true }),
        o(STR, 'emoji', 'Emoji', { required: true }), o(ROLE, 'role', 'Role', { required: true })] }),
      o(SUB, 'remove', 'Remove a reaction role', { options: [o(STR, 'message_link', 'Message link', { required: true }), o(STR, 'emoji', 'Emoji', { required: true })] }),
      o(SUB, 'list', 'Show reaction roles')] },
  { name: 'automod', description: 'Automatic moderation', description_localizations: ru('Автоматическая модерация'),
    default_member_permissions: String(PERM.MANAGE_GUILD), ...G, options: [
      o(SUB, 'show', 'Show automod settings'),
      o(SUB, 'toggle', 'Turn automod or a rule on/off', { options: [
        o(STR, 'rule', 'Rule', { required: true, choices: [{ name: 'automod (all)', value: 'enabled' }, ...AUTOMOD_RULES.map((r) => ({ name: r, value: r }))] }),
        o(BOOL, 'enabled', 'On or off', { required: true })] }),
      o(SUB, 'addword', 'Add blocked words (comma separated, * = part of a word)', { options: [o(STR, 'words', 'Words', { required: true })] }),
      o(SUB, 'removeword', 'Remove blocked words', { options: [o(STR, 'words', 'Words', { required: true })] }),
      o(SUB, 'allowdomain', 'Allow links to a domain (when links are blocked)', { options: [o(STR, 'domain', 'e.g. github.com', { required: true })] }),
      o(SUB, 'settings', 'Limits and punishment', { options: [
        o(INT, 'max_mentions', 'Mentions per message', { min_value: 2, max_value: 50 }),
        o(INT, 'timeout_after', 'Violations within an hour before a timeout (0 = never)', { min_value: 0, max_value: 20 }),
        o(INT, 'timeout_minutes', 'Timeout length, minutes', { min_value: 1, max_value: 40320 }),
        o(ROLE, 'ignore_role', 'Role automod ignores (toggles)')] })] },
  { name: 'ban', description: 'Ban a member', description_localizations: ru('Забанить участника'), default_member_permissions: String(PERM.BAN), ...G, options: [
    o(USER, 'user', 'Member', { required: true }), o(STR, 'reason', 'Reason', { max_length: 400 }), o(INT, 'delete_days', 'Delete messages for N days', { min_value: 0, max_value: 7 })] },
  { name: 'unban', description: 'Unban a user', description_localizations: ru('Разбанить пользователя'), default_member_permissions: String(PERM.BAN), ...G, options: [
    o(STR, 'user_id', 'User ID', { required: true }), o(STR, 'reason', 'Reason', { max_length: 400 })] },
  { name: 'kick', description: 'Kick a member', description_localizations: ru('Выгнать участника'), default_member_permissions: String(PERM.KICK), ...G, options: [
    o(USER, 'user', 'Member', { required: true }), o(STR, 'reason', 'Reason', { max_length: 400 })] },
  { name: 'timeout', description: 'Mute a member for a while', description_localizations: ru('Замутить участника на время'), default_member_permissions: String(PERM.MODERATE), ...G, options: [
    o(USER, 'user', 'Member', { required: true }), o(STR, 'duration', 'e.g. 10m, 1h, 2d (max 28d)', { required: true }), o(STR, 'reason', 'Reason', { max_length: 400 })] },
  { name: 'untimeout', description: 'Remove a timeout', description_localizations: ru('Снять мут'), default_member_permissions: String(PERM.MODERATE), ...G, options: [
    o(USER, 'user', 'Member', { required: true })] },
  { name: 'warn', description: 'Warn a member', description_localizations: ru('Выдать предупреждение'), default_member_permissions: String(PERM.MODERATE), ...G, options: [
    o(USER, 'user', 'Member', { required: true }), o(STR, 'reason', 'Reason', { required: true, max_length: 400 })] },
  { name: 'warnings', description: 'Show a member\'s cases', description_localizations: ru('История нарушений участника'), default_member_permissions: String(PERM.MODERATE), ...G, options: [
    o(USER, 'user', 'Member', { required: true })] },
  { name: 'clearwarns', description: 'Clear a member\'s warnings', description_localizations: ru('Очистить предупреждения'), default_member_permissions: String(PERM.MODERATE), ...G, options: [
    o(USER, 'user', 'Member', { required: true })] },
  { name: 'purge', description: 'Delete recent messages in this channel', description_localizations: ru('Удалить последние сообщения в канале'), default_member_permissions: String(PERM.MANAGE_MESSAGES), ...G, options: [
    o(INT, 'count', 'How many (1–100)', { required: true, min_value: 1, max_value: 100 }), o(USER, 'user', 'Only from this member')] },
  { name: 'log', description: 'Log channel for joins, leaves, deleted/edited messages and mod actions', description_localizations: ru('Канал логов: входы, выходы, удалённые сообщения, наказания'),
    default_member_permissions: String(PERM.MANAGE_GUILD), ...G, options: [
      o(SUB, 'channel', 'Set the log channel', { options: [o(CHANNEL, 'channel', 'Channel', { required: true, ...textChannel })] }),
      o(SUB, 'off', 'Turn logs off')] },
  { name: 'welcome', description: 'Welcome and leave messages', description_localizations: ru('Приветствие и прощание'),
    default_member_permissions: String(PERM.MANAGE_GUILD), ...G, options: [
      o(SUB, 'join', 'Welcome message ({user} {username} {server} {count})', { options: [o(CHANNEL, 'channel', 'Channel', { required: true, ...textChannel }), o(STR, 'message', 'Message', { required: true, max_length: 1500 })] }),
      o(SUB, 'leave', 'Leave message', { options: [o(CHANNEL, 'channel', 'Channel', { required: true, ...textChannel }), o(STR, 'message', 'Message', { required: true, max_length: 1500 })] }),
      o(SUB, 'dm', 'Private message to new members (empty = off)', { options: [o(STR, 'message', 'Message', { max_length: 1500 })] }),
      o(SUB, 'test', 'Show how the welcome looks'),
      o(SUB, 'off', 'Turn welcome and leave messages off')] },
  { name: 'tag', description: 'Saved replies', description_localizations: ru('Сохранённые ответы (теги)'), ...G, options: [
    o(SUB, 'show', 'Post a tag', { options: [o(STR, 'name', 'Tag', { required: true, autocomplete: true })] }),
    o(SUB, 'list', 'All tags'),
    o(SUB, 'create', 'Create or edit a tag (needs Manage Messages)', { options: [o(STR, 'name', 'Name', { required: true, max_length: 32 }), o(STR, 'content', 'Text (\\n = new line)', { required: true, max_length: 1900 })] }),
    o(SUB, 'delete', 'Delete a tag (needs Manage Messages)', { options: [o(STR, 'name', 'Tag', { required: true, autocomplete: true })] })] },
  { name: 'embed', description: 'Post a nice embed message', description_localizations: ru('Отправить красивое сообщение (embed)'), default_member_permissions: String(PERM.MANAGE_GUILD), ...G, options: [
    o(CHANNEL, 'channel', 'Channel', { required: true, ...textChannel }), o(STR, 'title', 'Title', { required: true, max_length: 256 }),
    o(STR, 'description', 'Text (\\n = new line)', { max_length: 4000 }), o(STR, 'color', 'Color, e.g. #8B5CF6'),
    o(STR, 'image', 'Image URL'), o(STR, 'footer', 'Footer', { max_length: 200 })] },
  { name: 'suggest', description: 'Send a suggestion', description_localizations: ru('Отправить предложение'), ...G, options: [
    o(STR, 'text', 'Your idea', { required: true, max_length: 1500 })] },
  { name: 'suggestions', description: 'Channel for /suggest', description_localizations: ru('Канал для /suggest'), default_member_permissions: String(PERM.MANAGE_GUILD), ...G, options: [
    o(CHANNEL, 'channel', 'Channel', { required: true, ...textChannel })] },
  { name: 'bumps', description: 'Bump reminders: DISBOARD every 2 h, Discadia every 24 h', description_localizations: ru('Напоминания о бампе: DISBOARD раз в 2 ч, Discadia раз в 24 ч'),
    default_member_permissions: String(PERM.MANAGE_GUILD), ...G, options: [
      o(SUB, 'setup', 'Where to remind and whom to ping', { options: [o(CHANNEL, 'channel', 'Channel for reminders', { required: true, ...textChannel }),
        o(ROLE, 'role', 'Role to ping (e.g. Bumper)')] }),
      o(SUB, 'status', 'When the next bumps are and who bumps the most'),
      o(SUB, 'off', 'Turn bump reminders off')] },
  { name: 'remind', description: 'Remind me later', description_localizations: ru('Напомнить позже'), integration_types: [0, 1], contexts: [0, 1, 2], options: [
    o(STR, 'in', 'When: 10m, 1h30m, 2d', { required: true }), o(STR, 'text', 'What to remind', { required: true, max_length: 1000 })] }
];
const GUILD_COMMAND_NAMES = new Set(GUILD_COMMANDS.map((c) => c.name));

function createGuildBot(pool, options = {}) {
  const now = options.now || (() => new Date());
  const token = options.token || '';
  const configs = new Map();
  let warmed = null;
  const strikes = new Map();   // guild:user -> [timestamps]
  const history = new Map();   // guild:user -> [{t, content}]
  const timers = new Map();    // reminder id -> timeout
  let client = null;
  let timersStarted = false;

  // ---------- Discord REST ----------
  const api = options.api || (async (method, path, body, reason) => {
    const res = await fetch(API + path, {
      method,
      headers: { authorization: `Bot ${token}`, 'content-type': 'application/json', ...(reason ? { 'x-audit-log-reason': encodeURIComponent(reason).slice(0, 500) } : {}) },
      body: body === undefined ? undefined : JSON.stringify(body)
    });
    if (res.status === 204) return null;
    const text = await res.text();
    let json = null;
    try { json = text ? JSON.parse(text) : null; } catch { json = null; }
    if (!res.ok) { const e = new Error((json && json.message) || `HTTP ${res.status}`); e.status = res.status; e.code = json && json.code; throw e; }
    return json;
  });

  // ---------- storage ----------
  function normalize(c) {
    const d = defaultConfig();
    const cfg = { ...d, ...(c || {}) };
    cfg.automod = { ...d.automod, ...((c && c.automod) || {}) };
    cfg.welcome = { ...d.welcome, ...((c && c.welcome) || {}) };
    cfg.bump = { ...d.bump, ...((c && c.bump) || {}) };
    cfg.bump.counts = { ...((c && c.bump && c.bump.counts) || {}) };
    return cfg;
  }
  function warm() {
    if (!warmed) {
      warmed = pool.query('SELECT guild_id, data FROM guild_config')
        .then(({ rows }) => { for (const r of rows) { try { configs.set(r.guild_id, normalize(JSON.parse(r.data))); } catch { /* skip broken row */ } } })
        .catch((e) => { warmed = null; throw e; });
    }
    return warmed;
  }
  async function getConfig(guildId) {
    await warm();
    if (!configs.has(guildId)) configs.set(guildId, defaultConfig());
    return configs.get(guildId);
  }
  async function updateConfig(guildId, fn) {
    const cfg = await getConfig(guildId);
    fn(cfg);
    await pool.query('INSERT INTO guild_config (guild_id, data) VALUES ($1, $2) ON CONFLICT (guild_id) DO UPDATE SET data = $2', [guildId, JSON.stringify(cfg)]);
    return cfg;
  }
  async function addCase(guildId, userId, modId, action, reason) {
    const { rows } = await pool.query('INSERT INTO mod_cases (guild_id, user_id, mod_id, action, reason) VALUES ($1, $2, $3, $4, $5) RETURNING id',
      [guildId, userId, modId, action, reason || null]);
    return rows[0].id;
  }

  // ---------- logging ----------
  async function log(guildId, embed) {
    const cfg = configs.get(guildId);
    if (!cfg || !cfg.logChannel) return;
    try { await api('POST', `/channels/${cfg.logChannel}/messages`, { embeds: [{ timestamp: now().toISOString(), ...embed }], allowed_mentions: { parse: [] } }); }
    catch (e) { console.error('log failed:', e.message); }
  }
  async function dm(userId, content) {
    try {
      const ch = await api('POST', '/users/@me/channels', { recipient_id: userId });
      await api('POST', `/channels/${ch.id}/messages`, { content: String(content).slice(0, 2000) });
      return true;
    } catch { return false; }
  }

  // ---------- helpers for interactions ----------
  const T = (isRu, r, e) => (isRu ? r : e);
  const reply = (content, extra = {}) => ({ type: 4, data: { content: String(content).slice(0, 2000), flags: 64, allowed_mentions: { parse: [] }, ...extra } });
  const publicReply = (data) => ({ type: 4, data: { allowed_mentions: { parse: [] }, ...data } });
  function parse(i) {
    let opts = i.data.options || [], sub = null;
    if (opts[0] && opts[0].type === SUB) { sub = opts[0].name; opts = opts[0].options || []; }
    const m = {};
    for (const x of opts) m[x.name] = x.value;
    return { sub, m, resolved: i.data.resolved || {} };
  }
  const errText = (isRu, e) => {
    if (e && (e.status === 403 || e.code === 50013)) return T(isRu, '❌ Не хватает прав. Подними роль бота **выше** ролей, которыми он управляет, и дай ему нужные права.', '❌ Missing permissions. Move the bot\'s role **above** the roles it manages and give it the needed permissions.');
    if (e && e.status === 404) return T(isRu, '❌ Не найдено (участник ушёл или неверный ID).', '❌ Not found (member left or wrong ID).');
    return T(isRu, '❌ Ошибка Discord: ', '❌ Discord error: ') + ((e && e.message) || '?');
  };

  // Moderator must be above the target (like Carl-bot); the owner can do anything.
  async function canModerate(i, targetId) {
    const actor = i.member.user.id;
    if (targetId === actor) return 'self';
    const guild = await api('GET', `/guilds/${i.guild_id}`);
    if (targetId === guild.owner_id) return 'owner';
    if (actor === guild.owner_id) return null;
    const target = (i.data.resolved && i.data.resolved.members && i.data.resolved.members[targetId]) || null;
    if (!target) return null; // not in the server (e.g. ban by user) — nothing to compare
    const pos = new Map((guild.roles || []).map((r) => [r.id, r.position]));
    const top = (ids) => Math.max(0, ...(ids || []).map((id) => pos.get(id) || 0));
    return top(target.roles) >= top(i.member.roles) ? 'higher' : null;
  }
  const modDenied = (isRu, why) => reply({
    self: T(isRu, '❌ Нельзя применить к себе.', '❌ You cannot do that to yourself.'),
    owner: T(isRu, '❌ Это владелец сервера.', '❌ That is the server owner.'),
    higher: T(isRu, '❌ У участника роль не ниже твоей.', '❌ That member\'s role is not below yours.')
  }[why]);

  function modEmbed(action, color, target, mod, reason, extra) {
    const lines = [`**Участник:** <@${target}> (\`${target}\`)`, `**Модератор:** <@${mod}>`];
    if (reason) lines.push(`**Причина:** ${reason}`);
    if (extra) lines.push(extra);
    return { title: action, color, description: lines.join('\n') };
  }

  // ---------- command handlers ----------
  async function handleCommand(i, isRu) {
    const name = i.data.name;
    const { sub, m, resolved } = parse(i);
    const gid = i.guild_id;
    const actor = (i.member && i.member.user) || i.user || {};
    if (name !== 'remind' && !gid) return reply(T(isRu, 'Эта команда работает только на сервере.', 'This command works only in a server.'));
    try {
      switch (name) {
        case 'autorole': {
          if (sub === 'list') {
            const cfg = await getConfig(gid);
            return reply(cfg.autoroles.length ? T(isRu, 'Автороли: ', 'Autoroles: ') + cfg.autoroles.map((r) => `<@&${r}>`).join(', ') : T(isRu, 'Авторолей нет. Добавь: `/autorole add`', 'No autoroles. Add one: `/autorole add`'));
          }
          const role = m.role;
          await updateConfig(gid, (c) => { c.autoroles = c.autoroles.filter((r) => r !== role); if (sub === 'add') c.autoroles.push(role); });
          return reply(sub === 'add'
            ? T(isRu, `✅ Роль <@&${role}> будет выдаваться всем новым участникам. Роль бота должна быть выше неё.`, `✅ <@&${role}> will be given to every new member. The bot's role must be above it.`)
            : T(isRu, `✅ Роль <@&${role}> больше не выдаётся автоматически.`, `✅ <@&${role}> is no longer given automatically.`));
        }
        case 'rolepanel': {
          const roles = ['role1', 'role2', 'role3', 'role4', 'role5'].map((k) => m[k]).filter(Boolean);
          const uniq = [...new Set(roles)];
          const mode = m.unique ? 'u' : 't';
          const buttons = uniq.map((id) => ({ type: 2, style: 2, custom_id: `rr:${mode}:${id}`, label: ((resolved.roles && resolved.roles[id] && resolved.roles[id].name) || 'role').slice(0, 80) }));
          await api('POST', `/channels/${m.channel}/messages`, {
            embeds: [{ title: m.title, color: COLOR.brand, description: (m.description ? m.description.replace(/\\n/g, '\n') + '\n\n' : '') + uniq.map((id) => `• <@&${id}>`).join('\n') +
              (m.unique ? T(isRu, '\n\n_Можно выбрать только одну роль._', '\n\n_You can pick only one role._') : T(isRu, '\n\n_Нажми ещё раз, чтобы снять роль._', '\n\n_Press again to remove the role._')) }],
            components: [{ type: 1, components: buttons }], allowed_mentions: { parse: [] }
          });
          return reply(T(isRu, `✅ Панель ролей отправлена в <#${m.channel}>.`, `✅ Role panel posted in <#${m.channel}>.`));
        }
        case 'reactionrole': {
          if (sub === 'list') {
            const cfg = await getConfig(gid);
            return reply(cfg.reactionRoles.length ? cfg.reactionRoles.map((r) => `${r.emoji} → <@&${r.roleId}> · https://discord.com/channels/${gid}/${r.channelId}/${r.messageId}`).join('\n').slice(0, 1900)
              : T(isRu, 'Ролей за реакции нет.', 'No reaction roles.'));
          }
          const link = parseMessageLink(m.message_link);
          if (!link || link.guildId !== gid) return reply(T(isRu, '❌ Нужна ссылка на сообщение с этого сервера (ПКМ по сообщению → «Копировать ссылку»).', '❌ Need a link to a message in this server (right click → Copy Message Link).'));
          const key = emojiKey(m.emoji);
          if (sub === 'remove') {
            await updateConfig(gid, (c) => { c.reactionRoles = c.reactionRoles.filter((r) => !(r.messageId === link.messageId && r.key === key)); });
            try { await api('DELETE', `/channels/${link.channelId}/messages/${link.messageId}/reactions/${emojiPath(m.emoji)}/@me`); } catch { /* fine */ }
            return reply(T(isRu, '✅ Роль за реакцию удалена.', '✅ Reaction role removed.'));
          }
          try { await api('PUT', `/channels/${link.channelId}/messages/${link.messageId}/reactions/${emojiPath(m.emoji)}/@me`); }
          catch (e) { return reply(e.status === 404 || e.status === 400 ? T(isRu, '❌ Не нашёл сообщение или эмодзи.', '❌ Message or emoji not found.') : errText(isRu, e)); }
          await updateConfig(gid, (c) => { c.reactionRoles = c.reactionRoles.filter((r) => !(r.messageId === link.messageId && r.key === key)); c.reactionRoles.push({ messageId: link.messageId, channelId: link.channelId, emoji: m.emoji, key, roleId: m.role }); });
          return reply(T(isRu, `✅ Реакция ${m.emoji} теперь выдаёт <@&${m.role}>.`, `✅ Reacting with ${m.emoji} now gives <@&${m.role}>.`));
        }
        case 'automod': {
          if (sub === 'show') return { type: 4, data: { embeds: [automodEmbed(await getConfig(gid), isRu)], flags: 64 } };
          if (sub === 'toggle') {
            await updateConfig(gid, (c) => { c.automod[m.rule] = !!m.enabled; });
          } else if (sub === 'addword' || sub === 'removeword') {
            const words = String(m.words).split(',').map((w) => w.trim().toLowerCase()).filter(Boolean).slice(0, 100);
            await updateConfig(gid, (c) => {
              const set = new Set(c.automod.words);
              for (const w of words) { if (sub === 'addword') set.add(w); else set.delete(w); }
              c.automod.words = [...set].slice(0, 500);
            });
          } else if (sub === 'allowdomain') {
            const d = String(m.domain).toLowerCase().replace(/^https?:\/\//, '').replace(/\/.*$/, '');
            await updateConfig(gid, (c) => { if (!c.automod.allowDomains.includes(d)) c.automod.allowDomains.push(d); });
          } else if (sub === 'settings') {
            await updateConfig(gid, (c) => {
              if (m.max_mentions != null) c.automod.maxMentions = m.max_mentions;
              if (m.timeout_after != null) c.automod.timeoutAfter = m.timeout_after;
              if (m.timeout_minutes != null) c.automod.timeoutMinutes = m.timeout_minutes;
              if (m.ignore_role) { const r = m.ignore_role; c.automod.ignoreRoles = c.automod.ignoreRoles.includes(r) ? c.automod.ignoreRoles.filter((x) => x !== r) : c.automod.ignoreRoles.concat(r); }
            });
          }
          return { type: 4, data: { content: '✅', embeds: [automodEmbed(await getConfig(gid), isRu)], flags: 64 } };
        }
        case 'ban': case 'kick': case 'timeout': case 'untimeout': case 'warn': {
          const target = m.user;
          const denied = name === 'untimeout' ? null : await canModerate(i, target);
          if (denied) return modDenied(isRu, denied);
          const reason = m.reason || '';
          const audit = `${actor.username || actor.id}: ${reason || '-'}`;
          const server = (await api('GET', `/guilds/${gid}`)).name;
          if (name === 'ban') {
            await dm(target, T(isRu, `🔨 Тебя забанили на сервере **${server}**.`, `🔨 You were banned from **${server}**.`) + (reason ? `\n${T(isRu, 'Причина', 'Reason')}: ${reason}` : ''));
            await api('PUT', `/guilds/${gid}/bans/${target}`, { delete_message_seconds: (m.delete_days || 0) * 86400 }, audit);
          } else if (name === 'kick') {
            await dm(target, T(isRu, `👢 Тебя выгнали с сервера **${server}**.`, `👢 You were kicked from **${server}**.`) + (reason ? `\n${T(isRu, 'Причина', 'Reason')}: ${reason}` : ''));
            await api('DELETE', `/guilds/${gid}/members/${target}`, undefined, audit);
          } else if (name === 'timeout') {
            const ms = parseDuration(m.duration);
            if (!ms || ms > 28 * 86400000) return reply(T(isRu, '❌ Время: например 10m, 1h, 2d (до 28 дней).', '❌ Duration: e.g. 10m, 1h, 2d (up to 28 days).'));
            await api('PATCH', `/guilds/${gid}/members/${target}`, { communication_disabled_until: new Date(now().getTime() + ms).toISOString() }, audit);
            await dm(target, T(isRu, `🔇 Тебе выдали мут на сервере **${server}** на ${m.duration}.`, `🔇 You were timed out in **${server}** for ${m.duration}.`) + (reason ? `\n${T(isRu, 'Причина', 'Reason')}: ${reason}` : ''));
          } else if (name === 'untimeout') {
            await api('PATCH', `/guilds/${gid}/members/${target}`, { communication_disabled_until: null }, audit);
          } else if (name === 'warn') {
            await dm(target, T(isRu, `⚠️ Предупреждение на сервере **${server}**: ${reason}`, `⚠️ Warning in **${server}**: ${reason}`));
          }
          const id = await addCase(gid, target, actor.id, name, reason);
          let extra = '';
          if (name === 'warn') {
            const { rows } = await pool.query("SELECT COUNT(*) AS c FROM mod_cases WHERE guild_id = $1 AND user_id = $2 AND action = 'warn'", [gid, target]);
            extra = T(isRu, `Всего предупреждений: **${rows[0].c}**`, `Total warnings: **${rows[0].c}**`);
          }
          const titles = { ban: ['🔨 Бан', '🔨 Ban'], kick: ['👢 Кик', '👢 Kick'], timeout: ['🔇 Мут', '🔇 Timeout'], untimeout: ['🔊 Мут снят', '🔊 Timeout removed'], warn: ['⚠️ Предупреждение', '⚠️ Warning'] };
          const colors = { ban: COLOR.red, kick: COLOR.red, timeout: COLOR.yellow, untimeout: COLOR.green, warn: COLOR.yellow };
          await log(gid, { ...modEmbed(`${titles[name][0]} · #${id}`, colors[name], target, actor.id, reason, name === 'timeout' ? `**Время:** ${m.duration}` : extra) });
          return reply(`✅ ${T(isRu, titles[name][0], titles[name][1])} <@${target}> · ${T(isRu, 'случай', 'case')} #${id}${extra ? '\n' + extra : ''}`);
        }
        case 'unban': {
          await api('DELETE', `/guilds/${gid}/bans/${String(m.user_id).replace(/\D/g, '')}`, undefined, `${actor.username || actor.id}: ${m.reason || '-'}`);
          const id = await addCase(gid, m.user_id, actor.id, 'unban', m.reason);
          await log(gid, modEmbed(`✅ Разбан · #${id}`, COLOR.green, m.user_id, actor.id, m.reason));
          return reply(T(isRu, `✅ <@${m.user_id}> разбанен.`, `✅ <@${m.user_id}> unbanned.`));
        }
        case 'warnings': {
          const { rows } = await pool.query('SELECT id, action, reason, mod_id, created_at FROM mod_cases WHERE guild_id = $1 AND user_id = $2 ORDER BY id DESC LIMIT 15', [gid, m.user]);
          if (!rows.length) return reply(T(isRu, `У <@${m.user}> нарушений нет. ✨`, `<@${m.user}> has no cases. ✨`));
          const lines = rows.map((r) => `**#${r.id}** · ${r.action} · <t:${Math.floor(new Date(r.created_at).getTime() / 1000)}:R> · <@${r.mod_id}>${r.reason ? ` — ${r.reason}` : ''}`);
          return { type: 4, data: { embeds: [{ title: T(isRu, 'Нарушения', 'Cases'), color: COLOR.yellow, description: `<@${m.user}>\n\n` + lines.join('\n').slice(0, 3800) }], flags: 64, allowed_mentions: { parse: [] } } };
        }
        case 'clearwarns': {
          const { rows } = await pool.query("DELETE FROM mod_cases WHERE guild_id = $1 AND user_id = $2 AND action = 'warn' RETURNING id", [gid, m.user]);
          await log(gid, modEmbed('🧹 Предупреждения очищены', COLOR.green, m.user, actor.id, null, `${rows.length}`));
          return reply(T(isRu, `✅ Удалено предупреждений: ${rows.length}.`, `✅ Warnings removed: ${rows.length}.`));
        }
        case 'purge': {
          const list = await api('GET', `/channels/${i.channel_id}/messages?limit=100`);
          const cutoff = now().getTime() - 13.9 * 86400000;
          const ids = (list || []).filter((msg) => (!m.user || msg.author.id === m.user) && new Date(msg.timestamp).getTime() > cutoff).slice(0, m.count).map((msg) => msg.id);
          if (!ids.length) return reply(T(isRu, 'Нечего удалять (сообщения старше 14 дней Discord удалять не даёт).', 'Nothing to delete (Discord does not allow deleting messages older than 14 days).'));
          if (ids.length === 1) await api('DELETE', `/channels/${i.channel_id}/messages/${ids[0]}`);
          else await api('POST', `/channels/${i.channel_id}/messages/bulk-delete`, { messages: ids });
          await log(gid, { title: '🧹 Очистка', color: COLOR.gray, description: `<@${actor.id}> удалил **${ids.length}** сообщ. в <#${i.channel_id}>${m.user ? ` от <@${m.user}>` : ''}` });
          return reply(T(isRu, `✅ Удалено сообщений: ${ids.length}.`, `✅ Deleted ${ids.length} messages.`));
        }
        case 'log': {
          await updateConfig(gid, (c) => { c.logChannel = sub === 'off' ? null : m.channel; });
          if (sub === 'off') return reply(T(isRu, '✅ Логи выключены.', '✅ Logs are off.'));
          await log(gid, { title: '📋 Логи включены', color: COLOR.brand, description: 'Сюда будут приходить: входы и выходы, удалённые и изменённые сообщения, баны, муты, предупреждения и автомод.' });
          return reply(T(isRu, `✅ Логи будут приходить в <#${m.channel}>.`, `✅ Logs will go to <#${m.channel}>.`));
        }
        case 'welcome': {
          if (sub === 'test') {
            const cfg = await getConfig(gid);
            const guild = await api('GET', `/guilds/${gid}?with_counts=true`);
            const vars = { user: `<@${actor.id}>`, username: actor.global_name || actor.username || '', server: guild.name, count: guild.approximate_member_count || 0 };
            const parts = [];
            if (cfg.welcome.message) parts.push(`**${T(isRu, 'Вход', 'Join')}** → <#${cfg.welcome.channel}>\n${fillTemplate(cfg.welcome.message, vars)}`);
            if (cfg.welcome.leaveMessage) parts.push(`**${T(isRu, 'Выход', 'Leave')}** → <#${cfg.welcome.leaveChannel}>\n${fillTemplate(cfg.welcome.leaveMessage, vars)}`);
            if (cfg.welcome.dm) parts.push(`**${T(isRu, 'Личное сообщение', 'DM')}**\n${fillTemplate(cfg.welcome.dm, vars)}`);
            return reply(parts.length ? parts.join('\n\n') : T(isRu, 'Ничего не настроено. `/welcome join`', 'Nothing set up. `/welcome join`'));
          }
          await updateConfig(gid, (c) => {
            if (sub === 'join') { c.welcome.channel = m.channel; c.welcome.message = m.message; }
            if (sub === 'leave') { c.welcome.leaveChannel = m.channel; c.welcome.leaveMessage = m.message; }
            if (sub === 'dm') c.welcome.dm = m.message || null;
            if (sub === 'off') c.welcome = defaultConfig().welcome;
          });
          return reply(sub === 'off' ? T(isRu, '✅ Приветствия выключены.', '✅ Welcome messages are off.') : T(isRu, '✅ Сохранено. Проверить: `/welcome test`', '✅ Saved. Check it: `/welcome test`'));
        }
        case 'tag': {
          const key = String(m.name || '').toLowerCase().trim();
          if (sub === 'create' || sub === 'delete') {
            if (!hasPerm(i.member, PERM.MANAGE_MESSAGES)) return reply(T(isRu, '❌ Нужно право «Управлять сообщениями».', '❌ You need Manage Messages.'));
            await updateConfig(gid, (c) => { if (sub === 'create') c.tags[key] = m.content; else delete c.tags[key]; });
            return reply(sub === 'create' ? T(isRu, `✅ Тег **${key}** сохранён. Показать: \`/tag show ${key}\``, `✅ Tag **${key}** saved. Show it: \`/tag show ${key}\``) : T(isRu, `✅ Тег **${key}** удалён.`, `✅ Tag **${key}** deleted.`));
          }
          const cfg = await getConfig(gid);
          if (sub === 'list') {
            const names = Object.keys(cfg.tags).sort();
            return reply(names.length ? T(isRu, 'Теги: ', 'Tags: ') + names.map((n) => `\`${n}\``).join(', ') : T(isRu, 'Тегов нет.', 'No tags.'));
          }
          const content = cfg.tags[key];
          if (!content) return reply(T(isRu, `❌ Тега **${key}** нет.`, `❌ No tag **${key}**.`));
          return publicReply({ content: String(content).replace(/\\n/g, '\n').slice(0, 2000) });
        }
        case 'embed': {
          const color = /^#?[0-9a-f]{6}$/i.test(m.color || '') ? parseInt(m.color.replace('#', ''), 16) : COLOR.brand;
          await api('POST', `/channels/${m.channel}/messages`, { embeds: [{ title: m.title, description: m.description ? m.description.replace(/\\n/g, '\n') : undefined, color,
            image: /^https?:\/\//.test(m.image || '') ? { url: m.image } : undefined, footer: m.footer ? { text: m.footer } : undefined }], allowed_mentions: { parse: [] } });
          return reply(T(isRu, `✅ Отправлено в <#${m.channel}>.`, `✅ Posted in <#${m.channel}>.`));
        }
        case 'suggestions': {
          await updateConfig(gid, (c) => { c.suggestChannel = m.channel; });
          return reply(T(isRu, `✅ Предложения (/suggest) будут приходить в <#${m.channel}>.`, `✅ Suggestions (/suggest) will go to <#${m.channel}>.`));
        }
        case 'suggest': {
          const cfg = await getConfig(gid);
          const channel = cfg.suggestChannel;
          if (!channel) return reply(T(isRu, '❌ Админ ещё не выбрал канал: `/suggestions`.', '❌ An admin has not set a channel yet: `/suggestions`.'));
          const avatar = actor.avatar ? `https://cdn.discordapp.com/avatars/${actor.id}/${actor.avatar}.png` : undefined;
          const msg = await api('POST', `/channels/${channel}/messages`, { embeds: [{ author: { name: actor.global_name || actor.username || 'user', icon_url: avatar }, description: m.text, color: COLOR.brand,
            footer: { text: T(isRu, 'Голосуй реакциями 👍 / 👎', 'Vote with 👍 / 👎') }, timestamp: now().toISOString() }], allowed_mentions: { parse: [] } });
          for (const e of ['👍', '👎']) { try { await api('PUT', `/channels/${channel}/messages/${msg.id}/reactions/${encodeURIComponent(e)}/@me`); } catch { /* ignore */ } }
          return reply(T(isRu, `✅ Предложение отправлено в <#${channel}>.`, `✅ Suggestion posted in <#${channel}>.`));
        }
        case 'bumps': {
          const ts = (iso) => (iso ? `<t:${Math.floor(Date.parse(iso) / 1000)}:R>` : '—');
          if (sub === 'setup') {
            await updateConfig(gid, (c) => {
              c.bump.channel = m.channel; c.bump.role = m.role || null;
              const iso = now().toISOString();
              if (!c.bump.disboardNext) { c.bump.disboardNext = iso; c.bump.disboardReminded = false; }
              if (!c.bump.discadiaNext) { c.bump.discadiaNext = iso; c.bump.discadiaReminded = false; }
            });
            return reply(T(isRu,
              `✅ Напоминания о бампе в <#${m.channel}>${m.role ? `, пингую <@&${m.role}>` : ''}.\nВижу каждый \`/bump\` у **DISBOARD** (раз в 2 ч) и **Discadia** (раз в 24 ч): благодарю бампера, считаю бампы и напоминаю, как только можно бампнуть снова.`,
              `✅ Bump reminders in <#${m.channel}>${m.role ? `, pinging <@&${m.role}>` : ''}.\nI see every \`/bump\` for **DISBOARD** (every 2 h) and **Discadia** (every 24 h): I thank the bumper, count bumps and remind as soon as the next bump is possible.`));
          }
          const cfg = await getConfig(gid);
          if (sub === 'off') {
            await updateConfig(gid, (c) => { c.bump.channel = null; });
            return reply(T(isRu, '🔕 Напоминания о бампе выключены.', '🔕 Bump reminders are off.'));
          }
          const b = cfg.bump;
          const top = Object.entries(b.counts).sort((x, y) => y[1] - x[1]).slice(0, 10).map(([u, n], k) => `${k + 1}. <@${u}> — ${n}`).join('\n');
          return reply(T(isRu,
            `**Бамп** ${b.channel ? `в <#${b.channel}>` : '— выключен'}\nDISBOARD: следующий ${ts(b.disboardNext)}\nDiscadia: следующий ${ts(b.discadiaNext)}\n\n**Топ бамперов**\n${top || '—'}`,
            `**Bump** ${b.channel ? `in <#${b.channel}>` : '— off'}\nDISBOARD: next ${ts(b.disboardNext)}\nDiscadia: next ${ts(b.discadiaNext)}\n\n**Top bumpers**\n${top || '—'}`));
        }
        case 'remind': {
          const ms = parseDuration(m.in);
          if (!ms || ms > 365 * 86400000) return reply(T(isRu, '❌ Время: например 10m, 1h30m, 2d (до года).', '❌ Time: e.g. 10m, 1h30m, 2d (up to a year).'));
          const due = new Date(now().getTime() + ms);
          const channel = i.channel_id || (await api('POST', '/users/@me/channels', { recipient_id: actor.id })).id;
          const { rows } = await pool.query('INSERT INTO reminders (user_id, channel_id, text, due_at) VALUES ($1, $2, $3, $4) RETURNING id', [actor.id, channel, m.text, due]);
          schedule({ id: rows[0].id, user_id: actor.id, channel_id: channel, text: m.text, due_at: due });
          return reply(T(isRu, `⏰ Напомню <t:${Math.floor(due.getTime() / 1000)}:R>.`, `⏰ I'll remind you <t:${Math.floor(due.getTime() / 1000)}:R>.`));
        }
      }
    } catch (e) {
      console.error(`/${name} failed:`, e.message);
      return reply(errText(isRu, e));
    }
    return null;
  }

  function automodEmbed(cfg, isRu) {
    const a = cfg.automod;
    const on = (v) => (v ? '🟢' : '⚫');
    const names = isRu
      ? { spam: 'Спам/флуд', invites: 'Инвайты на чужие серверы', links: 'Ссылки (кроме разрешённых)', caps: 'Капс', mentions: `Масс-упоминания (≥ ${a.maxMentions})`, badwords: `Запрещённые слова (${a.words.length})` }
      : { spam: 'Spam/flood', invites: 'Invites to other servers', links: 'Links (except allowed)', caps: 'Caps', mentions: `Mass mentions (≥ ${a.maxMentions})`, badwords: `Blocked words (${a.words.length})` };
    return {
      title: `${on(a.enabled)} Automod ${a.enabled ? '' : T(isRu, '(выключен — /automod toggle automod true)', '(off — /automod toggle automod true)')}`,
      color: a.enabled ? COLOR.green : COLOR.gray,
      description: AUTOMOD_RULES.map((r) => `${on(a[r])} ${names[r]}`).join('\n') +
        `\n\n${T(isRu, 'Наказание', 'Punishment')}: ${T(isRu, 'удаление сообщения', 'message deleted')}` +
        (a.timeoutAfter ? T(isRu, `, мут на ${a.timeoutMinutes} мин после ${a.timeoutAfter} нарушений за час`, `, ${a.timeoutMinutes} min timeout after ${a.timeoutAfter} violations within an hour`) : '') +
        (a.ignoreRoles.length ? `\n${T(isRu, 'Игнорирует', 'Ignores')}: ${a.ignoreRoles.map((r) => `<@&${r}>`).join(', ')}` : '') +
        (a.links ? `\n${T(isRu, 'Разрешённые домены', 'Allowed domains')}: ${a.allowDomains.join(', ')}` : '')
    };
  }

  async function handleAutocomplete(i) {
    if (i.data.name !== 'tag' || !i.guild_id) return { type: 8, data: { choices: [] } };
    const cfg = await getConfig(i.guild_id);
    const opts = (i.data.options && i.data.options[0] && i.data.options[0].options) || [];
    const q = String((opts.find((x) => x.focused) || {}).value || '').toLowerCase();
    return { type: 8, data: { choices: Object.keys(cfg.tags).filter((n) => n.includes(q)).sort().slice(0, 25).map((n) => ({ name: n, value: n })) } };
  }

  // Role panel button: rr:<t|u>:<roleId>
  async function handleComponent(i, isRu) {
    const [, mode, roleId] = String(i.data.custom_id).split(':');
    const gid = i.guild_id, uid = i.member.user.id;
    const has = (i.member.roles || []).includes(roleId);
    try {
      if (has) {
        await api('DELETE', `/guilds/${gid}/members/${uid}/roles/${roleId}`, undefined, 'Role panel');
        return reply(T(isRu, `➖ Роль <@&${roleId}> снята.`, `➖ Removed <@&${roleId}>.`));
      }
      if (mode === 'u') {
        const panelRoles = ((i.message && i.message.components) || []).flatMap((row) => row.components || []).map((c) => String(c.custom_id || '').split(':')[2]).filter(Boolean);
        for (const r of panelRoles) if (r !== roleId && (i.member.roles || []).includes(r)) await api('DELETE', `/guilds/${gid}/members/${uid}/roles/${r}`, undefined, 'Role panel (unique)');
      }
      await api('PUT', `/guilds/${gid}/members/${uid}/roles/${roleId}`, undefined, 'Role panel');
      return reply(T(isRu, `➕ Роль <@&${roleId}> выдана.`, `➕ Added <@&${roleId}>.`));
    } catch (e) { return reply(errText(isRu, e)); }
  }

  // ---------- reminders ----------
  function schedule(r) {
    const delay = new Date(r.due_at).getTime() - now().getTime();
    if (delay > 6 * 3600 * 1000) return; // far ones are picked up by the periodic check
    if (timers.has(r.id)) return;
    timers.set(r.id, setTimeout(() => fire(r), Math.max(0, delay)));
  }
  async function fire(r) {
    timers.delete(r.id);
    try {
      const { rows } = await pool.query('DELETE FROM reminders WHERE id = $1 RETURNING id', [r.id]);
      if (!rows.length) return; // already sent
      await api('POST', `/channels/${r.channel_id}/messages`, { content: `⏰ <@${r.user_id}> ${String(r.text).slice(0, 1800)}`, allowed_mentions: { users: [r.user_id] } })
        .catch(() => dm(r.user_id, `⏰ ${r.text}`));
    } catch (e) { console.error('reminder failed:', e.message); }
  }
  async function loadReminders() {
    const { rows } = await pool.query('SELECT id, user_id, channel_id, text, due_at FROM reminders WHERE due_at < $1', [new Date(now().getTime() + 6 * 3600 * 1000)]);
    rows.forEach(schedule);
  }

  // ---------- bump reminders ----------
  async function remindBump(gid, kind) {
    const cfg = configs.get(gid);
    const b = cfg.bump;
    const ping = b.role ? `<@&${b.role}> ` : '';
    const who = kind === 'disboard' ? 'DISBOARD' : 'Discadia';
    const msg = { content: `${ping}⏰ **Пора бампнуть сервер на ${who}!** Напиши \`/bump\` и выбери **${who}** — сервер поднимется в поиске.\n-# Time to bump on ${who}! Type \`/bump\` and pick ${who}.` };
    let sent = null;
    try { sent = await api('POST', `/channels/${b.channel}/messages`, { ...msg, allowed_mentions: { roles: b.role ? [b.role] : [] } }); }
    catch (e) { console.error('bump reminder failed:', e.message); }
    await updateConfig(gid, (c) => {
      if (kind === 'disboard') { c.bump.disboardReminded = true; c.bump.disboardMsg = sent && sent.id; }
      else { c.bump.discadiaReminded = true; c.bump.discadiaMsg = sent && sent.id; }
    });
  }
  async function bumpTick() {
    const t = now().getTime();
    for (const [gid, cfg] of configs) {
      for (const kind of dueBumps(cfg, t)) await remindBump(gid, kind).catch((e) => console.error('bump tick:', e.message));
    }
  }
  // A DISBOARD / Discadia reply in any channel. Successful bump → thank and count the bumper, remind when the
  // next one is possible; "too early" → move the reminder to the time the bot names.
  async function onBumpBot(msg) {
    const cfg = configs.get(msg.guild.id);
    if (!cfg || !cfg.bump.channel) return;
    const kind = msg.author.id === DISBOARD_ID ? 'disboard' : 'discadia';
    const text = [msg.content, ...(msg.embeds || []).map((e) => `${e.title || ''}\n${e.description || ''}\n${(e.footer && e.footer.text) || ''}`)].join('\n');
    const t = now().getTime();
    let ok = false, nextAt = null;
    if (kind === 'disboard') {
      const r = parseDisboard(text);
      if (!r) return;
      ok = !!r.ok;
      nextAt = r.ok ? t + DISBOARD_COOLDOWN : t + r.waitMinutes * 60000;
    } else {
      const r = parseDiscadia(text);
      if (!r) return;
      ok = !!r.ok;
      nextAt = r.nextAt || (r.ok ? t + DISCADIA_COOLDOWN : null);
      if (!nextAt) return;
    }
    const next = new Date(nextAt).toISOString();
    const nextKey = kind + 'Next', remKey = kind + 'Reminded', msgKey = kind + 'Msg';
    if (!ok) {
      await updateConfig(msg.guild.id, (c) => { c.bump[nextKey] = next; c.bump[remKey] = false; });
      return;
    }
    const meta = msg.interactionMetadata || msg.interaction;
    const uid = meta && meta.user && meta.user.id;
    const old = cfg.bump[msgKey];
    const c2 = await updateConfig(msg.guild.id, (c) => {
      c.bump[nextKey] = next; c.bump[remKey] = false; c.bump[msgKey] = null;
      if (uid) c.bump.counts[uid] = (c.bump.counts[uid] || 0) + 1;
    });
    if (old) api('DELETE', `/channels/${cfg.bump.channel}/messages/${old}`).catch(() => {});
    const who = kind === 'disboard' ? 'DISBOARD' : 'Discadia';
    const when = `<t:${Math.floor(nextAt / 1000)}:R>`;
    const content = uid
      ? `🙏 <@${uid}>, спасибо за бамп на ${who}! Бампов: **${c2.bump.counts[uid]}**. Следующий — ${when}, я напомню.\n-# Thanks for bumping on ${who}! Next one ${when}.`
      : `🙏 Спасибо за бамп на ${who}! Следующий — ${when}, я напомню.\n-# Thanks for bumping on ${who}! Next one ${when}.`;
    await msg.channel.send({ content, allowedMentions: { parse: [] } }).catch(() => {});
  }

  // ---------- gateway (member joins, messages, reactions) ----------
  async function sweepAutoroles(guild) {
    const cfg = configs.get(guild.id);
    if (!cfg || !cfg.autoroles.length) return 0;
    let given = 0;
    const members = await guild.members.fetch();
    for (const member of members.values()) {
      if (member.user.bot || member.pending) continue;
      const missing = cfg.autoroles.filter((r) => !member.roles.cache.has(r) && guild.roles.cache.has(r));
      if (!missing.length) continue;
      try { await member.roles.add(missing, 'Autorole (catch-up)'); given++; } catch { /* hierarchy */ }
    }
    return given;
  }

  async function onJoin(member) {
    const cfg = configs.get(member.guild.id);
    if (!cfg) return;
    if (!member.user.bot && cfg.autoroles.length && !member.pending) {
      try { await member.roles.add(cfg.autoroles.filter((r) => member.guild.roles.cache.has(r)), 'Autorole'); }
      catch (e) { console.error('autorole failed:', e.message); }
    }
    const vars = { user: `<@${member.id}>`, username: member.user.globalName || member.user.username, server: member.guild.name, count: member.guild.memberCount };
    if (cfg.welcome.channel && cfg.welcome.message) {
      api('POST', `/channels/${cfg.welcome.channel}/messages`, { content: fillTemplate(cfg.welcome.message, vars).slice(0, 2000), allowed_mentions: { users: [member.id] } }).catch(() => {});
    }
    if (cfg.welcome.dm && !member.user.bot) dm(member.id, fillTemplate(cfg.welcome.dm, vars));
    log(member.guild.id, { title: '📥 Вход', color: COLOR.green, description: `<@${member.id}> · ${member.user.username} (\`${member.id}\`)\nАккаунт создан <t:${Math.floor(member.user.createdTimestamp / 1000)}:R>`, thumbnail: { url: member.user.displayAvatarURL() } });
  }

  async function onLeave(member) {
    const cfg = configs.get(member.guild.id);
    if (!cfg) return;
    const vars = { user: `**${member.user.username}**`, username: member.user.globalName || member.user.username, server: member.guild.name, count: member.guild.memberCount };
    if (cfg.welcome.leaveChannel && cfg.welcome.leaveMessage) {
      api('POST', `/channels/${cfg.welcome.leaveChannel}/messages`, { content: fillTemplate(cfg.welcome.leaveMessage, vars).slice(0, 2000), allowed_mentions: { parse: [] } }).catch(() => {});
    }
    log(member.guild.id, { title: '📤 Выход', color: COLOR.gray, description: `${member.user.username} (\`${member.id}\`)` });
  }

  async function onMessage(msg) {
    if (msg.guild && (msg.author.id === DISBOARD_ID || msg.author.id === DISCADIA_ID)) return onBumpBot(msg);
    if (!msg.guild || msg.author.bot || msg.webhookId) return;
    const cfg = configs.get(msg.guild.id);
    if (!cfg || !cfg.automod.enabled) return;
    const member = msg.member;
    if (!member || member.permissions.has(PERM.MANAGE_MESSAGES) || cfg.automod.ignoreRoles.some((r) => member.roles.cache.has(r))) return;
    const key = `${msg.guild.id}:${msg.author.id}`;
    const t = Date.now();
    const h = (history.get(key) || []).filter((x) => t - x.t <= 15000);
    h.push({ t, content: msg.content });
    history.set(key, h);
    if (history.size > 5000) history.clear();
    const rule = detectViolation(cfg, msg.content, h, t);
    if (!rule) return;
    await msg.delete().catch(() => {});
    const texts = { spam: 'не флуди', invites: 'приглашения на другие серверы запрещены', links: 'ссылки запрещены', caps: 'не пиши капсом', mentions: 'не упоминай столько людей', badwords: 'это слово запрещено' };
    msg.channel.send({ content: `⚠️ <@${msg.author.id}>, ${texts[rule]}.`, allowedMentions: { users: [msg.author.id] } })
      .then((n) => setTimeout(() => n.delete().catch(() => {}), 6000)).catch(() => {});
    const s = (strikes.get(key) || []).filter((x) => t - x <= 3600000);
    s.push(t);
    strikes.set(key, s);
    let punished = '';
    const a = cfg.automod;
    if (a.timeoutAfter && s.length >= a.timeoutAfter) {
      strikes.delete(key);
      try {
        await member.timeout(a.timeoutMinutes * 60000, `Automod: ${a.timeoutAfter} violations`);
        const id = await addCase(msg.guild.id, msg.author.id, client.user.id, 'timeout', `Automod: ${rule}`);
        punished = `\n🔇 Мут на ${a.timeoutMinutes} мин · случай #${id}`;
      } catch { /* hierarchy */ }
    }
    log(msg.guild.id, { title: `🛡️ Автомод: ${rule}`, color: COLOR.yellow, description: `<@${msg.author.id}> в <#${msg.channel.id}>\n>>> ${(msg.content || '').slice(0, 900)}${punished}` });
  }

  async function onReaction(reaction, user, add) {
    if (user.bot) return;
    try { if (reaction.partial) await reaction.fetch(); } catch { return; }
    const guild = reaction.message.guild;
    if (!guild) return;
    const cfg = configs.get(guild.id);
    if (!cfg || !cfg.reactionRoles.length) return;
    const key = reaction.emoji.id || reaction.emoji.name;
    const rr = cfg.reactionRoles.find((r) => r.messageId === reaction.message.id && r.key === key);
    if (!rr) return;
    try {
      const member = await guild.members.fetch(user.id);
      if (add) await member.roles.add(rr.roleId, 'Reaction role'); else await member.roles.remove(rr.roleId, 'Reaction role');
    } catch (e) { console.error('reaction role failed:', e.message); }
  }

  async function startGateway() {
    if (!token) return false;
    await warm();
    if (!timersStarted) {
      timersStarted = true;
      await loadReminders().catch(() => {});
      setInterval(() => loadReminders().catch(() => {}), 10 * 60 * 1000).unref();
      setInterval(() => bumpTick().catch(() => {}), 60 * 1000).unref();
      setInterval(() => { if (client && client.isReady()) for (const g of client.guilds.cache.values()) sweepAutoroles(g).catch(() => {}); }, 30 * 60 * 1000).unref();
    }
    if (client) { try { client.destroy(); } catch { /* old connection */ } }
    const { Client, GatewayIntentBits, Partials, Options } = require('discord.js');
    client = new Client({
      intents: [GatewayIntentBits.Guilds, GatewayIntentBits.GuildMembers, GatewayIntentBits.GuildMessages, GatewayIntentBits.MessageContent,
        GatewayIntentBits.GuildMessageReactions, GatewayIntentBits.GuildModeration],
      partials: [Partials.Message, Partials.Reaction, Partials.User, Partials.GuildMember],
      makeCache: Options.cacheWithLimits({ MessageManager: 200, PresenceManager: 0, VoiceStateManager: 0, ThreadMemberManager: 0, GuildStickerManager: 0, GuildScheduledEventManager: 0 })
    });
    client.on('guildMemberAdd', (m) => onJoin(m).catch((e) => console.error('join:', e.message)));
    client.on('guildMemberRemove', (m) => onLeave(m).catch((e) => console.error('leave:', e.message)));
    // Members who pass "membership screening" get autoroles when they accept the rules.
    client.on('guildMemberUpdate', (before, after) => { if (before.pending && !after.pending) onJoin(after).catch(() => {}); });
    client.on('messageCreate', (m) => onMessage(m).catch((e) => console.error('automod:', e.message)));
    client.on('messageReactionAdd', (r, u) => onReaction(r, u, true));
    client.on('messageReactionRemove', (r, u) => onReaction(r, u, false));
    client.on('messageDelete', (m) => {
      if (!m.guild || (m.author && m.author.bot) || m.partial) return;
      log(m.guild.id, { title: '🗑️ Сообщение удалено', color: COLOR.red, description: `<@${m.author.id}> в <#${m.channel.id}>\n>>> ${(m.content || '_(вложение)_').slice(0, 1500)}` });
    });
    client.on('messageUpdate', (before, after) => {
      if (!after.guild || (after.author && after.author.bot) || before.partial || before.content === after.content) return;
      log(after.guild.id, { title: '✏️ Сообщение изменено', color: COLOR.yellow, description: `<@${after.author.id}> в <#${after.channel.id}> · [${'перейти'}](${after.url})\n**Было:** ${(before.content || '').slice(0, 900)}\n**Стало:** ${(after.content || '').slice(0, 900)}` });
    });
    client.on('guildBanAdd', (ban) => log(ban.guild.id, { title: '🔨 Бан', color: COLOR.red, description: `${ban.user.username} (\`${ban.user.id}\`)${ban.reason ? `\n${ban.reason}` : ''}` }));
    client.on('guildBanRemove', (ban) => log(ban.guild.id, { title: '✅ Разбан', color: COLOR.green, description: `${ban.user.username} (\`${ban.user.id}\`)` }));
    client.once('ready', async () => {
      console.log(`Discord gateway: ${client.user.tag}, серверов: ${client.guilds.cache.size}`);
      for (const g of client.guilds.cache.values()) {
        try { const n = await sweepAutoroles(g); if (n) console.log(`Автороль: досыпано ${n} участникам на ${g.name}`); } catch (e) { console.error('sweep:', e.message); }
      }
    });
    client.on('error', (e) => console.error('gateway:', e.message));
    await client.login(token);
    return true;
  }

  return { handleCommand, handleComponent, handleAutocomplete, startGateway, warm, getConfig, isCommand: (n) => GUILD_COMMAND_NAMES.has(n), _detect: detectViolation,
    _onBumpBot: onBumpBot, _bumpTick: bumpTick };
}

module.exports = { createGuildBot, GUILD_SCHEMA, GUILD_COMMANDS, parseDuration, fillTemplate, detectViolation, defaultConfig, parseDisboard, parseDiscadia, dueBumps, PERM };
