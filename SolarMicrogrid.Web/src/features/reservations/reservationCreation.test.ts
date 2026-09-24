import { describe, expect, it } from 'vitest'
import { ApiError } from '../../api/apiClient'
import { describeCreationError, getCreationFingerprint } from './reservationCreation'

describe('staff reservation creation helpers', () => {
  it('keeps one fingerprint for equivalent logical bookings', () => {
    const first = getCreationFingerprint({
      targetProsumerNic: ' 200012345678 ',
      slotId: 'slot-one',
      requestedEnergyKwh: 4.5,
    })
    const retry = getCreationFingerprint({
      targetProsumerNic: '200012345678',
      slotId: 'slot-one',
      requestedEnergyKwh: 4.5,
    })

    expect(retry).toBe(first)
  })

  it('changes the fingerprint when booking inputs change', () => {
    const first = getCreationFingerprint({
      targetProsumerNic: '200012345678',
      slotId: 'slot-one',
      requestedEnergyKwh: 4.5,
    })
    const changed = getCreationFingerprint({
      targetProsumerNic: '200012345678',
      slotId: 'slot-two',
      requestedEnergyKwh: 4.5,
    })

    expect(changed).not.toBe(first)
  })

  it('explains an unknown network outcome without claiming failure', () => {
    const result = describeCreationError(new ApiError(0, 'API unavailable'))

    expect(result.outcomeUnknown).toBe(true)
    expect(result.message).toContain('may have been saved')
    expect(result.message).toContain('without creating a duplicate')
  })

  it('provides specific guidance for capacity conflicts', () => {
    const result = describeCreationError(new ApiError(409, 'Insufficient available capacity.'))

    expect(result.title).toBe('Capacity is no longer available')
    expect(result.message).toContain('Reload')
  })

  it('provides specific guidance for the server booking horizon', () => {
    const result = describeCreationError(
      new ApiError(409, 'Reservations cannot be scheduled more than 7 days ahead.'),
    )

    expect(result.title).toBe('Slot is outside the booking window')
    expect(result.message).toContain('seven days')
  })

  it('explains inactive Prosumer eligibility failures', () => {
    const result = describeCreationError(
      new ApiError(409, 'The target user is not an eligible active prosumer.'),
    )

    expect(result.title).toBe('Prosumer is not eligible')
    expect(result.message).toContain('select an active Prosumer')
  })

  it('explains invalid slot failures', () => {
    const result = describeCreationError(
      new ApiError(404, 'The selected slot does not exist.'),
    )

    expect(result.title).toBe('Slot is no longer valid')
    expect(result.message).toContain('Reload available slots')
  })

  it('explains duplicate and overlapping bookings', () => {
    const result = describeCreationError(
      new ApiError(409, 'The prosumer already has a duplicate or overlapping active reservation.'),
    )

    expect(result.title).toBe('Conflicting reservation')
    expect(result.message).toContain('existing active bookings')
  })
})
