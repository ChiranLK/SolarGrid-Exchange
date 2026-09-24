import { useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import type { UserRole } from '../auth/authTypes'

interface NavigationItem {
  label: string
  path: string
  shortLabel: string
  roles?: readonly UserRole[]
}

const navigation: NavigationItem[] = [
  { label: 'Home', path: '/', shortLabel: 'H' },
  { label: 'Dashboard', path: '/dashboard', shortLabel: 'D' },
  { label: 'Users', path: '/users', shortLabel: 'U', roles: ['Backoffice', 'GridOperator'] },
  { label: 'Stations & slots', path: '/stations', shortLabel: 'S' },
  { label: 'Reservations', path: '/reservations', shortLabel: 'R' },
  { label: 'Operations', path: '/operations', shortLabel: 'O', roles: ['Backoffice', 'GridOperator'] },
  { label: 'Backoffice', path: '/backoffice', shortLabel: 'B', roles: ['Backoffice'] },
]

export function AppLayout() {
  const { session, logout } = useAuth()
  const location = useLocation()
  const [sidebarOpen, setSidebarOpen] = useState(false)

  const visibleNavigation = navigation.filter(
    (item) => !item.roles || (session && item.roles.includes(session.role)),
  )

  return (
    <div className="app-shell">
      <header className="app-topbar navbar navbar-dark px-3 px-lg-4">
        <button
          type="button"
          className="btn btn-link text-white d-lg-none p-1 me-2"
          aria-label="Open navigation"
          aria-expanded={sidebarOpen}
          onClick={() => setSidebarOpen((current) => !current)}
        >
          <span className="navbar-toggler-icon" aria-hidden="true" />
        </button>
        <NavLink to="/" className="navbar-brand d-flex align-items-center gap-2 mb-0">
          <span className="brand-mark" aria-hidden="true">SG</span>
          <span>SolarGrid Exchange</span>
        </NavLink>
        <div className="ms-auto d-flex align-items-center gap-3">
          <div className="text-end d-none d-sm-block">
            <div className="small fw-semibold">{session?.fullName}</div>
            <div className="topbar-role">{session?.role}</div>
          </div>
          <button type="button" className="btn btn-sm btn-outline-light" onClick={logout}>
            Sign out
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
        <aside className={`app-sidebar ${sidebarOpen ? 'is-open' : ''}`} aria-label="Primary navigation">
          <nav className="nav nav-pills flex-column gap-1 p-3">
            {visibleNavigation.map((item) => (
              <NavLink
                key={item.path}
                to={item.path}
                end={item.path === '/'}
                className={({ isActive }) => `nav-link d-flex align-items-center gap-3 ${isActive ? 'active' : ''}`}
                onClick={() => setSidebarOpen(false)}
              >
                <span className="nav-symbol" aria-hidden="true">{item.shortLabel}</span>
                <span>{item.label}</span>
              </NavLink>
            ))}
          </nav>
          <div className="sidebar-footer p-3">
            <div className="small text-uppercase text-body-secondary">Signed in as</div>
            <div className="fw-semibold text-truncate">{session?.nic}</div>
          </div>
        </aside>

        <main id="main-content" className="app-content" key={location.pathname}>
          <Outlet />
        </main>
      </div>
    </div>
  )
}
