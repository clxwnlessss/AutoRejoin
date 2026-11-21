'use strict';
// Telegram bot of AutoRejoin (webhook mode, runs inside this server on Render).
// Linking uses the same 6-digit code as the Discord bot (AutoRejoin → Settings → Link bot): one Telegram chat ↔ one
// Top AFK account. Commands: /status (with buttons), /screen (pick an account → photo), /report (last 24 h),
// /notify on|off, /unlink, /help. Notifications: drops (with the storyboard picture), returns, errors, "can't rejoin".
const crypto = require('crypto');

const TELEGRAM_SCHEMA = [
  `CREATE TABLE IF NOT EXISTS telegram_links (
     chat_id TEXT PRIMARY KEY,
     user_id INTEGER NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
     tg_name TEXT,
     notify BOOLEAN NOT NULL DEFAULT TRUE,
     created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
   )`
];

const esc = (s) => String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
// Discord-style **bold** from the app's texts → Telegram HTML.
const md = (s) => esc(s).replace(/\*\*([^*]+)\*\*/g, '<b>$1</b>').replace(/\\([*_`~|])/g, '$1');
function clip(s, n) { s = String(s == null ? '' : s); return s.length > n ? s.slice(0, n - 1) + '…' : s; }
function hm(sec, ru) {
  const h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60);
  return ru ? (h ? `${h} ч ${m} мин` : `${m} мин`) : (h ? `${h}h ${m}m` : `${m}m`);
}
function honeyText(n) {
  if (!n) return '0';
  if (n >= 1e9) return (n / 1e9).toFixed(2) + 'B';
  if (n >= 1e6) return (n / 1e6).toFixed(1) + 'M';
  if (n >= 1e3) return (n / 1e3).toFixed(1) + 'K';
  return String(n);
}

function createTelegram(pool, { token, farm, discord, now = () => new Date(), api: apiOverride } = {}) {
  const links = new Map();   // chatId -> { userId, nickname, name, notify }
  let warmed = null;
  let username = null;
  const secret = token ? crypto.createHash('sha256').update('tg:' + token).digest('hex').slice(0, 32) : null;

  const api = apiOverride || (async (method, body, file) => {
    const url = `https://api.telegram.org/bot${token}/${method}`;
    let res;
    if (file) {
      const form = new FormData();
      for (const [k, v] of Object.entries(body)) form.append(k, typeof v === 'object' ? JSON.stringify(v) : String(v));
      form.append(file.field, new Blob([file.data], { type: 'image/jpeg' }), file.name);
      res = await fetch(url, { method: 'POST', body: form });
    } else {
      res = await fetch(url, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) });
    }
    const json = await res.json().catch(() => ({}));
    if (!json.ok) throw new Error(`Telegram ${method}: ${json.description || res.status}`);
    return json.result;
  });

  function warm() {
    if (!warmed) {
      warmed = pool.query('SELECT t.chat_id, t.user_id, t.tg_name, t.notify, u.nickname FROM telegram_links t JOIN users u ON u.id = t.user_id')
        .then(({ rows }) => { for (const r of rows) links.set(r.chat_id, { userId: r.user_id, nickname: r.nickname, name: r.tg_name, notify: r.notify }); })
        .catch((e) => { warmed = null; throw e; });
    }
    return warmed;
  }
  async function linkOf(chatId) { await warm(); return links.get(String(chatId)) || null; }
  async function linkedForUser(userId) {
    await warm();
    for (const [chatId, v] of links) if (v.userId === userId) return { chatId, ...v };
    return null;
  }
  async function unlinkUser(userId) {
    await warm();
    await pool.query('DELETE FROM telegram_links WHERE user_id = $1', [userId]);
    for (const [k, v] of links) if (v.userId === userId) links.delete(k);
  }

  const send = (chatId, text, extra = {}) => api('sendMessage', { chat_id: chatId, text, parse_mode: 'HTML', disable_web_page_preview: true, ...extra });

  // ---------- status ----------
  function statusText(link) {
    const ru = true;
    const pcs = farm.view(link.userId);
    const online = pcs.filter((p) => p.online);
    const all = farm.accounts(link.userId).filter((a) => a.enabled);
    const inGame = all.filter((a) => a.state === 'ingame' || a.state === 'script').length;
    const running = online.some((p) => p.state && p.state.running);
    let head;
    if (!online.length) head = '⚫ Программа не на связи. Открой AutoRejoin на ПК и войди в аккаунт Топа АФК.';
    else if (!running) head = '⏸ AutoRejoin остановлен';
    else head = `🟢 Работает · в игре <b>${inGame} из ${all.length}</b>${pcs.length > 1 ? ` · ПК: ${online.length}` : ''}`;
    const lines = [`<b>AutoRejoin · ${esc(link.nickname)}</b>`, head, ''];
    const multi = pcs.length > 1;
    for (const pc of (multi ? pcs : online)) {
      if (multi) lines.push(`🖥 <b>${esc(pc.name)}</b> — ${!pc.online ? 'не на связи' : pc.state.running ? 'работает' : 'остановлен'}`);
      for (const a of (pc.online && pc.state ? pc.state.accounts.filter((x) => x.enabled) : []).slice(0, 25)) {
        const ok = a.state === 'ingame' || a.state === 'script';
        lines.push(`${ok ? '🟢' : ['joining', 'launching', 'loading'].includes(a.state) ? '🟡' : '🔴'} <b>${esc(a.name)}</b>${a.main ? ' ⭐' : ''} — ${esc(a.text || a.state)}${ok && a.uptime ? ` · ${esc(a.uptime)}` : ''}`);
      }
    }
    void ru;
    return lines.join('\n').slice(0, 4000);
  }
  function statusKeyboard(link) {
    const online = farm.view(link.userId).filter((p) => p.online);
    const running = online.some((p) => p.state && p.state.running);
    const rows = [[{ text: '🔄 Обновить', callback_data: 'st' }, { text: '📸 Скриншот', callback_data: 'sp' }]];
    if (online.length) rows.push(running
      ? [{ text: '⏹ Остановить', callback_data: 'stop' }, { text: '🔁 Перезайти всеми', callback_data: 'ra' }]
      : [{ text: '▶️ Запустить', callback_data: 'start' }]);
    rows.push([{ text: '📊 Отчёт за сутки', callback_data: 'rep' }]);
    return { inline_keyboard: rows };
  }
  function screenKeyboard(link) {
    const accs = farm.accounts(link.userId).filter((a) => a.enabled);
    const multi = new Set(accs.map((a) => a.pcId)).size > 1;
    const rows = [];
    for (let k = 0; k < Math.min(accs.length, 24); k += 2) {
      rows.push(accs.slice(k, k + 2).map((a) => ({ text: clip(`${a.main ? '⭐ ' : ''}${a.name}${multi ? ` · ${a.pcName}` : ''}`, 40), callback_data: 'sc:' + clip(a.id, 60) })));
    }
    return rows.length ? { inline_keyboard: rows } : null;
  }

  async function screenFor(chatId, link, accountId) {
    const req = farm.requestScreen(link.userId, accountId, async (s) => {
      if (s.error || !s.image) { await send(chatId, '⚠️ Не получилось сделать скриншот: ' + esc(s.error || '?')); return; }
      await api('sendPhoto', { chat_id: chatId, caption: `📸 <b>${esc(s.accountName)}</b>${s.pcName ? ' · ' + esc(s.pcName) : ''}`, parse_mode: 'HTML' },
        { field: 'photo', name: 'screen.jpg', data: s.image });
    });
    if (!req) return send(chatId, '❌ Этот аккаунт сейчас не на связи.');
    setTimeout(() => {
      const info = farm.screenInfo(link.userId, req.id);
      if (info && !info.ready && !info.error) farm.deliverScreen(link.userId, req.id, null, 'программа не ответила за минуту');
    }, 60000).unref?.();
    return send(chatId, `📸 Делаю скриншот <b>${esc(req.accountName)}</b>… (обычно до 15 сек)`);
  }

  async function reportText(link) {
    const r = await farm.report(link.userId);
    if (!r.accounts.length) return '📊 <b>Отчёт за сутки</b>\nДанных пока нет — они появятся, когда аккаунты постоят в игре с привязанной программой.';
    const total = r.accounts.reduce((s, a) => ({ seconds: s.seconds + a.seconds, drops: s.drops + a.drops, honey: s.honey + a.honey }), { seconds: 0, drops: 0, honey: 0 });
    const lines = ['📊 <b>Отчёт за сутки</b>', `⏱ В игре: <b>${hm(total.seconds, true)}</b> · 🔴 вылетов: <b>${total.drops}</b>${total.honey ? ` · 🍯 <b>${honeyText(total.honey)}</b>` : ''}`, ''];
    for (const a of r.accounts.slice(0, 20)) {
      lines.push(`• <b>${esc(a.name)}</b> — ${hm(a.seconds, true)}${a.honey ? ` · 🍯 ${honeyText(a.honey)} (${honeyText(Math.round(a.honey / Math.max(1, a.seconds / 3600)))}/ч)` : ''}${a.drops ? ` · вылетов: ${a.drops}` : ''}`);
    }
    if (r.causes.length) {
      lines.push('', '<b>Причины вылетов:</b>');
      for (const c of r.causes) lines.push(`• ${esc(c.text)} ×${c.n}`);
    }
    return lines.join('\n');
  }

  const HELP = '🐝 <b>AutoRejoin</b>\n\n<b>Команды:</b>\n/status — аккаунты и кнопки управления\n/screen — скриншот окна аккаунта\n/report — отчёт за сутки\n/notify — включить/выключить уведомления\n/unlink — отвязать Telegram\n\nУведомления приходят сами: вылеты (с раскадровкой), возвраты в игру, ошибки и «не может перезайти».';

  async function tryLink(chatId, from, code) {
    const userId = discord.consumeCode(code);
    if (!userId) return send(chatId, '❌ Код неверный или устарел. Возьми новый в AutoRejoin → Настройки → «Привязать бота» и пришли сюда 6 цифр.');
    await warm();
    const name = clip([from.first_name, from.last_name].filter(Boolean).join(' ') || from.username || '', 64);
    await pool.query('DELETE FROM telegram_links WHERE chat_id = $1 OR user_id = $2', [String(chatId), userId]);
    await pool.query('INSERT INTO telegram_links (chat_id, user_id, tg_name) VALUES ($1, $2, $3)', [String(chatId), userId, name]);
    const nick = (await pool.query('SELECT nickname FROM users WHERE id = $1', [userId])).rows[0].nickname;
    for (const [k, v] of links) if (v.userId === userId) links.delete(k);
    links.set(String(chatId), { userId, nickname: nick, name, notify: true });
    return send(chatId, `✅ Привязано к аккаунту <b>${esc(nick)}</b>.\nТеперь сюда будут приходить уведомления о вылетах. Жми /status.`, { reply_markup: { inline_keyboard: [[{ text: '📋 Мои аккаунты', callback_data: 'st' }]] } });
  }

  // ---------- updates ----------
  async function handleMessage(m) {
    const chatId = m.chat && m.chat.id;
    if (!chatId || (m.chat.type && m.chat.type !== 'private')) return;
    const text = String(m.text || '').trim();
    const [cmdRaw, ...rest] = text.split(/\s+/);
    const cmd = cmdRaw.replace(/@\w+$/, '').toLowerCase();
    const arg = rest.join(' ');
    if (/^\d{6}$/.test(text)) return tryLink(chatId, m.from || {}, text);
    if (cmd === '/start' && /^\d{6}$/.test(arg)) return tryLink(chatId, m.from || {}, arg);
    const link = await linkOf(chatId);
    if (cmd === '/start' || cmd === '/help') {
      return send(chatId, link ? HELP : '🐝 <b>AutoRejoin</b>\n\nЧтобы привязать бота:\n1. В программе: <b>Настройки → Привязать бота</b> — появится 6-значный код.\n2. Пришли этот код сюда.\n\nКод одноразовый и живёт 10 минут. Тот же код подходит и для Discord-бота.');
    }
    if (!link) return send(chatId, '🔗 Сначала привяжи аккаунт: в AutoRejoin открой <b>Настройки → Привязать бота</b> и пришли сюда 6 цифр кода.');
    if (cmd === '/status') return send(chatId, statusText(link), { reply_markup: statusKeyboard(link) });
    if (cmd === '/screen') {
      const kb = screenKeyboard(link);
      return kb ? send(chatId, '📸 <b>Скриншот какого аккаунта?</b>', { reply_markup: kb }) : send(chatId, '⚫ Нет аккаунтов на связи. Открой AutoRejoin на ПК.');
    }
    if (cmd === '/report') return send(chatId, await reportText(link));
    if (cmd === '/notify') {
      const on = arg ? /^(on|вкл|да|1)/i.test(arg) : !link.notify;
      await pool.query('UPDATE telegram_links SET notify = $1 WHERE chat_id = $2', [on, String(chatId)]);
      link.notify = on;
      return send(chatId, on ? '🔔 Уведомления включены.' : '🔕 Уведомления выключены. Включить снова: /notify');
    }
    if (cmd === '/unlink') { await unlinkUser(link.userId); return send(chatId, '✅ Telegram отвязан от AutoRejoin.'); }
    return send(chatId, HELP);
  }

  async function handleCallback(q) {
    const chatId = q.message && q.message.chat && q.message.chat.id;
    const msgId = q.message && q.message.message_id;
    const data = String(q.data || '');
    const answer = (text) => api('answerCallbackQuery', { callback_query_id: q.id, ...(text ? { text } : {}) }).catch(() => {});
    const link = chatId ? await linkOf(chatId) : null;
    if (!link) { await answer('Сначала привяжи аккаунт'); return; }
    const edit = (text, markup) => api('editMessageText', { chat_id: chatId, message_id: msgId, text, parse_mode: 'HTML', disable_web_page_preview: true, ...(markup ? { reply_markup: markup } : {}) }).catch(() => {});
    if (data === 'st') { await answer(); return edit(statusText(link), statusKeyboard(link)); }
    if (data === 'sp') {
      await answer();
      const kb = screenKeyboard(link);
      return kb ? send(chatId, '📸 <b>Скриншот какого аккаунта?</b>', { reply_markup: kb }) : send(chatId, '⚫ Нет аккаунтов на связи.');
    }
    if (data.startsWith('sc:')) { await answer('📸 Делаю скриншот…'); return screenFor(chatId, link, data.slice(3)); }
    if (data === 'rep') { await answer(); return send(chatId, await reportText(link)); }
    const cmds = { start: ['start', '▶️ Запуск отправлен'], stop: ['stop', '⏹ Остановка отправлена'], ra: ['rejoin-all', '🔁 Перезаход всеми отправлен'] };
    if (cmds[data]) {
      farm.enqueue(link.userId, cmds[data][0]);
      await answer(cmds[data][1] + ' — выполнится за ~10 сек');
      return edit(statusText(link), statusKeyboard(link));
    }
    await answer();
  }

  async function handleUpdate(u) {
    if (u.message) return handleMessage(u.message);
    if (u.callback_query) return handleCallback(u.callback_query);
  }

  // Express handler: Telegram calls POST /telegram/<secret> with the secret also in a header.
  async function webhook(req, res) {
    if (!token || req.params.secret !== secret || req.get('x-telegram-bot-api-secret-token') !== secret) return res.status(401).end();
    res.json({ ok: true }); // answer fast; Telegram retries slow webhooks
    try { await handleUpdate(req.body || {}); } catch (e) { console.error('Telegram: update failed:', e.message); }
  }

  // ---------- notifications from the farm ----------
  const ICON = { drop: '🔴', back: '🟢', error: '❌', stuck: '🟠', alert: '⚠️', info: 'ℹ️' };
  farm.onEvent(async (userId, e) => {
    if (!token) return;
    const link = await linkedForUser(userId);
    if (!link || !link.notify) return;
    const text = md(e.text);
    const body = /^[🔴🟢❌🟠⚠️ℹ]/u.test(e.text) ? text : `${ICON[e.kind] || ''} ${text}`;
    if (e.image) {
      await api('sendPhoto', { chat_id: link.chatId, caption: clip(body + (e.pcName ? `\n🖥 ${esc(e.pcName)}` : ''), 1000), parse_mode: 'HTML' }, { field: 'photo', name: 'drop.jpg', data: e.image });
    } else {
      await send(link.chatId, body + (e.pcName ? `\n🖥 ${esc(e.pcName)}` : ''));
    }
  });

  // Daily report for every linked chat (the app asks for it at the end of the day, see /api/agent/report).
  async function sendReport(userId) {
    const link = await linkedForUser(userId);
    if (!link || !token) return false;
    await send(link.chatId, await reportText(link));
    return true;
  }

  async function setup(publicUrl) {
    if (!token) return false;
    const me = await api('getMe', {});
    username = me.username;
    await api('setWebhook', { url: `${publicUrl.replace(/\/$/, '')}/telegram/${secret}`, secret_token: secret, allowed_updates: ['message', 'callback_query'], drop_pending_updates: false });
    await api('setMyCommands', { commands: [
      { command: 'status', description: 'Аккаунты и управление' },
      { command: 'screen', description: 'Скриншот окна аккаунта' },
      { command: 'report', description: 'Отчёт за сутки' },
      { command: 'notify', description: 'Уведомления вкл/выкл' },
      { command: 'unlink', description: 'Отвязать Telegram' },
      { command: 'help', description: 'Помощь' }
    ] });
    return username;
  }

  return { webhook, handleUpdate, setup, warm, linkedForUser, unlinkUser, sendReport, reportText, get username() { return username; }, get enabled() { return !!token; }, secret };
}

module.exports = { createTelegram, TELEGRAM_SCHEMA };
