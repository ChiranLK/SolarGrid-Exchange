import { createContext, useContext } from 'react'

export type ThemeMode = 'light' | 'dark' | 'system'
export type ResolvedTheme = Exclude<ThemeMode, 'system'>

export interface ThemeContextValue {
  mode: ThemeMode
  resolvedTheme: ResolvedTheme
  setMode: (mode: ThemeMode) => void
}

export const themeStorageKey = 'solargrid-theme'

/** Safe default so presentation components render without a provider (for example in tests). */
export const ThemeContext = createContext<ThemeContextValue>({
  mode: 'system',
  resolvedTheme: 'light',
  setMode: () => undefined,
})

export function useTheme(): ThemeContextValue {
  return useContext(ThemeContext)
}
