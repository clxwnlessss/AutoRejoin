import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { api } from './api.js';

// Session = the same Top AFK account as in the app. The token stays in this browser only (localStorage).
const AuthContext = createContext(null);

function load() {
  try { return JSON.parse(localStorage.getItem('ar-session') || 'null'); } catch { return null; }
}
function save(session) {
  try {
    if (session) localStorage.setItem('ar-session', JSON.stringify(session));
    else localStorage.removeItem('ar-session');
  } catch { /* storage blocked: session lives until the tab closes */ }
}

export function AuthProvider({ children }) {
  const [session, setSession] = useState(load);

  const signIn = useCallback(async (mode, nickname, password) => {
    const data = await api(mode === 'register' ? '/api/register' : '/api/login', { method: 'POST', body: { nickname, password } });
    const s = { token: data.token, nickname: data.nickname };
    save(s);
    setSession(s);
    return s;
  }, []);

  // Password reset by email signs the browser in with the token the server returns.
  const adopt = useCallback((token, nickname) => {
    const s = { token, nickname };
    save(s);
    setSession(s);
  }, []);

  // Sign-in modal: openAuth('login' | 'register' | 'forgot'), closeAuth().
  const [authModal, setAuthModal] = useState(null);
  const openAuth = useCallback((mode = 'login') => setAuthModal(mode), []);
  const closeAuth = useCallback(() => setAuthModal(null), []);

  const signOut = useCallback(async () => {
    const token = session && session.token;
    save(null);
    setSession(null);
    if (token) api('/api/logout', { method: 'POST', token }).catch(() => {});
  }, [session]);

  // Token rejected by the server (logged out elsewhere) → forget it.
  const expired = useCallback(() => { save(null); setSession(null); }, []);

  useEffect(() => {
    const onStorage = (e) => { if (e.key === 'ar-session') setSession(load()); };
    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, []);

  return <AuthContext.Provider value={{ session, signIn, signOut, expired, adopt, authModal, openAuth, closeAuth }}>{children}</AuthContext.Provider>;
}

export const useAuth = () => useContext(AuthContext);
