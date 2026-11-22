import { useState } from 'react';
import { useLang, errorText } from '../i18n.jsx';
import { useAuth } from '../auth.jsx';
import { api } from '../api.js';

// Password card in the dashboard:
//  • change — new password while signed in (the old one is optional: being signed in proves the account is yours);
//  • reset by email — a code goes to the linked email, code + new password → every device is signed out, this one signs in again.
export default function ChangePassword({ token, email, onExpired }) {
  const { t } = useLang();
  const { adopt } = useAuth();
  const [mode, setMode] = useState(null); // null | 'change' | 'email'
  const [oldPassword, setOld] = useState('');
  const [newPassword, setNew] = useState('');
  const [repeat, setRepeat] = useState('');
  const [code, setCode] = useState('');
  const [codeSent, setCodeSent] = useState(false);
  const [signOutOthers, setSignOut] = useState(true);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState(null);

  const close = () => { setMode(null); setOld(''); setNew(''); setRepeat(''); setCode(''); setCodeSent(false); };
  const open = (m) => { close(); setMsg(null); setMode(m); };
  const checkNew = () => {
    if (newPassword.length < 6) return t('Новый пароль: от 6 символов.', 'New password: at least 6 characters.');
    if (newPassword !== repeat) return t('Пароли не совпадают.', 'Passwords don’t match.');
    return null;
  };

  async function change(e) {
    e.preventDefault();
    setMsg(null);
    const bad = checkNew(); if (bad) return setMsg({ err: bad });
    setBusy(true);
    try {
      await api('/api/password', { method: 'POST', token, body: { newPassword, oldPassword: oldPassword || undefined, signOutOthers } });
      close();
      setMsg({ ok: t('Пароль изменён. Входи с новым паролем на сайте и в программе.', 'Password changed. Use the new one on the site and in the app.') });
    } catch (e2) {
      if (e2.status === 401 && !oldPassword) onExpired();
      else setMsg({ err: e2.message === 'Старый пароль не подходит.' ? t('Старый пароль не подходит.', 'The old password is wrong.') : errorText(e2, t) });
    } finally { setBusy(false); }
  }

  async function sendCode() {
    setMsg(null);
    setBusy(true);
    try {
      await api('/api/password/forgot', { method: 'POST', body: { email } });
      setCodeSent(true);
      setMsg({ ok: t(`Код отправлен на ${email}. Проверь и «Спам».`, `Code sent to ${email}. Check spam too.`) });
    } catch (e2) { setMsg({ err: errorText(e2, t) }); } finally { setBusy(false); }
  }

  async function reset(e) {
    e.preventDefault();
    setMsg(null);
    if (!/^\d{6}$/.test(code)) return setMsg({ err: t('Код — 6 цифр из письма.', 'The code is the 6 digits from the email.') });
    const bad = checkNew(); if (bad) return setMsg({ err: bad });
    setBusy(true);
    try {
      const r = await api('/api/password/reset', { method: 'POST', body: { email, code, newPassword } });
      adopt(r.token, r.nickname);
      close();
      setMsg({ ok: t('Пароль сброшен. Все устройства вышли из аккаунта — в программе войди с новым паролем.', 'Password reset. All devices were signed out — sign in to the app with the new password.') });
    } catch (e2) { setMsg({ err: errorText(e2, t) }); } finally { setBusy(false); }
  }

  const newFields = (
    <>
      <div className="field">
        <label htmlFor="np">{t('Новый пароль', 'New password')}</label>
        <input id="np" type="password" value={newPassword} onChange={(e) => setNew(e.target.value)} autoComplete="new-password" required maxLength={100} />
      </div>
      <div className="field">
        <label htmlFor="np2">{t('Повтори новый пароль', 'Repeat new password')}</label>
        <input id="np2" type="password" value={repeat} onChange={(e) => setRepeat(e.target.value)} autoComplete="new-password" required maxLength={100} />
      </div>
    </>
  );

  return (
    <div className="card" style={{ marginTop: 16 }}>
      <div className="row between">
        <div>
          <h3 style={{ margin: 0 }}>🔑 {t('Пароль', 'Password')}</h3>
          <span className="muted" style={{ fontSize: 14 }}>{t('Сменить пароль или сбросить его кодом из письма.', 'Change your password or reset it with a code from an email.')}</span>
        </div>
        {!mode && (
          <div className="row">
            <button className="btn btn-sm btn-ghost" onClick={() => open('change')}>{t('Сменить пароль', 'Change password')}</button>
            <button className="btn btn-sm btn-ghost" onClick={() => open('email')}>✉️ {t('Сбросить пароль', 'Reset password')}</button>
          </div>
        )}
      </div>
      {msg && msg.err && <div className="error" style={{ marginTop: 16, marginBottom: 0 }}>{msg.err}</div>}
      {msg && msg.ok && <div className="info" style={{ marginTop: 16 }}>✅ {msg.ok}</div>}

      {mode === 'change' && (
        <form onSubmit={change} style={{ marginTop: 18, maxWidth: 420 }}>
          <div className="field">
            <label htmlFor="op">{t('Старый пароль (если помнишь)', 'Old password (if you remember it)')}</label>
            <input id="op" type="password" value={oldPassword} onChange={(e) => setOld(e.target.value)} autoComplete="current-password" maxLength={100} />
          </div>
          {newFields}
          <label className="check">
            <input type="checkbox" checked={signOutOthers} onChange={(e) => setSignOut(e.target.checked)} />
            {t('Выйти на других устройствах (программа попросит войти заново)', 'Sign out on other devices (the app will ask to sign in again)')}
          </label>
          <div className="row">
            <button className="btn btn-sm btn-primary" disabled={busy}>{busy ? <span className="spinner" /> : t('Сохранить', 'Save')}</button>
            <button type="button" className="btn btn-sm btn-ghost" onClick={close}>{t('Отмена', 'Cancel')}</button>
          </div>
        </form>
      )}

      {mode === 'email' && (
        <div style={{ marginTop: 18, maxWidth: 420 }}>
          {!email ? (
            <>
              <div className="info" style={{ marginBottom: 14 }}>{t('Сначала привяжи почту в карточке «Почта» ниже — код придёт на неё.', 'Link an email in the “Email” card below first — the code goes there.')}</div>
              <button className="btn btn-sm btn-ghost" onClick={close}>{t('Понятно', 'Got it')}</button>
            </>
          ) : !codeSent ? (
            <div className="row">
              <button className="btn btn-sm btn-primary" onClick={sendCode} disabled={busy}>{busy ? <span className="spinner" /> : t(`Прислать код на ${email}`, `Send a code to ${email}`)}</button>
              <button className="btn btn-sm btn-ghost" onClick={close}>{t('Отмена', 'Cancel')}</button>
            </div>
          ) : (
            <form onSubmit={reset}>
              <div className="field">
                <label htmlFor="rc">{t('Код из письма', 'Code from the email')}</label>
                <input id="rc" className="code-input" inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))} placeholder="••••••" autoFocus required />
                <div className="hint"><a href="#resend" onClick={(e) => { e.preventDefault(); sendCode(); }}>{t('Прислать код ещё раз', 'Send the code again')}</a></div>
              </div>
              {newFields}
              <div className="row">
                <button className="btn btn-sm btn-primary" disabled={busy}>{busy ? <span className="spinner" /> : t('Сбросить пароль', 'Reset password')}</button>
                <button type="button" className="btn btn-sm btn-ghost" onClick={close}>{t('Отмена', 'Cancel')}</button>
              </div>
            </form>
          )}
        </div>
      )}
    </div>
  );
}
