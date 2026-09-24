export const reservationStatuses = [
  'Pending',
  'Approved',
  'Rejected',
  'Cancelled',
  'Completed',
] as const

export const reservationViews = [
  'All',
  'Pending',
  'Current',
  'ApprovedFuture',
  'History',
] as const

export type ReservationStatus = (typeof reservationStatuses)[number]
export type ReservationView = (typeof reservationViews)[number]

export interface ReservationAllowedActions {
  canUpdate: boolean
  updateUnavailableReason: string | null
  canCancel: boolean
  cancelUnavailableReason: string | null
  canApprove: boolean
  approveUnavailableReason: string | null
  canReject: boolean
  rejectUnavailableReason: string | null
  canGetQr: boolean
  getQrUnavailableReason: string | null
  canVerifyQr: boolean
  verifyQrUnavailableReason: string | null
  canComplete: boolean
  completeUnavailableReason: string | null
}

export interface ReservationListItem {
  id: string
  prosumerNic: string
  prosumerFullName: string | null
  stationId: string
  stationName: string | null
  stationAddress: string | null
  slotId: string
  slotAvailabilityStatus: string | null
  scheduledStartTimeUtc: string
  scheduledEndTimeUtc: string
  requestedEnergyKwh: number
  status: string
  version: number
  qrEligible: boolean
  allowedActions: ReservationAllowedActions
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ReservationStatusHistoryEntry {
  fromStatus: string | null
  toStatus: string
  changedAtUtc: string
  actorNic: string
  actorRole: string
  version: number
  reason: string | null
}

export interface ReservationDetail extends ReservationListItem {
  createdByActorNic: string
  updatedByActorNic: string
  approvedAtUtc: string | null
  approvedByActorNic: string | null
  rejectedAtUtc: string | null
  rejectedByActorNic: string | null
  rejectionReason: string | null
  cancelledAtUtc: string | null
  cancelledByActorNic: string | null
  cancellationReason: string | null
  completedAtUtc: string | null
  completedByActorNic: string | null
  completedVerificationId: string | null
  statusHistory: ReservationStatusHistoryEntry[]
}

export interface PagedReservations {
  items: ReservationListItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

export interface ReservationListQuery {
  view?: ReservationView
  status?: ReservationStatus | ''
  stationId?: string
  search?: string
  page?: number
  pageSize?: number
}

export interface CreateReservationRequest {
  slotId: string
  requestedEnergyKwh: number
  targetProsumerNic?: string
}

export interface UpdateReservationRequest {
  slotId: string
  requestedEnergyKwh: number
  expectedVersion: number
}
