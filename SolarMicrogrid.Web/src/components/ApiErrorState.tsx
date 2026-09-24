import { ApiError } from '../api/apiClient'
import { ErrorState } from './ErrorState'

interface ApiErrorStateProps {
  error: unknown
  onRetry?: () => void
  resourceName?: string
}

export function ApiErrorState({
  error,
  onRetry,
  resourceName = 'resource',
}: ApiErrorStateProps) {
  if (!(error instanceof ApiError)) {
    return <ErrorState message="An unexpected error occurred." onRetry={onRetry} />
  }

  if (error.status === 403) {
    return (
      <ErrorState
        title="Access denied"
        message={error.message}
        onRetry={onRetry}
      />
    )
  }

  if (error.status === 404) {
    return (
      <ErrorState
        title={`${capitalize(resourceName)} not found`}
        message={error.message}
      />
    )
  }

  if (error.status === 0) {
    return (
      <ErrorState
        title="API unavailable"
        message={error.message}
        onRetry={onRetry}
      />
    )
  }

  return <ErrorState message={error.message} onRetry={onRetry} />
}

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1)
}
