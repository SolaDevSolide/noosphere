import { defineConfig } from 'vitest/config';
import { loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig(({ command, mode }) => {
  // The mock API is a development aid only: a production build refuses to start with it.
  if (command === 'build' && loadEnv(mode, '.', 'VITE_').VITE_MOCK) {
    throw new Error('VITE_MOCK must not be set for a production build.');
  }
  // NOOSPHERE_API_URL points the development proxy at another API (default http://localhost:5080).
  const api = loadEnv(mode, '.', 'NOOSPHERE_').NOOSPHERE_API_URL || 'http://localhost:5080';
  return {
    plugins: [react(), tailwindcss()],
    server: {
      port: 5173,
      proxy: {
        '/api': { target: api, changeOrigin: false, secure: false },
      },
    },
    build: { outDir: 'dist', emptyOutDir: true },
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: ['./src/test/setup.ts'],
      css: false,
    },
  };
});
