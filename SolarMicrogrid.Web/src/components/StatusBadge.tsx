interface StatusBadgeProps {
  status: string
}

const statusClasses: Record<string, string> = {
  Active: 'text-bg-success',
  Available: 'text-bg-success',
  Approved: 'text-bg-success',
  Completed: 'text-bg-primary',
  Pending: 'text-bg-warning',
  PendingActivation: 'text-bg-warning',
  Cancelled: 'text-bg-secondary',
  Deactivated: 'text-bg-secondary',
  FullyBooked: 'text-bg-dark',
  Rejected: 'text-bg-danger',
  Unavailable: 'text-bg-danger',
}

export function StatusBadge({ status }: StatusBadgeProps) {
  const badgeClass = statusClasses[status] ?? 'text-bg-secondary'
  const label = status.replace(/([a-z])([A-Z])/g, '$1 $2')

  return (
    <span className={`badge rounded-pill ${badgeClass}`} aria-label={`Status: ${label}`}>
      {label}
    </span>
  )
}
