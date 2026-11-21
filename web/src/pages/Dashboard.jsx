import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useLang, errorText } from '../i18n.jsx';
import { useAuth } from '../auth.jsx';
import { api, formatHours, DISCORD } from '../api.js';
import ChangePassword from '../components/ChangePassword.jsx';

const BADGE = { ingame: 'b-green', script: 'b-cyan', loading: 'b-cyan', launching: 'b-cyan', queued: 'b-orange', waiting: 'b-orange', offline: 'b-orange', problem: 'b-red', blocked: 'b-red', deferred: 'b-red' };

function stateLabel(state, t) {
  const map = {
    ingame: t('В игре', 'In game'), script: t('Ждёт скрипт', 'Waiting for script'), loading: t('Заходит', 'Joining'), launching: t('Запускается', 'Launching'),
    queued: t('В очереди', 'Queued'), waiting: t('Ждёт', 'Waiting'), offline: t('Отключён', 'Disconnected'), problem: t('Проблема', 'Problem'),
    blocked: t('Заблокирован', 'Blocked'), deferred: t('Отложен', 'Deferred'), off: t('Выключен', 'Off')
  };
  return map[state] || state || '—';
}

export default function Dashboard() {
  const { t, lang } = useLang();
  const { session, signOut, expired } = useAuth();
  const [me, setMe] = useState(null);
  const [agent, setAgent] = useState(null);
  const [err, setErr] = useState(null);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState('');
  const token = session.token;
  const noteTimer = useRef(null);

  const handle = useCallback((e) => { if (e.status === 401) expired(); else setErr(e); }, [expired]);

  useEffect(() => {
    api('/api/me', { token }).then(setMe).catch(handle);
  }, [token, handle]);

  // The app reports every ~10 s; poll a bit faster while the tab is visible.
  useEffect(() => {
    let alive = true;
    const load = () => { if (document.visibilityState === 'visible') api('/api/agent/state', { token }).then((d) => alive && setAgent(d)).catch((e) => alive && handle(e)); };
    load();
    const id = setInterval(load, 5000);
    return () => { alive = false; clearInterval(id); };
  }, [token, handle]);

  async function command(cmd, arg, label) {
    setBusy(cmd + (arg || ''));
    try {
      await api('/api/agent/command', { method: 'POST', token, body: { cmd, arg } });
      setNote(`✅ ${label} — ${t('команда отправлена, программа выполнит её в течение 10 секунд.', 'command sent, the app runs it within 10 seconds.')}`);
    } catch (e) {
      setNote(`❌ ${errorText(e, t)}`);
    } finally {
      setBusy('');
      clearTimeout(noteTimer.current);
      noteTimer.current = setTimeout(() => setNote(''), 8000);
    }
  }

  const st = agent && agent.state;
  const accounts = st ? st.accounts : [];
  const enabled = accounts.filter((a) => a.enabled);
  const inGame = enabled.filter((a) => a.state === 'ingame' || a.state === 'script').length;
  const monthName = me && new Date(`${me.month}-01T00:00:00Z`).toLocaleDateString(lang === 'ru' ? 'ru-RU' : 'en-US', { month: 'long', timeZone: 'UTC' });

  let head = null;
  if (agent && !agent.linked) head = null;
  else if (agent && !agent.online) head = <span className="badge b-gray">{t('Программа не на связи', 'App offline')}{agent.lastSeen ? ` · ${new Date(agent.lastSeen).toLocaleString(lang === 'ru' ? 'ru-RU' : 'en-US')}` : ''}</span>;
  else if (st && !st.running) head = <span className="badge b-red">{t('Остановлен', 'Stopped')}</span>;
  else if (st && st.phase !== 'running') head = <span className="badge b-orange">{t('Запускается…', 'Starting…')}</span>;
  else if (st) head = <span className={`badge ${inGame === enabled.length && enabled.length ? 'b-green' : 'b-orange'}`}>{t('В игре', 'In game')} {inGame} / {enabled.length}</span>;

  return (
    <div className="wrap page">
      <div className="row between" style={{ marginBottom: 26 }}>
        <div>
          <h1 style={{ marginBottom: 4 }}>👋 {session.nickname}</h1>
          <span className="muted">{t('Твой аккаунт AutoRejoin', 'Your AutoRejoin account')}</span>
        </div>
        <div className="row">
          {me && me.admin && <Link to="/admin" className="btn btn-sm btn-ghost">🛠️ {t('Админка', 'Admin')}</Link>}
          <button className="btn btn-sm btn-ghost" onClick={signOut}>{t('Выйти', 'Sign out')}</button>
        </div>
      </div>
      {err && <div className="error">{errorText(err, t)}</div>}

      <div className="grid-2" style={{ marginBottom: 16 }}>
        <div className="card">
          <div className="muted">{t('АФК за ', 'AFK in ')}{monthName || '…'}</div>
          <div className="big">{me ? formatHours(me.seconds) : <span className="spinner" />}</div>
          <div className="muted" style={{ fontSize: 14 }}>{t('Считается, пока хотя бы один аккаунт в игре.', 'Counts while at least one account is in game.')}</div>
        </div>
        <div className="card">
          <div className="muted">{t('Место в топе', 'Rank in the top')}</div>
          <div className="big">{me ? (me.rank ? `#${me.rank}` : '—') : <span className="spinner" />}</div>
          <div className="muted" style={{ fontSize: 14 }}><Link to="/top">{t('Открыть топ →', 'Open the top →')}</Link></div>
        </div>
      </div>

      <div className="card">
        <div className="row between" style={{ marginBottom: 14 }}>
          <h2 style={{ fontSize: 24, margin: 0 }}>{t('Аккаунты', 'Accounts')}</h2>
          {head}
        </div>

        {!agent && <div className="center" style={{ padding: 24 }}><span className="spinner" /></div>}

        {agent && !agent.linked && (
          <div className="info">
            <b>{t('Чтобы видеть аккаунты здесь и управлять ими:', 'To see your accounts here and control them:')}</b>
            <ol style={{ margin: '8px 0 0', paddingLeft: 20 }}>
              <li>{t('В программе войди в этот аккаунт (Топ АФК).', 'In the app, sign in to this account (AFK top).')}</li>
              <li>{t('Настройки → «Привязать бота» → получишь 6-значный код.', 'Settings → “Link bot” → you get a 6-digit code.')}</li>
              <li>{t('В Discord у бота AutoRejoin нажми «Привязать аккаунт» и введи код.', 'In Discord, press “Link account” in the AutoRejoin bot and enter the code.')} <a href={DISCORD} target="_blank" rel="noreferrer">Discord ↗</a></li>
            </ol>
            <p style={{ margin: '8px 0 0' }}>{t('Названия аккаунтов программа отправляет только после привязки — это сделано для приватности.', 'The app sends account names only after linking — for privacy.')}</p>
          </div>
        )}

        {agent && agent.linked && !agent.online && (
          <p className="muted">{t('Открой AutoRejoin на ПК и войди в аккаунт — аккаунты появятся здесь через несколько секунд.', 'Open AutoRejoin on your PC and sign in — accounts appear here within seconds.')}</p>
        )}

        {st && (
          <>
            <div className="row" style={{ marginBottom: 8 }}>
              {!st.running
                ? <button className="btn btn-sm btn-green" disabled={!!busy} onClick={() => command('start', '', t('Запуск', 'Start'))}>▶ {t('Запустить', 'Start')}</button>
                : <button className="btn btn-sm btn-danger" disabled={!!busy} onClick={() => command('stop', '', t('Остановка', 'Stop'))}>■ {t('Остановить', 'Stop')}</button>}
              <button className="btn btn-sm btn-ghost" disabled={!!busy || !st.running} onClick={() => command('rejoin-all', '', t('Перезаход всеми', 'Rejoin all'))}>↻ {t('Перезайти всеми', 'Rejoin all')}</button>
              {agent.discordName && <span className="muted" style={{ fontSize: 14 }}>Discord: {agent.discordName}</span>}
            </div>
            {note && <div className="info" style={{ margin: '10px 0' }}>{note}</div>}
            <div>
              {accounts.length === 0 && <p className="muted">{t('В программе пока нет аккаунтов.', 'No accounts in the app yet.')}</p>}
              {accounts.map((a) => (
                <div className="acc" key={a.id}>
                  <div className="grow">
                    <div className="name">{a.name}{a.main && <span className="main-tag">{t('МЕЙН', 'MAIN')}</span>}</div>
                    <div className="text">{a.enabled ? (a.text || '') : t('Выключен — AutoRejoin его не запускает', 'Off — AutoRejoin doesn’t launch it')}{a.uptime ? ` · ${a.uptime}` : ''}</div>
                  </div>
                  <span className={`badge ${a.enabled ? (BADGE[a.state] || 'b-gray') : 'b-gray'}`}>{stateLabel(a.enabled ? a.state : 'off', t)}</span>
                  <button className="icon-btn" title={t('Перезайти', 'Rejoin')} disabled={!!busy || !st.running || !a.enabled}
                    onClick={() => command('rejoin', a.id, `${t('Перезаход', 'Rejoin')} ${a.name}`)}>↻</button>
                </div>
              ))}
            </div>
          </>
        )}
      </div>

      <ChangePassword token={token} onExpired={expired} />
    </div>
  );
}
