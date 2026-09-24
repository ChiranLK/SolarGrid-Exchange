import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../api/apiClient'
import { stationApi } from '../../api/stationApi'
import { useAuth } from '../../auth/useAuth'
import { ApiErrorState } from '../../components/ApiErrorState'
import { EmptyState } from '../../components/EmptyState'
import { LoadingState } from '../../components/LoadingState'
import { PageHeader } from '../../components/PageHeader'
import type { SlotSummary, StationSummary } from '../stations/stationTypes'
import { createIdempotencyKey, reservationApi } from './reservationApi'
import { formatEnergy, formatSlotRange } from './reservationFormatters'
import { buildReservationDetailPath, getSafeReservationReturnTo } from './reservationNavigation'

export function ReservationCreatePage() {
  const { session } = useAuth()
  const navigate = useNavigate()
  const [searchParameters] = useSearchParams()
  const returnTo = getSafeReservationReturnTo(searchParameters.get('returnTo'))
  const [stations, setStations] = useState<StationSummary[]>([])
  const [slots, setSlots] = useState<SlotSummary[]>([])
  const [stationId, setStationId] = useState('')
  const [slotId, setSlotId] = useState('')
  const [requestedEnergyKwh, setRequestedEnergyKwh] = useState('')
  const [targetProsumerNic, setTargetProsumerNic] = useState('')
  const [isLoadingStations, setIsLoadingStations] = useState(true)
  const [isLoadingSlots, setIsLoadingSlots] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [loadError, setLoadError] = useState<unknown>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)
  const idempotencyKey = useRef(createIdempotencyKey('create'))
  const isStaff = session?.role === 'Backoffice' || session?.role === 'GridOperator'

  useEffect(() => {
    document.title = 'Create reservation | SolarGrid Exchange'
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    stationApi.listActive(controller.signal)
      .then((response) => setStations(response.items))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setLoadError(error)
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setIsLoadingStations(false)
        }
      })
    return () => controller.abort()
  }, [reloadToken])

  useEffect(() => {
    if (!stationId) {
      return
    }

    const controller = new AbortController()
    stationApi.listAvailableSlots(stationId, controller.signal)
      .then((response) => setSlots(response.items))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setLoadError(error)
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setIsLoadingSlots(false)
        }
      })
    return () => controller.abort()
  }, [stationId])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const energy = Number(requestedEnergyKwh)
    if (!slotId || !Number.isFinite(energy) || energy <= 0) {
      setSubmitError('Choose an available slot and enter an energy amount greater than zero.')
      return
    }
    if (isStaff && !targetProsumerNic.trim()) {
      setSubmitError('Enter the target Prosumer NIC.')
      return
    }

    setIsSubmitting(true)
    setSubmitError(null)
    try {
      const created = isStaff
        ? await reservationApi.createForProsumer({
            slotId,
            requestedEnergyKwh: energy,
            targetProsumerNic: targetProsumerNic.trim(),
          }, idempotencyKey.current)
        : await reservationApi.createOwn(
            { slotId, requestedEnergyKwh: energy },
            idempotencyKey.current,
          )
      navigate(buildReservationDetailPath(created.id, returnTo), { replace: true })
    } catch (error) {
      setSubmitError(error instanceof ApiError ? error.message : 'The reservation could not be created.')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <>
      <PageHeader
        eyebrow="Energy reservations"
        title="Create reservation"
        description="Station, slot, ownership, schedule, and capacity rules are validated by the API."
      />

      {isLoadingStations && <LoadingState label="Loading active stations…" />}
      {!isLoadingStations && loadError && !stationId && (
        <ApiErrorState
          error={loadError}
          onRetry={() => {
            setIsLoadingStations(true)
            setLoadError(null)
            setReloadToken((value) => value + 1)
          }}
          resourceName="stations"
        />
      )}
      {!isLoadingStations && !loadError && stations.length === 0 && (
        <EmptyState
          title="No active stations available"
          description="A reservation cannot be created until the API returns an active station."
          action={<Link className="btn btn-outline-secondary" to={returnTo}>Back to reservations</Link>}
        />
      )}
      {!isLoadingStations && stations.length > 0 && (
        <form className="card border-0 shadow-sm" onSubmit={handleSubmit} noValidate>
          <div className="card-body p-4">
            {submitError && <div className="alert alert-danger" role="alert">{submitError}</div>}
            {Boolean(loadError) && stationId && (
              <div className="alert alert-danger" role="alert">
                Available slots could not be loaded. Change the station or try again.
              </div>
            )}

            {isStaff && (
              <div className="mb-3">
                <label htmlFor="target-prosumer-nic" className="form-label">Target Prosumer NIC</label>
                <input
                  id="target-prosumer-nic"
                  className="form-control"
                  maxLength={12}
                  required
                  value={targetProsumerNic}
                  onChange={(event) => setTargetProsumerNic(event.target.value)}
                  aria-describedby="target-prosumer-help"
                />
                <div id="target-prosumer-help" className="form-text">
                  The API verifies that this Prosumer exists and is active.
                </div>
              </div>
            )}

            <div className="row g-3">
              <div className="col-12 col-lg-6">
                <label htmlFor="create-station" className="form-label">Station</label>
                <select
                  id="create-station"
                  className="form-select"
                  required
                  value={stationId}
                  onChange={(event) => {
                    setSlotId('')
                    setSlots([])
                    setLoadError(null)
                    setIsLoadingSlots(Boolean(event.target.value))
                    setStationId(event.target.value)
                  }}
                >
                  <option value="">Choose a station</option>
                  {stations.map((station) => (
                    <option key={station.id} value={station.id}>{station.name} — {station.address}</option>
                  ))}
                </select>
              </div>
              <div className="col-12 col-lg-6">
                <label htmlFor="create-slot" className="form-label">Available slot</label>
                <select
                  id="create-slot"
                  className="form-select"
                  required
                  disabled={!stationId || isLoadingSlots}
                  value={slotId}
                  onChange={(event) => setSlotId(event.target.value)}
                >
                  <option value="">{isLoadingSlots ? 'Loading slots…' : 'Choose a slot'}</option>
                  {slots.map((slot) => (
                    <option key={slot.id} value={slot.id}>
                      {formatSlotRange(slot.startTimeUtc, slot.endTimeUtc)} · {formatEnergy(slot.availableCapacityKwh)} available
                    </option>
                  ))}
                </select>
                {stationId && !isLoadingSlots && !loadError && slots.length === 0 && (
                  <div className="form-text">No future available slots were returned for this station.</div>
                )}
              </div>
              <div className="col-12 col-lg-6">
                <label htmlFor="requested-energy" className="form-label">Requested energy (kWh)</label>
                <input
                  id="requested-energy"
                  type="number"
                  className="form-control"
                  min="0.001"
                  step="0.001"
                  required
                  value={requestedEnergyKwh}
                  onChange={(event) => setRequestedEnergyKwh(event.target.value)}
                />
              </div>
            </div>
          </div>
          <div className="card-footer bg-white border-top p-3 d-flex flex-wrap justify-content-end gap-2">
            <Link className="btn btn-outline-secondary" to={returnTo}>Cancel</Link>
            <button
              type="submit"
              className="btn btn-success"
              disabled={isSubmitting || isLoadingSlots}
            >
              {isSubmitting ? 'Creating…' : 'Create reservation'}
            </button>
          </div>
        </form>
      )}
    </>
  )
}
