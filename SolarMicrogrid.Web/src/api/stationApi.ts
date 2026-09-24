import type { PagedSlots, PagedStations } from '../features/stations/stationTypes'
import { apiRequest } from './apiClient'

export const stationApi = {
  listActive(signal?: AbortSignal): Promise<PagedStations> {
    return apiRequest<PagedStations>('/stations?isActive=true&page=1&pageSize=100', { signal })
  },

  listAll(signal?: AbortSignal): Promise<PagedStations> {
    return apiRequest<PagedStations>('/stations?page=1&pageSize=100', { signal })
  },

  listAvailableSlots(stationId: string, signal?: AbortSignal): Promise<PagedSlots> {
    const parameters = new URLSearchParams({
      fromUtc: new Date().toISOString(),
      page: '1',
      pageSize: '100',
    })
    return apiRequest<PagedSlots>(
      `/stations/${encodeURIComponent(stationId)}/slots/available?${parameters.toString()}`,
      { signal },
    )
  },
}
