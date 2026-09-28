import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import { ScheduleFields } from './ScheduleFields'
import { initialSlotValues, toSlotInput, validateSlot } from './slotFormModel'
import type { SlotSummary } from './stationTypes'

const slot: SlotSummary = {
  id: 'slot-id', stationId: 'station-id', startTimeUtc: '2026-12-01T08:30:00Z',
  endTimeUtc: '2026-12-01T09:30:00Z', totalCapacityKwh: 12,
  availableCapacityKwh: 7, availabilityStatus: 'Available',
  createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: '2026-01-01T00:00:00Z',
}

describe('slot form', () => {
  it('preserves UTC minutes and submits only editable slot fields', () => {
    const request = toSlotInput(initialSlotValues(slot))
    expect(request).toEqual({
      startTimeUtc: '2026-12-01T08:30:00.000Z',
      endTimeUtc: '2026-12-01T09:30:00.000Z',
      totalCapacityKwh: 12,
    })
    expect(request).not.toHaveProperty('availableCapacityKwh')
    expect(request).not.toHaveProperty('reservedCapacityKwh')
  })

  it('rejects past, reversed, and invalid capacity values', () => {
    const errors = validateSlot({ startUtc: '2026-01-01T00:00', endUtc: '2025-12-31T23:00', totalCapacityKwh: '0' }, new Date('2026-09-01T00:00:00Z'))
    expect(errors.join(' ')).toContain('future UTC')
    expect(errors.join(' ')).toContain('End must be after start')
    expect(errors.join(' ')).toContain('0.01 kWh')
  })

  it('renders the schedule editor with the actual operating timezone', () => {
    const html = renderToStaticMarkup(<ScheduleFields schedule={[{ dayOfWeek: 'Monday', isOpen: true, openingTime: '08:00', closingTime: '17:00' }]} onChange={vi.fn()} />)
    expect(html).toContain('Asia/Colombo')
    expect(html).toContain('Monday open')
    expect(html).toContain('08:00')
  })
})
