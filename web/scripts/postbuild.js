// GitHub Pages has no SPA fallback: a copy of index.html as 404.html makes /AutoRejoin/top etc. open the app.
// Also writes sitemap.xml with every page.
import { copyFileSync, mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const out = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'docs');
copyFileSync(join(out, 'index.html'), join(out, '404.html'));
const base = 'https://clxwnlessss.github.io/AutoRejoin/';
const pages = ['', 'download', 'top', 'faq', 'changelog'];
// Real files for every route, so they answer 200 (not the 404 fallback) — better for search engines.
for (const p of [...pages.filter(Boolean), 'login', 'register', 'dashboard', 'admin']) {
  mkdirSync(join(out, p), { recursive: true });
  copyFileSync(join(out, 'index.html'), join(out, p, 'index.html'));
}
writeFileSync(join(out, 'sitemap.xml'),
  '<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">' +
  pages.map((p) => `<url><loc>${base}${p}</loc><changefreq>weekly</changefreq></url>`).join('') + '</urlset>\n');
console.log('postbuild: 404.html + sitemap.xml');
