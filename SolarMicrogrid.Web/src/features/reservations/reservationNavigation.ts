export function getSafeReservationReturnTo(value: string | null): string {
  return value?.startsWith('/reservations') ? value : '/reservations'
}

export function buildReservationDetailPath(reservationId: string, returnTo: string): string {
  const parameters = new URLSearchParams({ returnTo })
  return `/reservations/${encodeURIComponent(reservationId)}?${parameters.toString()}`
}
