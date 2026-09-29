import { apiRequest } from '../../api/apiClient'
import type {
  BookingHistoryQuery,
  DashboardResponse,
  PagedBookingHistory,
} from './dashboardTypes'

export const dashboardApi = {
  getDashboard(recentLimit = 5, signal?: AbortSignal): Promise<DashboardResponse> {
    const parameters = new URLSearchParams({ recentLimit: String(recentLimit) })
    return apiRequest<DashboardResponse>(`/dashboard?${parameters.toString()}`, { signal })
  },

  getHistory(
    query: BookingHistoryQuery,
    signal?: AbortSignal,
  ): Promise<PagedBookingHistory> {
    return apiRequest<PagedBookingHistory>(buildBookingHistoryPath(query), { signal })
  },
}

export function buildBookingHistoryPath(query: BookingHistoryQuery): string {
  const parameters = new URLSearchParams()
  append(parameters, 'search', query.search?.trim())
  append(parameters, 'status', query.status)
  append(parameters, 'stationId', query.stationId)
  append(parameters, 'fromUtc', toUtcBoundary(query.fromDate, false))
  append(parameters, 'toUtc', toUtcBoundary(query.toDate, true))
  append(parameters, 'page', query.page)
  append(parameters, 'pageSize', query.pageSize)
  return `/dashboard/history?${parameters.toString()}`
}

function toUtcBoundary(value: string | undefined, inclusiveEnd: boolean): string | undefined {
  if (!value) {
    return undefined
  }

  return `${value}T${inclusiveEnd ? '23:59:59.999' : '00:00:00.000'}Z`
}

function append(
  parameters: URLSearchParams,
  key: string,
  value: string | number | undefined,
): void {
  if (value !== undefined && value !== '') {
    parameters.set(key, String(value))
  }
}
