import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useLang } from '../i18n.jsx';
import { useAuth } from '../auth.jsx';
import AuthForm from './AuthForm.jsx';

// Sign in / sign up / forgot password in a modal over any page. Esc or a click outside closes it.
export default function AuthModal() {
  const { t } = useLang();
  const { authModal, openAuth, closeAuth } = useAuth();
  const navigate = useNavigate();

  useEffect(() => {
    if (!authModal) return undefined;
    const onKey = (e) => { if (e.key === 'Escape') closeAuth(); };
    window.addEventListener('keydown', onKey);
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => { window.removeEventListener('keydown', onKey); document.body.style.overflow = prev; };
  }, [authModal, closeAuth]);

  if (!authModal) return null;
  return (
    <div className="modal-back" onMouseDown={(e) => { if (e.target === e.currentTarget) closeAuth(); }}>
      <div className="card modal auth-modal" role="dialog" aria-modal="true" aria-label={t('Вход в аккаунт', 'Sign in')}>
        <div className="row between" style={{ marginBottom: 14 }}>
          <div className="brand small" style={{ margin: 0 }}>AutoRejoin</div>
          <button className="icon-btn" onClick={closeAuth} aria-label={t('Закрыть', 'Close')}>✕</button>
        </div>
        <AuthForm mode={authModal} onMode={openAuth} autoFocus onDone={() => { closeAuth(); navigate('/dashboard'); }} />
      </div>
    </div>
  );
}
