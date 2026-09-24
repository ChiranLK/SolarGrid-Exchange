import { Link } from 'react-router-dom'
import type { ReservationAllowedActions } from './reservationTypes'

interface ReservationActionLinksProps {
  reservationId: string
  allowedActions: ReservationAllowedActions
  returnTo: string
  compact?: boolean
}

export function ReservationActionLinks({
  reservationId,
  allowedActions,
  returnTo,
  compact = false,
}: ReservationActionLinksProps) {
  const baseClass = compact ? 'btn btn-sm' : 'btn'
  const detailPath = buildDetailPath(reservationId, returnTo)

  return (
    <div className="d-flex flex-wrap gap-2" aria-label="Available reservation actions">
      <Link className={`${baseClass} btn-outline-secondary`} to={detailPath}>View</Link>
      {allowedActions.canUpdate && (
        <Link className={`${baseClass} btn-outline-success`} to={buildDetailPath(reservationId, returnTo, 'update')}>
          Update
        </Link>
      )}
      {allowedActions.canCancel && (
        <Link className={`${baseClass} btn-outline-danger`} to={buildDetailPath(reservationId, returnTo, 'cancel')}>
          Cancel
        </Link>
      )}
      {allowedActions.canApprove && (
        <Link className={`${baseClass} btn-success`} to={buildDetailPath(reservationId, returnTo, 'approve')}>
          Approve
        </Link>
      )}
      {allowedActions.canReject && (
        <Link className={`${baseClass} btn-danger`} to={buildDetailPath(reservationId, returnTo, 'reject')}>
          Reject
        </Link>
      )}
    </div>
  )
}

function buildDetailPath(
  reservationId: string,
  returnTo: string,
  action?: string,
): string {
  const parameters = new URLSearchParams({ returnTo })
  if (action) {
    parameters.set('action', action)
  }

  return `/reservations/${encodeURIComponent(reservationId)}?${parameters.toString()}`
}
