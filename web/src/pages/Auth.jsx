import { useEffect, useState } from 'react';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { useLang, errorText } from '../i18n.jsx';
import { useAuth } from '../auth.jsx';

const NICK_RE = /^[A-Za-z0-9_]{3,20}$/;

export default function Auth({ mode }) {
  const { t } = useLang();
  const { session, signIn } = useAuth();
  const navigate = useNavigate();
  const [nickname, setNickname] = useState('');
  const [password, setPassword] = useState('');
  const [repeat, setRepeat] = useState('');
  const [busy, setBusy] = useState(false);
  const [slow, setSlow] = useState(false);
  const [err, setErr] = useState('');
  const register = mode === 'register';

  useEffect(() => { setErr(''); }, [mode]);
  if (session) return <Navigate to="/dashboard" replace />;

  async function submit(e) {
    e.preventDefault();
    setErr('');
    if (register) {
      if (!NICK_RE.test(nickname.trim())) return setErr(t('Ник: 3–20 символов, латиница, цифры и _.', 'Nickname: 3–20 characters, latin letters, digits and _.'));
      if (password.length < 6) return setErr(t('Пароль: от 6 символов.', 'Password: at least 6 characters.'));
      if (password !== repeat) return setErr(t('Пароли не совпадают.', 'Passwords don’t match.'));
    }
    setBusy(true);
    const timer = setTimeout(() => setSlow(true), 4000);
    try {
      await signIn(mode, nickname.trim(), password);
      navigate('/dashboard', { replace: true });
    } catch (e2) {
      setErr(errorText(e2, t));
    } finally {
      clearTimeout(timer);
      setBusy(false);
      setSlow(false);
    }
  }

  return (
    <div className="wrap page">
      <div className="auth">
        <h1 className="center">{register ? t('Регистрация', 'Sign up') : t('Вход', 'Sign in')}</h1>
        <p className="muted center" style={{ marginTop: -6, marginBottom: 26 }}>
          {t('Один аккаунт для сайта и программы AutoRejoin (Топ АФК).', 'One account for the site and the AutoRejoin app (AFK top).')}
        </p>
        <div className="card">
          <div className="tabs">
            <Link to="/login" className={register ? '' : 'on'}>{t('Вход', 'Sign in')}</Link>
            <Link to="/register" className={register ? 'on' : ''}>{t('Регистрация', 'Sign up')}</Link>
          </div>
          {err && <div className="error" role="alert">{err}</div>}
          <form onSubmit={submit}>
            <div className="field">
              <label htmlFor="nick">{t('Ник', 'Nickname')}</label>
              <input id="nick" value={nickname} onChange={(e) => setNickname(e.target.value)} autoComplete="username" required maxLength={20} />
              {register && <div className="hint">{t('Виден в Топе АФК. Латиница, цифры и _, 3–20 символов.', 'Shown in the AFK top. Latin letters, digits and _, 3–20 characters.')}</div>}
            </div>
            <div className="field">
              <label htmlFor="pass">{t('Пароль', 'Password')}</label>
              <input id="pass" type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete={register ? 'new-password' : 'current-password'} required maxLength={100} />
            </div>
            {register && (
              <div className="field">
                <label htmlFor="pass2">{t('Повтори пароль', 'Repeat password')}</label>
                <input id="pass2" type="password" value={repeat} onChange={(e) => setRepeat(e.target.value)} autoComplete="new-password" required maxLength={100} />
              </div>
            )}
            <button className="btn btn-primary full" disabled={busy}>
              {busy ? <span className="spinner" /> : register ? t('Создать аккаунт', 'Create account') : t('Войти', 'Sign in')}
            </button>
            {slow && <p className="muted center" style={{ fontSize: 14 }}>{t('Сервер просыпается — до минуты…', 'The server is waking up — up to a minute…')}</p>}
          </form>
        </div>
        <p className="muted center" style={{ fontSize: 14, marginTop: 18 }}>
          {register
            ? t('Уже регистрировался в программе? Просто войди — аккаунт тот же.', 'Already signed up in the app? Just sign in — it’s the same account.')
            : t('Аккаунт из программы (Топ АФК) подходит и сюда.', 'Your app account (AFK top) works here too.')}
        </p>
      </div>
    </div>
  );
}
