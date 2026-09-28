import { useCallback, useState } from 'react'
import { performAccountAction, type AccountAction } from './accountActions'
import { describeActionError, type ActionFeedback } from './userAdminModel'

/**
 * Confirmation, busy and feedback state for account actions. The pending action is only sent
 * after the user confirms; on success every admin view is refreshed through onChanged.
 */
export function useAccountAdministration(onChanged: () => void) {
  const [pendingAction, setPendingAction] = useState<AccountAction | null>(null)
  const [isBusy, setIsBusy] = useState(false)
  const [feedback, setFeedback] = useState<ActionFeedback | null>(null)

  const requestAction = useCallback((action: AccountAction) => {
    setFeedback(null)
    setPendingAction(action)
  }, [])

  const cancelAction = useCallback(() => {
    if (!isBusy) setPendingAction(null)
  }, [isBusy])

  const confirmAction = useCallback(async (stationId?: string) => {
    if (!pendingAction || isBusy) return
    setIsBusy(true)
    try {
      const action = stationId ? { ...pendingAction, stationId } : pendingAction
      const result = await performAccountAction(action, [onChanged])
      setFeedback({ tone: 'success', message: result.message })
    } catch (error) {
      setFeedback(describeActionError(error))
    } finally {
      setIsBusy(false)
      setPendingAction(null)
    }
  }, [isBusy, onChanged, pendingAction])

  return {
    pendingAction,
    isBusy,
    feedback,
    requestAction,
    cancelAction,
    confirmAction,
    dismissFeedback: () => setFeedback(null),
  }
}
