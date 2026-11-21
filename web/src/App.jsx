import { useEffect, useState } from 'react';
import { Link, NavLink, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { useLang } from './i18n.jsx';
import { useAuth } from './auth.jsx';
import { DISCORD, REPO } from './api.js';
import Home from './pages/Home.jsx';
import Download from './pages/Download.jsx';
import Top from './pages/Top.jsx';
import Auth from './pages/Auth.jsx';
import Dashboard from './pages/Dashboard.jsx';
import Changelog from './pages/Changelog.jsx';
import Faq from './pages/Faq.jsx';
import NotFound from './pages/NotFound.jsx';
import Admin from './pages/Admin.jsx';

const ICON = `${import.meta.env.BASE_URL}icon.png`;

function Header() {
  const { t, lang, setLang } = useLang();
  const { session } = useAuth();
  const [open, setOpen] = useState(false);
  const location = useLocation();
  useEffect(() => setOpen(false), [location.pathname]);
  const links = [
    ['/download', t('Установка', 'Install')],
    ['/top', t('Топ АФК', 'AFK top')],
    ['/faq', 'FAQ'],
    ['/changelog', t('Обновления', 'Changelog')]
  ];
  return (
    <header className="header">
      <div className="wrap header-row">
        <Link to="/" className="brand"><img src={ICON} alt="" width="36" height="36" />AutoRejoin</Link>
        <button className="burger" aria-label={t('Меню', 'Menu')} aria-expanded={open} onClick={() => setOpen(!open)}>
          <span /><span /><span />
        </button>
        <nav className={`nav ${open ? 'open' : ''}`}>
          {links.map(([to, label]) => <NavLink key={to} to={to}>{label}</NavLink>)}
          <button className="lang" onClick={() => setLang(lang === 'ru' ? 'en' : 'ru')} title={t('Switch to English', 'Переключить на русский')}>
            {lang === 'ru' ? 'EN' : 'RU'}
          </button>
          {session
            ? <NavLink to="/dashboard" className="btn btn-sm btn-ghost">👤 {session.nickname}</NavLink>
            : <NavLink to="/login" className="btn btn-sm btn-ghost">{t('Войти', 'Sign in')}</NavLink>}
          <Link to="/download" className="btn btn-sm btn-primary">{t('Скачать', 'Download')}</Link>
        </nav>
      </div>
    </header>
  );
}

function Footer() {
  const { t } = useLang();
  return (
    <footer className="footer">
      <div className="wrap footer-row">
        <div>
          <div className="brand small"><img src={ICON} alt="" width="26" height="26" />AutoRejoin</div>
          <p className="muted">{t('Бесплатно и с открытым кодом. Не связан с Roblox и Onett.', 'Free and open source. Not affiliated with Roblox or Onett.')}</p>
        </div>
        <div className="footer-links">
          <Link to="/download">{t('Скачать', 'Download')}</Link>
          <Link to="/top">{t('Топ АФК', 'AFK top')}</Link>
          <Link to="/faq">FAQ</Link>
          <a href={`https://github.com/${REPO}`} target="_blank" rel="noreferrer">GitHub</a>
          <a href={DISCORD} target="_blank" rel="noreferrer">Discord</a>
        </div>
      </div>
      <div className="wrap muted tiny">© {new Date().getFullYear()} clxwnlesss</div>
    </footer>
  );
}

function Private({ children }) {
  const { session } = useAuth();
  return session ? children : <Navigate to="/login" replace state={{ from: '/dashboard' }} />;
}

function Title() {
  const { t } = useLang();
  const { pathname } = useLocation();
  useEffect(() => {
    const names = {
      '/download': t('Скачать и установить', 'Download & install'),
      '/top': t('Топ АФК за месяц', 'Monthly AFK top'),
      '/login': t('Вход', 'Sign in'),
      '/register': t('Регистрация', 'Sign up'),
      '/dashboard': t('Мой аккаунт', 'My account'),
      '/faq': 'FAQ',
      '/admin': t('Админка', 'Admin'),
      '/changelog': t('Обновления', 'Changelog')
    };
    document.title = names[pathname] ? `${names[pathname]} — AutoRejoin` : 'AutoRejoin — free auto rejoin for Bee Swarm Simulator (Roblox)';
    window.scrollTo(0, 0);
  }, [pathname, t]);
  return null;
}

export default function App() {
  return (
    <>
      <div className="glow" aria-hidden="true" />
      <Title />
      <Header />
      <main>
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/download" element={<Download />} />
          <Route path="/top" element={<Top />} />
          <Route path="/login" element={<Auth mode="login" />} />
          <Route path="/register" element={<Auth mode="register" />} />
          <Route path="/dashboard" element={<Private><Dashboard /></Private>} />
          <Route path="/admin" element={<Private><Admin /></Private>} />
          <Route path="/changelog" element={<Changelog />} />
          <Route path="/faq" element={<Faq />} />
          <Route path="*" element={<NotFound />} />
        </Routes>
      </main>
      <Footer />
    </>
  );
}
