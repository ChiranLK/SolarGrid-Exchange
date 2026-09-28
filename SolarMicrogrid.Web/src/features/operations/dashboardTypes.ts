import type { UserRole } from '../../auth/authTypes'
import type { ReservationStatus } from '../reservations/reservationTypes'

export interface DashboardStatusSummary {
  pendingTotal: number
  approvedTotal: number
  rejectedTotal: number
  cancelledTotal: number
  completedTotal: number
  currentCount: number
  pendingCount: number
  approvedFutureCount: number
  historyCount: number
}

export interface DashboardReservationSummary {
  reservationId: string
  reference: string
  prosumerNic: string
  prosumerFullName: string | null
  stationId: string
  stationName: string | null
  stationAddress: string | null
  slotId: string
  scheduledStartTimeUtc: string
  scheduledEndTimeUtc: string
  requestedEnergyKwh: number
  status: ReservationStatus
  version: number
  createdAtUtc: string
  updatedAtUtc: string
  completedAtUtc: string | null
}

export interface DashboardResponse {
  serverNowUtc: string
  role: UserRole
  scope: 'OwnReservations' | 'AssignedStation' | 'Global'
  stationId: string | null
  statusSummary: DashboardStatusSummary
  currentReservations: DashboardReservationSummary[]
  pendingReservations: DashboardReservationSummary[]
  recentHistory: DashboardReservationSummary[]
  recentTransfers: DashboardReservationSummary[]
  activeTransfers: DashboardReservationSummary[]
  completedTransfers: DashboardReservationSummary[]
}

export interface BookingHistoryQuery {
  search?: string
  status?: ReservationStatus | ''
  stationId?: string
  fromDate?: string
  toDate?: string
  page: number
  pageSize: number
}

export interface PagedBookingHistory {
  serverNowUtc: string
  items: DashboardReservationSummary[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}
