interface StatusBadgeProps {
  status: string
}

type Tone = 'success' | 'info' | 'warning' | 'neutral' | 'dark' | 'danger'

const statusTones: Record<string, Tone> = {
  Active: 'success',
  Available: 'success',
  Approved: 'success',
  Completed: 'info',
  Pending: 'warning',
  PendingActivation: 'warning',
  Cancelled: 'neutral',
  Deactivated: 'neutral',
  FullyBooked: 'dark',
  Rejected: 'danger',
  Unavailable: 'danger',
}

export function StatusBadge({ status }: StatusBadgeProps) {
  const tone = statusTones[status] ?? 'neutral'
  const label = status.replace(/([a-z])([A-Z])/g, '$1 $2')

  return (
    <span className={`badge rounded-pill sg-status sg-status-${tone}`} aria-label={`Status: ${label}`}>
      {label}
    </span>
  )
}
