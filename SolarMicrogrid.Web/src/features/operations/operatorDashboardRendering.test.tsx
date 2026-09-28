import { renderToStaticMarkup } from 'react-dom/server'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/apiClient'
import { Pagination } from '../../components/Pagination'
import { OperatorDashboardState } from './OperatorDashboardPage'
import type {
  DashboardReservationSummary,
  DashboardResponse,
} from './dashboardTypes'

const reservation: DashboardReservationSummary = {
  reservationId: '000000000000000000000001',
  reference: 'RES-00000001',
  prosumerNic: 'NIC-REDACTED',
  prosumerFullName: 'Sanitized Prosumer',
  stationId: '000000000000000000000002',
  stationName: 'Test Solar Hub',
  stationAddress: 'Sanitized address',
  slotId: '000000000000000000000003',
  scheduledStartTimeUtc: '2030-01-15T10:00:00Z',
  scheduledEndTimeUtc: '2030-01-15T11:00:00Z',
  requestedEnergyKwh: 5,
  status: 'Pending',
  version: 1,
  createdAtUtc: '2030-01-14T10:00:00Z',
  updatedAtUtc: '2030-01-14T10:00:00Z',
  completedAtUtc: null,
}

const emptyDashboard: DashboardResponse = {
  serverNowUtc: '2030-01-15T09:00:00Z',
  role: 'GridOperator',
  scope: 'AssignedStation',
  stationId: reservation.stationId,
  statusSummary: {
    pendingTotal: 0,
    approvedTotal: 0,
    rejectedTotal: 0,
    cancelledTotal: 0,
    completedTotal: 0,
    currentCount: 0,
    pendingCount: 0,
    approvedFutureCount: 0,
    historyCount: 0,
  },
  currentReservations: [],
  pendingReservations: [],
  recentHistory: [],
  recentTransfers: [],
  activeTransfers: [],
  completedTransfers: [],
}

describe('Grid Operator dashboard rendering', () => {
  it('renders live API counts and reservation summaries', () => {
    const dashboard: DashboardResponse = {
      ...emptyDashboard,
      statusSummary: {
        ...emptyDashboard.statusSummary,
        pendingTotal: 1,
        pendingCount: 1,
        approvedTotal: 2,
        approvedFutureCount: 1,
        currentCount: 1,
        completedTotal: 3,
        historyCount: 3,
      },
      pendingReservations: [reservation],
      recentTransfers: [reservation],
    }

    const html = renderState('ready', dashboard, null)

    expect(html).toContain('Live operational counts')
    expect(html).toContain('Approved future')
    expect(html).toContain('RES-00000001')
    expect(html).toContain('Test Solar Hub')
    expect(html).toContain('5 kWh')
  })

  it('renders loading, empty, and retryable error states without fake success data', () => {
    const loading = renderState('loading', null, null)
    const empty = renderState('empty', emptyDashboard, null)
    const error = renderState(
      'error',
      null,
      new ApiError(0, 'Hosted API is unavailable.'),
    )

    expect(loading).toContain('Loading operator dashboard')
    expect(empty).toContain('No operational reservations')
    expect(empty).toContain('Refresh')
    expect(error).toContain('API unavailable')
    expect(error).toContain('Hosted API is unavailable.')
    expect(error).toContain('Try again')
  })

  it('renders authoritative pagination state and bounded controls', () => {
    const html = renderToStaticMarkup(
      <Pagination page={2} totalPages={3} totalCount={25} onPageChange={vi.fn()} />,
    )

    expect(html).toContain('Page 2 of 3')
    expect(html).toContain('25 results')
    expect(html).toContain('Previous')
    expect(html).toContain('Next')
  })
})

function renderState(
  contentState: 'loading' | 'error' | 'empty' | 'ready',
  dashboard: DashboardResponse | null,
  error: unknown,
): string {
  return renderToStaticMarkup(
    <MemoryRouter>
      <OperatorDashboardState
        contentState={contentState}
        dashboard={dashboard}
        error={error}
        onRetry={vi.fn()}
      />
    </MemoryRouter>,
  )
}
