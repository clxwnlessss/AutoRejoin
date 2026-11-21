import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useLang } from '../i18n.jsx';
import { releases, REPO, DISCORD } from '../api.js';

function ruPlural(n, one, few, many) {
  const m10 = n % 10, m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return one;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
  return many;
}

const FALLBACK =`https://github.com/${REPO}/releases/latest/download/AutoRejoin.zip`;

export default function Download() {
  const { t, lang } = useLang();
  const [rel, setRel] = useState(null);
  const [err, setErr] = useState(false);
  useEffect(() => { releases().then((r) => setRel(r[0] || null)).catch(() => setErr(true)); }, []);
  const asset = rel && (rel.assets || []).find((a) => /\.zip$/i.test(a.name));
  const url = (asset && asset.browser_download_url) || FALLBACK;
  const date = rel && new Date(rel.published_at).toLocaleDateString(lang === 'ru' ? 'ru-RU' : 'en-US', { day: 'numeric', month: 'long', year: 'numeric' });
  const downloads = rel && (rel.assets || []).reduce((n, a) => n + (a.download_count || 0), 0);

  return (
    <div className="wrap page">
      <h1>{t('Скачать и установить', 'Download & install')}</h1>
      <p className="lead">{t('Бесплатно, без рекламы и установщика. Windows 10 и 11.', 'Free, no ads, no installer. Windows 10 and 11.')}</p>

      <div className="card release">
        <div>
          <div className="muted">{t('Последняя версия', 'Latest version')}</div>
          <div className="ver">{rel ? rel.tag_name.replace(/^v/, '') : err ? '—' : <span className="spinner" />}</div>
          <div className="muted" style={{ fontSize: 14 }}>
            {rel && <>{date}{asset ? ` · ${(asset.size / 1048576).toFixed(1)} MB` : ''}{downloads ? ` · ${downloads.toLocaleString()} ${t(ruPlural(downloads, 'загрузка', 'загрузки', 'загрузок'), downloads === 1 ? 'download' : 'downloads')}` : ''}</>}
          </div>
        </div>
        <div className="btns">
          <a className="btn btn-primary" href={url}>⬇ {t('Скачать AutoRejoin.zip', 'Download AutoRejoin.zip')}</a>
          <a className="btn btn-ghost" href={`https://github.com/${REPO}`} target="_blank" rel="noreferrer">{t('Исходный код', 'Source code')}</a>
        </div>
      </div>

      <section style={{ paddingBottom: 30 }}>
        <h2>{t('Установка', 'Installation')}</h2>
        <p className="sub">{t('5 шагов — и аккаунты в игре.', '5 steps and your accounts are in game.')}</p>
        <div className="steps">
          <div className="card step">
            <h3>{t('Распакуй архив', 'Unzip the archive')}</h3>
            <p>{t('Создай папку, например ', 'Create a folder, e.g. ')}<code>C:\AutoRejoin</code>{t(', и распакуй туда AutoRejoin.zip целиком (правый клик → «Извлечь всё»). Не запускай из архива — программе нужны файлы рядом с ней.', ', and extract the whole AutoRejoin.zip there (right click → “Extract All”). Don’t run it from inside the zip — the app needs its files next to it.')}</p>
          </div>
          <div className="card step">
            <h3>{t('Запусти AutoRejoin.exe', 'Run AutoRejoin.exe')}</h3>
            <p>{t('Если Windows покажет «Windows защитила ваш компьютер» — нажми «Подробнее» → «Выполнить в любом случае». Так бывает с любой новой программой без платной подписи; код открыт на GitHub.', 'If Windows shows “Windows protected your PC”, click “More info” → “Run anyway”. This happens to any new app without a paid signature; the code is open on GitHub.')}</p>
          </div>
          <div className="card step">
            <h3>{t('Войди в Топ АФК (по желанию)', 'Sign in to the AFK top (optional)')}</h3>
            <p>{t('Тот же ник и пароль, что на этом сайте. ', 'Same nickname and password as on this site. ')}<Link to="/register">{t('Создать аккаунт', 'Create an account')}</Link>{t(' — тогда часы АФК идут в зачёт, а аккаунты видно в панели на сайте.', ' — then your AFK hours count and your accounts show up in the site panel.')}</p>
          </div>
          <div className="card step">
            <h3>{t('Добавь аккаунты Roblox', 'Add your Roblox accounts')}</h3>
            <p>{t('«Добавить» → войди в аккаунт во встроенном окне (или вставь куку). Укажи ссылку на свой приватный сервер Bee Swarm и отметь мейна ⭐ — он заходит первым.', '“Add” → sign in in the built-in window (or paste a cookie). Set your Bee Swarm private server link and mark your main ⭐ — it joins first.')}</p>
          </div>
          <div className="card step">
            <h3>{t('Нажми «Запустить»', 'Press “Start”')}</h3>
            <p>{t('Аккаунты заходят по очереди. Дальше AutoRejoin сам перезаходит при вылетах. В настройках можно включить вебхук Discord и привязать Discord-бота.', 'Accounts join one by one, then AutoRejoin rejoins on its own after drops. In Settings you can turn on the Discord webhook and link the Discord bot.')}</p>
          </div>
        </div>
      </section>

      <section style={{ paddingTop: 30 }}>
        <div className="grid">
          <div className="card"><div className="ico">💻</div><h3>{t('Требования', 'Requirements')}</h3><p>{t('Windows 10/11 x64, .NET Framework 4.8 (уже есть в Windows), Roblox. Примерно 1,5–2,5 ГБ ОЗУ на одно окно.', 'Windows 10/11 x64, .NET Framework 4.8 (built into Windows), Roblox. About 1.5–2.5 GB RAM per window.')}</p></div>
          <div className="card"><div className="ico">🔄</div><h3>{t('Обновления', 'Updates')}</h3><p>{t('Программа сама проверяет GitHub и предлагает обновиться в один клик. Настройки и аккаунты сохраняются.', 'The app checks GitHub and offers a one-click update. Settings and accounts are kept.')}</p></div>
          <div className="card"><div className="ico">🛟</div><h3>{t('Нужна помощь?', 'Need help?')}</h3><p>{t('Загляни в ', 'Check the ')}<Link to="/faq">FAQ</Link>{t(' или спроси в ', ' or ask in our ')}<a href={DISCORD} target="_blank" rel="noreferrer">Discord</a>.</p></div>
        </div>
      </section>
    </div>
  );
}
