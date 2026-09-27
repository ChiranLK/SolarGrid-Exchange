import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('../../api/apiClient', () => ({ apiRequest: vi.fn() }))

import { apiRequest } from '../../api/apiClient'
import { slotApi } from '../../api/slotApi'
import { stationApi } from '../../api/stationApi'

const request = vi.mocked(apiRequest)

beforeEach(() => request.mockClear())

describe('station and slot endpoint contracts', () => {
  it('uses the Backoffice station status endpoints', () => {
    stationApi.activate('station-id')
    expect(request).toHaveBeenCalledWith('/stations/station-id/activate', { method: 'PATCH', signal: undefined })
    stationApi.deactivate('station-id')
    expect(request).toHaveBeenLastCalledWith('/stations/station-id/deactivate', { method: 'PATCH', signal: undefined })
  })

  it('uses paged station slots and the assigned-station-checked availability endpoint', () => {
    slotApi.list('station-id', { page: 2, pageSize: 10, status: 'Unavailable' })
    expect(request).toHaveBeenCalledWith('/stations/station-id/slots?page=2&pageSize=10&status=Unavailable', { signal: undefined })
    slotApi.changeAvailability('slot-id', 'Available')
    expect(request).toHaveBeenLastCalledWith('/slots/slot-id/availability', {
      method: 'PATCH', body: JSON.stringify({ status: 'Available' }), signal: undefined,
    })
  })

  it('never sends available or reserved capacity when editing slot details', () => {
    slotApi.update('slot-id', {
      startTimeUtc: '2026-12-01T08:30:00Z', endTimeUtc: '2026-12-01T09:30:00Z', totalCapacityKwh: 12,
    })
    const options = request.mock.calls.at(-1)?.[1]
    expect(options?.body).not.toContain('availableCapacityKwh')
    expect(options?.body).not.toContain('reservedCapacityKwh')
  })
})
