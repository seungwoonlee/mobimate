import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// 개발 서버: API·인증은 로컬 서버(기본 17800)로 넘긴다. 서버는 Origin을 포트까지 검사하므로(SEC-02) 대상 주소로 바꿔 보낸다.
const target = process.env.MOBIMATE_SERVER ?? 'http://127.0.0.1:17800';

export default defineConfig({
  plugins: [react()],
  base: '/',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    assetsInlineLimit: 0,
    chunkSizeWarningLimit: 800,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target, changeOrigin: true, configure: p => p.on('proxyReq', r => r.setHeader('origin', target)) },
      '/auth': { target, changeOrigin: true },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    exclude: ['e2e/**', 'node_modules/**', 'dist/**'],   // e2e는 Playwright가 돌린다
  },
});
