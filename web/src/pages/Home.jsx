import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useLang } from '../i18n.jsx';
import { api, DISCORD } from '../api.js';

const B = import.meta.env.BASE_URL;

function Stats() {
  const { t } = useLang();
  const [s, setS] = useState(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    let alive = true;
    api('/api/stats').then((d) => alive && setS(d)).catch(() => alive && setFailed(true));
    return () => { alive = false; };
  }, []);
  if (failed) return null;
  const items = [
    [s && s.onlineAccounts, <><span className="live" />{t('аккаунтов в игре сейчас', 'accounts in game now')}</>],
    [s && s.onlineUsers, t('человек фармят прямо сейчас', 'people farming right now')],
    [s && s.monthAccountHours != null ? s.monthAccountHours.toLocaleString() : null, t('часов АФК аккаунтов за месяц', 'account AFK hours this month')],
    [s && s.users, t('зарегистрировано в Топе АФК', 'registered in the AFK top')]
  ];
  return (
    <div className="stats">
      {items.map(([value, label], i) => (
        <div className="stat" key={i}>
          {value == null ? <div className="skeleton" style={{ height: 36, width: 70, marginBottom: 6 }} /> : <b>{value}</b>}
          <span>{label}</span>
        </div>
      ))}
    </div>
  );
}

export default function Home() {
  const { t, lang } = useLang();
  const features = [
    ['🔁', t('Сам перезаходит', 'Rejoins by itself'), t('Кик, дисконнект, зависание, вылет, утечка памяти — окно закрывается и аккаунт заходит обратно.', 'Kick, disconnect, freeze, crash or memory leak — the window closes and the account joins back.')],
    ['👥', t('Сколько угодно аккаунтов', 'Unlimited accounts'), t('Запуск по очереди, мейн первым. Свой сервер, FPS и графика для каждого аккаунта.', 'Launches one by one, main first. Own server, FPS and graphics per account.')],
    ['🔔', t('Оповещения в Discord', 'Discord alerts'), t('Вебхук: кто вылетел и почему, отчёт раз в N минут, анализ, на чём аккаунт не может зайти.', 'Webhook: who dropped and why, a report every N minutes, analysis of why an account can’t rejoin.')],
    ['🤖', t('Управление из Discord', 'Control from Discord'), t('Бот показывает аккаунты в игре и запускает, останавливает и перезаходит ими по кнопке.', 'The bot shows accounts in game and starts, stops and rejoins them with a button.')],
    ['🏆', t('Топ АФК за месяц', 'Monthly AFK top'), t('Сервер считает часы, пока твои аккаунты в игре. Топ-3 месяца получают роль в Discord.', 'The server counts hours while your accounts are in game. Monthly top 3 get a Discord role.')],
    ['🧠', t('Бережёт память', 'Saves memory'), t('Лимит памяти на окно, перезапуск раздутых окон, расписание плановых перезаходов.', 'Memory cap per window, restart of bloated windows, scheduled rejoins.')]
  ];
  return (
    <>
      <div className="wrap hero">
        <div>
          <div className="chips">
            <span className="chip">🐝 Bee Swarm Simulator</span><span className="chip">Windows 10 / 11</span><span className="chip">{t('Бесплатно', 'Free')}</span><span className="chip">Open source</span>
          </div>
          <h1>{t('Твои аккаунты ', 'Keep your accounts ')}<span className="grad">{t('в игре 24/7', 'in game 24/7')}</span></h1>
          <p className="lead">{t('AutoRejoin следит за каждым окном Roblox и сам возвращает аккаунты в Bee Swarm после кика, дисконнекта или зависания. Спи спокойно — ферма работает.', 'AutoRejoin watches every Roblox window and brings your accounts back to Bee Swarm after kicks, disconnects and freezes. Sleep — your farm keeps going.')}</p>
          <div className="btns">
            <Link to="/download" className="btn btn-primary">⬇ {t('Скачать бесплатно', 'Download free')}</Link>
            <Link to="/register" className="btn btn-ghost">{t('Создать аккаунт', 'Create account')}</Link>
          </div>
          <p className="note">{t('Один аккаунт для программы и сайта: Топ АФК, панель аккаунтов, Discord-бот.', 'One account for the app and the site: AFK top, account panel, Discord bot.')}</p>
        </div>
        <img className="shot" src={`${B}${lang === 'ru' ? 'screenshot.png' : 'screenshot-en.png'}`} alt={t('Окно AutoRejoin со списком аккаунтов', 'AutoRejoin window with the account list')} width="1140" height="760" />
      </div>

      <div className="wrap"><Stats /></div>

      <section className="wrap">
        <h2>{t('Всё, что нужно для АФК-фермы', 'Everything an AFK farm needs')}</h2>
        <p className="sub">{t('Без макросов в игре: AutoRejoin работает с окнами Roblox снаружи.', 'No in-game macros: AutoRejoin manages Roblox windows from the outside.')}</p>
        <div className="grid">
          {features.map(([ico, title, text]) => (
            <div className="card" key={title}><div className="ico">{ico}</div><h3>{title}</h3><p>{text}</p></div>
          ))}
        </div>
      </section>

      <section className="wrap">
        <h2>{t('Запуск за 3 минуты', 'Up and running in 3 minutes')}</h2>
        <p className="sub">{t('Подробная инструкция — на странице установки.', 'Step-by-step guide on the install page.')}</p>
        <div className="grid">
          <div className="card"><div className="ico">1️⃣</div><h3>{t('Скачай и распакуй', 'Download & unzip')}</h3><p>{t('Один архив, без установщика. Обновления ставятся сами.', 'One zip, no installer. Updates install themselves.')}</p></div>
          <div className="card"><div className="ico">2️⃣</div><h3>{t('Добавь аккаунты', 'Add accounts')}</h3><p>{t('Вход прямо в программе. Укажи ссылку на сервер и выбери мейна.', 'Sign in right in the app. Set the server link and pick your main.')}</p></div>
          <div className="card"><div className="ico">3️⃣</div><h3>{t('Нажми «Запустить»', 'Press “Start”')}</h3><p>{t('Аккаунты заходят по очереди, дальше AutoRejoin следит сам.', 'Accounts join one by one, then AutoRejoin watches on its own.')}</p></div>
        </div>
      </section>

      <section className="wrap">
        <div className="card grid-2" style={{ alignItems: 'center', padding: 32 }}>
          <div>
            <h2>{t('Топ АФК месяца', 'AFK top of the month')}</h2>
            <p className="sub" style={{ marginBottom: 22 }}>{t('Войди в программе в свой аккаунт — и каждый час, пока аккаунты в игре, идёт в зачёт. Лучшие трое получают роль Top Stander.', 'Sign in to your account in the app — every hour your accounts spend in game counts. The best three get the Top Stander role.')}</p>
            <div className="btns">
              <Link to="/top" className="btn btn-primary">🏆 {t('Открыть топ', 'Open the top')}</Link>
              <a href={DISCORD} target="_blank" rel="noreferrer" className="btn btn-ghost">Discord</a>
            </div>
          </div>
          <img className="shot" src={`${B}top.png`} alt={t('Топ АФК в программе', 'AFK top in the app')} loading="lazy" />
        </div>
      </section>
    </>
  );
}
