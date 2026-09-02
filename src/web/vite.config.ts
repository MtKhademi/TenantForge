import { execFileSync } from 'node:child_process'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// The development .NET API (src/api/TenantForge.Api) binds port 5000.
// Proxying /api keeps the web app same-origin, so no CORS is needed in dev
// or in `vite preview` (used by the Playwright e2e suite).
//
// Target resolution, in order:
// 1. VITE_API_PROXY_TARGET — explicit override, always wins (e.g. the API
//    running inside the same machine as Vite).
// 2. Inside WSL (WSL_DISTRO_NAME is set by WSL itself): the API normally
//    runs on the Windows host via the .NET SDK, and WSL2's loopback does
//    not reach Windows ports, so use the default-route gateway IP.
// 3. Otherwise assume the API runs on the same host as Vite.
function resolveApiProxyTarget(): string {
  const override = process.env.VITE_API_PROXY_TARGET
  if (override) return override
  if (process.env.WSL_DISTRO_NAME) {
    try {
      const defaultRoute = execFileSync('ip', ['route'], { encoding: 'utf8' })
        .split('\n')
        .find((line) => line.startsWith('default via '))
      const gateway = defaultRoute?.trim().split(/\s+/)[2]
      if (gateway && /^\d{1,3}(\.\d{1,3}){3}$/.test(gateway)) {
        return `http://${gateway}:5000`
      }
    } catch {
      // `ip` unavailable — fall through to the same-host default.
    }
  }
  return 'http://localhost:5000'
}

const apiProxy = {
  target: resolveApiProxyTarget(),
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
