import { useEffect } from 'react'
import { subscribeToReservationChanges } from '../reservations/reservationSync'

export function useOperatorRefresh(refresh: () => void): void {
  useEffect(() => {
    const refreshWhenVisible = () => {
      if (document.visibilityState === 'visible') {
        refresh()
      }
    }
    const interval = window.setInterval(refreshWhenVisible, 30_000)
    const unsubscribe = subscribeToReservationChanges(refresh)

    window.addEventListener('focus', refresh)
    document.addEventListener('visibilitychange', refreshWhenVisible)
    return () => {
      window.clearInterval(interval)
      window.removeEventListener('focus', refresh)
      document.removeEventListener('visibilitychange', refreshWhenVisible)
      unsubscribe()
    }
  }, [refresh])
}
