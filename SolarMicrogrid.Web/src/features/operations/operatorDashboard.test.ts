import { describe, expect, it } from 'vitest'
import { ApiError } from '../../api/apiClient'
import { userRoles } from '../../auth/authTypes'
import { reservationStatuses } from '../reservations/reservationTypes'
import { buildBookingHistoryPath } from './dashboardApi'
import type { DashboardResponse, PagedBookingHistory } from './dashboardTypes'
import {
  canAccessOperatorScreens,
  classifyDashboardContent,
  classifyHistoryContent,
  nextRefreshToken,
  operatorRoutes,
  readHistoryFilters,
  validateHistoryDateRange,
} from './operatorDashboardModel'
import { getVisibleNavigation } from './operatorNavigation'

const emptyDashboard: DashboardResponse = {
  serverNowUtc: '2026-09-27T00:00:00Z',
  role: 'GridOperator',
  scope: 'AssignedStation',
  stationId: 'station-one',
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

describe('Grid Operator dashboard routing and states', () => {
  it('uses the exact shared role and reservation status labels', () => {
    expect(userRoles).toEqual(['Backoffice', 'GridOperator', 'Prosumer'])
    expect(reservationStatuses).toEqual([
      'Pending',
      'Approved',
      'Rejected',
      'Cancelled',
      'Completed',
    ])
  })

  it('allows only Grid Operators and hides operator navigation from Prosumers', () => {
    expect(canAccessOperatorScreens('GridOperator')).toBe(true)
    expect(canAccessOperatorScreens('Prosumer')).toBe(false)
    expect(canAccessOperatorScreens(null)).toBe(false)

    const operatorPaths = getVisibleNavigation('GridOperator').map((item) => item.path)
    const prosumerPaths = getVisibleNavigation('Prosumer').map((item) => item.path)
    expect(operatorPaths).toContain(`/${operatorRoutes.dashboard}`)
    expect(operatorPaths).toContain(`/${operatorRoutes.history}`)
    expect(prosumerPaths).not.toContain(`/${operatorRoutes.dashboard}`)
    expect(prosumerPaths).not.toContain(`/${operatorRoutes.history}`)
  })

  it('classifies loading, empty, successful, and error dashboards explicitly', () => {
    expect(classifyDashboardContent(true, null, null)).toBe('loading')
    expect(classifyDashboardContent(false, null, emptyDashboard)).toBe('empty')
    expect(classifyDashboardContent(false, null, {
      ...emptyDashboard,
      statusSummary: { ...emptyDashboard.statusSummary, pendingCount: 1 },
    })).toBe('ready')
    expect(classifyDashboardContent(
      false,
      new ApiError(503, 'Service unavailable'),
      null,
    )).toBe('error')
  })

  it('increments the request token for manual, focus, timer, or reservation refreshes', () => {
    expect(nextRefreshToken(0)).toBe(1)
    expect(nextRefreshToken(nextRefreshToken(4))).toBe(6)
  })
})

describe('Grid Operator booking-history filters', () => {
  it('builds trimmed, encoded, inclusive UTC query parameters and pagination', () => {
    const path = buildBookingHistoryPath({
      search: '  RES-A1B2C3D4  ',
      status: 'Completed',
      stationId: 'station one',
      fromDate: '2026-09-01',
      toDate: '2026-09-30',
      page: 3,
      pageSize: 10,
    })
    const parameters = new URLSearchParams(path.split('?')[1])

    expect(path.startsWith('/dashboard/history?')).toBe(true)
    expect(parameters.get('search')).toBe('RES-A1B2C3D4')
    expect(parameters.get('status')).toBe('Completed')
    expect(parameters.get('stationId')).toBe('station one')
    expect(parameters.get('fromUtc')).toBe('2026-09-01T00:00:00.000Z')
    expect(parameters.get('toUtc')).toBe('2026-09-30T23:59:59.999Z')
    expect(parameters.get('page')).toBe('3')
    expect(parameters.get('pageSize')).toBe('10')
  })

  it('normalizes invalid URL status/page values and preserves valid filters', () => {
    const filters = readHistoryFilters(new URLSearchParams({
      search: 'solar hub',
      status: 'Unknown',
      stationId: 'station-one',
      fromDate: '2026-09-01',
      toDate: '2026-09-30',
      page: '-4',
    }))

    expect(filters).toEqual({
      search: 'solar hub',
      status: '',
      stationId: 'station-one',
      fromDate: '2026-09-01',
      toDate: '2026-09-30',
      page: 1,
    })
  })

  it('rejects an inverted date range before making an API request', () => {
    expect(validateHistoryDateRange('2026-09-30', '2026-09-01'))
      .toBe('From date cannot be later than to date.')
    expect(validateHistoryDateRange('2026-09-01', '2026-09-30')).toBeNull()
    expect(validateHistoryDateRange('', '')).toBeNull()
  })

  it('classifies empty, successful, and failed paged results without reordering them', () => {
    const history: PagedBookingHistory = {
      serverNowUtc: '2026-09-27T00:00:00Z',
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 10,
      totalPages: 0,
    }

    expect(classifyHistoryContent(false, null, history)).toBe('empty')
    expect(classifyHistoryContent(false, null, {
      ...history,
      items: [{ reservationId: 'later' }, { reservationId: 'earlier' }] as PagedBookingHistory['items'],
    })).toBe('ready')
    expect(classifyHistoryContent(false, new ApiError(403, 'Denied'), null)).toBe('error')
  })
})
