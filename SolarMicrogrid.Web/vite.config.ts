import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig(({ command, mode }) => {
  const environment = loadEnv(mode, process.cwd(), '')
  const apiProxyTarget = environment.VITE_API_PROXY_TARGET || 'https://localhost:7168'
  const apiBaseUrl = environment.VITE_API_BASE_URL?.trim() || '/api'

  if (command === 'build' && /^https?:\/\/(localhost|127\.0\.0\.1|\[::1\])(?=[:/]|$)/i.test(apiBaseUrl)) {
    throw new Error('VITE_API_BASE_URL must not target localhost in a production build')
  }

  if (command === 'build' && /^http:\/\//i.test(apiBaseUrl)) {
    throw new Error('VITE_API_BASE_URL must use HTTPS or a same-origin relative path in production')
  }

  return {
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        '/api': {
          target: apiProxyTarget,
          changeOrigin: true,
          secure: false,
        },
      },
    },
  }
})
