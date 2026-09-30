import { Link } from 'react-router-dom'
import { CompassIcon } from '../components/icons'

export function NotFoundPage() {
  return (
    <section className="state-panel text-center" role="alert">
      <div className="state-icon" aria-hidden="true"><CompassIcon width={28} height={28} /></div>
      <div className="display-5 fw-semibold text-success mb-2">404</div>
      <h1 className="h4">Page not found</h1>
      <p className="text-body-secondary">The page may have moved or the address may be incorrect.</p>
      <Link to="/" className="btn btn-success">Return home</Link>
    </section>
  )
}
