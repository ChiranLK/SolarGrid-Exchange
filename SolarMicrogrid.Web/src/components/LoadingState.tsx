interface LoadingStateProps {
  label?: string
  compact?: boolean
}

export function LoadingState({
  label = 'Loading…',
  compact = false,
}: LoadingStateProps) {
  return (
    <div
      className={`d-flex align-items-center justify-content-center gap-3 ${compact ? 'py-3' : 'state-panel'}`}
      role="status"
      aria-live="polite"
    >
      <span className="spinner-border spinner-border-sm text-success" aria-hidden="true" />
      <span>{label}</span>
    </div>
  )
}
