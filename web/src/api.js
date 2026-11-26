// AutoRejoin server on Render (same accounts as the app). Free Render sleeps when idle, so the first request
// after a pause can take ~30–50 s — requests wait up to 60 s and the UI says the server is waking up.
export const API = import.meta.env.VITE_API || 'https://autorejoin-server.onrender.com';
export const REPO = 'clxwnlessss/AutoRejoin';
export const DISCORD = 'https://discord.gg/bZFB8dwBdw';

export class ApiError extends Error {
  constructor(message, status) { super(message); this.status = status; }
}

export async function api(path, { method = 'GET', body, token, timeout = 60000 } = {}) {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), timeout);
  try {
    const res = await fetch(API + path, {
      method,
      signal: ctrl.signal,
      headers: { ...(body ? { 'content-type': 'application/json' } : {}), ...(token ? { authorization: `Bearer ${token}` } : {}) },
      body: body ? JSON.stringify(body) : undefined
    });
    let data = null;
    try { data = await res.json(); } catch { data = null; }
    if (!res.ok) throw new ApiError((data && data.error) || `HTTP ${res.status}`, res.status);
    return data;
  } catch (e) {
    if (e instanceof ApiError) throw e;
    throw new ApiError(e.name === 'AbortError' ? 'timeout' : 'network', 0);
  } finally {
    clearTimeout(timer);
  }
}

// Latest releases straight from GitHub (public API, no key; cached for 10 min in this tab).
let releasesCache = null;
export async function releases() {
  if (releasesCache && releasesCache.exp > Date.now()) return releasesCache.data;
  const res = await fetch(`https://api.github.com/repos/${REPO}/releases?per_page=20`, { headers: { accept: 'application/vnd.github+json' } });
  if (!res.ok) throw new Error(`GitHub ${res.status}`);
  const data = (await res.json()).filter((r) => !r.draft && !r.prerelease);
  releasesCache = { data, exp: Date.now() + 10 * 60 * 1000 };
  return data;
}

// PC releases are tagged vX.Y.Z (AutoRejoin.zip), Android ones mobile-vX.Y (AutoRejoin-Mobile.apk).
export const isMobileRelease = (r) => /^mobile-v/i.test(r.tag_name || '');
export async function pcReleases() { return (await releases()).filter((r) => !isMobileRelease(r)); }
export async function mobileReleases() { return (await releases()).filter(isMobileRelease); }

export function formatHours(seconds) {
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  return h > 0 ? `${h}h ${String(m).padStart(2, '0')}m` : `${m}m`;
}
