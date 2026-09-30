import { renderToStaticMarkup } from 'react-dom/server'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('../../api/apiClient', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/apiClient')>()),
  apiRequest: vi.fn(),
}))

import { apiRequest } from '../../api/apiClient'
import App from '../../App'
import { AuthContext, type AuthContextValue } from '../../auth/authContext'
import type { UserRole } from '../../auth/authTypes'
import { contactDetails } from './siteContent'

const request = vi.mocked(apiRequest)

function renderApp(path: string, role: UserRole | null = null) {
  const context: AuthContextValue = {
    session: role ? { token: 'redacted', nic: '200000000001', fullName: 'Test User', role, status: 'Active' } : null,
    isAuthenticated: role !== null,
    isInitializing: false,
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

describe('public site', () => {
  it('shows the public home page to visitors at "/"', () => {
    const html = renderApp('/')
    expect(html).toContain('booked</em> from a station near you')
    expect(html).toContain('id="features"')
    expect(html).toContain('id="mobile-app"')
    expect(html).toContain('href="/login"')
    expect(html).not.toContain('Primary navigation')
  })

  it('keeps the signed-in workspace at "/" for members', () => {
    const html = renderApp('/', 'Backoffice')
    expect(html).toContain('Primary navigation')
    expect(html).not.toContain('id="features"')
  })

  it('renders About and Contact without signing in', () => {
    expect(renderApp('/about')).toContain('We help neighbours share the sun.')
    const contact = renderApp('/contact')
    expect(contact).toContain('We are here to help.')
    expect(contact).toContain(contactDetails.phoneHref)
    expect(contact).toContain(`mailto:${contactDetails.email}`)
  })

  it('offers the workspace instead of sign-in to members on public pages', () => {
    const html = renderApp('/about', 'GridOperator')
    expect(html).toContain('Open workspace')
    expect(html).toContain('href="/operator/dashboard"')
  })

  it('still sends visitors to sign in for protected pages', () => {
    expect(renderApp('/users')).not.toContain('User management')
    expect(renderApp('/users')).not.toContain('id="features"')
  })

  it('uses the real app icon and links back home from sign-in', () => {
    const html = renderApp('/login')
    expect(html).toContain('fill="#0B3D2E"')
    expect(html).toContain('Back to home')
    expect(html).not.toContain('API')
  })
})
