import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import type { UserRole } from '../auth/authTypes'

interface RoleRouteProps {
  allowedRoles: readonly UserRole[]
}

export function RoleRoute({ allowedRoles }: RoleRouteProps) {
  const { session } = useAuth()

  if (!session || !allowedRoles.includes(session.role)) {
    return <Navigate to="/forbidden" replace />
  }

  return <Outlet />
}
