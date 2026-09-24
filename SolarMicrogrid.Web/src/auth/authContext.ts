import { createContext } from 'react'
import type { AuthSession, LoginRequest } from './authTypes'

export interface AuthContextValue {
  session: AuthSession | null
  isAuthenticated: boolean
  isInitializing: boolean
  login: (request: LoginRequest) => Promise<void>
  logout: () => void
}

export const AuthContext = createContext<AuthContextValue | undefined>(undefined)
