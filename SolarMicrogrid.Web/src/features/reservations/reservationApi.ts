import { apiRequest } from '../../api/apiClient'
import type {
  CreateReservationRequest,
  PagedReservations,
  ReservationDetail,
  ReservationListQuery,
  UpdateReservationRequest,
} from './reservationTypes'

export const reservationApi = {
  list(query: ReservationListQuery, signal?: AbortSignal): Promise<PagedReservations> {
    const parameters = new URLSearchParams()
    appendQuery(parameters, 'view', query.view)
    appendQuery(parameters, 'status', query.status)
    appendQuery(parameters, 'stationId', query.stationId)
    appendQuery(parameters, 'search', query.search)
    appendQuery(parameters, 'page', query.page)
    appendQuery(parameters, 'pageSize', query.pageSize)
    return apiRequest<PagedReservations>(`/reservations?${parameters.toString()}`, { signal })
  },

  getById(reservationId: string, signal?: AbortSignal): Promise<ReservationDetail> {
    return apiRequest<ReservationDetail>(`/reservations/${encodeURIComponent(reservationId)}`, { signal })
  },

  createOwn(request: CreateReservationRequest, idempotencyKey: string): Promise<ReservationDetail> {
    return apiRequest<ReservationDetail>('/reservations', {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify({
        slotId: request.slotId,
        requestedEnergyKwh: request.requestedEnergyKwh,
      }),
    })
  },

  createForProsumer(
    request: CreateReservationRequest,
    idempotencyKey: string,
  ): Promise<ReservationDetail> {
    return apiRequest<ReservationDetail>('/reservations/staff', {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify({
        targetProsumerNic: request.targetProsumerNic,
        slotId: request.slotId,
        requestedEnergyKwh: request.requestedEnergyKwh,
      }),
    })
  },

  update(reservationId: string, request: UpdateReservationRequest, idempotencyKey: string) {
    return apiRequest<ReservationDetail>(`/reservations/${encodeURIComponent(reservationId)}`, {
      method: 'PUT',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify(request),
    })
  },

  cancel(
    reservationId: string,
    expectedVersion: number,
    reason: string,
    idempotencyKey: string,
  ) {
    return apiRequest<ReservationDetail>(
      `/reservations/${encodeURIComponent(reservationId)}/cancel`,
      {
        method: 'POST',
        headers: { 'Idempotency-Key': idempotencyKey },
        body: JSON.stringify({ expectedVersion, reason: reason.trim() || null }),
      },
    )
  },

  approve(reservationId: string, expectedVersion: number, idempotencyKey: string) {
    return apiRequest<ReservationDetail>(
      `/reservations/${encodeURIComponent(reservationId)}/approve`,
      {
        method: 'POST',
        headers: { 'Idempotency-Key': idempotencyKey },
        body: JSON.stringify({ expectedVersion }),
      },
    )
  },

  reject(
    reservationId: string,
    expectedVersion: number,
    reason: string,
    idempotencyKey: string,
  ) {
    return apiRequest<ReservationDetail>(
      `/reservations/${encodeURIComponent(reservationId)}/reject`,
      {
        method: 'POST',
        headers: { 'Idempotency-Key': idempotencyKey },
        body: JSON.stringify({ expectedVersion, reason: reason.trim() }),
      },
    )
  },
}

export function createIdempotencyKey(action: string): string {
  return `${action}-${crypto.randomUUID()}`
}

function appendQuery(
  parameters: URLSearchParams,
  key: string,
  value: string | number | undefined,
): void {
  if (value !== undefined && value !== '') {
    parameters.set(key, String(value))
  }
}
