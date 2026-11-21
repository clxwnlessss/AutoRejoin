import { useEffect, useState } from 'react';
import { useLang, errorText } from '../i18n.jsx';
import { api, formatHours } from '../api.js';

const COLORS = ['#A855F7', '#22D3EE', '#F472B6', '#2EF2A6', '#FFD23F', '#FB923C', '#818CF8', '#F87171'];

function short(n) {
  if (!n) return '0';
  if (n >= 1e9) return (n / 1e9).toFixed(2) + 'B';
  if (n >= 1e6) return (n / 1e6).toFixed(1) + 'M';
  if (n >= 1e3) return (n / 1e3).toFixed(1) + 'K';
  return String(Math.round(n));
}

// Stacked bars: hours in game per day, one color per account.
function HoursChart({ data, lang }) {
  const W = 760, H = 220, padL = 34, padB = 26, padT = 10;
  const days = data.days;
  const totals = days.map((d) => data.accounts.reduce((s, a) => s + ((a.days[d] && a.days[d].seconds) || 0), 0) / 3600);
  const max = Math.max(1, ...totals);
  const step = (W - padL) / days.length, bw = Math.min(46, step * 0.62);
  const y = (h) => H - padB - (h / max) * (H - padB - padT);
  const ticks = [0, max / 2, max];
  return (
    <svg viewBox={`0 0 ${W} ${H}`} className="chart" role="img" aria-label="hours per day">
      {ticks.map((tk, i) => (
        <g key={i}>
          <line x1={padL} x2={W} y1={y(tk)} y2={y(tk)} stroke="#251D42" strokeDasharray={i ? '4 4' : ''} />
          <text x={padL - 6} y={y(tk) + 4} textAnchor="end" fontSize="11" fill="#706694">{Math.round(tk)}{lang === 'ru' ? 'ч' : 'h'}</text>
        </g>
      ))}
      {days.map((d, i) => {
        let acc = 0;
        const x = padL + i * step + (step - bw) / 2;
        return (
          <g key={d}>
            {data.accounts.map((a, k) => {
              const h = ((a.days[d] && a.days[d].seconds) || 0) / 3600;
              if (!h) return null;
              const top = y(acc + h), bottom = y(acc);
              acc += h;
              return <rect key={a.id} x={x} y={top} width={bw} height={Math.max(1, bottom - top)} rx="3" fill={COLORS[k % COLORS.length]} opacity="0.9"><title>{`${a.name}: ${h.toFixed(1)}`}</title></rect>;
            })}
            <text x={x + bw / 2} y={H - 8} textAnchor="middle" fontSize="11" fill="#706694">
              {new Date(d + 'T12:00:00Z').toLocaleDateString(lang === 'ru' ? 'ru-RU' : 'en-US', { day: 'numeric', month: 'short' })}
            </text>
          </g>
        );
      })}
    </svg>
  );
}

export default function Analytics({ token, onExpired }) {
  const { t, lang } = useLang();
  const [days, setDays] = useState(7);
  const [data, setData] = useState(null);
  const [err, setErr] = useState(null);

  useEffect(() => {
    let alive = true;
    setData(null);
    api(`/api/farm/analytics?days=${days}`, { token })
      .then((d) => alive && setData(d))
      .catch((e) => { if (e.status === 401) onExpired(); else if (alive) setErr(e); });
    return () => { alive = false; };
  }, [days, token, onExpired]);

  const total = data ? data.accounts.reduce((s, a) => ({ seconds: s.seconds + a.seconds, honey: s.honey + a.honey, drops: s.drops + a.drops }), { seconds: 0, honey: 0, drops: 0 }) : null;
  const fmtTime = (iso) => new Date(iso).toLocaleString(lang === 'ru' ? 'ru-RU' : 'en-US', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });

  return (
    <div className="card" style={{ marginTop: 16 }}>
      <div className="row between" style={{ marginBottom: 14 }}>
        <h2 style={{ fontSize: 24, margin: 0 }}>📊 {t('Аналитика фермы', 'Farm analytics')}</h2>
        <div className="tabs tabs-sm">
          {[1, 7, 30].map((d) => <a key={d} href="#" className={days === d ? 'on' : ''} onClick={(e) => { e.preventDefault(); setDays(d); }}>{d === 1 ? t('Сутки', 'Day') : `${d} ${t('дн.', 'days')}`}</a>)}
        </div>
      </div>
      {err && <div className="error">{errorText(err, t)}</div>}
      {!data && !err && <div className="center" style={{ padding: 24 }}><span className="spinner" /></div>}
      {data && data.accounts.length === 0 && (
        <p className="muted">{t('Данных пока нет — они появятся, когда аккаунты постоят в игре с привязанной программой (обновляется каждую минуту).', 'No data yet — it appears once accounts spend time in game with the app linked (updates every minute).')}</p>
      )}
      {data && data.accounts.length > 0 && (
        <>
          <div className="stats" style={{ marginBottom: 18 }}>
            <div className="stat"><b>{formatHours(total.seconds)}</b><span>{t('в игре (все аккаунты)', 'in game (all accounts)')}</span></div>
            <div className="stat"><b>{short(total.honey)}</b><span>{t('мёда нафармлено', 'honey farmed')}</span></div>
            <div className="stat"><b>{short(total.honey / Math.max(1, total.seconds / 3600))}</b><span>{t('мёда в час', 'honey per hour')}</span></div>
            <div className="stat"><b>{total.drops}</b><span>{t('вылетов', 'drops')}</span></div>
          </div>
          {days > 1 && <HoursChart data={data} lang={lang} />}
          <div style={{ overflowX: 'auto', marginTop: 10 }}>
            <table className="table">
              <thead><tr><th>{t('Аккаунт', 'Account')}</th><th className="right">{t('В игре', 'In game')}</th><th className="right">{t('Мёд', 'Honey')}</th><th className="right hide-sm">{t('Мёд/ч', 'Honey/h')}</th><th className="right">{t('Вылеты', 'Drops')}</th></tr></thead>
              <tbody>
                {data.accounts.map((a, k) => (
                  <tr key={a.id}>
                    <td><span className="dot" style={{ background: COLORS[k % COLORS.length] }} /><b>{a.name}</b>{a.pc && <div className="muted" style={{ fontSize: 12 }}>🖥️ {a.pc}</div>}</td>
                    <td className="right"><b>{formatHours(a.seconds)}</b></td>
                    <td className="right">{a.honey ? short(a.honey) : '—'}</td>
                    <td className="right hide-sm muted">{a.honey ? short(a.honey / Math.max(1, a.seconds / 3600)) : '—'}</td>
                    <td className="right">{a.drops || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {!data.accounts.some((a) => a.honey) && (
            <p className="muted" style={{ fontSize: 13 }}>{t('Мёд появится, когда включено «Считать мёд» в настройках программы (нужен Потассиум).', 'Honey shows up when “Count honey” is on in the app settings (needs Potassium).')}</p>
          )}
          {data.events.length > 0 && (
            <>
              <h3 style={{ marginTop: 18 }}>🔴 {t('Последние вылеты и проблемы', 'Recent drops and problems')}</h3>
              <div className="events">
                {data.events.slice(0, 12).map((e, i) => (
                  <div className="event" key={i}>
                    <span className="muted">{fmtTime(e.at)}</span>
                    <span>{String(e.text || '').replace(/\*\*/g, '').replace(/\\([*_`~|])/g, '$1')}</span>
                  </div>
                ))}
              </div>
            </>
          )}
        </>
      )}
    </div>
  );
}
