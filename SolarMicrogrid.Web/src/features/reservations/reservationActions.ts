import { ApiError } from '../../api/apiClient'
import type { ReservationDetail } from './reservationTypes'

export type ReservationAction = 'update' | 'cancel' | 'approve' | 'reject'

export interface MutationIntent {
  action: ReservationAction
  slotId?: string
  requestedEnergyKwh?: number
  reason?: string
}

export interface ActionErrorPresentation {
  title: string
  message: string
  stale: boolean
  outcomeUnknown: boolean
}

export function describeActionError(error: unknown): ActionErrorPresentation {
  if (!(error instanceof ApiError)) {
    return {
      title: 'Action not completed',
      message: 'The action could not be completed. Review the current reservation and try again.',
      stale: false,
      outcomeUnknown: false,
    }
  }

  const normalized = error.message.toLowerCase()
  const stale = error.status === 409 && (
    normalized.includes('reservation changed') ||
    normalized.includes('version') ||
    normalized.includes('stale')
  )

  if (error.status === 0) {
    return {
      title: 'Action outcome is being checked',
      message: 'The server response was not received. Current reservation state will be checked before this action can be retried.',
      stale: false,
      outcomeUnknown: true,
    }
  }
  if (stale) {
    return {
      title: 'Reservation changed elsewhere',
      message: `${error.message} The latest server version has been loaded; review it before starting another action.`,
      stale: true,
      outcomeUnknown: false,
    }
  }
  if (normalized.includes('notice') || normalized.includes('12 hour')) {
    return {
      title: 'Change cutoff has passed',
      message: `${error.message} The server uses the existing booking start and its current clock for this decision.`,
      stale: false,
      outcomeUnknown: false,
    }
  }
  if (normalized.includes('capacity') || normalized.includes('allocation') || normalized.includes('available energy')) {
    return {
      title: 'Slot capacity changed',
      message: `${error.message} Reloaded slot availability must be used for the next attempt.`,
      stale: false,
      outcomeUnknown: false,
    }
  }

  return {
    title: error.status === 403 ? 'Action not permitted' : 'Action rejected by the server',
    message: error.message,
    stale: false,
    outcomeUnknown: false,
  }
}

export function mutationMatchesCurrentState(
  intent: MutationIntent,
  current: ReservationDetail,
): boolean {
  switch (intent.action) {
    case 'update':
      return current.slotId === intent.slotId &&
        current.requestedEnergyKwh === intent.requestedEnergyKwh
    case 'cancel':
      return current.status === 'Cancelled' &&
        normalizeReason(current.cancellationReason) === normalizeReason(intent.reason)
    case 'approve':
      return current.status === 'Approved'
    case 'reject':
      return current.status === 'Rejected' &&
        normalizeReason(current.rejectionReason) === normalizeReason(intent.reason)
  }
}

export function describeReservationChange(
  previous: ReservationDetail,
  current: ReservationDetail,
): string {
  const changes: string[] = []
  if (previous.status !== current.status) {
    changes.push(`status changed from ${previous.status} to ${current.status}`)
  }
  if (previous.slotId !== current.slotId) {
    changes.push('the scheduled slot changed')
  }
  if (previous.requestedEnergyKwh !== current.requestedEnergyKwh) {
    changes.push(`energy changed from ${previous.requestedEnergyKwh} to ${current.requestedEnergyKwh} kWh`)
  }
  if (previous.version !== current.version) {
    changes.push(`version changed from ${previous.version} to ${current.version}`)
  }

  return changes.length > 0
    ? `The server reports that ${changes.join(', ')}. No newer data was overwritten.`
    : 'The latest server state was loaded. No newer data was overwritten.'
}

function normalizeReason(reason: string | null | undefined): string {
  return reason?.trim() ?? ''
}
