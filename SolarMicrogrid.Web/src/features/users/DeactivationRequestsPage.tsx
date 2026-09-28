import { useEffect, useState } from 'react'
import { userApi } from '../../api/userApi'
import { ApiErrorState } from '../../components/ApiErrorState'
import { ConfirmationDialog } from '../../components/ConfirmationDialog'
import { EmptyState } from '../../components/EmptyState'
import { LoadingState } from '../../components/LoadingState'
import { StatusBadge } from '../../components/StatusBadge'
import { describeConfirmation, type AccountAction } from './accountActions'
import { FeedbackAlert } from './FeedbackAlert'
import { useAccountAdministration } from './useAccountAdministration'
import { useUserAdmin } from './userAdminContext'
import { formatUtcDateTime } from './userAdminModel'
import type { DeactivationRequest } from './userTypes'

interface DeactivationRequestsStateProps {
  loading: boolean
  error: unknown
  requests: DeactivationRequest[] | null
  onRetry: () => void
  onApprove: (action: AccountAction) => void
}

/** Deactivation-request table with loading/empty/error states; exported for rendering tests. */
export function DeactivationRequestsState({ loading, error, requests, onRetry, onApprove }: DeactivationRequestsStateProps) {
  if (loading) return <LoadingState label="Loading deactivation requests…" />
  if (error) return <ApiErrorState error={error} resourceName="deactivation requests" onRetry={onRetry} />
  if (!requests || requests.length === 0) {
    return <EmptyState title="No deactivation requests" description="Requests Prosumers make from the mobile app will appear here, oldest first." />
  }

  return (
    <div className="card border-0 shadow-sm">
      <div className="table-responsive">
        <table className="table table-hover align-middle mb-0 reservation-table">
          <caption className="visually-hidden">Prosumer deactivation requests, oldest first</caption>
          <thead>
            <tr>
              <th scope="col">NIC</th>
              <th scope="col">Name</th>
              <th scope="col">Contact</th>
              <th scope="col">Status</th>
              <th scope="col">Requested</th>
              <th scope="col"><span className="visually-hidden">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            {requests.map((request) => (
              <tr key={request.nic}>
                <td className="text-nowrap">{request.nic}</td>
                <td>{request.fullName}</td>
                <td>
                  <div>{request.email}</div>
                  <div className="small text-body-secondary">{request.phone}</div>
                </td>
                <td><StatusBadge status={request.status} /></td>
                <td className="text-nowrap">
                  {request.deactivationRequestedAtUtc
                    ? <time dateTime={request.deactivationRequestedAtUtc}>{formatUtcDateTime(request.deactivationRequestedAtUtc)}</time>
                    : 'Not recorded'}
                </td>
                <td className="text-end">
                  <button type="button" className="btn btn-sm btn-outline-danger" aria-label={`Approve deactivation for ${request.fullName}`}
                    onClick={() => onApprove({ kind: 'approveDeactivation', nic: request.nic, fullName: request.fullName })}>
                    Approve deactivation
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="small text-body-secondary px-3 py-2 mb-0">
        The API does not currently support rejecting a request; the account stays active until a request is approved.
      </p>
    </div>
  )
}

/** Backoffice review of Prosumer deactivation requests (GET /api/users/deactivation-requests). */
export function DeactivationRequestsPage() {
  const { version, notifyChanged } = useUserAdmin()
  const [requests, setRequests] = useState<DeactivationRequest[] | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [loading, setLoading] = useState(true)
  const [retry, setRetry] = useState(0)
  const administration = useAccountAdministration(notifyChanged)

  useEffect(() => {
    document.title = 'Deactivation requests | SolarGrid Exchange'
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    userApi.listDeactivationRequests(controller.signal)
      .then((response) => { setRequests(response); setError(null) })
      .catch((requestError: unknown) => { if (!controller.signal.aborted) setError(requestError) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [retry, version])

  const pending = administration.pendingAction

  return (
    <>
      <FeedbackAlert feedback={administration.feedback} onDismiss={administration.dismissFeedback} />
      <DeactivationRequestsState
        loading={loading}
        error={error}
        requests={requests}
        onRetry={() => { setLoading(true); setRetry((value) => value + 1) }}
        onApprove={administration.requestAction}
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
