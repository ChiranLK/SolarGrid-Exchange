import type { UserRole } from './authTypes'

/** Landing page for each role after sign-in. Prosumer features live in the Android app. */
export const roleHomePaths: Record<UserRole, string> = {
  Backoffice: '/users',
  GridOperator: '/operator/dashboard',
  Prosumer: '/prosumer',
}

export function getHomePathForRole(role: UserRole): string {
  return roleHomePaths[role]
}

// Paths that only mean "somewhere in the app" and should resolve to the role's landing page.
const neutralPaths = new Set(['', '/', '/login', '/dashboard', '/forbidden'])

export interface LoginRedirectSource {
  pathname?: string
  search?: string
}

/**
 * Where to go after sign-in. A deep link captured by ProtectedRoute is honoured; otherwise the
 * role's landing page is used. Route guards still decide access, so an unsuitable deep link lands
 * on the forbidden page rather than looping back to login.
 */
export function resolvePostLoginDestination(role: UserRole, from?: LoginRedirectSource | null): string {
  const pathname = from?.pathname ?? ''
  if (!pathname.startsWith('/') || pathname.startsWith('//') || neutralPaths.has(pathname)) {
    return getHomePathForRole(role)
  }

  return `${pathname}${from?.search ?? ''}`
}
