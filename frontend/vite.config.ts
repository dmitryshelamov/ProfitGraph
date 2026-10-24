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
