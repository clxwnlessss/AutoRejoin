import { Link } from 'react-router-dom';
import { useLang } from '../i18n.jsx';
import { DISCORD } from '../api.js';

export default function Faq() {
  const { t } = useLang();
  const items = [
    [t('Это бесплатно?', 'Is it free?'), t('Да. AutoRejoin бесплатный и с открытым кодом, без рекламы и подписок. Весь код на GitHub.', 'Yes. AutoRejoin is free and open source, with no ads or subscriptions. All the code is on GitHub.')],
    [t('Это макрос? Меня забанят?', 'Is it a macro? Will I get banned?'), t('Нет, это не макрос: AutoRejoin ничего не нажимает в игре. Он запускает окна Roblox, следит за ними снаружи и перезапускает, если аккаунт вылетел. Фармит твой скрипт — AutoRejoin только держит аккаунты в игре.', 'No, it’s not a macro: AutoRejoin doesn’t press anything in the game. It launches Roblox windows, watches them from the outside and relaunches them when an account drops. Your script farms — AutoRejoin just keeps accounts in game.')],
    [t('Сколько аккаунтов можно держать?', 'How many accounts can it keep?'), t('Сколько потянет ПК. Окна запускаются по очереди, мейн первым. Одно окно занимает примерно 1,5–2,5 ГБ ОЗУ — есть лимит памяти на окно и перезапуск раздутых окон.', 'As many as your PC can run. Windows launch one by one, main first. One window takes about 1.5–2.5 GB RAM — there’s a per-window memory cap and restarts of bloated windows.')],
    [t('Что он делает при дисконнекте или зависании?', 'What does it do on a disconnect or freeze?'), t('Видит ошибку Roblox, кик, зависшее окно, выход в меню или нехватку памяти, закрывает окно и заходит заново на твой сервер. Если не получается — пишет в Discord, на чём именно застрял и что сделать.', 'It detects the Roblox error, kick, frozen window, exit to menu or low memory, closes the window and joins your server again. If it can’t, it tells you in Discord exactly where it got stuck and what to do.')],
    [t('Аккаунт на сайте и в программе — один и тот же?', 'Is the site account the same as in the app?'), t('Да. Регистрация здесь = аккаунт Топа АФК в программе, и наоборот. На сайте видно твои часы, место в топе и (после привязки Discord-бота) аккаунты в игре с кнопками управления.', 'Yes. Signing up here = an AFK top account in the app, and vice versa. The site shows your hours, rank and (after linking the Discord bot) your accounts in game with control buttons.')],
    [t('Сайт видит мои куки или пароли Roblox?', 'Does the site see my Roblox cookies or passwords?'), t('Нет. Куки и пароли Roblox хранятся только у тебя на ПК в зашифрованном виде. На сервер уходят только ник Топа АФК, часы и — если привязал бота — названия аккаунтов и их состояние.', 'No. Roblox cookies and passwords stay on your PC, encrypted. The server only gets your AFK-top nickname, hours and — if you linked the bot — account names and their state.')],
    [t('Windows пишет «защитила ваш компьютер»', 'Windows says “protected your PC”'), t('Нажми «Подробнее» → «Выполнить в любом случае». У программы нет платной подписи кода, поэтому SmartScreen её не знает. Код открыт — можешь проверить сам.', 'Click “More info” → “Run anyway”. The app has no paid code signature, so SmartScreen doesn’t know it. The code is open — check it yourself.')],
    [t('Пишет «Ждёт скрипт», хотя скрипт работает', 'It says “Waiting for script” but the script runs'), t('Обнови программу до 2.5.2 или новее: теперь AutoRejoin видит и Atlas, запущенный вручную, по его файлам в папке Потассиума.', 'Update to 2.5.2 or newer: AutoRejoin now also detects Atlas injected by hand, by its files in the Potassium folder.')],
    [t('Как попасть в Топ АФК?', 'How do I get into the AFK top?'), t('Зарегистрируйся, войди в аккаунт в программе (вкладка «Топ АФК») и держи аккаунты в игре. Сервер сам считает время по пульсам раз в минуту.', 'Sign up, sign in to the account in the app (“AFK top” tab) and keep accounts in game. The server counts time by heartbeats once a minute.')]
  ];
  return (
    <div className="wrap page" style={{ maxWidth: 860 }}>
      <h1>FAQ</h1>
      <p className="lead">{t('Частые вопросы. Не нашёл ответ — спроси в ', 'Common questions. Didn’t find yours — ask in our ')}<a href={DISCORD} target="_blank" rel="noreferrer">Discord</a>.</p>
      {items.map(([q, a], i) => (
        <details key={i} open={i === 0}><summary>{q}</summary><p>{a}</p></details>
      ))}
      <div className="center" style={{ marginTop: 30 }}><Link to="/download" className="btn btn-primary">⬇ {t('Скачать AutoRejoin', 'Download AutoRejoin')}</Link></div>
    </div>
  );
}
