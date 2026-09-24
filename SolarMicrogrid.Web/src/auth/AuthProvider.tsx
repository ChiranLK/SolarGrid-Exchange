import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { authApi } from '../api/authApi'
import { setUnauthorizedHandler } from '../api/apiClient'
import { AuthContext, type AuthContextValue } from './authContext'
import type { AuthSession, LoginRequest } from './authTypes'
import { isUserRole } from './authTypes'
import { clearSession, readSession, writeSession } from './sessionStorage'

interface AuthProviderProps {
  children: ReactNode
}

export function AuthProvider({ children }: AuthProviderProps) {
  const [session, setSession] = useState<AuthSession | null>(() => readSession())
  const [isInitializing, setIsInitializing] = useState(true)

  const logout = useCallback(() => {
    clearSession()
    setSession(null)
  }, [])

  useEffect(() => {
    setUnauthorizedHandler(logout)
    return () => setUnauthorizedHandler(null)
  }, [logout])

  useEffect(() => {
    let active = true

    async function validateStoredSession() {
      const storedSession = readSession()
      if (!storedSession) {
        if (active) {
          setIsInitializing(false)
        }
        return
      }

      try {
        const currentUser = await authApi.me()
        if (!isUserRole(currentUser.role)) {
          throw new Error('The API returned an unsupported user role.')
        }

        const validatedSession: AuthSession = {
          ...storedSession,
          nic: currentUser.nic,
          role: currentUser.role,
        }
        writeSession(validatedSession)
        if (active) {
          setSession(validatedSession)
        }
      } catch {
        clearSession()
        if (active) {
          setSession(null)
        }
      } finally {
        if (active) {
          setIsInitializing(false)
        }
      }
    }

    void validateStoredSession()
    return () => {
      active = false
    }
  }, [])

  const login = useCallback(async (request: LoginRequest) => {
    const response = await authApi.login(request)
    if (!isUserRole(response.role)) {
      throw new Error('The API returned an unsupported user role.')
    }

    const authenticatedSession: AuthSession = {
      token: response.token,
      nic: response.nic,
      fullName: response.fullName,
      role: response.role,
      status: response.status,
    }
    writeSession(authenticatedSession)
    setSession(authenticatedSession)
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({
      session,
      isAuthenticated: session !== null,
      isInitializing,
      login,
      logout,
    }),
    [isInitializing, login, logout, session],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
