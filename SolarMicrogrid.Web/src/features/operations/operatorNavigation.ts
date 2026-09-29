import type { UserRole } from '../../auth/authTypes'
import { operatorRoles, operatorRoutes } from './operatorDashboardModel'

export interface NavigationItem {
  label: string
  path: string
  shortLabel: string
  roles?: readonly UserRole[]
}

export const navigationItems: NavigationItem[] = [
  { label: 'Home', path: '/', shortLabel: 'H' },
  { label: 'Operator dashboard', path: `/${operatorRoutes.dashboard}`, shortLabel: 'D', roles: operatorRoles },
  { label: 'Booking history', path: `/${operatorRoutes.history}`, shortLabel: 'BH', roles: operatorRoles },
  { label: 'Users', path: '/users', shortLabel: 'U', roles: ['Backoffice'] },
  { label: 'Mobile app', path: '/prosumer', shortLabel: 'M', roles: ['Prosumer'] },
  { label: 'Stations & slots', path: '/stations', shortLabel: 'S' },
  { label: 'Reservations', path: '/reservations', shortLabel: 'R' },
  { label: 'Operations', path: '/operations', shortLabel: 'O', roles: ['Backoffice', 'GridOperator'] },
  { label: 'Backoffice', path: '/backoffice', shortLabel: 'B', roles: ['Backoffice'] },
]

export function getVisibleNavigation(role: UserRole | null): NavigationItem[] {
  return navigationItems.filter((item) => !item.roles || (role && item.roles.includes(role)))
}
