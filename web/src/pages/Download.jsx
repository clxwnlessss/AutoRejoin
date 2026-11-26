import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useLang } from '../i18n.jsx';
import { useAuth } from '../auth.jsx';
import { pcReleases, mobileReleases, REPO, DISCORD } from '../api.js';

function ruPlural(n, one, few, many) {
  const m10 = n % 10, m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return one;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
  return many;
}

const PC_FALLBACK = `https://github.com/${REPO}/releases/latest/download/AutoRejoin.zip`;
const APK_FALLBACK = `https://github.com/${REPO}/releases`;

// One download card: platform, version, date, size, downloads and the button.
function ReleaseCard({ icon, platform, note, rel, err, pattern, fallback, button, lang, t }) {
  const asset = rel && (rel.assets || []).find((a) => pattern.test(a.name));
  const url = (asset && asset.browser_download_url) || fallback;
  const date = rel && new Date(rel.published_at).toLocaleDateString(lang === 'ru' ? 'ru-RU' : 'en-US', { day: 'numeric', month: 'long', year: 'numeric' });
  const downloads = rel && (rel.assets || []).reduce((n, a) => n + (a.download_count || 0), 0);
  return (
    <div className="card release platform">
      <div className="platform-head">
        <div className="platform-ico">{icon}</div>
        <div>
          <h3>{platform}</h3>
          <div className="muted" style={{ fontSize: 13 }}>{note}</div>
        </div>
      </div>
      <div>
        <div className="muted">{t('Последняя версия', 'Latest version')}</div>
        <div className="ver">{rel ? rel.tag_name.replace(/^(mobile-)?v/, '') : err ? '—' : <span className="spinner" />}</div>
        <div className="muted" style={{ fontSize: 14 }}>
          {rel && <>{date}{asset ? ` · ${(asset.size / 1048576).toFixed(1)} MB` : ''}{downloads ? ` · ${downloads.toLocaleString()} ${t(ruPlural(downloads, 'загрузка', 'загрузки', 'загрузок'), downloads === 1 ? 'download' : 'downloads')}` : ''}</>}
        </div>
      </div>
      <a className="btn btn-primary" href={url}>⬇ {button}</a>
    </div>
  );
}

export default function Download() {
  const { t, lang } = useLang();
  const { openAuth } = useAuth();
  const [pc, setPc] = useState(null);
  const [mobile, setMobile] = useState(null);
  const [err, setErr] = useState(false);
  const [tab, setTab] = useState(() => (/android/i.test(navigator.userAgent) ? 'android' : 'pc'));
  useEffect(() => {
    pcReleases().then((r) => setPc(r[0] || null)).catch(() => setErr(true));
    mobileReleases().then((r) => setMobile(r[0] || null)).catch(() => setErr(true));
  }, []);

  return (
    <div className="wrap page">
      <h1>{t('Скачать и установить', 'Download & install')}</h1>
      <p className="lead">{t('Бесплатно и без рекламы. Для ПК на Windows и для Android — телефоны, UGPhone и другие облачные телефоны, эмуляторы.', 'Free, no ads. For Windows PCs and for Android — phones, UGPhone and other cloud phones, emulators.')}</p>

      <div className="dl-grid">
        <ReleaseCard icon="💻" platform="Windows" note={t('Windows 10 / 11 · без установщика', 'Windows 10 / 11 · no installer')}
          rel={pc} err={err} pattern={/\.zip$/i} fallback={PC_FALLBACK} button={t('Скачать AutoRejoin.zip', 'Download AutoRejoin.zip')} lang={lang} t={t} />
        <ReleaseCard icon="📱" platform="Android" note={t('Телефон, UGPhone, эмулятор · root по желанию', 'Phone, UGPhone, emulator · root optional')}
          rel={mobile} err={err} pattern={/\.apk$/i} fallback={APK_FALLBACK} button={t('Скачать APK', 'Download APK')} lang={lang} t={t} />
      </div>

      <section style={{ paddingBottom: 30 }}>
        <h2>{t('Установка', 'Installation')}</h2>
        <div className="tabs" style={{ maxWidth: 420, marginTop: 14 }}>
          <a href="#pc" className={tab === 'pc' ? 'on' : ''} onClick={(e) => { e.preventDefault(); setTab('pc'); }}>💻 Windows</a>
          <a href="#android" className={tab === 'android' ? 'on' : ''} onClick={(e) => { e.preventDefault(); setTab('android'); }}>📱 Android</a>
        </div>

        {tab === 'pc' ? (
          <>
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
                <p>{t('Тот же ник и пароль, что на этом сайте. ', 'Same nickname and password as on this site. ')}<a href="/register" onClick={(e) => { e.preventDefault(); openAuth('register'); }}>{t('Создать аккаунт', 'Create an account')}</a>{t(' — тогда часы АФК идут в зачёт, а аккаунты видно в панели на сайте.', ' — then your AFK hours count and your accounts show up in the site panel.')}</p>
              </div>
              <div className="card step">
                <h3>{t('Добавь аккаунты Roblox', 'Add your Roblox accounts')}</h3>
                <p>{t('«Добавить» → войди в аккаунт во встроенном окне (или вставь куку). Выбери игру и при желании VIP-сервер, отметь мейна ⭐ — он заходит первым.', '“Add” → sign in in the built-in window (or paste a cookie). Pick the game and, if you like, a VIP server, and mark your main ⭐ — it joins first.')}</p>
              </div>
              <div className="card step">
                <h3>{t('Нажми «Запустить»', 'Press “Start”')}</h3>
                <p>{t('Аккаунты заходят по очереди. Дальше AutoRejoin сам перезаходит при вылетах. В настройках можно включить вебхук Discord и привязать Discord-бота.', 'Accounts join one by one, then AutoRejoin rejoins on its own after drops. In Settings you can turn on the Discord webhook and link the Discord bot.')}</p>
              </div>
            </div>
          </>
        ) : (
          <>
            <p className="sub">{t('Аккаунты уже вошли в приложениях Роблокса — куки не нужны.', 'Accounts are already logged in in the Roblox apps — no cookies needed.')}</p>
            <div className="steps">
              <div className="card step">
                <h3>{t('Скачай APK на телефон', 'Download the APK on the phone')}</h3>
                <p>{t('Открой эту страницу прямо на телефоне или в облачном телефоне (UGPhone и др.), нажми «Скачать APK» и открой файл. Android спросит про установку из неизвестного источника — разреши.', 'Open this page right on the phone or in the cloud phone (UGPhone etc.), tap “Download APK” and open the file. Android asks about installing from an unknown source — allow it.')}</p>
              </div>
              <div className="card step">
                <h3>{t('Выбери игру', 'Pick the game')}</h3>
                <p>{t('В разделе GAME — ID плейса, ссылка на игру или VIP-ссылка (Bee Swarm — ', 'In GAME — a place ID, game link or VIP link (Bee Swarm — ')}<code>1537690962</code>{t('). У каждого аккаунта можно задать свою игру или VIP.', '). Each account can have its own game or VIP.')}</p>
              </div>
              <div className="card step">
                <h3>{t('Включи приложения Роблокса', 'Turn on your Roblox apps')}</h3>
                <p>{t('AutoRejoin сам находит Роблокс и его клоны — каждый клон это отдельный аккаунт. Без root впиши у каждого ник в Роблоксе: по нему видно, в игре ли аккаунт.', 'AutoRejoin finds Roblox and its clones — every clone is a separate account. Without root, enter each account’s Roblox username: that’s how it sees whether the account is in game.')}</p>
              </div>
              <div className="card step">
                <h3>{t('Исправь жёлтые проверки', 'Fix the yellow checks')}</h3>
                <p>{t('Нажми на жёлтые Overlay, Battery и Updates — откроются нужные настройки. Root по желанию, но лучше включить (в облачных телефонах — в настройках устройства): тогда вылет виден за 3–5 сек, без root — примерно за 30 сек.', 'Tap the yellow Overlay, Battery and Updates — the right settings open. Root is optional but better on (in cloud phones — in the device settings): with it a drop is seen in 3–5 s, without it in about 30 s.')}</p>
              </div>
              <div className="card step">
                <h3>{t('Нажми Start', 'Tap Start')}</h3>
                <p>{t('AutoRejoin держит аккаунты в игре и перезаходит при вылетах, киках и ошибке 256. Обновления ставятся сами, после обновления и перезагрузки ферма продолжает работать.', 'AutoRejoin keeps the accounts in game and rejoins after drops, kicks and error 256. Updates install themselves; after an update or a reboot the farm keeps going.')}</p>
              </div>
            </div>
          </>
        )}
      </section>

      <section style={{ paddingTop: 30 }}>
        <div className="grid">
          <div className="card"><div className="ico">💻</div><h3>{t('Требования', 'Requirements')}</h3><p>{t('ПК: Windows 10/11 x64, .NET Framework 4.8 (уже есть в Windows), Роблокс, ~1,5–2,5 ГБ ОЗУ на окно. Android: 7.0+, приложение Роблокса (и клоны для нескольких аккаунтов).', 'PC: Windows 10/11 x64, .NET Framework 4.8 (built into Windows), Roblox, ~1.5–2.5 GB RAM per window. Android: 7.0+, the Roblox app (and clones for several accounts).')}</p></div>
          <div className="card"><div className="ico">🔄</div><h3>{t('Обновления', 'Updates')}</h3><p>{t('И программа, и приложение сами проверяют GitHub и ставят новую версию. Настройки и аккаунты сохраняются.', 'Both the PC app and the Android app check GitHub and install new versions themselves. Settings and accounts are kept.')}</p></div>
          <div className="card"><div className="ico">🛟</div><h3>{t('Нужна помощь?', 'Need help?')}</h3><p>{t('Загляни в ', 'Check the ')}<Link to="/faq">FAQ</Link>{t(' или спроси в ', ' or ask in our ')}<a href={DISCORD} target="_blank" rel="noreferrer">Discord</a>.</p></div>
        </div>
      </section>
    </div>
  );
}
