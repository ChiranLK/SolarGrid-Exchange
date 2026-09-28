import { useOutletContext } from 'react-router-dom'

/** Shared by the account-administration screens so one change refreshes every view and count. */
export interface UserAdminContextValue {
  /** Increments after every successful account change; list screens reload when it changes. */
  version: number
  notifyChanged: () => void
}

export function useUserAdmin(): UserAdminContextValue {
  return useOutletContext<UserAdminContextValue>()
}
