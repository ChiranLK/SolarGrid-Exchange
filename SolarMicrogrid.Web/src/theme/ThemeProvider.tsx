import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { ThemeContext, themeStorageKey, type ResolvedTheme, type ThemeMode } from './themeContext'

const darkQuery = '(prefers-color-scheme: dark)'

function readStoredMode(): ThemeMode {
  try {
    const stored = window.localStorage.getItem(themeStorageKey)
    return stored === 'light' || stored === 'dark' || stored === 'system' ? stored : 'system'
  } catch {
    return 'system'
  }
}

function resolve(mode: ThemeMode): ResolvedTheme {
  if (mode !== 'system') return mode
  return window.matchMedia(darkQuery).matches ? 'dark' : 'light'
}

function applyTheme(theme: ResolvedTheme) {
  document.documentElement.setAttribute('data-bs-theme', theme)
  document.querySelector('meta[name="theme-color"]')
    ?.setAttribute('content', theme === 'dark' ? '#06120E' : '#EDF5EF')
}

/** Light / dark / system appearance, persisted under `solargrid-theme`. */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [mode, setMode] = useState<ThemeMode>(readStoredMode)
  const [systemDark, setSystemDark] = useState(() => window.matchMedia(darkQuery).matches)
  const resolvedTheme: ResolvedTheme = mode === 'system' ? (systemDark ? 'dark' : 'light') : resolve(mode)

  useEffect(() => {
    const media = window.matchMedia(darkQuery)
    const onChange = (event: MediaQueryListEvent) => setSystemDark(event.matches)
    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [])

  useEffect(() => {
    try {
      window.localStorage.setItem(themeStorageKey, mode)
    } catch {
      // Storage may be unavailable; the theme still applies for this visit.
    }
  }, [mode])

  useEffect(() => {
    applyTheme(resolvedTheme)
  }, [resolvedTheme])

  const value = useMemo(() => ({ mode, resolvedTheme, setMode }), [mode, resolvedTheme])
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}
