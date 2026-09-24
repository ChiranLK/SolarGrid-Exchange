import { Link } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { PageHeader } from '../components/PageHeader'
import { StatusBadge } from '../components/StatusBadge'

export function HomePage() {
  const { session } = useAuth()

  return (
    <>
      <PageHeader
        eyebrow="SolarGrid Exchange"
        title={`Welcome, ${session?.fullName ?? 'team member'}`}
        description="Use the shared navigation to access the features available to your current API role."
      />

      <div className="row g-4">
        <div className="col-12 col-xl-8">
          <section className="card border-0 shadow-sm h-100">
            <div className="card-body p-4 p-lg-5">
              <p className="page-eyebrow">Shared web foundation</p>
              <h2 className="h4">Centralized, API-backed access</h2>
              <p className="text-body-secondary">
                Authentication, session restoration, protected routes, and role-aware navigation all use the existing ASP.NET Core API contract.
              </p>
              <Link to="/dashboard" className="btn btn-success">Open dashboard</Link>
            </div>
          </section>
        </div>
        <div className="col-12 col-xl-4">
          <section className="card border-0 shadow-sm h-100" aria-labelledby="account-summary-title">
            <div className="card-body p-4">
              <h2 id="account-summary-title" className="h5 mb-3">Account summary</h2>
              <dl className="mb-0 account-summary">
                <dt>NIC</dt>
                <dd>{session?.nic}</dd>
                <dt>Role</dt>
                <dd>{session?.role}</dd>
                <dt>Status</dt>
                <dd><StatusBadge status={session?.status ?? 'Unknown'} /></dd>
              </dl>
            </div>
          </section>
        </div>
      </div>
    </>
  )
}
