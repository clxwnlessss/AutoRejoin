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
    const app = createApp(pool, { discord });
    app.listen(port, () => console.log(`AutoRejoin Server слушает порт ${port}`));
    app.locals.discord.registerCommands()
      .then((done) => console.log(done ? 'Discord: слэш-команды зарегистрированы' : 'Discord: бот не настроен (нет DISCORD_APP_ID/DISCORD_TOKEN)'))
      .catch((e) => console.error('Discord: не удалось зарегистрировать команды:', e.message));
  })
  .catch((e) => {
    console.error('База данных недоступна:', e.message);
    process.exit(1);
  });
