import { describe, expect, it } from 'vitest'
import {
  formatDateTime,
  formatEnergy,
  formatReservationReference,
  formatSlotRange,
} from './reservationFormatters'

describe('reservationFormatters', () => {
  it('creates a stable client-facing reference from the MongoDB identifier', () => {
    expect(formatReservationReference('68d3f998e18a65b9280abc12')).toBe('RES-280ABC12')
  })

  it('returns safe fallback text for absent or invalid timestamps', () => {
    expect(formatDateTime(null)).toBe('—')
    expect(formatDateTime('not-a-date')).toBe('—')
    expect(formatSlotRange('not-a-date', 'also-invalid')).toBe('Schedule unavailable')
  })

  it('labels reservation energy using the API unit', () => {
    expect(formatEnergy(12.5)).toContain('12.5')
    expect(formatEnergy(12.5)).toContain('kWh')
  })
})
