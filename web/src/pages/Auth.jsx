import { Navigate, useNavigate } from 'react-router-dom';
import { useLang } from '../i18n.jsx';
import { useAuth } from '../auth.jsx';
import AuthForm from '../components/AuthForm.jsx';

// /login and /register as pages (direct links, redirects from private pages). The header opens the same form in a modal.
export default function Auth({ mode }) {
  const { t } = useLang();
  const { session } = useAuth();
  const navigate = useNavigate();
  if (session) return <Navigate to="/dashboard" replace />;
  const go = (m) => navigate(m === 'register' ? '/register' : m === 'forgot' ? '/forgot' : '/login');
  return (
    <div className="wrap page">
      <div className="auth">
        <h1 className="center">{mode === 'register' ? t('Регистрация', 'Sign up') : mode === 'forgot' ? t('Сброс пароля', 'Reset password') : t('Вход', 'Sign in')}</h1>
        <p className="muted center" style={{ marginTop: -6, marginBottom: 26 }}>
          {t('Один аккаунт для сайта и программы AutoRejoin (Топ АФК).', 'One account for the site and the AutoRejoin app (AFK top).')}
        </p>
        <div className="card">
          <AuthForm mode={mode} onMode={go} onDone={() => navigate('/dashboard', { replace: true })} />
        </div>
      </div>
    </div>
  );
}
