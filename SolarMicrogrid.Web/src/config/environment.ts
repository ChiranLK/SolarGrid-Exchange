const configuredApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim() || '/api'

if (import.meta.env.PROD && /^https?:\/\/(localhost|127\.0\.0\.1|\[::1\])(?=[:/]|$)/i.test(configuredApiBaseUrl)) {
  throw new Error('VITE_API_BASE_URL must not target localhost in a production build')
}

if (import.meta.env.PROD && /^http:\/\//i.test(configuredApiBaseUrl)) {
  throw new Error('VITE_API_BASE_URL must use HTTPS or a same-origin relative path in production')
}

export const environment = {
  apiBaseUrl: configuredApiBaseUrl.replace(/\/+$/, ''),
} as const
