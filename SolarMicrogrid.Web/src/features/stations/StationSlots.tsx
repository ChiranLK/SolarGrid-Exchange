import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../../api/apiClient'
import { slotApi } from '../../api/slotApi'
import { useAuth } from '../../auth/useAuth'
import { ApiErrorState } from '../../components/ApiErrorState'
import { EmptyState } from '../../components/EmptyState'
import { ErrorState } from '../../components/ErrorState'
import { LoadingState } from '../../components/LoadingState'
import { Pagination } from '../../components/Pagination'
import { StatusBadge } from '../../components/StatusBadge'
import type { PagedSlots, SlotSummary } from './stationTypes'

const pageSize = 10

export function StationSlots({ stationId, stationIsActive }: { stationId: string; stationIsActive: boolean }) {
  const { session } = useAuth()
  const [results, setResults] = useState<PagedSlots | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<unknown>(null)
  const [actionError, setActionError] = useState<unknown>(null)
  const [notice, setNotice] = useState('')
  const [denied, setDenied] = useState(false)
  const [busySlotId, setBusySlotId] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const [status, setStatus] = useState<'' | 'Available' | 'FullyBooked' | 'Unavailable'>('')
  const [retry, setRetry] = useState(0)
  const canManageAvailability = session?.role === 'Backoffice' || session?.role === 'GridOperator'

  useEffect(() => {
    const controller = new AbortController()
    slotApi.list(stationId, { page, pageSize, status: status || undefined }, controller.signal)
      .then((response) => { setResults(response); setLoadError(null) })
      .catch((error: unknown) => { if (!controller.signal.aborted) setLoadError(error) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [stationId, page, status, retry])

  async function changeAvailability(slot: SlotSummary) {
    const nextStatus = slot.availabilityStatus === 'Unavailable' ? 'Available' : 'Unavailable'
    setBusySlotId(slot.id)
    setActionError(null)
    setNotice('')
    try {
      const updated = await slotApi.changeAvailability(slot.id, nextStatus)
      setResults((current) => current ? {
        ...current,
        items: current.items.map((item) => item.id === updated.id ? updated : item),
      } : current)
      setNotice(`Slot availability updated to ${updated.availabilityStatus}.`)
      if (status) { setLoading(true); setRetry((value) => value + 1) }
    } catch (error) {
      setActionError(error)
      if (error instanceof ApiError && error.status === 403) setDenied(true)
    } finally {
      setBusySlotId(null)
    }
  }

  return (
    <section className="mt-4" aria-labelledby="station-slots-heading">
      <div className="d-flex flex-column flex-sm-row justify-content-between gap-2 mb-3">
        <div><h2 id="station-slots-heading" className="h4 mb-1">Energy slots</h2><p className="text-body-secondary mb-0">Remaining capacity updates as reservations are approved.</p></div>
        {session?.role === 'Backoffice' && stationIsActive && <Link className="btn btn-success align-self-start" to={`/stations/${encodeURIComponent(stationId)}/slots/new`}>Create slot</Link>}
      </div>
      {session?.role === 'GridOperator' && <p className="alert alert-info">You can change availability only for the station you are currently assigned to.</p>}
      <div className="d-flex flex-wrap align-items-center gap-2 mb-3">
        <label htmlFor="slot-status" className="form-label mb-0">Status</label>
        <select id="slot-status" className="form-select w-auto" value={status} onChange={(event) => { setLoading(true); setPage(1); setStatus(event.target.value as typeof status) }}>
          <option value="">All</option><option value="Available">Available</option><option value="FullyBooked">Fully booked</option><option value="Unavailable">Unavailable</option>
        </select>
        <button className="btn btn-outline-secondary" type="button" onClick={() => { setDenied(false); setActionError(null); setLoading(true); setRetry((value) => value + 1) }}>Refresh</button>
      </div>
      {notice && <div className="alert alert-success" role="status">{notice}</div>}
      {actionError !== null && <div className="mb-3">{actionError instanceof ApiError && actionError.status === 403
        ? <ErrorState title="Availability access denied" message="You cannot manage slots at this station. Your assignment may have changed; refresh or contact Backoffice." />
        : actionError instanceof ApiError && actionError.status === 409
          ? <ErrorState title="Availability change blocked" message={actionError.message} />
        : <ApiErrorState error={actionError} resourceName="slot" />}</div>}
      {loading ? <LoadingState label="Loading slots…" /> : loadError ? <ApiErrorState error={loadError} resourceName="slots" onRetry={() => { setLoading(true); setRetry((value) => value + 1) }} /> : results?.items.length ? (
        <>
          <div className="row g-3 mb-3">{results.items.map((slot) => (
            <div className="col-12 col-lg-6" key={slot.id}><article className="card border-0 shadow-sm h-100"><div className="card-body">
              <div className="d-flex justify-content-between gap-2"><h3 className="h6">{formatUtc(slot.startTimeUtc)} – {formatUtc(slot.endTimeUtc)}</h3><StatusBadge status={slot.availabilityStatus} /></div>
              <p className="mb-2">Available: <strong>{slot.availableCapacityKwh} kWh</strong> of {slot.totalCapacityKwh} kWh total</p>
              <div className="d-flex flex-wrap gap-2">
                {session?.role === 'Backoffice' && <Link className="btn btn-sm btn-outline-success" to={`/stations/${encodeURIComponent(stationId)}/slots/${encodeURIComponent(slot.id)}/edit`}>Edit slot</Link>}
                {canManageAvailability && !denied && slot.availabilityStatus !== 'FullyBooked' && <button className="btn btn-sm btn-outline-secondary" type="button" disabled={busySlotId !== null} onClick={() => void changeAvailability(slot)}>
                  {busySlotId === slot.id ? 'Updating…' : slot.availabilityStatus === 'Unavailable' ? 'Enable availability' : 'Disable availability'}
                </button>}
              </div>
            </div></article></div>
          ))}</div>
          <Pagination page={results.page} totalPages={results.totalPages} totalCount={results.totalCount} onPageChange={(next) => { setLoading(true); setPage(next) }} />
        </>
      ) : <EmptyState title="No slots found" description="Try another status or create a slot for this station." />}
    </section>
  )
}

function formatUtc(value: string): string {
  return `${new Date(value).toISOString().slice(0, 16).replace('T', ' ')} UTC`
}
