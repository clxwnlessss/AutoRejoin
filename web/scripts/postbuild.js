// GitHub Pages has no SPA fallback: a copy of index.html as 404.html makes unknown paths open the app, and a real
// index.html per route makes known pages answer 200. Also writes sitemap.xml, robots.txt and — on a custom domain — CNAME.
import { copyFileSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { SITE_URL } from '../site.config.js';

const out = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'docs');
copyFileSync(join(out, 'index.html'), join(out, '404.html'));
const pages = ['', 'download', 'top', 'faq', 'changelog'];
for (const p of [...pages.filter(Boolean), 'login', 'register', 'forgot', 'dashboard', 'admin']) {
  mkdirSync(join(out, p), { recursive: true });
  copyFileSync(join(out, 'index.html'), join(out, p, 'index.html'));
}
writeFileSync(join(out, 'sitemap.xml'),
  '<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">' +
  pages.map((p) => `<url><loc>${SITE_URL}${p}</loc><changefreq>weekly</changefreq></url>`).join('') + '</urlset>\n');
writeFileSync(join(out, 'robots.txt'), `User-agent: *\nAllow: /\nSitemap: ${SITE_URL}sitemap.xml\n`);
const host = new URL(SITE_URL).hostname;
if (host.endsWith('github.io')) rmSync(join(out, 'CNAME'), { force: true });
else writeFileSync(join(out, 'CNAME'), host + '\n');
console.log(`postbuild for ${SITE_URL}: 404.html, route pages, sitemap.xml, robots.txt${host.endsWith('github.io') ? '' : ', CNAME'}`);
