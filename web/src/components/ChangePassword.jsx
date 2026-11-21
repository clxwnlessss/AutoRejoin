import { useState } from 'react';
import { useLang, errorText } from '../i18n.jsx';
import { api } from '../api.js';

// Change password while signed in. The old password is optional — you can set a new one even if you forgot it,
// because being signed in already proves the account is yours.
export default function ChangePassword({ token, onExpired }) {
  const { t } = useLang();
  const [open, setOpen] = useState(false);
  const [oldPassword, setOld] = useState('');
  const [newPassword, setNew] = useState('');
  const [repeat, setRepeat] = useState('');
  const [signOutOthers, setSignOut] = useState(true);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState(null);

  async function submit(e) {
    e.preventDefault();
    setMsg(null);
    if (newPassword.length < 6) return setMsg({ err: t('Новый пароль: от 6 символов.', 'New password: at least 6 characters.') });
    if (newPassword !== repeat) return setMsg({ err: t('Пароли не совпадают.', 'Passwords don’t match.') });
    setBusy(true);
    try {
      await api('/api/password', { method: 'POST', token, body: { newPassword, oldPassword: oldPassword || undefined, signOutOthers } });
      setMsg({ ok: t('Пароль изменён. Входи с новым паролем на сайте и в программе.', 'Password changed. Use the new one on the site and in the app.') });
      setOld(''); setNew(''); setRepeat('');
    } catch (e2) {
      if (e2.status === 401 && !oldPassword) onExpired();
      else setMsg({ err: e2.message === 'Старый пароль не подходит.' ? t('Старый пароль не подходит.', 'The old password is wrong.') : errorText(e2, t) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="card" style={{ marginTop: 16 }}>
      <div className="row between">
        <div>
          <h3 style={{ margin: 0 }}>🔑 {t('Пароль', 'Password')}</h3>
          <span className="muted" style={{ fontSize: 14 }}>{t('Забыл старый? Можно сменить и без него — ты уже в аккаунте.', 'Forgot the old one? You can change it without it — you’re signed in.')}</span>
        </div>
        {!open && <button className="btn btn-sm btn-ghost" onClick={() => setOpen(true)}>{t('Сменить пароль', 'Change password')}</button>}
      </div>
      {open && (
        <form onSubmit={submit} style={{ marginTop: 18, maxWidth: 420 }}>
          {msg && msg.err && <div className="error">{msg.err}</div>}
          {msg && msg.ok && <div className="info" style={{ marginBottom: 16 }}>✅ {msg.ok}</div>}
          <div className="field">
            <label htmlFor="op">{t('Старый пароль (если помнишь)', 'Old password (if you remember it)')}</label>
            <input id="op" type="password" value={oldPassword} onChange={(e) => setOld(e.target.value)} autoComplete="current-password" maxLength={100} />
          </div>
          <div className="field">
            <label htmlFor="np">{t('Новый пароль', 'New password')}</label>
            <input id="np" type="password" value={newPassword} onChange={(e) => setNew(e.target.value)} autoComplete="new-password" required maxLength={100} />
          </div>
          <div className="field">
            <label htmlFor="np2">{t('Повтори новый пароль', 'Repeat new password')}</label>
            <input id="np2" type="password" value={repeat} onChange={(e) => setRepeat(e.target.value)} autoComplete="new-password" required maxLength={100} />
          </div>
          <label className="check">
            <input type="checkbox" checked={signOutOthers} onChange={(e) => setSignOut(e.target.checked)} />
            {t('Выйти на других устройствах (программа попросит войти заново)', 'Sign out on other devices (the app will ask to sign in again)')}
          </label>
          <div className="row">
            <button className="btn btn-sm btn-primary" disabled={busy}>{busy ? <span className="spinner" /> : t('Сохранить', 'Save')}</button>
            <button type="button" className="btn btn-sm btn-ghost" onClick={() => { setOpen(false); setMsg(null); }}>{t('Отмена', 'Cancel')}</button>
          </div>
        </form>
      )}
    </div>
  );
}
