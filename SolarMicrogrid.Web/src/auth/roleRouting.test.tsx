import { renderToStaticMarkup } from 'react-dom/server'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('../api/apiClient', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/apiClient')>()),
  apiRequest: vi.fn(),
}))

import { apiRequest } from '../api/apiClient'
import App from '../App'
import { getVisibleNavigation } from '../features/operations/operatorNavigation'
import { AuthContext, type AuthContextValue } from './authContext'
import type { UserRole } from './authTypes'
import { getHomePathForRole, resolvePostLoginDestination } from './roleRouting'

const request = vi.mocked(apiRequest)

function renderApp(path: string, role: UserRole | null, isInitializing = false) {
  // Renders the real route tree for a signed-in role (or signed-out when role is null).
  const context: AuthContextValue = {
    session: role ? { token: 'redacted', nic: '200000000001', fullName: 'Test User', role, status: 'Active' } : null,
    isAuthenticated: role !== null,
    isInitializing,
    login: vi.fn(),
    logout: vi.fn(),
  }
  return renderToStaticMarkup(
    <AuthContext.Provider value={context}>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

beforeEach(() => {
  request.mockReset()
  request.mockReturnValue(new Promise(() => {}))
})

describe('post-login role redirection', () => {
  it('sends each role to its own landing page', () => {
    expect(getHomePathForRole('Backoffice')).toBe('/users')
    expect(getHomePathForRole('GridOperator')).toBe('/operator/dashboard')
    expect(getHomePathForRole('Prosumer')).toBe('/prosumer')
    expect(resolvePostLoginDestination('Backoffice')).toBe('/users')
    expect(resolvePostLoginDestination('GridOperator', null)).toBe('/operator/dashboard')
    expect(resolvePostLoginDestination('Prosumer', {})).toBe('/prosumer')
  })

  it('honours the deep link ProtectedRoute captured, including its query string', () => {
    expect(resolvePostLoginDestination('Backoffice', { pathname: '/users/pending' })).toBe('/users/pending')
    expect(resolvePostLoginDestination('GridOperator', { pathname: '/operator/history', search: '?page=2' }))
      .toBe('/operator/history?page=2')
  })

  it.each(['/', '/login', '/dashboard', '/forbidden', ''])('treats %j as "no preference" to avoid loops', (pathname) => {
    expect(resolvePostLoginDestination('Backoffice', { pathname })).toBe('/users')
    expect(resolvePostLoginDestination('GridOperator', { pathname })).toBe('/operator/dashboard')
  })

  it('never follows protocol-relative or non-path destinations', () => {
    expect(resolvePostLoginDestination('Backoffice', { pathname: '//evil.example' })).toBe('/users')
    expect(resolvePostLoginDestination('Backoffice', { pathname: 'https://evil.example' })).toBe('/users')
  })

  it('does not flash the sign-in form or a forbidden page for an already signed-in user', () => {
    const html = renderApp('/login', 'Backoffice')
    expect(html).not.toContain('Sign in to your account')
    expect(html).not.toContain('Access denied')
  })

  it('shows a loading state, not a page, while the session is being restored', () => {
    const html = renderApp('/users', 'Backoffice', true)
    expect(html).toContain('Restoring your session')
    expect(html).not.toContain('Account administration')
  })
})

describe('Member 1 route protection (direct URL access)', () => {
  const adminPaths = ['/users', '/users/new', '/users/200000000004/edit', '/users/pending', '/users/deactivation-requests']

  it.each(adminPaths)('renders %s for Backoffice', (path) => {
    const html = renderApp(path, 'Backoffice')
    expect(html).toContain('Account administration')
  })

  it.each(adminPaths.flatMap((path) => [[path, 'GridOperator'], [path, 'Prosumer']] as const))(
    'blocks %s for %s',
    (path, role) => {
      const html = renderApp(path, role)
      expect(html).not.toContain('Account administration')
      expect(html).not.toContain('Create staff account')
    },
  )

  it.each(adminPaths)('redirects signed-out visitors away from %s', (path) => {
    const html = renderApp(path, null)
    expect(html).not.toContain('Account administration')
  })

  it('keeps Prosumers out of Grid Operator pages and shows them the mobile-app notice', () => {
    expect(renderApp('/operator/dashboard', 'Prosumer')).not.toContain('Operator dashboard</h1>')
    const notice = renderApp('/prosumer', 'Prosumer')
    expect(notice).toContain('Use the SolarGrid mobile app')
    expect(renderApp('/prosumer', 'Backoffice')).not.toContain('Use the SolarGrid mobile app')
    expect(renderApp('/prosumer', 'GridOperator')).not.toContain('Use the SolarGrid mobile app')
  })

  it.each(['/stations', '/stations/station-id', '/reservations', '/reservations/new'])(
    'keeps Prosumers out of the staff web route %s',
    (path) => {
      const html = renderApp(path, 'Prosumer')
      expect(html).not.toContain('Loading stations')
      expect(html).not.toContain('Loading reservations')
      expect(html).not.toContain('Create reservation')
    },
  )

  it('loads the admin views only through the Web API', () => {
    renderApp('/users', 'Backoffice')
    // Effects do not run in static rendering, so no request is sent and no direct data access exists.
    expect(request).not.toHaveBeenCalled()
  })
})

describe('role-based navigation', () => {
  it('shows account administration to Backoffice only', () => {
    const paths = (role: UserRole) => getVisibleNavigation(role).map((item) => item.path)
    expect(paths('Backoffice')).toContain('/users')
    expect(paths('GridOperator')).not.toContain('/users')
    expect(paths('Prosumer')).not.toContain('/users')
    expect(paths('GridOperator')).not.toContain('/backoffice')
    expect(paths('Prosumer')).toContain('/prosumer')
    expect(paths('Backoffice')).not.toContain('/prosumer')
  })

  it('renders the Backoffice Users link and hides it from a Grid Operator', () => {
    expect(renderApp('/', 'Backoffice')).toContain('href="/users"')
    expect(renderApp('/', 'GridOperator')).not.toContain('href="/users"')
  })
})
