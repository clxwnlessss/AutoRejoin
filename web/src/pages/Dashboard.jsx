import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useLang, errorText } from '../i18n.jsx';
import { useAuth } from '../auth.jsx';
import { api, formatHours, DISCORD } from '../api.js';
import ChangePassword from '../components/ChangePassword.jsx';
import Analytics from '../components/Analytics.jsx';

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
  const [shot, setShot] = useState(null);
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

  async function command(cmd, arg, label, pcId) {
    setBusy(cmd + (arg || '') + (pcId || ''));
    try {
      await api('/api/agent/command', { method: 'POST', token, body: { cmd, arg, pcId } });
      setNote(`✅ ${label} — ${t('команда отправлена, программа выполнит её в течение 10 секунд.', 'command sent, the app runs it within 10 seconds.')}`);
    } catch (e) {
      setNote(`❌ ${errorText(e, t)}`);
    } finally {
      setBusy('');
      clearTimeout(noteTimer.current);
      noteTimer.current = setTimeout(() => setNote(''), 8000);
    }
  }

  // Screenshot: ask the server, then poll until the app uploads the picture (or says why it could not).
  async function screen(a) {
    setShot({ name: a.name, pc: a.pcName, loading: true });
    try {
      const r = await api('/api/agent/screen-request', { method: 'POST', token, body: { accountId: a.id } });
      for (let i = 0; i < 45; i++) {
        await new Promise((res) => setTimeout(res, 1500));
        const s = await api(`/api/agent/screen/${r.id}`, { token });
        if (s.ready || s.error) { setShot({ name: s.accountName, pc: s.pcName, image: s.image, error: s.error }); return; }
      }
      setShot({ name: a.name, error: t('программа не ответила', 'the app did not answer') });
    } catch (e) {
      setShot({ name: a.name, error: errorText(e, t) });
    }
  }

  const pcs = (agent && agent.pcs) || [];
  const onlinePcs = pcs.filter((p) => p.online);
  const all = onlinePcs.flatMap((p) => (p.state ? p.state.accounts.filter((a) => a.enabled) : []));
  const inGame = all.filter((a) => a.state === 'ingame' || a.state === 'script').length;
  const multi = pcs.length > 1;
  const monthName = me && new Date(`${me.month}-01T00:00:00Z`).toLocaleDateString(lang === 'ru' ? 'ru-RU' : 'en-US', { month: 'long', timeZone: 'UTC' });

  let head = null;
  if (agent && agent.linked && !onlinePcs.length) head = <span className="badge b-gray">{t('Программа не на связи', 'App offline')}</span>;
  else if (onlinePcs.length && !onlinePcs.some((p) => p.state.running)) head = <span className="badge b-red">{t('Остановлен', 'Stopped')}</span>;
  else if (onlinePcs.length) head = <span className={`badge ${inGame === all.length && all.length ? 'b-green' : 'b-orange'}`}>{t('В игре', 'In game')} {inGame} / {all.length}{multi ? ` · ${t('ПК', 'PCs')}: ${onlinePcs.length}` : ''}</span>;

  const pcBlock = (pc) => {
    const st = pc.state;
    const accounts = pc.online && st ? st.accounts : [];
    return (
      <div key={pc.id} className={multi ? 'pc-block' : ''}>
        {multi && (
          <div className="row between" style={{ marginBottom: 6 }}>
            <div className="pc-name">🖥️ {pc.name} {!pc.online && <span className="badge b-gray">{t('не на связи', 'offline')}</span>}</div>
          </div>
        )}
        {pc.online && st && (
          <div className="row" style={{ marginBottom: 8 }}>
            {!st.running
              ? <button className="btn btn-sm btn-green" disabled={!!busy} onClick={() => command('start', '', t('Запуск', 'Start') + (multi ? ` · ${pc.name}` : ''), pc.id)}>▶ {t('Запустить', 'Start')}</button>
              : <button className="btn btn-sm btn-danger" disabled={!!busy} onClick={() => command('stop', '', t('Остановка', 'Stop') + (multi ? ` · ${pc.name}` : ''), pc.id)}>■ {t('Остановить', 'Stop')}</button>}
            <button className="btn btn-sm btn-ghost" disabled={!!busy || !st.running} onClick={() => command('rejoin-all', '', t('Перезаход всеми', 'Rejoin all'), pc.id)}>↻ {t('Перезайти всеми', 'Rejoin all')}</button>
          </div>
        )}
        {pc.online && accounts.length === 0 && <p className="muted">{t('В программе пока нет аккаунтов.', 'No accounts in the app yet.')}</p>}
        {accounts.map((a) => (
          <div className="acc" key={a.id}>
            <div className="grow">
              <div className="name">{a.name}{a.main && <span className="main-tag">{t('МЕЙН', 'MAIN')}</span>}</div>
              <div className="text">{a.enabled ? (a.text || '') : t('Выключен — AutoRejoin его не запускает', 'Off — AutoRejoin doesn’t launch it')}{a.uptime ? ` · ${a.uptime}` : ''}</div>
            </div>
            <span className={`badge ${a.enabled ? (BADGE[a.state] || 'b-gray') : 'b-gray'}`}>{stateLabel(a.enabled ? a.state : 'off', t)}</span>
            <button className="icon-btn" title={t('Скриншот окна', 'Window screenshot')} disabled={!a.enabled}
              onClick={() => screen({ ...a, pcName: pc.name })}>📸</button>
            <button className="icon-btn" title={t('Перезайти', 'Rejoin')} disabled={!!busy || !st.running || !a.enabled}
              onClick={() => command('rejoin', a.id, `${t('Перезаход', 'Rejoin')} ${a.name}`)}>↻</button>
          </div>
        ))}
      </div>
    );
  };

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
          <h2 style={{ fontSize: 24, margin: 0 }}>{multi ? t('Ферма', 'Farm') : t('Аккаунты', 'Accounts')}</h2>
          {head}
        </div>

        {!agent && <div className="center" style={{ padding: 24 }}><span className="spinner" /></div>}

        {agent && !agent.linked && (
          <div className="info">
            <b>{t('Чтобы видеть аккаунты здесь и управлять ими:', 'To see your accounts here and control them:')}</b>
            <ol style={{ margin: '8px 0 0', paddingLeft: 20 }}>
              <li>{t('В программе войди в этот аккаунт (Топ АФК).', 'In the app, sign in to this account (AFK top).')}</li>
              <li>{t('Настройки → «Привязать бота» → получишь 6-значный код.', 'Settings → “Link bot” → you get a 6-digit code.')}</li>
              <li>{t('Введи код у Discord-бота («Привязать аккаунт») или пришли его Telegram-боту.', 'Enter the code in the Discord bot (“Link account”) or send it to the Telegram bot.')} <a href={DISCORD} target="_blank" rel="noreferrer">Discord ↗</a></li>
            </ol>
            <p style={{ margin: '8px 0 0' }}>{t('Названия аккаунтов программа отправляет только после привязки — это сделано для приватности.', 'The app sends account names only after linking — for privacy.')}</p>
          </div>
        )}

        {agent && agent.linked && !onlinePcs.length && (
          <p className="muted">{t('Открой AutoRejoin на ПК и войди в аккаунт — аккаунты появятся здесь через несколько секунд.', 'Open AutoRejoin on your PC and sign in — accounts appear here within seconds.')}</p>
        )}

        {agent && agent.linked && (
          <div className="row" style={{ marginBottom: 6, fontSize: 14 }}>
            {agent.discordName && <span className="muted">Discord: {agent.discordName}</span>}
            {agent.telegram && <span className="muted">Telegram: {agent.telegram.name || t('привязан', 'linked')}</span>}
          </div>
        )}
        {note && <div className="info" style={{ margin: '10px 0' }}>{note}</div>}
        {(multi ? pcs : onlinePcs).map(pcBlock)}
      </div>

      {agent && agent.linked && <Analytics token={token} onExpired={expired} />}

      {shot && (
        <div className="modal-back" onClick={() => setShot(null)}>
          <div className="card modal shot-modal" onClick={(e) => e.stopPropagation()} role="dialog" aria-modal="true">
            <div className="row between" style={{ marginBottom: 12 }}>
              <h3 style={{ margin: 0 }}>📸 {shot.name}{shot.pc ? ` · ${shot.pc}` : ''}</h3>
              <button className="icon-btn" onClick={() => setShot(null)} aria-label={t('Закрыть', 'Close')}>✕</button>
            </div>
            {shot.loading && <div className="center muted" style={{ padding: 30 }}><span className="spinner" /><p>{t('Программа делает скриншот… (обычно до 15 сек)', 'The app is taking the screenshot… (usually under 15 s)')}</p></div>}
            {shot.error && <div className="error">{t('Не получилось: ', 'Could not: ')}{shot.error}</div>}
            {shot.image && <img src={shot.image} alt={shot.name} style={{ width: '100%', borderRadius: 10, display: 'block' }} />}
          </div>
        </div>
      )}

      <ChangePassword token={token} onExpired={expired} />
    </div>
  );
}
