import type { ReservationDetail } from './reservationTypes'

export const reservationChangedEvent = 'solargrid:reservation-changed'

const storageKey = 'solargrid.reservation-change'

export interface ReservationChangeNotice {
  reservationId: string
  status: string
  version: number
  changedAt: number
}

export function announceReservationChange(reservation: ReservationDetail): void {
  const notice: ReservationChangeNotice = {
    reservationId: reservation.id,
    status: reservation.status,
    version: reservation.version,
    changedAt: Date.now(),
  }

  window.dispatchEvent(new CustomEvent<ReservationChangeNotice>(reservationChangedEvent, {
    detail: notice,
  }))

  try {
    localStorage.setItem(storageKey, JSON.stringify(notice))
  } catch {
    // Storage may be unavailable; the same-tab event and server polling still refresh data.
  }
}

export function subscribeToReservationChanges(
  listener: (notice: ReservationChangeNotice | null) => void,
): () => void {
  const handleLocalChange = (event: Event) => {
    listener((event as CustomEvent<ReservationChangeNotice>).detail ?? null)
  }
  const handleStorageChange = (event: StorageEvent) => {
    if (event.key !== storageKey) {
      return
    }

    try {
      listener(event.newValue ? JSON.parse(event.newValue) as ReservationChangeNotice : null)
    } catch {
      listener(null)
    }
  }

  window.addEventListener(reservationChangedEvent, handleLocalChange)
  window.addEventListener('storage', handleStorageChange)
  return () => {
    window.removeEventListener(reservationChangedEvent, handleLocalChange)
    window.removeEventListener('storage', handleStorageChange)
  }
}
