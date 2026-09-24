import { Link } from 'react-router-dom'
import { ErrorState } from '../components/ErrorState'

export function ForbiddenPage() {
  return (
    <ErrorState
      title="Access denied"
      message="Your current API role does not allow access to this page."
      onRetry={undefined}
    />
  )
}

export function ForbiddenPageWithNavigation() {
  return (
    <div>
      <ForbiddenPage />
      <div className="text-center mt-3">
        <Link to="/" className="btn btn-success">Return home</Link>
      </div>
    </div>
  )
}
