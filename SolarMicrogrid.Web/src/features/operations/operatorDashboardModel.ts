import type { UserRole } from '../../auth/authTypes'
import { reservationStatuses, type ReservationStatus } from '../reservations/reservationTypes'
import type { DashboardResponse, PagedBookingHistory } from './dashboardTypes'

export const operatorRoles = ['GridOperator'] as const satisfies readonly UserRole[]

export const operatorRoutes = {
  dashboard: 'operator/dashboard',
  history: 'operator/history',
} as const

export type DashboardContentState = 'loading' | 'error' | 'empty' | 'ready'

export interface HistoryFilters {
  search: string
  status: ReservationStatus | ''
  stationId: string
  fromDate: string
  toDate: string
  page: number
}

export function canAccessOperatorScreens(role: UserRole | null): boolean {
  return role !== null && operatorRoles.includes(role as 'GridOperator')
}

export function classifyDashboardContent(
  isLoading: boolean,
  error: unknown,
  dashboard: DashboardResponse | null,
): DashboardContentState {
  if (isLoading) {
    return 'loading'
  }
  if (error) {
    return 'error'
  }
  if (!dashboard || dashboard.statusSummary.historyCount === 0) {
    const hasLiveWork = Boolean(
      dashboard && (
        dashboard.statusSummary.pendingCount > 0 ||
        dashboard.statusSummary.approvedFutureCount > 0 ||
        dashboard.statusSummary.currentCount > 0
      ),
    )
    return hasLiveWork ? 'ready' : 'empty'
  }
  return 'ready'
}

export function classifyHistoryContent(
  isLoading: boolean,
  error: unknown,
  history: PagedBookingHistory | null,
): DashboardContentState {
  if (isLoading) {
    return 'loading'
  }
  if (error) {
    return 'error'
  }
  return history?.items.length ? 'ready' : 'empty'
}

export function readHistoryFilters(parameters: URLSearchParams): HistoryFilters {
  const statusValue = parameters.get('status')
  const parsedPage = Number.parseInt(parameters.get('page') ?? '1', 10)
  return {
    search: parameters.get('search') ?? '',
    status: reservationStatuses.includes(statusValue as ReservationStatus)
      ? (statusValue as ReservationStatus)
      : '',
    stationId: parameters.get('stationId') ?? '',
    fromDate: parameters.get('fromDate') ?? '',
    toDate: parameters.get('toDate') ?? '',
    page: Number.isFinite(parsedPage) && parsedPage > 0 ? parsedPage : 1,
  }
}

export function validateHistoryDateRange(fromDate: string, toDate: string): string | null {
  if (fromDate && toDate && fromDate > toDate) {
    return 'From date cannot be later than to date.'
  }
  return null
}

export function nextRefreshToken(current: number): number {
  return current + 1
}
