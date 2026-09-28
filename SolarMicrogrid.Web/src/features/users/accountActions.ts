import { userApi } from '../../api/userApi'
import type { UserAccount } from './userTypes'

export type AccountActionKind = 'activate' | 'reactivate' | 'deactivate' | 'approveDeactivation' | 'assignStation'

export interface AccountAction {
  kind: AccountActionKind
  nic: string
  fullName: string
  stationId?: string
}

export interface ConfirmationCopy {
  title: string
  message: string
  confirmLabel: string
  destructive: boolean
}

/** Text for the confirmation dialog shown before a state-changing request. */
export function describeConfirmation(action: AccountAction): ConfirmationCopy {
  switch (action.kind) {
    case 'activate':
      return {
        title: 'Activate account?',
        message: `${action.fullName} (${action.nic}) will be able to sign in and use SolarGrid.`,
        confirmLabel: 'Activate',
        destructive: false,
      }
    case 'reactivate':
      return {
        title: 'Reactivate account?',
        message: `${action.fullName} (${action.nic}) will be able to sign in again.`,
        confirmLabel: 'Reactivate',
        destructive: false,
      }
    case 'deactivate':
      return {
        title: 'Deactivate account?',
        message: `${action.fullName} (${action.nic}) will be signed out of every protected function and cannot sign in until reactivated.`,
        confirmLabel: 'Deactivate',
        destructive: true,
      }
    case 'approveDeactivation':
      return {
        title: 'Approve deactivation request?',
        message: `${action.fullName} (${action.nic}) asked to close their account. Approving deactivates it now.`,
        confirmLabel: 'Approve and deactivate',
        destructive: true,
      }
    case 'assignStation':
      return {
        title: 'Assign station?',
        message: `${action.fullName} (${action.nic}) will operate the selected station.`,
        confirmLabel: 'Assign station',
        destructive: false,
      }
  }
}

function successMessage(action: AccountAction, user: UserAccount): string {
  switch (action.kind) {
    case 'activate':
      return `${user.fullName} is now active.`
    case 'reactivate':
      return `${user.fullName} has been reactivated.`
    case 'deactivate':
      return `${user.fullName} has been deactivated.`
    case 'approveDeactivation':
      return `Deactivation approved. ${user.fullName} has been deactivated.`
    case 'assignStation':
      return `${user.fullName} has been assigned to the selected station.`
  }
}

/**
 * Sends one account-administration request, then refreshes every affected view.
 * Refresh callbacks run only after the API confirms success.
 */
export async function performAccountAction(
  action: AccountAction,
  refresh: ReadonlyArray<() => void>,
): Promise<{ user: UserAccount; message: string }> {
  let user: UserAccount
  switch (action.kind) {
    case 'activate':
    case 'reactivate':
      user = await userApi.activate(action.nic)
      break
    case 'deactivate':
    case 'approveDeactivation':
      user = await userApi.deactivate(action.nic)
      break
    case 'assignStation':
      if (!action.stationId) throw new Error('Choose a station first.')
      user = await userApi.assignStation(action.nic, action.stationId)
      break
  }

  refresh.forEach((callback) => callback())
  return { user, message: successMessage(action, user) }
}
