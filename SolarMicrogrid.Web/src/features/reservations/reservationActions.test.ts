import { describe, expect, it } from 'vitest'
import { ApiError } from '../../api/apiClient'
import {
  describeActionError,
  describeReservationChange,
  mutationMatchesCurrentState,
} from './reservationActions'
import type { ReservationDetail } from './reservationTypes'

const baseReservation = {
  id: 'reservation-one',
  slotId: 'slot-one',
  requestedEnergyKwh: 4,
  status: 'Pending',
  version: 2,
  cancellationReason: null,
  rejectionReason: null,
} as ReservationDetail

describe('reservation action reconciliation', () => {
  it('recognizes an update confirmed by current server state', () => {
    const current = { ...baseReservation, slotId: 'slot-two', requestedEnergyKwh: 6, version: 3 }

    expect(mutationMatchesCurrentState({
      action: 'update',
      slotId: 'slot-two',
      requestedEnergyKwh: 6,
    }, current)).toBe(true)
  })

  it('requires the agreed rejection reason when reconciling', () => {
    const current = { ...baseReservation, status: 'Rejected', rejectionReason: 'No capacity', version: 3 }

    expect(mutationMatchesCurrentState({ action: 'reject', reason: ' No capacity ' }, current)).toBe(true)
    expect(mutationMatchesCurrentState({ action: 'reject', reason: 'Different reason' }, current)).toBe(false)
  })

  it('reconciles cancellation with an omitted optional reason', () => {
    const current = { ...baseReservation, status: 'Cancelled', cancellationReason: null, version: 3 }

    expect(mutationMatchesCurrentState({ action: 'cancel', reason: '   ' }, current)).toBe(true)
  })

  it('reconciles an approval from its authoritative status', () => {
    const current = { ...baseReservation, status: 'Approved', version: 3 }

    expect(mutationMatchesCurrentState({ action: 'approve' }, current)).toBe(true)
  })

  it('identifies and explains stale version conflicts', () => {
    const result = describeActionError(new ApiError(409, 'The reservation changed. Reload it and try again.'))

    expect(result.stale).toBe(true)
    expect(result.title).toBe('Reservation changed elsewhere')
    expect(result.message).toContain('latest server version')
  })

  it('keeps the server twelve-hour cutoff message', () => {
    const result = describeActionError(new ApiError(409, 'The minimum notice period to cancel this reservation has passed.'))

    expect(result.title).toBe('Change cutoff has passed')
    expect(result.message).toContain('minimum notice period')
  })

  it('preserves a clear backend rejection message', () => {
    const result = describeActionError(new ApiError(409, 'The selected slot does not have enough available energy.'))

    expect(result.title).toBe('Slot capacity changed')
    expect(result.message).toContain('selected slot does not have enough available energy')
  })

  it('describes visible cross-client changes without overwriting them', () => {
    const current = { ...baseReservation, status: 'Approved', version: 3 }

    expect(describeReservationChange(baseReservation, current)).toContain('status changed from Pending to Approved')
    expect(describeReservationChange(baseReservation, current)).toContain('No newer data was overwritten')
  })
})
