import { Link } from 'react-router-dom';
import { useLang } from '../i18n.jsx';

export default function NotFound() {
  const { t } = useLang();
  return (
    <div className="wrap page center">
      <div style={{ fontSize: 64 }}>🐝</div>
      <h1>404</h1>
      <p className="lead">{t('Такой страницы нет — пчела улетела.', 'This page doesn’t exist — the bee flew away.')}</p>
      <Link to="/" className="btn btn-primary">{t('На главную', 'Home')}</Link>
    </div>
  );
}
