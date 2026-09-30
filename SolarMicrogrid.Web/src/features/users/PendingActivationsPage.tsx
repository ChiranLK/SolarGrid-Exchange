import { useEffect, useState } from 'react'
import { userApi } from '../../api/userApi'
import { ApiErrorState } from '../../components/ApiErrorState'
import { ConfirmationDialog } from '../../components/ConfirmationDialog'
import { EmptyState } from '../../components/EmptyState'
import { LoadingState } from '../../components/LoadingState'
import { describeConfirmation, type AccountAction } from './accountActions'
import { FeedbackAlert } from './FeedbackAlert'
import { useAccountAdministration } from './useAccountAdministration'
import { useUserAdmin } from './userAdminContext'
import { formatRole, formatUtcDateTime } from './userAdminModel'
import type { UserAccount } from './userTypes'

interface PendingActivationsStateProps {
  loading: boolean
  error: unknown
  users: UserAccount[] | null
  onRetry: () => void
  onActivate: (action: AccountAction) => void
}

/** Pending-activation table with loading/empty/error states; exported for rendering tests. */
export function PendingActivationsState({ loading, error, users, onRetry, onActivate }: PendingActivationsStateProps) {
  if (loading) return <LoadingState label="Loading pending accounts…" />
  if (error) return <ApiErrorState error={error} resourceName="pending accounts" onRetry={onRetry} />
  if (!users || users.length === 0) {
    return <EmptyState title="No accounts awaiting activation" description="New Prosumer registrations will appear here for approval." />
  }

  return (
    <>
    <div className="card border-0 shadow-sm d-none d-lg-block">
      <div className="table-responsive">
        <table className="table table-hover align-middle mb-0 reservation-table">
          <caption className="visually-hidden">Accounts awaiting activation</caption>
          <thead>
            <tr>
              <th scope="col">NIC</th>
              <th scope="col">Name</th>
              <th scope="col">Contact</th>
              <th scope="col">Role</th>
              <th scope="col">Registered</th>
              <th scope="col"><span className="visually-hidden">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            {users.map((user) => (
              <tr key={user.nic}>
                <td className="text-nowrap">{user.nic}</td>
                <td>{user.fullName}</td>
                <td>
                  <div>{user.email}</div>
                  <div className="small text-body-secondary">{user.phone}</div>
                </td>
                <td>{formatRole(user.role)}</td>
                <td className="text-nowrap">{formatUtcDateTime(user.createdAtUtc)}</td>
                <td className="text-end">
                  <button type="button" className="btn btn-sm btn-success" aria-label={`Activate ${user.fullName}`}
                    onClick={() => onActivate({ kind: 'activate', nic: user.nic, fullName: user.fullName })}>
                    Activate
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
    <div className="sg-mobile-list d-grid gap-3 d-lg-none">
      {users.map((user) => (
        <article className="sg-mobile-card" key={user.nic}>
          <div className="d-flex justify-content-between align-items-start gap-3 mb-3">
            <div className="min-w-0">
              <h2 className="h6 mb-1 text-break">{user.fullName}</h2>
              <div className="small text-body-secondary font-monospace text-break">{user.nic}</div>
            </div>
            <span className="badge text-bg-warning">Pending activation</span>
          </div>
          <dl className="sg-mobile-facts mb-3">
            <div><dt>Role</dt><dd>{formatRole(user.role)}</dd></div>
            <div><dt>Registered</dt><dd>{formatUtcDateTime(user.createdAtUtc)}</dd></div>
            <div><dt>Email</dt><dd className="text-break">{user.email}</dd></div>
            <div><dt>Phone</dt><dd>{user.phone}</dd></div>
          </dl>
          <button type="button" className="btn btn-sm btn-success w-100" aria-label={`Activate ${user.fullName}`}
            onClick={() => onActivate({ kind: 'activate', nic: user.nic, fullName: user.fullName })}>
            Activate
          </button>
        </article>
      ))}
    </div>
    </>
  )
}

/** Backoffice approval queue for newly registered Prosumers (GET /api/users/pending). */
export function PendingActivationsPage() {
  const { version, notifyChanged } = useUserAdmin()
  const [users, setUsers] = useState<UserAccount[] | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [loading, setLoading] = useState(true)
  const [retry, setRetry] = useState(0)
  const administration = useAccountAdministration(notifyChanged)

  useEffect(() => {
    document.title = 'Pending activation | SolarGrid Exchange'
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    userApi.listPending(controller.signal)
      .then((response) => { setUsers(response); setError(null) })
      .catch((requestError: unknown) => { if (!controller.signal.aborted) setError(requestError) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [retry, version])

  const pending = administration.pendingAction

  return (
    <>
      <FeedbackAlert feedback={administration.feedback} onDismiss={administration.dismissFeedback} />
      <PendingActivationsState
        loading={loading}
        error={error}
        users={users}
        onRetry={() => { setLoading(true); setRetry((value) => value + 1) }}
        onActivate={administration.requestAction}
      />
      {pending && (
        <ConfirmationDialog
          isOpen
          {...describeConfirmation(pending)}
          isBusy={administration.isBusy}
          onConfirm={() => { void administration.confirmAction() }}
          onCancel={administration.cancelAction}
        />
      )}
    </>
  )
}
