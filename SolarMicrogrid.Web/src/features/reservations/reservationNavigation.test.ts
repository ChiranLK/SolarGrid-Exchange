import { describe, expect, it } from 'vitest'
import {
  buildReservationDetailPath,
  getSafeReservationReturnTo,
} from './reservationNavigation'

describe('reservation navigation', () => {
  it('preserves reservation list filters in the detail return path', () => {
    const returnTo = '/reservations?status=Pending&stationId=station-1&page=2'
    const path = buildReservationDetailPath('reservation-1', returnTo)
    const query = new URLSearchParams(path.split('?')[1])

    expect(path.startsWith('/reservations/reservation-1?')).toBe(true)
    expect(query.get('returnTo')).toBe(returnTo)
  })

  it('rejects external and unrelated return targets', () => {
    expect(getSafeReservationReturnTo('https://example.com')).toBe('/reservations')
    expect(getSafeReservationReturnTo('/users')).toBe('/reservations')
    expect(getSafeReservationReturnTo(null)).toBe('/reservations')
  })

  it('accepts reservation list query strings', () => {
    expect(getSafeReservationReturnTo('/reservations?view=History&page=3'))
      .toBe('/reservations?view=History&page=3')
  })
})
