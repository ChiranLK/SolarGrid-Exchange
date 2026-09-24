import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { LoadingState } from '../components/LoadingState'
import { useAuth } from '../auth/useAuth'

export function ProtectedRoute() {
  const { isAuthenticated, isInitializing } = useAuth()
  const location = useLocation()

  if (isInitializing) {
    return <LoadingState label="Restoring your session…" />
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location }} />
  }

  return <Outlet />
}
