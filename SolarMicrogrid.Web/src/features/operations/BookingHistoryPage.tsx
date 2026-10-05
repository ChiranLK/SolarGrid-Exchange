import {
  useCallback,
  useEffect,
  useMemo,
  useState,
  type FormEvent,
} from 'react'
import { useLocation, useSearchParams } from 'react-router-dom'
import { stationApi } from '../../api/stationApi'
import { ApiErrorState } from '../../components/ApiErrorState'
import { EmptyState } from '../../components/EmptyState'
import { LoadingState } from '../../components/LoadingState'
import { PageHeader } from '../../components/PageHeader'
import { Pagination } from '../../components/Pagination'
import type { StationSummary } from '../stations/stationTypes'
import { reservationStatuses } from '../reservations/reservationTypes'
import { dashboardApi } from './dashboardApi'
import type { PagedBookingHistory } from './dashboardTypes'
import {
  classifyHistoryContent,
  nextRefreshToken,
  readHistoryFilters,
  validateHistoryDateRange,
} from './operatorDashboardModel'
import { ReservationSummaryTable } from './ReservationSummaryTable'
import { useOperatorRefresh } from './useOperatorRefresh'

const pageSize = 10

export function BookingHistoryPage() {
  const location = useLocation()
  const [searchParameters, setSearchParameters] = useSearchParams()
  const filters = useMemo(() => readHistoryFilters(searchParameters), [searchParameters])
  const [searchInput, setSearchInput] = useState(filters.search)
  const [fromDateInput, setFromDateInput] = useState(filters.fromDate)
  const [toDateInput, setToDateInput] = useState(filters.toDate)
  const [history, setHistory] = useState<PagedBookingHistory | null>(null)
  const [stations, setStations] = useState<StationSummary[]>([])
  const [stationError, setStationError] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [formValidationError, setFormValidationError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [reloadToken, setReloadToken] = useState(0)

  const refresh = useCallback(() => {
    setIsLoading(true)
    setError(null)
    setReloadToken(nextRefreshToken)
  }, [])
  useOperatorRefresh(refresh)

  useEffect(() => {
    document.title = 'Booking history | SolarGrid Exchange'
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    stationApi.listAll(controller.signal)
      .then((response) => {
        setStations(response.items)
        setStationError(false)
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setStationError(true)
        }
      })
    return () => controller.abort()
  }, [])

  useEffect(() => {
    const rangeError = validateHistoryDateRange(filters.fromDate, filters.toDate)
    if (rangeError) {
      return
    }

    const controller = new AbortController()
    dashboardApi.getHistory(
      {
        search: filters.search,
        status: filters.status,
        stationId: filters.stationId,
        fromDate: filters.fromDate,
        toDate: filters.toDate,
        page: filters.page,
        pageSize,
      },
      controller.signal,
    )
      .then(setHistory)
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
  }, [filters, reloadToken])

  const updateFilter = useCallback((key: string, value: string) => {
    setIsLoading(true)
    setError(null)
    setFormValidationError(null)
    setSearchParameters((current) => {
      const next = new URLSearchParams(current)
      if (value) {
        next.set(key, value)
      } else {
        next.delete(key)
      }
      if (key !== 'page') {
        next.delete('page')
      }
      return next
    })
  }, [setSearchParameters])

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const rangeError = validateHistoryDateRange(fromDateInput, toDateInput)
    setFormValidationError(rangeError)
    if (rangeError) {
      return
    }

    setIsLoading(true)
    setError(null)
    setSearchParameters((current) => {
      const next = new URLSearchParams(current)
      setOrDelete(next, 'search', searchInput.trim())
      setOrDelete(next, 'fromDate', fromDateInput)
      setOrDelete(next, 'toDate', toDateInput)
      next.delete('page')
      return next
    })
  }

  function clearFilters() {
    setSearchInput('')
    setFromDateInput('')
    setToDateInput('')
    setFormValidationError(null)
    setIsLoading(true)
    setError(null)
    setSearchParameters(new URLSearchParams())
  }

  const validationError = formValidationError ?? validateHistoryDateRange(
    filters.fromDate,
    filters.toDate,
  )
  const contentState = classifyHistoryContent(isLoading, error, history)
  const returnTo = `${location.pathname}${location.search}`

  return (
    <>
      <PageHeader
        eyebrow="Grid operations"
        title="Booking history"
        description="Search the booking history for your assigned station."
        actions={(
          <button type="button" className="btn btn-success" onClick={refresh} disabled={isLoading}>
            Refresh
          </button>
        )}
      />

      <section className="card border-0 shadow-sm mb-4" aria-labelledby="booking-history-filters-title">
        <div className="card-body p-3 p-lg-4">
          <h2 id="booking-history-filters-title" className="h5 mb-3">Filter booking history</h2>
          <form onSubmit={applyFilters}>
            <div className="row g-3 align-items-end">
              <div className="col-12 col-lg-6">
                <label htmlFor="history-search" className="form-label">Reference or text</label>
                <input
                  id="history-search"
                  type="search"
                  className="form-control"
                  maxLength={100}
                  placeholder="RES-reference, Prosumer, station name or address"
                  value={searchInput}
                  onChange={(event) => setSearchInput(event.target.value)}
                />
              </div>
              <div className="col-12 col-sm-6 col-lg-3">
                <label htmlFor="history-status" className="form-label">Status</label>
                <select
                  id="history-status"
                  className="form-select"
                  value={filters.status}
                  onChange={(event) => updateFilter('status', event.target.value)}
                >
                  <option value="">All statuses</option>
                  {reservationStatuses.map((status) => (
                    <option key={status} value={status}>{status}</option>
                  ))}
                </select>
              </div>
              <div className="col-12 col-sm-6 col-lg-3">
                <label htmlFor="history-station" className="form-label">Station</label>
                <select
                  id="history-station"
                  className="form-select"
                  value={filters.stationId}
                  onChange={(event) => updateFilter('stationId', event.target.value)}
                  aria-describedby={stationError ? 'history-station-error' : undefined}
                >
                  <option value="">Assigned station</option>
                  {stations.map((station) => (
                    <option key={station.id} value={station.id}>{station.name}</option>
                  ))}
                </select>
                {stationError && (
                  <div id="history-station-error" className="form-text text-danger">
                    Station names are temporarily unavailable.
                  </div>
                )}
              </div>
              <div className="col-12 col-sm-6 col-lg-3">
                <label htmlFor="history-from-date" className="form-label">From date (UTC)</label>
                <input
                  id="history-from-date"
                  type="date"
                  className={`form-control ${validationError ? 'is-invalid' : ''}`}
                  value={fromDateInput}
                  onChange={(event) => setFromDateInput(event.target.value)}
                  aria-describedby={validationError ? 'history-date-error' : undefined}
                />
              </div>
              <div className="col-12 col-sm-6 col-lg-3">
                <label htmlFor="history-to-date" className="form-label">To date (UTC)</label>
                <input
                  id="history-to-date"
                  type="date"
                  className={`form-control ${validationError ? 'is-invalid' : ''}`}
                  value={toDateInput}
                  onChange={(event) => setToDateInput(event.target.value)}
                  aria-describedby={validationError ? 'history-date-error' : undefined}
                />
              </div>
              <div className="col-12 col-sm-6 col-lg-3">
                <button type="submit" className="btn btn-success w-100">Apply filters</button>
              </div>
              <div className="col-12 col-sm-6 col-lg-3">
                <button type="button" className="btn btn-outline-secondary w-100" onClick={clearFilters}>
                  Clear filters
                </button>
              </div>
            </div>
            {validationError && (
              <div id="history-date-error" className="invalid-feedback d-block" role="alert">
                {validationError}
              </div>
            )}
          </form>
        </div>
      </section>

      {!validationError && contentState === 'loading' && (
        <LoadingState label="Loading booking history…" />
      )}
      {!validationError && contentState === 'error' && (
        <ApiErrorState error={error} resourceName="booking history" onRetry={refresh} />
      )}
      {!validationError && contentState === 'empty' && (
        <EmptyState
          title="No booking history found"
          description="No historical bookings match the selected server-side filters."
          action={<button type="button" className="btn btn-outline-success" onClick={clearFilters}>Clear filters</button>}
        />
      )}
      {!validationError && contentState === 'ready' && history && (
        <section className="card border-0 shadow-sm" aria-labelledby="booking-history-results-title">
          <div className="card-body p-3 p-lg-4">
            <div className="d-flex flex-column flex-sm-row justify-content-between gap-2 mb-3">
              <h2 id="booking-history-results-title" className="h5 mb-0">History results</h2>
              <span className="small text-body-secondary">Newest scheduled time first</span>
            </div>
            <ReservationSummaryTable
              items={history.items}
              returnTo={returnTo}
              emptyMessage="No historical bookings match these filters."
            />
            <div className="mt-4">
              <Pagination
                page={history.page}
                totalPages={history.totalPages}
                totalCount={history.totalCount}
                onPageChange={(page) => updateFilter('page', String(page))}
              />
            </div>
          </div>
        </section>
      )}
    </>
  )
}

function setOrDelete(parameters: URLSearchParams, key: string, value: string): void {
  if (value) {
    parameters.set(key, value)
  } else {
    parameters.delete(key)
  }
}
