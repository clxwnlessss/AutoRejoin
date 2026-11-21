import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// GitHub Pages serves the repo at /AutoRejoin/, the build goes to ../docs (Pages source: main /docs).
export default defineConfig({
  plugins: [react()],
  base: '/AutoRejoin/',
  build: { outDir: '../docs', emptyOutDir: true, assetsDir: 'assets' }
});
