import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useLang, errorText } from '../i18n.jsx';
import { useAuth } from '../auth.jsx';
import { api, formatHours } from '../api.js';

function lastMonths(n) {
  const out = [];
  const d = new Date();
  for (let i = 0; i < n; i++) {
    const m = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() - i, 1));
    out.push(m.toISOString().slice(0, 7));
  }
  return out;
}

export default function Top() {
  const { t, lang } = useLang();
  const { session, openAuth } = useAuth();
  const months = useMemo(() => lastMonths(6), []);
  const [month, setMonth] = useState(months[0]);
  const [data, setData] = useState(null);
  const [err, setErr] = useState(null);
  const [slow, setSlow] = useState(false);

  useEffect(() => {
    let alive = true;
    setData(null); setErr(null); setSlow(false);
    const timer = setTimeout(() => alive && setSlow(true), 4000);
    api(`/api/top?month=${month}&limit=100`)
      .then((d) => alive && setData(d))
      .catch((e) => alive && setErr(e))
      .finally(() => clearTimeout(timer));
    return () => { alive = false; clearTimeout(timer); };
  }, [month]);

  const label = (m) => new Date(`${m}-01T00:00:00Z`).toLocaleDateString(lang === 'ru' ? 'ru-RU' : 'en-US', { month: 'long', year: 'numeric', timeZone: 'UTC' });
  const medals = ['🥇', '🥈', '🥉'];
  const me = session && session.nickname.toLowerCase();

  return (
    <div className="wrap page">
      <h1>🏆 {t('Топ АФК за месяц', 'Monthly AFK top')}</h1>
      <p className="lead">{t('Часы, пока у человека хотя бы один аккаунт в игре. Считает сервер, накрутить нельзя. Топ-3 получают роль Top Stander в Discord.', 'Hours while someone has at least one account in game. Counted by the server, can’t be faked. The top 3 get the Top Stander role in Discord.')}</p>
      <div className="card">
        <div className="toolbar">
          <select value={month} onChange={(e) => setMonth(e.target.value)} aria-label={t('Месяц', 'Month')}>
            {months.map((m) => <option key={m} value={m}>{label(m)}</option>)}
          </select>
          {!session && <button className="btn btn-sm btn-primary" onClick={() => openAuth('register')}>{t('Попасть в топ', 'Join the top')}</button>}
        </div>
        {err && <div className="error">{errorText(err, t)}</div>}
        {!data && !err && (
          <div className="center muted" style={{ padding: 30 }}>
            <span className="spinner" />
            {slow && <p>{t('Сервер просыпается (бесплатный хостинг засыпает без запросов) — до минуты…', 'The server is waking up (free hosting sleeps when idle) — up to a minute…')}</p>}
          </div>
        )}
        {data && data.top.length === 0 && <p className="center muted" style={{ padding: 30 }}>{t('В этом месяце пока никого нет — стань первым!', 'Nobody here yet this month — be the first!')}</p>}
        {data && data.top.length > 0 && (
          <table className="table">
            <thead><tr><th>#</th><th>{t('Ник', 'Nickname')}</th><th className="right">{t('Время', 'Time')}</th><th className="right hide-sm">{t('Часы аккаунтов', 'Account hours')}</th></tr></thead>
            <tbody>
              {data.top.map((r) => (
                <tr key={r.nickname} className={me === r.nickname.toLowerCase() ? 'me' : ''}>
                  <td className="rank">{r.rank <= 3 ? <span className="medal">{medals[r.rank - 1]}</span> : r.rank}</td>
                  <td>{r.online && <span className="live" title={t('Сейчас в игре', 'In game now')} />}<b>{r.nickname}</b></td>
                  <td className="right"><b>{formatHours(r.seconds)}</b></td>
                  <td className="right muted hide-sm">{formatHours(r.accountSeconds)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
