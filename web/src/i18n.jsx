import { createContext, useContext, useEffect, useState } from 'react';

// Two languages, like the app. t('Русский', 'English') picks by the current language.
const LangContext = createContext({ lang: 'en', setLang: () => {}, t: (ru, en) => en });

function initialLang() {
  try {
    const saved = localStorage.getItem('ar-lang');
    if (saved === 'ru' || saved === 'en') return saved;
  } catch { /* storage blocked */ }
  const nav = (navigator.language || 'en').toLowerCase();
  return /^(ru|uk|be|kk)/.test(nav) ? 'ru' : 'en';
}

export function LangProvider({ children }) {
  const [lang, setLangState] = useState(initialLang);
  const setLang = (l) => {
    setLangState(l);
    try { localStorage.setItem('ar-lang', l); } catch { /* storage blocked */ }
  };
  useEffect(() => { document.documentElement.lang = lang; }, [lang]);
  const t = (ru, en) => (lang === 'ru' ? ru : en);
  return <LangContext.Provider value={{ lang, setLang, t }}>{children}</LangContext.Provider>;
}

export const useLang = () => useContext(LangContext);

// Error text from the API (server answers in Russian) or our own network errors, in the current language.
export function errorText(e, t) {
  if (!e) return '';
  if (e.message === 'timeout') return t('Сервер не ответил за минуту — попробуй ещё раз.', 'The server did not answer within a minute — try again.');
  if (e.message === 'network') return t('Нет связи с сервером. Проверь интернет.', 'Can’t reach the server. Check your connection.');
  if (t('ru', 'en') === 'ru') return e.message;
  const map = {
    'Ник: 3–20 символов, латиница, цифры и _.': 'Nickname: 3–20 characters, latin letters, digits and _.',
    'Пароль: от 6 до 100 символов.': 'Password: 6 to 100 characters.',
    'Такой ник уже занят.': 'This nickname is taken.',
    'Неверный ник или пароль.': 'Wrong nickname or password.',
    'Слишком много попыток. Подожди 10 минут.': 'Too many attempts. Wait 10 minutes.',
    'Сессия устарела — войди заново.': 'Session expired — sign in again.',
    'Нужно войти в аккаунт.': 'Please sign in.',
    'Только для администратора.': 'Admins only.',
    'Пользователь не найден.': 'User not found.',
    'Сначала привяжи Discord-бота в программе: Настройки → Привязать бота.': 'Link the Discord bot in the app first: Settings → Link bot.',
    'Похоже, это не почта.': 'That doesn’t look like an email.',
    'Эта почта уже привязана к другому аккаунту.': 'This email is already linked to another account.',
    'Код уже отправлен. Новый можно запросить через минуту.': 'A code was just sent. You can ask for a new one in a minute.',
    'Неверный или просроченный код.': 'Wrong or expired code.',
    'Отправка писем на сервере ещё не настроена.': 'Sending emails isn’t set up on the server yet.',
    'Не удалось отправить письмо. Попробуй позже.': 'Couldn’t send the email. Try again later.'
  };
  return map[e.message] || e.message;
}
