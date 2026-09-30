import { Link } from 'react-router-dom'
import { getHomePathForRole } from '../auth/roleRouting'
import { useAuth } from '../auth/useAuth'
import { PageHeader } from '../components/PageHeader'
import { StatusBadge } from '../components/StatusBadge'
import { ArrowRightIcon, ShieldIcon } from '../components/icons'

export function HomePage() {
  const { session } = useAuth()

  return (
    <>
      <PageHeader
        eyebrow="SolarGrid Exchange"
        title={`Welcome, ${session?.fullName ?? 'team member'}`}
        description="Use the menu to open the tools available to your account."
      />

      <div className="row g-4">
        <div className="col-12 col-xl-8">
          <section className="card border-0 shadow-sm h-100 home-foundation-card">
            <div className="card-body p-4 p-lg-5">
              <p className="page-eyebrow">Your workspace</p>
              <h2 className="h4">Everything for your role, in one place</h2>
              <p className="text-body-secondary">
                Stations, bookings and member accounts are a click away. Only the tools that match your role are shown.
              </p>
              {session && <Link to={getHomePathForRole(session.role)} className="btn btn-success">Open your workspace <ArrowRightIcon width={18} height={18} /></Link>}
            </div>
          </section>
        </div>
        <div className="col-12 col-xl-4">
          <section className="card border-0 shadow-sm h-100 account-summary-card" aria-labelledby="account-summary-title">
            <div className="card-body p-4">
              <div className="d-flex justify-content-between align-items-center gap-3 mb-3">
                <h2 id="account-summary-title" className="h5 mb-0">Account summary</h2>
                <span className="account-summary-icon" aria-hidden="true"><ShieldIcon /></span>
              </div>
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
