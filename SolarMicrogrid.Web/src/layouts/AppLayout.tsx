import { useEffect, useRef, useState, type ComponentType, type SVGProps } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import {
  CalendarIcon,
  DashboardIcon,
  HistoryIcon,
  HomeIcon,
  LogOutIcon,
  MenuIcon,
  PhoneIcon,
  ShieldIcon,
  SolarGridLogoIcon,
  SunIcon,
  UsersIcon,
  BoltIcon,
} from '../components/icons'
import { ThemeToggle } from '../components/ThemeToggle'
import { getVisibleNavigation } from '../features/operations/operatorNavigation'

type IconComponent = ComponentType<SVGProps<SVGSVGElement>>

/** Presentation-only icon per navigation path; the navigation items and role rules are unchanged. */
const navigationIcons: Record<string, IconComponent> = {
  '/': HomeIcon,
  '/operator/dashboard': DashboardIcon,
  '/operator/history': HistoryIcon,
  '/users': UsersIcon,
  '/prosumer': PhoneIcon,
  '/stations': SunIcon,
  '/reservations': CalendarIcon,
  '/operations': BoltIcon,
  '/backoffice': ShieldIcon,
}

function formatRoleLabel(role: string | undefined): string {
  return role === 'GridOperator' ? 'Grid Operator' : role ?? ''
}

export function AppLayout() {
  const { session, logout } = useAuth()
  const location = useLocation()
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const navigationButtonRef = useRef<HTMLButtonElement>(null)
  const sidebarRef = useRef<HTMLElement>(null)
  const mainRef = useRef<HTMLElement>(null)

  const visibleNavigation = getVisibleNavigation(session?.role ?? null)

  useEffect(() => {
    mainRef.current?.focus({ preventScroll: true })
  }, [location.pathname])

  useEffect(() => {
    if (!sidebarOpen) return
    const previousFocus = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : navigationButtonRef.current
    sidebarRef.current?.querySelector<HTMLElement>('a, button')?.focus()

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setSidebarOpen(false)
        navigationButtonRef.current?.focus()
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => {
      window.removeEventListener('keydown', handleKeyDown)
      previousFocus?.focus()
    }
  }, [sidebarOpen])

  return (
    <div className="app-shell">
      <div className="sg-aurora" aria-hidden="true" />
      <a className="skip-link" href="#main-content">Skip to main content</a>
      <header className="app-topbar navbar px-3 px-lg-4">
        <button
          ref={navigationButtonRef}
          type="button"
          className="sg-icon-button d-lg-none me-2"
          aria-label="Open navigation"
          aria-expanded={sidebarOpen}
          onClick={() => setSidebarOpen((current) => !current)}
        >
          <MenuIcon />
        </button>
        <NavLink to="/" className="navbar-brand d-flex align-items-center gap-2 mb-0">
          <span className="brand-mark" aria-hidden="true"><SolarGridLogoIcon /></span>
          <span className="brand-name">SolarGrid Exchange</span>
        </NavLink>
        <div className="ms-auto d-flex align-items-center gap-2 gap-sm-3">
          <div className="topbar-identity text-end d-none d-sm-block">
            <div className="topbar-name">{session?.fullName}</div>
            <div className="topbar-role">{formatRoleLabel(session?.role)}</div>
          </div>
          <ThemeToggle />
          <button type="button" className="btn topbar-signout" onClick={logout}>
            <LogOutIcon className="d-sm-none" />
            <span className="d-none d-sm-inline">Sign out</span>
            <span className="visually-hidden d-sm-none">Sign out</span>
          </button>
        </div>
      </header>

      <div className="app-body">
        {sidebarOpen && (
          <button
            type="button"
            className="sidebar-scrim d-lg-none"
            aria-label="Close navigation"
            onClick={() => setSidebarOpen(false)}
          />
        )}
        <aside ref={sidebarRef} className={`app-sidebar ${sidebarOpen ? 'is-open' : ''}`} aria-label="Primary navigation">
          <div className="sidebar-label" aria-hidden="true">Workspace</div>
          <nav className="nav nav-pills flex-column gap-1 px-2 pb-3">
            {visibleNavigation.map((item) => {
              const Icon = navigationIcons[item.path] ?? DashboardIcon
              return (
                <NavLink
                  key={item.path}
                  to={item.path}
                  end={item.path === '/'}
                  className={({ isActive }) => `nav-link d-flex align-items-center gap-3 ${isActive ? 'active' : ''}`}
                  onClick={() => setSidebarOpen(false)}
                >
                  <span className="nav-symbol" aria-hidden="true"><Icon /></span>
                  <span>{item.label}</span>
                </NavLink>
              )
            })}
          </nav>
          <div className="sidebar-footer p-3">
            <div className="small text-uppercase text-body-secondary">Signed in as</div>
            <div className="fw-semibold text-truncate">{session?.nic}</div>
          </div>
        </aside>

        <main ref={mainRef} id="main-content" className="app-content" tabIndex={-1} key={location.pathname}>
          <Outlet />
        </main>
      </div>
    </div>
  )
}
