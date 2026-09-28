import { apiRequest } from './apiClient'
import type { PagedSlots, SlotInput, SlotListQuery, SlotSummary } from '../features/stations/stationTypes'

export const slotApi = {
  list(stationId: string, query: SlotListQuery, signal?: AbortSignal): Promise<PagedSlots> {
    const parameters = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) })
    if (query.status) parameters.set('status', query.status)
    return apiRequest<PagedSlots>(`/stations/${encodeURIComponent(stationId)}/slots?${parameters.toString()}`, { signal })
  },

  getById(slotId: string, signal?: AbortSignal): Promise<SlotSummary> {
    return apiRequest<SlotSummary>(`/slots/${encodeURIComponent(slotId)}`, { signal })
  },

  create(stationId: string, request: SlotInput, signal?: AbortSignal): Promise<SlotSummary> {
    return apiRequest<SlotSummary>(`/stations/${encodeURIComponent(stationId)}/slots`, {
      method: 'POST', body: JSON.stringify(request), signal,
    })
  },

  update(slotId: string, request: SlotInput, signal?: AbortSignal): Promise<SlotSummary> {
    return apiRequest<SlotSummary>(`/slots/${encodeURIComponent(slotId)}`, {
      method: 'PUT', body: JSON.stringify(request), signal,
    })
  },

  changeAvailability(slotId: string, status: 'Available' | 'Unavailable', signal?: AbortSignal): Promise<SlotSummary> {
    return apiRequest<SlotSummary>(`/slots/${encodeURIComponent(slotId)}/availability`, {
      method: 'PATCH', body: JSON.stringify({ status }), signal,
    })
  },
}
