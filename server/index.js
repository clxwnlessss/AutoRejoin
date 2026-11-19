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

initDb(pool)
  .then(() => {
    const port = process.env.PORT || 3000;
    createApp(pool).listen(port, () => console.log(`AutoRejoin Server слушает порт ${port}`));
  })
  .catch((e) => {
    console.error('База данных недоступна:', e.message);
    process.exit(1);
  });
