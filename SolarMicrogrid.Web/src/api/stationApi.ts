import type { PagedSlots, PagedStations, Station, StationInput, StationListQuery } from '../features/stations/stationTypes'
import { apiRequest } from './apiClient'

export const stationApi = {
  list(query: StationListQuery, signal?: AbortSignal): Promise<PagedStations> {
    const parameters = new URLSearchParams({
      page: String(query.page),
      pageSize: String(query.pageSize),
    })
    if (query.search) parameters.set('search', query.search)
    if (query.isActive !== undefined) parameters.set('isActive', String(query.isActive))
    return apiRequest<PagedStations>(`/stations?${parameters.toString()}`, { signal })
  },

  getById(stationId: string, signal?: AbortSignal): Promise<Station> {
    return apiRequest<Station>(`/stations/${encodeURIComponent(stationId)}`, { signal })
  },

  create(request: StationInput, signal?: AbortSignal): Promise<Station> {
    return apiRequest<Station>('/stations', {
      method: 'POST', body: JSON.stringify(request), signal,
    })
  },

  update(stationId: string, request: StationInput, signal?: AbortSignal): Promise<Station> {
    return apiRequest<Station>(`/stations/${encodeURIComponent(stationId)}`, {
      method: 'PUT', body: JSON.stringify(request), signal,
    })
  },

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
