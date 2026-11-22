'use strict';
// Outgoing mail for AutoRejoin (email codes). Free Render blocks outgoing SMTP, so mail goes through the
// Brevo HTTP API (free: 300 emails/day, only a verified sender address is needed, no own domain).
// Render env: BREVO_API_KEY (secret) and MAIL_FROM (the sender address verified in Brevo).

function createMailer(options = {}) {
  const key = options.brevoKey || '';
  const from = options.from || '';
  const fromName = options.fromName || 'AutoRejoin';
  const doFetch = options.fetch || globalThis.fetch;
  return {
    configured: !!(key && from),
    async send(to, subject, text, html) {
      if (!key || !from) throw new Error('mail not configured');
      const res = await doFetch('https://api.brevo.com/v3/smtp/email', {
        method: 'POST',
        headers: { 'api-key': key, 'content-type': 'application/json', accept: 'application/json' },
        body: JSON.stringify({ sender: { email: from, name: fromName }, to: [{ email: to }], subject, textContent: text, htmlContent: html })
      });
      if (!res.ok) throw new Error(`Brevo ${res.status}: ${(await res.text().catch(() => '')).slice(0, 200)}`);
      return true;
    }
  };
}

// Code email in both languages (the user may read either).
function codeEmail(code, purpose, nickname) {
  const what = purpose === 'reset'
    ? { ru: 'сброса пароля', en: 'to reset your password', subj: 'Код для сброса пароля / Password reset code' }
    : purpose === 'signup'
      ? { ru: 'подтверждения регистрации', en: 'to finish signing up', subj: 'Код подтверждения регистрации / Sign-up code' }
      : { ru: 'привязки почты', en: 'to link this email', subj: 'Код подтверждения почты / Email confirmation code' };
  const text = `AutoRejoin — ${nickname}\n\nКод для ${what.ru}: ${code}\nYour code ${what.en}: ${code}\n\n` +
    'Код действует 15 минут. Если это был не ты — просто игнорируй письмо.\nThe code is valid for 15 minutes. If it wasn’t you, ignore this email.';
  const html = `<div style="font-family:Segoe UI,Arial,sans-serif;max-width:460px;margin:0 auto;padding:24px;background:#0b0b12;color:#ededed;border-radius:16px">
<h2 style="margin:0 0 6px">AutoRejoin</h2><p style="margin:0 0 18px;color:#a1a1a1">${escapeHtml(nickname)}</p>
<p style="margin:0 0 6px">Код для ${what.ru} · Your code ${what.en}:</p>
<div style="font-size:34px;font-weight:800;letter-spacing:8px;background:#16161f;border:1px solid #2e2e3a;border-radius:12px;padding:14px 0;text-align:center">${code}</div>
<p style="margin:18px 0 0;color:#8a8a8a;font-size:13px">Код действует 15 минут. Если это был не ты — просто игнорируй письмо.<br>The code is valid for 15 minutes. If it wasn’t you, ignore this email.</p></div>`;
  return { subject: `AutoRejoin: ${what.subj}`, text, html };
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

module.exports = { createMailer, codeEmail };
