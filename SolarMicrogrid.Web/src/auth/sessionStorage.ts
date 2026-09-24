import type { AuthSession } from './authTypes'
import { isUserRole } from './authTypes'

const sessionKey = 'solargrid.auth.session'

export function readSession(): AuthSession | null {
  try {
    const value = window.sessionStorage.getItem(sessionKey)
    if (!value) {
      return null
    }

    const session = JSON.parse(value) as Partial<AuthSession>
    if (
      typeof session.token !== 'string' ||
      typeof session.nic !== 'string' ||
      typeof session.fullName !== 'string' ||
      typeof session.role !== 'string' ||
      !isUserRole(session.role) ||
      typeof session.status !== 'string'
    ) {
      clearSession()
      return null
    }

    return session as AuthSession
  } catch {
    clearSession()
    return null
  }
}

export function writeSession(session: AuthSession): void {
  window.sessionStorage.setItem(sessionKey, JSON.stringify(session))
}

export function clearSession(): void {
  window.sessionStorage.removeItem(sessionKey)
}
