import { BoltIcon, CheckIcon, ClockIcon, HistoryIcon } from '../../components/icons'
import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiErrorState } from '../../components/ApiErrorState'
import { EmptyState } from '../../components/EmptyState'
import { LoadingState } from '../../components/LoadingState'
import { PageHeader } from '../../components/PageHeader'
import { StatusBadge } from '../../components/StatusBadge'
import { formatDateTime } from '../reservations/reservationFormatters'
import { dashboardApi } from './dashboardApi'
import type { DashboardResponse } from './dashboardTypes'
import {
  classifyDashboardContent,
  nextRefreshToken,
  type DashboardContentState,
} from './operatorDashboardModel'
import { ReservationSummaryTable } from './ReservationSummaryTable'
import { useOperatorRefresh } from './useOperatorRefresh'

const recentLimit = 5

export function OperatorDashboardPage() {
  const [dashboard, setDashboard] = useState<DashboardResponse | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [reloadToken, setReloadToken] = useState(0)

  const refresh = useCallback(() => {
    setIsLoading(true)
    setError(null)
    setReloadToken(nextRefreshToken)
  }, [])
  useOperatorRefresh(refresh)

  useEffect(() => {
    document.title = 'Operator dashboard | SolarGrid Exchange'
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    dashboardApi.getDashboard(recentLimit, controller.signal)
      .then(setDashboard)
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) {
          setError(requestError)
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setIsLoading(false)
        }
      })
    return () => controller.abort()
  }, [reloadToken])

  const contentState = classifyDashboardContent(isLoading, error, dashboard)

  return (
    <>
      <PageHeader
        eyebrow="Grid operations"
        title="Operator dashboard"
        description="Live counts and activity for your assigned station."
        actions={(
          <>
            <Link className="btn btn-outline-success" to="/operator/history">
              Booking history
            </Link>
            <button type="button" className="btn btn-success" onClick={refresh} disabled={isLoading}>
              Refresh
            </button>
          </>
        )}
      />

      <OperatorDashboardState
        contentState={contentState}
        dashboard={dashboard}
        error={error}
        onRetry={refresh}
      />
    </>
  )
}

export function OperatorDashboardState({
  contentState,
  dashboard,
  error,
  onRetry,
}: {
  contentState: DashboardContentState
  dashboard: DashboardResponse | null
  error: unknown
  onRetry: () => void
}) {
  if (contentState === 'loading') {
    return <LoadingState label="Loading operator dashboard…" />
  }
  if (contentState === 'error') {
    return <ApiErrorState error={error} resourceName="dashboard" onRetry={onRetry} />
  }
  if (contentState === 'empty' && dashboard) {
    return (
      <>
        <DashboardMetrics dashboard={dashboard} />
        <EmptyState
          title="No operational reservations"
          description="There are no pending, scheduled, active, or historical transfers for your assigned station."
          action={<button type="button" className="btn btn-success" onClick={onRetry}>Refresh</button>}
        />
      </>
    )
  }
  if (contentState === 'ready' && dashboard) {
    return <DashboardContent dashboard={dashboard} />
  }
  return null
}

function DashboardContent({ dashboard }: { dashboard: DashboardResponse }) {
  return (
    <>
      <DashboardMetrics dashboard={dashboard} />

      <section className="card border-0 shadow-sm mb-4" aria-labelledby="pending-reservations-title">
        <div className="card-body p-3 p-lg-4">
          <div className="d-flex justify-content-between align-items-center gap-3 mb-3">
            <h2 id="pending-reservations-title" className="h5 mb-0">Pending reservations</h2>
            <span className="badge text-bg-warning rounded-pill">
              {dashboard.statusSummary.pendingCount}
            </span>
          </div>
          <ReservationSummaryTable
            items={dashboard.pendingReservations}
            returnTo="/operator/dashboard"
            emptyMessage="No pending reservations require attention."
          />
        </div>
      </section>

      <div className="row g-4 mb-4">
        <DashboardListSection
          id="active-transfers-title"
          title="Active transfers"
          items={dashboard.activeTransfers}
          emptyMessage="No transfers are active right now."
        />
        <DashboardListSection
          id="completed-transfers-title"
          title="Recently completed"
          items={dashboard.completedTransfers}
          emptyMessage="No completed transfers to show."
        />
      </div>

      <section className="card border-0 shadow-sm" aria-labelledby="recent-activity-title">
        <div className="card-body p-3 p-lg-4">
          <div className="d-flex flex-column flex-sm-row justify-content-between gap-2 mb-3">
            <h2 id="recent-activity-title" className="h5 mb-0">Recent activity</h2>
            <span className="small text-body-secondary">
              Updated {formatDateTime(dashboard.serverNowUtc)}
            </span>
          </div>
          <ReservationSummaryTable
            items={dashboard.recentTransfers}
            returnTo="/operator/dashboard"
            emptyMessage="No recent approved or completed transfer activity."
          />
        </div>
      </section>
    </>
  )
}

function DashboardMetrics({ dashboard }: { dashboard: DashboardResponse }) {
  const metrics = [
    { label: 'Pending', value: dashboard.statusSummary.pendingCount, tone: 'warning', Icon: ClockIcon },
    { label: 'Approved future', value: dashboard.statusSummary.approvedFutureCount, tone: 'success', Icon: CheckIcon },
    { label: 'Active now', value: dashboard.statusSummary.currentCount, tone: 'primary', Icon: BoltIcon },
    { label: 'Completed', value: dashboard.statusSummary.completedTotal, tone: 'secondary', Icon: HistoryIcon },
  ]
  const statuses = [
    ['Pending', dashboard.statusSummary.pendingTotal],
    ['Approved', dashboard.statusSummary.approvedTotal],
    ['Rejected', dashboard.statusSummary.rejectedTotal],
    ['Cancelled', dashboard.statusSummary.cancelledTotal],
    ['Completed', dashboard.statusSummary.completedTotal],
  ] as const

  return (
    <>
      <section className="row g-3 mb-4" aria-label="Live operational counts">
        {metrics.map((metric) => (
          <div key={metric.label} className="col-6 col-xl-3">
            <article className={`operator-metric-card operator-metric-${metric.tone}`}>
              <span className="operator-metric-icon" aria-hidden="true"><metric.Icon /></span>
              <div className="operator-metric-label">{metric.label}</div>
              <div className="operator-metric-value">{metric.value}</div>
            </article>
          </div>
        ))}
      </section>

      <section className="card border-0 shadow-sm mb-4" aria-labelledby="operational-status-title">
        <div className="card-body p-3 p-lg-4">
          <h2 id="operational-status-title" className="h5 mb-3">Operational status summary</h2>
          <div className="d-flex flex-wrap gap-3">
            {statuses.map(([status, count]) => (
              <div key={status} className="operator-status-total">
                <StatusBadge status={status} />
                <span className="fw-semibold">{count}</span>
              </div>
            ))}
          </div>
        </div>
      </section>
    </>
  )
}

function DashboardListSection({
  id,
  title,
  items,
  emptyMessage,
}: {
  id: string
  title: string
  items: DashboardResponse['activeTransfers']
  emptyMessage: string
}) {
  return (
    <section className="col-12 col-xl-6" aria-labelledby={id}>
      <div className="card border-0 shadow-sm h-100">
        <div className="card-body p-3 p-lg-4">
          <h2 id={id} className="h5 mb-3">{title}</h2>
          <ReservationSummaryTable
            items={items}
            returnTo="/operator/dashboard"
            emptyMessage={emptyMessage}
          />
        </div>
      </div>
    </section>
  )
}
