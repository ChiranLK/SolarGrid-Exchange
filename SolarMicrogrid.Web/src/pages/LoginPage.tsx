import { useEffect, useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation } from 'react-router-dom'
import { ApiError } from '../api/apiClient'
import { resolvePostLoginDestination } from '../auth/roleRouting'
import { useAuth } from '../auth/useAuth'
import { LoadingState } from '../components/LoadingState'
import { ArrowLeftIcon, ArrowRightIcon, SolarGridLogoIcon } from '../components/icons'
import { ThemeToggle } from '../components/ThemeToggle'

interface LoginLocationState {
  from?: {
    pathname?: string
    search?: string
  }
}

export function LoginPage() {
  const { isAuthenticated, isInitializing, login, session } = useAuth()
  const location = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  useEffect(() => {
    document.title = 'Sign in | SolarGrid Exchange'
  }, [])

  if (isInitializing) {
    return <LoadingState label="Restoring your session…" />
  }

  // Already signed in (or just signed in): go straight to the role's page, never back to /login.
  if (isAuthenticated && session) {
    const state = location.state as LoginLocationState | null
    return <Navigate to={resolvePostLoginDestination(session.role, state?.from)} replace />
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setIsSubmitting(true)

    try {
      // On success the session is set and the render above redirects to the role's page.
      await login({ email: email.trim(), password })
    } catch (caughtError) {
      setError(
        caughtError instanceof ApiError || caughtError instanceof Error
          ? caughtError.message
          : 'Sign in failed. Please try again.',
      )
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <main className="login-page">
      <section className="login-brand-panel" aria-label="SolarGrid Exchange introduction">
        <div className="login-brand-content">
          <Link to="/" className="login-brand-link" aria-label="SolarGrid Exchange home">
            <span className="brand-mark brand-mark-large" aria-hidden="true"><SolarGridLogoIcon /></span>
            <span>SolarGrid Exchange</span>
          </Link>
          <p className="page-eyebrow text-white-50 mt-5">Local energy. Shared possibility.</p>
          <h1 className="display-5 fw-semibold">Share clean energy with confidence.</h1>
          <p className="lead text-white-50 mb-0">
            Book solar energy from stations near you, run each site safely and look after the whole network, all from one place.
          </p>
          <div className="login-facts" aria-hidden="true">
            <div><strong>7 days</strong><span>Book ahead</span></div>
            <div><strong>QR pass</strong><span>Checked on site</span></div>
            <div><strong>25 km</strong><span>Stations near you</span></div>
          </div>
        </div>
      </section>

      <section className="login-form-panel" aria-labelledby="login-heading">
        <Link to="/" className="login-back"><ArrowLeftIcon width={16} height={16} /> Back to home</Link>
        <ThemeToggle className="login-theme" />
        <div className="login-card">
          <Link to="/" className="d-lg-none d-flex align-items-center gap-2 mb-4 login-mobile-brand">
            <span className="brand-mark" aria-hidden="true"><SolarGridLogoIcon /></span>
            <span className="fw-semibold">SolarGrid Exchange</span>
          </Link>
          <p className="page-eyebrow">Welcome back</p>
          <h2 id="login-heading" className="h2 mb-2">Sign in to your account</h2>
          <p className="text-body-secondary mb-4">
            Enter the email and password for your SolarGrid account.
          </p>

          {error && (
            <div className="alert alert-danger" role="alert" aria-live="assertive">
              {error}
            </div>
          )}

          <form onSubmit={handleSubmit} noValidate>
            <div className="mb-3">
              <label htmlFor="email" className="form-label">Email address</label>
              <input
                id="email"
                name="email"
                type="email"
                className="form-control form-control-lg"
                autoComplete="email"
                required
                value={email}
                onChange={(event) => setEmail(event.target.value)}
              />
            </div>
            <div className="mb-4">
              <label htmlFor="password" className="form-label">Password</label>
              <input
                id="password"
                name="password"
                type="password"
                className="form-control form-control-lg"
                autoComplete="current-password"
                required
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            </div>
            <button
              type="submit"
              className="btn btn-success btn-lg w-100"
              disabled={isSubmitting || !email.trim() || !password}
            >
              {isSubmitting ? 'Signing in…' : 'Sign in'}
              {!isSubmitting && <ArrowRightIcon width={18} height={18} />}
            </button>
          </form>
          <p className="small text-body-secondary mt-4 mb-0">
            New here? Prosumers register in the SolarGrid Android app. <Link to="/contact">Contact us</Link> for help.
          </p>
        </div>
      </section>
    </main>
  )
}
