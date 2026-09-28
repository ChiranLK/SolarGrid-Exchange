import { useEffect } from 'react'
import { useAuth } from '../auth/useAuth'
import { PageHeader } from '../components/PageHeader'

/** Safe landing page for Prosumers: their features are delivered by the native Android app. */
export function ProsumerWebNoticePage() {
  const { session, logout } = useAuth()

  useEffect(() => {
    document.title = 'Use the mobile app | SolarGrid Exchange'
  }, [])

  return (
    <>
      <PageHeader eyebrow="Prosumer account" title={`Hello, ${session?.fullName ?? 'Prosumer'}`} />
      <section className="card border-0 shadow-sm" aria-labelledby="prosumer-mobile-title">
        <div className="card-body p-4 p-lg-5">
          <h2 id="prosumer-mobile-title" className="h4">Use the SolarGrid mobile app</h2>
          <p className="text-body-secondary">
            Prosumer registration, profile, reservations and account requests are available in the SolarGrid
            Android app. The web workspace is for Backoffice and Grid Operator staff.
          </p>
          <button type="button" className="btn btn-outline-success" onClick={logout}>Sign out</button>
        </div>
      </section>
    </>
  )
}
