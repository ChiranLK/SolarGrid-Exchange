import { useTheme } from '../theme/themeContext'
import { MoonIcon, SunIcon } from './icons'

/** Toggles between light and dark. Shows the moon in light mode and the sun in dark mode. */
export function ThemeToggle({ className = '' }: { className?: string }) {
  const { resolvedTheme, setMode } = useTheme()
  const isDark = resolvedTheme === 'dark'
  const label = isDark ? 'Switch to light mode' : 'Switch to dark mode'
  const Icon = isDark ? SunIcon : MoonIcon

  return (
    <button
      type="button"
      className={`sg-theme-toggle ${className}`.trim()}
      aria-label={label}
      title={label}
      onClick={() => setMode(isDark ? 'light' : 'dark')}
    >
      <Icon />
    </button>
  )
}
