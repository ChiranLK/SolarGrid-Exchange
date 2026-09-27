import { renderToStaticMarkup } from 'react-dom/server'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthContext, type AuthContextValue } from '../auth/authContext'
import type { UserRole } from '../auth/authTypes'
import { AppLayout } from '../layouts/AppLayout'
import { RoleRoute } from '../routes/RoleRoute'

vi.mock('../api/apiClient', () => ({ apiRequest: vi.fn() }))

import { apiRequest } from '../api/apiClient'
import { authApi } from '../api/authApi'
import { reservationApi } from '../features/reservations/reservationApi'

const request = vi.mocked(apiRequest)

function renderAs(role: UserRole, path = '/') {
  const context: AuthContextValue = {
    session: { token: '', nic: '', fullName: 'Test User', role, status: 'Active' },
    isAuthenticated: true,
    isInitializing: false,
    login: vi.fn(),
    logout: vi.fn(),
  }
  return renderToStaticMarkup(
    <AuthContext.Provider value={context}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route index element={<p>Home content</p>} />
            <Route element={<RoleRoute allowedRoles={['Backoffice']} />}>
              <Route path="staff-only" element={<p>Backoffice content</p>} />
            </Route>
          </Route>
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

beforeEach(() => request.mockClear())

describe('shared staff integration', () => {
  it('shows navigation using the API role names and protects Backoffice routes', () => {
    const backoffice = renderAs('Backoffice')
    const operator = renderAs('GridOperator')
    const prosumer = renderAs('Prosumer')
    expect(backoffice).toContain('href="/backoffice"')
    expect(operator).toContain('href="/operations"')
    expect(operator).not.toContain('href="/backoffice"')
    expect(prosumer).not.toContain('href="/users"')
    expect(prosumer).not.toContain('href="/operations"')
    expect(renderAs('Backoffice', '/staff-only')).toContain('Backoffice content')
    expect(renderAs('GridOperator', '/staff-only')).not.toContain('Backoffice content')
  })

  it('uses the implemented authentication route and response boundary', () => {
    authApi.login({ email: '', password: '' })
    expect(request).toHaveBeenCalledWith('/auth/login', {
      method: 'POST', body: JSON.stringify({ email: '', password: '' }), anonymous: true,
    })
    authApi.me()
    expect(request).toHaveBeenLastCalledWith('/auth/me')
  })

  it('uses API history filtering and staff decision endpoints', () => {
    reservationApi.list({ view: 'History', page: 1, pageSize: 10 })
    expect(request).toHaveBeenCalledWith('/reservations?view=History&page=1&pageSize=10', { signal: undefined })
    reservationApi.approve('reservation-id', 3, 'test-key')
    expect(request).toHaveBeenLastCalledWith('/reservations/reservation-id/approve', {
      method: 'POST', headers: { 'Idempotency-Key': 'test-key' }, body: JSON.stringify({ expectedVersion: 3 }), signal: undefined,
    })
    reservationApi.reject('reservation-id', 3, 'Reason', 'test-key')
    expect(request).toHaveBeenLastCalledWith('/reservations/reservation-id/reject', {
      method: 'POST', headers: { 'Idempotency-Key': 'test-key' }, body: JSON.stringify({ expectedVersion: 3, reason: 'Reason' }), signal: undefined,
    })
  })
})
