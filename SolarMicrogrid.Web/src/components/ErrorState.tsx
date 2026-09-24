interface ErrorStateProps {
  title?: string
  message: string
  onRetry?: () => void
}

export function ErrorState({
  title = 'Something went wrong',
  message,
  onRetry,
}: ErrorStateProps) {
  return (
    <section className="state-panel text-center" role="alert" aria-labelledby="error-state-title">
      <div className="state-icon state-icon-error" aria-hidden="true">!</div>
      <h2 id="error-state-title" className="h5 mb-2">{title}</h2>
      <p className="text-body-secondary mb-3">{message}</p>
      {onRetry && (
        <button type="button" className="btn btn-outline-success" onClick={onRetry}>
          Try again
        </button>
      )}
    </section>
  )
}
