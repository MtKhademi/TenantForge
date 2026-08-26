import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// The development .NET API (src/api/TenantForge.Api) binds port 5000.
// Proxying /api keeps the web app same-origin, so no CORS is needed in dev
// or in `vite preview` (used by the Playwright e2e suite).
//
// The default assumes the API runs on the same host as Vite. On this
// development machine the API runs through the Windows .NET SDK via WSL
// interop, whose sockets are only reachable through the WSL gateway IP —
// override with VITE_API_PROXY_TARGET=http://<gateway>:5000 in that case.
const apiProxy = {
  target: process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5000',
  changeOrigin: false,
}

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': new URL('./src', import.meta.url).pathname,
    },
  },
  server: {
    proxy: {
      '/api': apiProxy,
    },
  },
  preview: {
    proxy: {
      '/api': apiProxy,
    },
  },
  test: {
    environment: 'jsdom',
    exclude: ['e2e/**', 'node_modules/**', 'dist/**'],
    globals: true,
    setupFiles: './src/test/setup.ts',
  },
})
