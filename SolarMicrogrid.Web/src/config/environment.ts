const configuredApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim() || '/api'

export const environment = {
  apiBaseUrl: configuredApiBaseUrl.replace(/\/+$/, ''),
} as const
