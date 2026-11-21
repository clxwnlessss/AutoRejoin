import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useLang, errorText } from '../i18n.jsx';
import { useAuth } from '../auth.jsx';
import { api, formatHours } from '../api.js';

// Readable temporary password: no 0/O/1/l confusion.
function tempPassword() {
  const abc = 'abcdefghjkmnpqrstuvwxyz23456789';
  const bytes = new Uint32Array(10);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => abc[b % abc.length]).join('');
}

function ResetDialog({ user, token, onClose, onDone }) {
  const { t } = useLang();
  const [password, setPassword] = useState(tempPassword);
  const [signOut, setSignOut] = useState(false);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState('');
  const [done, setDone] = useState(false);
  const [copied, setCopied] = useState(false);

  async function submit(e) {
    e.preventDefault();
    setErr('');
    if (password.length < 6) return setErr(t('Пароль: от 6 символов.', 'Password: at least 6 characters.'));
    setBusy(true);
    try {
      await api(`/api/admin/users/${user.id}/password`, { method: 'POST', token, body: { newPassword: password, signOut } });
      setDone(true);
      onDone();
    } catch (e2) { setErr(errorText(e2, t)); } finally { setBusy(false); }
  }
  const copy = () => { navigator.clipboard.writeText(password).then(() => setCopied(true)).catch(() => {}); };

  return (
    <div className="modal-back" onClick={onClose}>
      <div className="card modal" onClick={(e) => e.stopPropagation()} role="dialog" aria-modal="true">
        <h3>🔑 {t('Новый пароль для', 'New password for')} {user.nickname}</h3>
        {done ? (
          <>
            <div className="info" style={{ margin: '14px 0' }}>
              ✅ {t('Готово. Передай игроку новый пароль лично (например, в ЛС Discord) и попроси сменить его в «Мой аккаунт».', 'Done. Send the new password to the player privately (e.g. a Discord DM) and ask them to change it in “My account”.')}
            </div>
            <div className="row"><code className="pass">{password}</code><button className="btn btn-sm btn-ghost" onClick={copy}>{copied ? '✓' : t('Копировать', 'Copy')}</button></div>
            <div className="row" style={{ marginTop: 18 }}><button className="btn btn-sm btn-primary" onClick={onClose}>{t('Закрыть', 'Close')}</button></div>
          </>
        ) : (
          <form onSubmit={submit} style={{ marginTop: 14 }}>
            {err && <div className="error">{err}</div>}
            <p className="muted" style={{ marginTop: 0, fontSize: 14 }}>{t('Убедись, что это правда владелец аккаунта (например, напиши ему в Discord).', 'Make sure it really is the account owner (e.g. message them on Discord).')}</p>
            <div className="field">
              <label htmlFor="tp">{t('Временный пароль', 'Temporary password')}</label>
              <div className="row" style={{ flexWrap: 'nowrap' }}>
                <input id="tp" value={password} onChange={(e) => setPassword(e.target.value)} maxLength={100} autoComplete="off" />
                <button type="button" className="icon-btn" title={t('Сгенерировать', 'Generate')} onClick={() => setPassword(tempPassword())}>🎲</button>
              </div>
            </div>
            <label className="check">
              <input type="checkbox" checked={signOut} onChange={(e) => setSignOut(e.target.checked)} />
              {t('Выйти на всех устройствах (программа игрока тоже выйдет и часы перестанут идти, пока он не войдёт)', 'Sign out everywhere (the player’s app signs out too and stops counting hours until they sign in)')}
            </label>
            <div className="row">
              <button className="btn btn-sm btn-primary" disabled={busy}>{busy ? <span className="spinner" /> : t('Сменить пароль', 'Set password')}</button>
              <button type="button" className="btn btn-sm btn-ghost" onClick={onClose}>{t('Отмена', 'Cancel')}</button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}

export default function Admin() {
  const { t, lang } = useLang();
  const { session, expired } = useAuth();
  const [q, setQ] = useState('');
  const [data, setData] = useState(null);
  const [err, setErr] = useState(null);
  const [reset, setReset] = useState(null);
  const [note, setNote] = useState('');
  const token = session.token;

  const load = useCallback((query) => {
    api(`/api/admin/users?q=${encodeURIComponent(query || '')}`, { token })
      .then((d) => { setData(d); setErr(null); })
      .catch((e) => { if (e.status === 401) expired(); else setErr(e); });
  }, [token, expired]);

  useEffect(() => { const id = setTimeout(() => load(q), 250); return () => clearTimeout(id); }, [q, load]);

  async function signOutUser(u) {
    if (!window.confirm(t(`Выйти из аккаунта ${u.nickname} на всех устройствах?`, `Sign ${u.nickname} out on all devices?`))) return;
    try {
      await api(`/api/admin/users/${u.id}/signout`, { method: 'POST', token });
      setNote(t(`${u.nickname}: все сессии закрыты.`, `${u.nickname}: all sessions closed.`));
      load(q);
    } catch (e) { setNote(errorText(e, t)); }
  }

  if (err && err.status === 403) {
    return (
      <div className="wrap page center">
        <h1>🔒 {t('Только для администратора', 'Admins only')}</h1>
        <Link to="/dashboard" className="btn btn-ghost">{t('Мой аккаунт', 'My account')}</Link>
      </div>
    );
  }
  const fmt = (d) => (d ? new Date(d).toLocaleString(lang === 'ru' ? 'ru-RU' : 'en-US', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }) : '—');

  return (
    <div className="wrap page">
      <h1>🛠️ {t('Админка', 'Admin')}</h1>
      <p className="lead">{t('Пользователи Топа АФК. Пароли не видны никому — их можно только заменить.', 'AFK top users. Nobody can see passwords — they can only be replaced.')}</p>
      {err && <div className="error">{errorText(err, t)}</div>}
      {note && <div className="info" style={{ marginBottom: 14 }}>{note}</div>}
      <div className="card">
        <div className="toolbar">
          <input style={{ maxWidth: 320 }} placeholder={t('Поиск по нику…', 'Search by nickname…')} value={q} onChange={(e) => setQ(e.target.value)} aria-label={t('Поиск', 'Search')} />
          <span className="muted" style={{ fontSize: 14 }}>{data ? `${data.users.length} ${t('польз.', 'users')}` : ''}</span>
        </div>
        {!data && !err && <div className="center" style={{ padding: 24 }}><span className="spinner" /></div>}
        {data && (
          <div style={{ overflowX: 'auto' }}>
            <table className="table">
              <thead><tr><th>{t('Ник', 'Nickname')}</th><th className="right">{t('За месяц', 'This month')}</th><th className="hide-sm">{t('Последний пульс', 'Last heartbeat')}</th><th className="hide-sm">Discord</th><th className="right hide-sm">{t('Сессии', 'Sessions')}</th><th /></tr></thead>
              <tbody>
                {data.users.map((u) => (
                  <tr key={u.id}>
                    <td>{u.online && <span className="live" />}<b>{u.nickname}</b>{u.admin && <span className="main-tag">ADMIN</span>}<div className="muted" style={{ fontSize: 12 }}>#{u.id} · {fmt(u.createdAt)}</div></td>
                    <td className="right"><b>{formatHours(u.seconds)}</b></td>
                    <td className="hide-sm muted">{fmt(u.lastBeat)}{u.online ? ` · ${u.inGame} ${t('в игре', 'in game')}` : ''}</td>
                    <td className="hide-sm muted">{u.discord || '—'}</td>
                    <td className="right hide-sm">{u.sessions}</td>
                    <td className="right">
                      <div className="row" style={{ justifyContent: 'flex-end', flexWrap: 'nowrap' }}>
                        <button className="btn btn-sm btn-ghost" onClick={() => setReset(u)}>🔑 {t('Пароль', 'Password')}</button>
                        <button className="icon-btn" title={t('Выйти везде', 'Sign out everywhere')} onClick={() => signOutUser(u)}>⏏</button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {reset && <ResetDialog user={reset} token={token} onClose={() => setReset(null)} onDone={() => load(q)} />}
    </div>
  );
}
