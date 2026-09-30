import { useTheme } from '../theme/themeContext'
import type { ThemeMode } from '../theme/themeContext'
import { MonitorIcon, MoonIcon, SunIcon } from './icons'

const nextMode: Record<ThemeMode, ThemeMode> = { light: 'dark', dark: 'system', system: 'light' }
const modeLabel: Record<ThemeMode, string> = { light: 'Light', dark: 'Dark', system: 'System' }

/** Cycles Light → Dark → System. The current mode is announced in the accessible name. */
export function ThemeToggle({ className = '' }: { className?: string }) {
  const { mode, setMode } = useTheme()
  const upcoming = nextMode[mode]
  const Icon = mode === 'light' ? SunIcon : mode === 'dark' ? MoonIcon : MonitorIcon

  return (
    <button
      type="button"
      className={`sg-theme-toggle ${className}`.trim()}
      aria-label={`Theme: ${modeLabel[mode]}. Switch to ${modeLabel[upcoming]}`}
      title={`Theme: ${modeLabel[mode]}`}
      onClick={() => setMode(upcoming)}
    >
      <Icon />
    </button>
  )
}
