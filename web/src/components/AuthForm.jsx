import { useEffect, useRef, useState } from 'react';
import { useLang, errorText } from '../i18n.jsx';
import { useAuth } from '../auth.jsx';
import { api } from '../api.js';

const NICK_RE = /^[A-Za-z0-9_]{3,20}$/;
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

// Sign in / sign up / forgot password. Used both in the modal and on the /login and /register pages.
// mode: 'login' | 'register' | 'forgot'; onMode switches tabs; onDone runs after a successful sign-in.
export default function AuthForm({ mode, onMode, onDone, autoFocus }) {
  const { t } = useLang();
  const { signIn, adopt } = useAuth();
  const [nickname, setNickname] = useState('');
  const [password, setPassword] = useState('');
  const [repeat, setRepeat] = useState('');
  const [email, setEmail] = useState('');
  const [code, setCode] = useState('');
  const [step, setStep] = useState(1); // forgot / register: 1 = form, 2 = code from the email
  const [busy, setBusy] = useState(false);
  const [slow, setSlow] = useState(false);
  const [err, setErr] = useState('');
  const [note, setNote] = useState('');
  const first = useRef(null);
  const register = mode === 'register';
  const forgot = mode === 'forgot';

  useEffect(() => { setErr(''); setNote(''); setStep(1); setCode(''); }, [mode]);
  useEffect(() => { if (autoFocus && first.current) first.current.focus(); }, [mode, step, autoFocus]);

  async function run(fn) {
    setErr('');
    setBusy(true);
    const timer = setTimeout(() => setSlow(true), 4000);
    try { await fn(); } catch (e) { setErr(errorText(e, t)); } finally { clearTimeout(timer); setBusy(false); setSlow(false); }
  }

  function submit(e) {
    e.preventDefault();
    if (forgot) return step === 1 ? sendCode() : resetPassword();
    if (register) return step === 1 ? signupCode() : signupConfirm();
    return run(async () => { await signIn('login', nickname.trim(), password); onDone && onDone(); });
  }

  // Sign-up: email + nickname + password → a code goes to the email → the code creates the account.
  function signupCode() {
    if (!EMAIL_RE.test(email.trim())) return setErr(t('Похоже, это не почта.', 'That doesn’t look like an email.'));
    if (!NICK_RE.test(nickname.trim())) return setErr(t('Ник: 3–20 символов, латиница, цифры и _.', 'Nickname: 3–20 characters, latin letters, digits and _.'));
    if (password.length < 6) return setErr(t('Пароль: от 6 символов.', 'Password: at least 6 characters.'));
    if (password !== repeat) return setErr(t('Пароли не совпадают.', 'Passwords don’t match.'));
    return run(async () => {
      const r = await api('/api/register/start', { method: 'POST', body: { email: email.trim(), nickname: nickname.trim(), password } });
      setStep(2);
      setCode('');
      setNote(t(`Мы отправили 6-значный код на ${r.email}. Проверь и «Спам».`, `We sent a 6-digit code to ${r.email}. Check spam too.`));
    });
  }

  function signupConfirm() {
    if (!/^\d{6}$/.test(code.trim())) return setErr(t('Код — 6 цифр из письма.', 'The code is the 6 digits from the email.'));
    return run(async () => {
      const data = await api('/api/register/confirm', { method: 'POST', body: { email: email.trim(), code: code.trim() } });
      adopt(data.token, data.nickname);
      onDone && onDone();
    });
  }

  function sendCode() {
    if (!EMAIL_RE.test(email.trim())) return setErr(t('Похоже, это не почта.', 'That doesn’t look like an email.'));
    return run(async () => {
      await api('/api/password/forgot', { method: 'POST', body: { email: email.trim() } });
      setStep(2);
      setNote(t('Если эта почта привязана к аккаунту, на неё пришёл 6-значный код. Проверь и «Спам».', 'If this email is linked to an account, a 6-digit code was sent to it. Check spam too.'));
    });
  }

  function resetPassword() {
    if (!/^\d{6}$/.test(code.trim())) return setErr(t('Код — 6 цифр из письма.', 'The code is the 6 digits from the email.'));
    if (password.length < 6) return setErr(t('Новый пароль: от 6 символов.', 'New password: at least 6 characters.'));
    if (password !== repeat) return setErr(t('Пароли не совпадают.', 'Passwords don’t match.'));
    return run(async () => {
      const data = await api('/api/password/reset', { method: 'POST', body: { email: email.trim(), code: code.trim(), newPassword: password } });
      adopt(data.token, data.nickname);
      onDone && onDone();
    });
  }

  return (
    <div>
      {register && step === 2 && (
        <div style={{ marginBottom: 18 }}>
          <h3 style={{ margin: '0 0 4px' }}>✉️ {t('Подтверди почту', 'Confirm your email')}</h3>
          <p className="muted" style={{ margin: 0, fontSize: 14 }}>{t('Аккаунт создастся, когда введёшь код из письма.', 'The account is created once you enter the code from the email.')}</p>
        </div>
      )}
      {!forgot && !(register && step === 2) && (
        <div className="tabs">
          <a href="#login" className={register ? '' : 'on'} onClick={(e) => { e.preventDefault(); onMode('login'); }}>{t('Вход', 'Sign in')}</a>
          <a href="#register" className={register ? 'on' : ''} onClick={(e) => { e.preventDefault(); onMode('register'); }}>{t('Регистрация', 'Sign up')}</a>
        </div>
      )}
      {forgot && (
        <div style={{ marginBottom: 18 }}>
          <h3 style={{ margin: '0 0 4px' }}>🔐 {t('Сброс пароля', 'Reset password')}</h3>
          <p className="muted" style={{ margin: 0, fontSize: 14 }}>
            {step === 1
              ? t('Введи почту, привязанную к аккаунту, — пришлём код.', 'Enter the email linked to your account — we’ll send a code.')
              : t('Введи код из письма и новый пароль.', 'Enter the code from the email and a new password.')}
          </p>
        </div>
      )}
      {err && <div className="error" role="alert">{err}</div>}
      {note && !err && <div className="info" style={{ marginBottom: 16 }}>{note}</div>}
      <form onSubmit={submit}>
        {register && step === 1 && (
          <div className="field">
            <label htmlFor="af-remail">{t('Почта', 'Email')}</label>
            <input id="af-remail" ref={first} type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" required maxLength={254} />
            <div className="hint">{t('Придёт код подтверждения. Нужна, чтобы восстановить пароль.', 'A confirmation code goes here. Needed to recover your password.')}</div>
          </div>
        )}
        {register && step === 2 && (
          <div className="field">
            <label htmlFor="af-scode">{t('Код из письма', 'Code from the email')}</label>
            <input id="af-scode" ref={first} className="code-input" inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))} required placeholder="••••••" />
            <div className="hint">{email} · <a href="#resend" onClick={(e) => { e.preventDefault(); signupCode(); }}>{t('прислать ещё раз', 'send again')}</a> · <a href="#edit" onClick={(e) => { e.preventDefault(); setStep(1); setNote(''); }}>{t('изменить данные', 'edit details')}</a></div>
          </div>
        )}
        {!forgot && !(register && step === 2) && (
          <>
            <div className="field">
              <label htmlFor="af-nick">{register ? t('Ник', 'Nickname') : t('Ник или почта', 'Nickname or email')}</label>
              <input id="af-nick" ref={register ? undefined : first} value={nickname} onChange={(e) => setNickname(e.target.value)} autoComplete="username" required maxLength={register ? 20 : 254} />
              {register && <div className="hint">{t('Виден в Топе АФК. Латиница, цифры и _, 3–20 символов.', 'Shown in the AFK top. Latin letters, digits and _, 3–20 characters.')}</div>}
            </div>
            <div className="field">
              <div className="row between" style={{ marginBottom: 7 }}>
                <label htmlFor="af-pass" style={{ margin: 0 }}>{t('Пароль', 'Password')}</label>
                {!register && <a href="#forgot" className="tiny-link" onClick={(e) => { e.preventDefault(); onMode('forgot'); }}>{t('Забыл пароль?', 'Forgot password?')}</a>}
              </div>
              <input id="af-pass" type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete={register ? 'new-password' : 'current-password'} required maxLength={100} />
            </div>
            {register && (
              <div className="field">
                <label htmlFor="af-pass2">{t('Повтори пароль', 'Repeat password')}</label>
                <input id="af-pass2" type="password" value={repeat} onChange={(e) => setRepeat(e.target.value)} autoComplete="new-password" required maxLength={100} />
              </div>
            )}
          </>
        )}
        {forgot && step === 1 && (
          <div className="field">
            <label htmlFor="af-email">{t('Почта', 'Email')}</label>
            <input id="af-email" ref={first} type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" required maxLength={254} />
          </div>
        )}
        {forgot && step === 2 && (
          <>
            <div className="field">
              <label htmlFor="af-code">{t('Код из письма', 'Code from the email')}</label>
              <input id="af-code" ref={first} className="code-input" inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))} required placeholder="••••••" />
              <div className="hint">{email} · <a href="#again" onClick={(e) => { e.preventDefault(); setStep(1); setNote(''); }}>{t('другая почта / новый код', 'other email / new code')}</a></div>
            </div>
            <div className="field">
              <label htmlFor="af-np">{t('Новый пароль', 'New password')}</label>
              <input id="af-np" type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" required maxLength={100} />
            </div>
            <div className="field">
              <label htmlFor="af-np2">{t('Повтори новый пароль', 'Repeat new password')}</label>
              <input id="af-np2" type="password" value={repeat} onChange={(e) => setRepeat(e.target.value)} autoComplete="new-password" required maxLength={100} />
            </div>
          </>
        )}
        <button className="btn btn-primary full" disabled={busy}>
          {busy ? <span className="spinner" />
            : forgot ? (step === 1 ? t('Прислать код', 'Send code') : t('Сменить пароль и войти', 'Change password & sign in'))
            : register ? (step === 1 ? t('Продолжить', 'Continue') : t('Подтвердить и войти', 'Confirm & sign in')) : t('Войти', 'Sign in')}
        </button>
        {slow && <p className="muted center" style={{ fontSize: 14 }}>{t('Сервер просыпается — до минуты…', 'The server is waking up — up to a minute…')}</p>}
      </form>
      {forgot && (
        <p className="muted center" style={{ fontSize: 14, margin: '16px 0 0' }}>
          <a href="#login" onClick={(e) => { e.preventDefault(); onMode('login'); }}>← {t('Назад ко входу', 'Back to sign in')}</a>
          <br />
          <span className="tiny">{t('Почту не привязывал? Напиши в Discord — поможем.', 'No email linked? Ask in Discord — we’ll help.')}</span>
        </p>
      )}
      {!forgot && (
        <p className="muted center" style={{ fontSize: 13, margin: '16px 0 0' }}>
          {register
            ? t('Уже регистрировался в программе? Просто войди — аккаунт тот же.', 'Already signed up in the app? Just sign in — it’s the same account.')
            : t('Аккаунт из программы (Топ АФК) подходит и сюда.', 'Your app account (AFK top) works here too.')}
        </p>
      )}
    </div>
  );
}
