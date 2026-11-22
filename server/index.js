'use strict';
// Запуск сервера AutoRejoin (Render задаёт PORT и DATABASE_URL).
const { Pool } = require('pg');
const { createApp, initDb } = require('./app');

const url = process.env.DATABASE_URL;
if (!url) {
  console.error('Не задан DATABASE_URL — укажи строку подключения к PostgreSQL.');
  process.exit(1);
}
const local = /@(localhost|127\.0\.0\.1)[:/]/.test(url);
const pool = new Pool({ connectionString: url, ssl: local ? false : { rejectUnauthorized: false }, max: 5 });

// Discord bot (optional): DISCORD_APP_ID, DISCORD_PUBLIC_KEY, DISCORD_TOKEN from the Developer Portal.
const discord = {
  appId: process.env.DISCORD_APP_ID || '',
  publicKey: process.env.DISCORD_PUBLIC_KEY || '',
  token: process.env.DISCORD_TOKEN || ''
};

initDb(pool)
  .then(() => {
    const port = process.env.PORT || 3000;
    // Telegram bot (optional): TELEGRAM_BOT_TOKEN from @BotFather; the webhook goes to this server's public URL.
    const telegram = { token: process.env.TELEGRAM_BOT_TOKEN || '' };
    // Email codes (optional): BREVO_API_KEY + MAIL_FROM (sender verified in Brevo). Without them email features answer 503.
    const mail = { brevoKey: process.env.BREVO_API_KEY || '', from: process.env.MAIL_FROM || '' };
    const app = createApp(pool, { discord, telegram, mail });
    console.log(mail.brevoKey && mail.from ? `Почта: письма уходят от ${mail.from}` : 'Почта: не настроена (нет BREVO_API_KEY/MAIL_FROM)');
    app.listen(port, () => console.log(`AutoRejoin Server слушает порт ${port}`));
    const publicUrl = process.env.PUBLIC_URL || process.env.RENDER_EXTERNAL_URL || '';
    if (telegram.token && publicUrl) {
      app.locals.telegram.setup(publicUrl)
        .then((name) => console.log(`Telegram: бот @${name} подключён, вебхук настроен`))
        .catch((e) => console.error('Telegram: не удалось настроить бота:', e.message));
    } else console.log('Telegram: бот не настроен (нет TELEGRAM_BOT_TOKEN)');
    app.locals.discord.warm().catch((e) => console.error('Discord: не удалось загрузить привязки:', e.message));
    // Gateway connection for member joins, automod, logs and reaction roles.
    const connect = () => app.locals.discord.guild.startGateway()
      .then((on) => { if (!on) console.log('Discord gateway: нет DISCORD_TOKEN — автороли и автомод выключены'); })
      .catch((e) => {
        console.error('Discord gateway не подключился (включи Server Members и Message Content Intent в Developer Portal → Bot):', e.message, '— повтор через 2 мин');
        setTimeout(connect, 2 * 60 * 1000);
      });
    connect();
    app.locals.discord.registerCommands()
      .then((done) => console.log(done ? 'Discord: слэш-команды зарегистрированы' : 'Discord: бот не настроен (нет DISCORD_APP_ID/DISCORD_TOKEN)'))
      .catch((e) => console.error('Discord: не удалось зарегистрировать команды:', e.message));
  })
  .catch((e) => {
    console.error('База данных недоступна:', e.message);
    process.exit(1);
  });
