import { useEffect, useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { ApiError } from '../api/apiClient'
import { useAuth } from '../auth/useAuth'
import { LoadingState } from '../components/LoadingState'

interface LoginLocationState {
  from?: {
    pathname?: string
    search?: string
  }
}

export function LoginPage() {
  const { isAuthenticated, isInitializing, login } = useAuth()
  const location = useLocation()
  const navigate = useNavigate()
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

  if (isAuthenticated) {
    return <Navigate to="/dashboard" replace />
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setIsSubmitting(true)

    try {
      await login({ email: email.trim(), password })
      const state = location.state as LoginLocationState | null
      const destination = state?.from?.pathname
        ? `${state.from.pathname}${state.from.search ?? ''}`
        : '/dashboard'
      navigate(destination, { replace: true })
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
          <span className="brand-mark brand-mark-large" aria-hidden="true">SG</span>
          <p className="page-eyebrow text-white-50 mt-4">Smart microgrid operations</p>
          <h1 className="display-5 fw-semibold">Share clean energy with confidence.</h1>
          <p className="lead text-white-50 mb-0">
            One secure workspace for Prosumers, Grid Operators, and Backoffice teams.
          </p>
        </div>
      </section>

      <section className="login-form-panel" aria-labelledby="login-heading">
        <div className="login-card">
          <div className="d-lg-none d-flex align-items-center gap-2 mb-4">
            <span className="brand-mark" aria-hidden="true">SG</span>
            <span className="fw-semibold">SolarGrid Exchange</span>
          </div>
          <p className="page-eyebrow">Welcome back</p>
          <h2 id="login-heading" className="h2 mb-2">Sign in to your account</h2>
          <p className="text-body-secondary mb-4">
            Use the credentials managed by the central SolarGrid API.
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
            </button>
          </form>
          <p className="small text-body-secondary mt-4 mb-0">
            New Prosumer registration is available in the API but is not part of this shared foundation yet.
          </p>
        </div>
      </section>
    </main>
  )
}
