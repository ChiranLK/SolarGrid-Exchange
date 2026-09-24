import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { stationApi } from '../../api/stationApi'
import { useAuth } from '../../auth/useAuth'
import { ApiErrorState } from '../../components/ApiErrorState'
import { EmptyState } from '../../components/EmptyState'
import { LoadingState } from '../../components/LoadingState'
import { PageHeader } from '../../components/PageHeader'
import { Pagination } from '../../components/Pagination'
import { StatusBadge } from '../../components/StatusBadge'
import type { StationSummary } from '../stations/stationTypes'
import { ReservationActionLinks } from './ReservationActionLinks'
import { reservationApi } from './reservationApi'
import {
  formatDateTime,
  formatEnergy,
  formatReservationReference,
  formatSlotRange,
} from './reservationFormatters'
import {
  reservationStatuses,
  reservationViews,
  type PagedReservations,
  type ReservationStatus,
  type ReservationView,
} from './reservationTypes'

const pageSize = 10

export function ReservationListPage() {
  const { session } = useAuth()
  const location = useLocation()
  const [searchParameters, setSearchParameters] = useSearchParams()
  const [reservations, setReservations] = useState<PagedReservations | null>(null)
  const [stations, setStations] = useState<StationSummary[]>([])
  const [stationError, setStationError] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [reloadToken, setReloadToken] = useState(0)
  const [searchInput, setSearchInput] = useState(searchParameters.get('search') ?? '')

  const filters = useMemo(() => readFilters(searchParameters), [searchParameters])
  const returnTo = `${location.pathname}${location.search}`

  useEffect(() => {
    document.title = 'Reservations | SolarGrid Exchange'
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    stationApi.listAll(controller.signal)
      .then((response) => {
        setStations(response.items)
        setStationError(false)
      })
      .catch(() => setStationError(true))
    return () => controller.abort()
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    reservationApi.list(
      {
        view: filters.view,
        status: filters.status,
        stationId: filters.stationId,
        search: session?.role === 'Backoffice' ? filters.search : undefined,
        page: filters.page,
        pageSize,
      },
      controller.signal,
    )
      .then(setReservations)
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
  }, [filters, reloadToken, session?.role])

  const updateFilter = useCallback((key: string, value: string) => {
    setIsLoading(true)
    setError(null)
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

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    updateFilter('search', searchInput.trim())
  }

  return (
    <>
      <PageHeader
        eyebrow="Energy reservations"
        title="Reservations"
        description="Results and actions are scoped by the central API for your current account."
        actions={(
          <Link
            className="btn btn-success"
            to={`/reservations/new?returnTo=${encodeURIComponent(returnTo)}`}
          >
            Create reservation
          </Link>
        )}
      />

      <section className="card border-0 shadow-sm mb-4" aria-labelledby="reservation-filters-title">
        <div className="card-body p-3 p-lg-4">
          <h2 id="reservation-filters-title" className="h6 mb-3">Filter reservations</h2>
          <div className="row g-3 align-items-end">
            <div className="col-12 col-md-4 col-xl-3">
              <label htmlFor="reservation-view" className="form-label">View</label>
              <select
                id="reservation-view"
                className="form-select"
                value={filters.view}
                onChange={(event) => updateFilter('view', event.target.value)}
              >
                {reservationViews.map((view) => (
                  <option key={view} value={view}>{formatEnumLabel(view)}</option>
                ))}
              </select>
            </div>
            <div className="col-12 col-md-4 col-xl-3">
              <label htmlFor="reservation-station" className="form-label">Station</label>
              <select
                id="reservation-station"
                className="form-select"
                value={filters.stationId}
                onChange={(event) => updateFilter('stationId', event.target.value)}
                aria-describedby={stationError ? 'station-filter-error' : undefined}
              >
                <option value="">All stations</option>
                {stations.map((station) => (
                  <option key={station.id} value={station.id}>{station.name}</option>
                ))}
              </select>
              {stationError && (
                <div id="station-filter-error" className="form-text text-danger">
                  Station names are temporarily unavailable.
                </div>
              )}
            </div>
            <div className="col-12 col-md-4 col-xl-3">
              <label htmlFor="reservation-status" className="form-label">Status</label>
              <select
                id="reservation-status"
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
            <div className="col-12 col-xl-3 d-flex align-items-end">
              <button
                type="button"
                className="btn btn-outline-secondary w-100"
                onClick={() => {
                  setIsLoading(true)
                  setError(null)
                  setSearchInput('')
                  setSearchParameters(new URLSearchParams())
                }}
              >
                Clear filters
              </button>
            </div>
          </div>

          {session?.role === 'Backoffice' && (
            <form className="row g-2 mt-2" role="search" onSubmit={handleSearch}>
              <div className="col-12 col-lg-9">
                <label htmlFor="reservation-search" className="visually-hidden">Search reservations</label>
                <input
                  id="reservation-search"
                  type="search"
                  className="form-control"
                  placeholder="Search reference, Prosumer NIC, station name or address"
                  value={searchInput}
                  onChange={(event) => setSearchInput(event.target.value)}
                />
              </div>
              <div className="col-12 col-lg-3">
                <button type="submit" className="btn btn-outline-success w-100">Search</button>
              </div>
            </form>
          )}
        </div>
      </section>

      {isLoading && <LoadingState label="Loading reservations…" />}
      {!isLoading && error && (
        <ApiErrorState
          error={error}
          resourceName="reservations"
          onRetry={() => {
            setIsLoading(true)
            setError(null)
            setReloadToken((value) => value + 1)
          }}
        />
      )}
      {!isLoading && !error && reservations?.items.length === 0 && (
        <EmptyState
          title="No reservations found"
          description="Try adjusting the filters or create a new reservation."
          action={(
            <Link className="btn btn-success" to={`/reservations/new?returnTo=${encodeURIComponent(returnTo)}`}>
              Create reservation
            </Link>
          )}
        />
      )}
      {!isLoading && !error && reservations && reservations.items.length > 0 && (
        <section aria-label="Reservation results">
          <div className="card border-0 shadow-sm d-none d-lg-block mb-4">
            <div className="table-responsive">
              <table className="table align-middle mb-0 reservation-table">
                <thead>
                  <tr>
                    <th scope="col">Reference</th>
                    <th scope="col">Prosumer</th>
                    <th scope="col">Station</th>
                    <th scope="col">Slot</th>
                    <th scope="col">Status</th>
                    <th scope="col">Requested</th>
                    <th scope="col">Available actions</th>
                  </tr>
                </thead>
                <tbody>
                  {reservations.items.map((reservation) => (
                    <tr key={reservation.id}>
                      <td>
                        <span className="fw-semibold" title={reservation.id}>
                          {formatReservationReference(reservation.id)}
                        </span>
                        <div className="small text-body-secondary">{formatEnergy(reservation.requestedEnergyKwh)}</div>
                      </td>
                      <td>
                        <div className="fw-medium">{reservation.prosumerFullName ?? 'Prosumer'}</div>
                        <div className="small text-body-secondary">{reservation.prosumerNic}</div>
                      </td>
                      <td>
                        <div className="fw-medium">{reservation.stationName ?? 'Unknown station'}</div>
                        <div className="small text-body-secondary text-truncate table-address">{reservation.stationAddress}</div>
                      </td>
                      <td>{formatSlotRange(reservation.scheduledStartTimeUtc, reservation.scheduledEndTimeUtc)}</td>
                      <td><StatusBadge status={reservation.status} /></td>
                      <td>{formatDateTime(reservation.createdAtUtc)}</td>
                      <td>
                        <ReservationActionLinks
                          reservationId={reservation.id}
                          allowedActions={reservation.allowedActions}
                          returnTo={returnTo}
                          compact
                        />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <div className="d-grid gap-3 d-lg-none mb-4">
            {reservations.items.map((reservation) => (
              <article key={reservation.id} className="card border-0 shadow-sm">
                <div className="card-body p-3">
                  <div className="d-flex justify-content-between align-items-start gap-3 mb-3">
                    <div>
                      <div className="fw-semibold" title={reservation.id}>{formatReservationReference(reservation.id)}</div>
                      <div className="small text-body-secondary">Requested {formatDateTime(reservation.createdAtUtc)}</div>
                    </div>
                    <StatusBadge status={reservation.status} />
                  </div>
                  <dl className="reservation-card-details">
                    <dt>Prosumer</dt>
                    <dd>{reservation.prosumerFullName ?? reservation.prosumerNic}<span className="d-block small text-body-secondary">{reservation.prosumerFullName ? reservation.prosumerNic : ''}</span></dd>
                    <dt>Station</dt>
                    <dd>{reservation.stationName ?? 'Unknown station'}</dd>
                    <dt>Slot</dt>
                    <dd>{formatSlotRange(reservation.scheduledStartTimeUtc, reservation.scheduledEndTimeUtc)}</dd>
                    <dt>Energy</dt>
                    <dd>{formatEnergy(reservation.requestedEnergyKwh)}</dd>
                  </dl>
                  <ReservationActionLinks
                    reservationId={reservation.id}
                    allowedActions={reservation.allowedActions}
                    returnTo={returnTo}
                  />
                </div>
              </article>
            ))}
          </div>

          <Pagination
            page={reservations.page}
            totalPages={reservations.totalPages}
            totalCount={reservations.totalCount}
            onPageChange={(page) => updateFilter('page', String(page))}
          />
        </section>
      )}
    </>
  )
}

interface ReservationFilters {
  view: ReservationView
  status: ReservationStatus | ''
  stationId: string
  search: string
  page: number
}

function readFilters(parameters: URLSearchParams): ReservationFilters {
  const viewValue = parameters.get('view')
  const statusValue = parameters.get('status')
  const parsedPage = Number.parseInt(parameters.get('page') ?? '1', 10)

  return {
    view: reservationViews.includes(viewValue as ReservationView)
      ? (viewValue as ReservationView)
      : 'All',
    status: reservationStatuses.includes(statusValue as ReservationStatus)
      ? (statusValue as ReservationStatus)
      : '',
    stationId: parameters.get('stationId') ?? '',
    search: parameters.get('search') ?? '',
    page: Number.isFinite(parsedPage) && parsedPage > 0 ? parsedPage : 1,
  }
}

function formatEnumLabel(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2')
}
