import { useEffect, useState } from 'react'
import { Link, useLocation, useParams } from 'react-router-dom'
import { ApiError } from '../../api/apiClient'
import { stationApi } from '../../api/stationApi'
import { useAuth } from '../../auth/useAuth'
import { ApiErrorState } from '../../components/ApiErrorState'
import { ConfirmationDialog } from '../../components/ConfirmationDialog'
import { ErrorState } from '../../components/ErrorState'
import { LoadingState } from '../../components/LoadingState'
import { PageHeader } from '../../components/PageHeader'
import { StatusBadge } from '../../components/StatusBadge'
import type { Station } from './stationTypes'
import { StationSlots } from './StationSlots'

export function StationDetailPage() {
  const { stationId } = useParams()
  const location = useLocation()
  const { session } = useAuth()
  const [station, setStation] = useState<Station | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [loading, setLoading] = useState(true)
  const [retry, setRetry] = useState(0)
  const [confirmActive, setConfirmActive] = useState<boolean | null>(null)
  const [updatingStatus, setUpdatingStatus] = useState(false)
  const [statusError, setStatusError] = useState<unknown>(null)
  const [statusNotice, setStatusNotice] = useState('')
  const routeNotice = (location.state as { notice?: string } | null)?.notice

  async function changeStatus() {
    if (!station || confirmActive === null) return
    setUpdatingStatus(true)
    setStatusError(null)
    try {
      const updated = confirmActive
        ? await stationApi.activate(station.id)
        : await stationApi.deactivate(station.id)
      setStation(updated)
      setStatusNotice(updated.isActive ? 'Station activated.' : 'Station deactivated.')
      setConfirmActive(null)
    } catch (requestError) {
      setStatusError(requestError)
      setConfirmActive(null)
    } finally {
      setUpdatingStatus(false)
    }
  }

  useEffect(() => {
    const controller = new AbortController()
    if (stationId) {
      stationApi.getById(stationId, controller.signal)
        .then((result) => { setStation(result); setError(null); document.title = `${result.name} | SolarGrid Exchange` })
        .catch((requestError: unknown) => { if (!controller.signal.aborted) setError(requestError) })
        .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    }
    return () => controller.abort()
  }, [stationId, retry])

  return (
    <>
      <div className="mb-3"><Link to="/stations">← Back to stations</Link></div>
      {loading ? <LoadingState label="Loading station…" /> : error ? <ApiErrorState error={error} resourceName="station" onRetry={() => { setLoading(true); setRetry((value) => value + 1) }} /> : station ? (
        <>
          <PageHeader eyebrow="Solar station" title={station.name} description={station.address}
            actions={session?.role === 'Backoffice' ? <>
              <Link className="btn btn-outline-success" to={`/stations/${encodeURIComponent(station.id)}/edit`}>Edit station</Link>
              <Link className="btn btn-outline-success" to={`/stations/${encodeURIComponent(station.id)}/schedule`}>Edit schedule</Link>
              <button className={`btn ${station.isActive ? 'btn-outline-danger' : 'btn-success'}`} type="button" onClick={() => setConfirmActive(!station.isActive)}>{station.isActive ? 'Deactivate' : 'Activate'}</button>
            </> : undefined} />
          {(statusNotice || routeNotice) && <div className="alert alert-success" role="status">{statusNotice || routeNotice}</div>}
          {statusError && <div className="mb-4">{statusError instanceof ApiError && statusError.status === 409
            ? <ErrorState title="Station cannot be deactivated" message={statusError.message} />
            : <ApiErrorState error={statusError} resourceName="station" />}</div>}
          <div className="row g-4">
            <div className="col-12 col-lg-7">
              <section className="card border-0 shadow-sm h-100"><div className="card-body p-4">
                <h2 className="h5 mb-3">Station details</h2>
                <p><StatusBadge status={station.isActive ? 'Active' : 'Deactivated'} /></p>
                {station.description && <p>{station.description}</p>}
                <dl className="row mb-0">
                  <dt className="col-sm-6">Latitude</dt><dd className="col-sm-6">{station.latitude}°</dd>
                  <dt className="col-sm-6">Longitude</dt><dd className="col-sm-6">{station.longitude}°</dd>
                  <dt className="col-sm-6">Generation capacity</dt><dd className="col-sm-6">{station.energyGenerationCapacityKw} kW</dd>
                  <dt className="col-sm-6">Battery storage</dt><dd className="col-sm-6">{station.batteryStorageCapacityKwh} kWh</dd>
                </dl>
              </div></section>
            </div>
            <div className="col-12 col-lg-5">
              <section className="card border-0 shadow-sm h-100"><div className="card-body p-4">
                <h2 className="h5 mb-3">Operating schedule</h2>
                <ul className="list-group list-group-flush">
                  {station.operatingSchedule.map((item) => (
                    <li className="list-group-item d-flex justify-content-between gap-2 px-0" key={item.dayOfWeek}>
                      <span>{item.dayOfWeek}</span><span>{item.isOpen ? `${item.openingTime}–${item.closingTime}` : 'Closed'}</span>
                    </li>
                  ))}
                </ul>
              </div></section>
            </div>
          </div>
          <StationSlots stationId={station.id} stationIsActive={station.isActive} />
          <ConfirmationDialog isOpen={confirmActive !== null} title={confirmActive ? 'Activate station?' : 'Deactivate station?'}
            message={confirmActive ? 'Make this station active again?' : 'Deactivate this station? Active reservations will block deactivation.'}
            confirmLabel={confirmActive ? 'Activate' : 'Deactivate'} destructive={!confirmActive} isBusy={updatingStatus}
            onConfirm={() => void changeStatus()} onCancel={() => { if (!updatingStatus) setConfirmActive(null) }} />
        </>
      ) : <ApiErrorState error={new Error('Station not found')} resourceName="station" />}
    </>
  )
}
