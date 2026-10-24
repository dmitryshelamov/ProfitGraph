/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The backend does not configure CORS: in development, HTTP and realtime traffic go through the Vite proxy.
const backend = process.env.PROFITGRAPH_API ?? 'http://localhost:5291';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': new URL('./src', import.meta.url).pathname },
  },
  // Routes load these libraries lazily; pre-bundling them up front keeps the dev server from re-optimizing and
  // reloading the page in the middle of a session, which would also break end-to-end tests on a cold start.
  optimizeDeps: {
    include: ['@microsoft/signalr', 'lightweight-charts'],
  },
  server: {
    port: 5273,
    strictPort: true,
    proxy: {
      '/api': backend,
      '/ws': { target: backend, ws: true },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
