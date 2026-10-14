import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';

export default defineConfig({
  // the AirWorks icons and wallpapers, served under /icons/
  publicDir: fileURLToPath(new URL('node_modules/@fgilde/airworks/package', import.meta.url)),
  build: { outDir: fileURLToPath(new URL('../wwwroot', import.meta.url)), emptyOutDir: true, target: 'es2022' },
});
