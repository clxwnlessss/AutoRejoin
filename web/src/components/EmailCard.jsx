import { useState } from 'react';
import { useLang, errorText } from '../i18n.jsx';
import { api } from '../api.js';

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

// Link an email to the account: address → 6-digit code from the letter → linked. Needed to reset a forgotten password.
export default function EmailCard({ token, email, onChange, onExpired }) {
  const { t } = useLang();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState('');
  const [code, setCode] = useState('');
  const [sentTo, setSentTo] = useState('');
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState(null);

  async function run(fn) {
    setMsg(null);
    setBusy(true);
    try { await fn(); } catch (e) { if (e.status === 401) onExpired(); else setMsg({ err: errorText(e, t) }); } finally { setBusy(false); }
  }
  const reset = () => { setEditing(false); setValue(''); setCode(''); setSentTo(''); };

  function send(e) {
    e && e.preventDefault();
    if (!EMAIL_RE.test(value.trim())) return setMsg({ err: t('Похоже, это не почта.', 'That doesn’t look like an email.') });
    return run(async () => {
      const r = await api('/api/email/start', { method: 'POST', token, body: { email: value.trim() } });
      setSentTo(r.email);
      setMsg({ ok: t(`Код отправлен на ${r.email}. Проверь и «Спам».`, `Code sent to ${r.email}. Check spam too.`) });
    });
  }
  function confirm(e) {
    e.preventDefault();
    if (!/^\d{6}$/.test(code)) return setMsg({ err: t('Код — 6 цифр из письма.', 'The code is the 6 digits from the email.') });
    return run(async () => {
      const r = await api('/api/email/confirm', { method: 'POST', token, body: { code } });
      onChange(r.email);
      reset();
      setMsg({ ok: t('Почта привязана ✅ Теперь забытый пароль можно сбросить кодом из письма.', 'Email linked ✅ Now a forgotten password can be reset with a code from an email.') });
    });
  }
  function unlink() {
    if (!window.confirm(t('Отвязать почту? Без неё нельзя сбросить забытый пароль.', 'Unlink the email? Without it you can’t reset a forgotten password.'))) return;
    run(async () => { await api('/api/email', { method: 'DELETE', token }); onChange(null); });
  }

  return (
    <div className="card" style={{ marginTop: 16 }}>
      <div className="row between">
        <div>
          <h3 style={{ margin: 0 }}>✉️ {t('Почта', 'Email')}</h3>
          <span className="muted" style={{ fontSize: 14 }}>
            {email === undefined ? '…'
              : email ? <>{t('Привязана: ', 'Linked: ')}<b style={{ color: 'var(--text)' }}>{email}</b></>
              : t('Не привязана. Привяжи — и забытый пароль можно будет сбросить кодом из письма.', 'Not linked. Link one to reset a forgotten password with a code from an email.')}
          </span>
        </div>
        {!editing && email !== undefined && (
          <div className="row">
            <button className="btn btn-sm btn-ghost" onClick={() => { setEditing(true); setMsg(null); }}>{email ? t('Сменить', 'Change') : t('Привязать почту', 'Link email')}</button>
            {email && <button className="btn btn-sm btn-danger" onClick={unlink} disabled={busy}>{t('Отвязать', 'Unlink')}</button>}
          </div>
        )}
      </div>
      {msg && msg.err && <div className="error" style={{ marginTop: 14, marginBottom: 0 }}>{msg.err}</div>}
      {msg && msg.ok && <div className="info" style={{ marginTop: 14 }}>{msg.ok}</div>}
      {editing && (
        <div style={{ marginTop: 16, maxWidth: 460 }}>
          {!sentTo ? (
            <form onSubmit={send} className="email-row">
              <input type="email" value={value} onChange={(e) => setValue(e.target.value)} placeholder="name@gmail.com" autoComplete="email" maxLength={254} autoFocus aria-label={t('Почта', 'Email')} />
              <button className="btn btn-sm btn-primary" disabled={busy}>{busy ? <span className="spinner" /> : t('Прислать код', 'Send code')}</button>
              <button type="button" className="btn btn-sm btn-ghost" onClick={() => { reset(); setMsg(null); }}>{t('Отмена', 'Cancel')}</button>
            </form>
          ) : (
            <form onSubmit={confirm}>
              <div className="email-row">
                <input className="code-input" inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))} placeholder="••••••" autoFocus aria-label={t('Код из письма', 'Code from the email')} />
                <button className="btn btn-sm btn-primary" disabled={busy}>{busy ? <span className="spinner" /> : t('Подтвердить', 'Confirm')}</button>
              </div>
              <p className="muted" style={{ fontSize: 13, margin: '10px 0 0' }}>
                <a href="#resend" onClick={(e) => { e.preventDefault(); send(); }}>{t('Прислать ещё раз', 'Send again')}</a>
                {' · '}
                <a href="#other" onClick={(e) => { e.preventDefault(); setSentTo(''); setCode(''); setMsg(null); }}>{t('Другая почта', 'Other email')}</a>
                {' · '}
                <a href="#cancel" onClick={(e) => { e.preventDefault(); reset(); setMsg(null); }}>{t('Отмена', 'Cancel')}</a>
              </p>
            </form>
          )}
        </div>
      )}
    </div>
  );
}
