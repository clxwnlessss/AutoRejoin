import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { SITE_URL } from './site.config.js';

// The site lives at SITE_URL (site.config.js): GitHub Pages project path (/AutoRejoin/) or its own domain (/).
// Build output goes to ../docs (Pages source: main /docs).
const base = new URL(SITE_URL).pathname;

export default defineConfig({
  plugins: [
    react(),
    { name: 'site-url', transformIndexHtml: { order: 'pre', handler: (html) => html.replaceAll('%SITE_URL%', SITE_URL) } }
  ],
  base,
  build: { outDir: '../docs', emptyOutDir: true, assetsDir: 'assets' }
});
