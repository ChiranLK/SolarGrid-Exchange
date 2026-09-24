import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../api/apiClient'
import { stationApi } from '../../api/stationApi'
import { useAuth } from '../../auth/useAuth'
import { ApiErrorState } from '../../components/ApiErrorState'
import { EmptyState } from '../../components/EmptyState'
import { LoadingState } from '../../components/LoadingState'
import { PageHeader } from '../../components/PageHeader'
import { StatusBadge } from '../../components/StatusBadge'
import type { SlotSummary, StationSummary } from '../stations/stationTypes'
import type { EligibleProsumer } from '../users/userTypes'
import { createIdempotencyKey, reservationApi } from './reservationApi'
import {
  describeCreationError,
  getCreationFingerprint,
  type CreationErrorPresentation,
} from './reservationCreation'
import {
  formatDateTime,
  formatEnergy,
  formatReservationReference,
  formatSlotRange,
} from './reservationFormatters'
import { buildReservationDetailPath, getSafeReservationReturnTo } from './reservationNavigation'
import { announceReservationChange } from './reservationSync'
import type { CreateReservationRequest, ReservationDetail } from './reservationTypes'
import { StaffProsumerPicker } from './StaffProsumerPicker'

type CreationStep = 'details' | 'review' | 'saved'

interface LogicalAttempt {
  fingerprint: string
  idempotencyKey: string
  startedAtEpochMs: number
}

export function ReservationCreatePage() {
  const { session } = useAuth()
  const [searchParameters] = useSearchParams()
  const returnTo = getSafeReservationReturnTo(searchParameters.get('returnTo'))
  const [step, setStep] = useState<CreationStep>('details')
  const [stations, setStations] = useState<StationSummary[]>([])
  const [slots, setSlots] = useState<SlotSummary[]>([])
  const [selectedProsumer, setSelectedProsumer] = useState<EligibleProsumer | null>(null)
  const [stationId, setStationId] = useState('')
  const [slotId, setSlotId] = useState('')
  const [requestedEnergyKwh, setRequestedEnergyKwh] = useState('')
  const [savedReservation, setSavedReservation] = useState<ReservationDetail | null>(null)
  const [isLoadingStations, setIsLoadingStations] = useState(true)
  const [isLoadingSlots, setIsLoadingSlots] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [isReconciling, setIsReconciling] = useState(false)
  const [stationError, setStationError] = useState<unknown>(null)
  const [slotError, setSlotError] = useState<unknown>(null)
  const [submitError, setSubmitError] = useState<CreationErrorPresentation | null>(null)
  const [stationReloadToken, setStationReloadToken] = useState(0)
  const [slotReloadToken, setSlotReloadToken] = useState(0)
  const logicalAttempt = useRef<LogicalAttempt | null>(null)
  const submissionInFlight = useRef(false)
  const isStaff = session?.role === 'Backoffice' || session?.role === 'GridOperator'

  const selectedStation = stations.find((station) => station.id === stationId) ?? null
  const selectedSlot = slots.find((slot) => slot.id === slotId) ?? null

  useEffect(() => {
    document.title = 'Create reservation | SolarGrid Exchange'
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    stationApi.listActive(controller.signal)
      .then((response) => {
        setStations(response.items)
        setStationError(null)
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setStationError(error)
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setIsLoadingStations(false)
        }
      })
    return () => controller.abort()
  }, [stationReloadToken])

  useEffect(() => {
    if (!stationId) {
      return
    }

    const controller = new AbortController()
    stationApi.listAvailableSlots(stationId, controller.signal)
      .then((response) => {
        setSlots(response.items)
        setSlotError(null)
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setSlotError(error)
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setIsLoadingSlots(false)
        }
      })
    return () => controller.abort()
  }, [stationId, slotReloadToken])

  function buildRequest(): CreateReservationRequest | null {
    const energy = Number(requestedEnergyKwh)
    if (!slotId || !Number.isFinite(energy) || energy <= 0) {
      setSubmitError({
        title: 'Booking details are incomplete',
        message: 'Choose an available slot and enter an energy quantity greater than zero.',
        outcomeUnknown: false,
      })
      return null
    }
    if (isStaff && !selectedProsumer) {
      setSubmitError({
        title: 'Prosumer is required',
        message: 'Search for and select an eligible active Prosumer before continuing.',
        outcomeUnknown: false,
      })
      return null
    }

    return {
      slotId,
      requestedEnergyKwh: energy,
      targetProsumerNic: isStaff ? selectedProsumer?.nic : undefined,
    }
  }

  function handleReview() {
    setSubmitError(null)
    if (buildRequest()) {
      setStep('review')
      window.scrollTo({ top: 0, behavior: 'smooth' })
    }
  }

  async function handleConfirm() {
    if (submissionInFlight.current) {
      return
    }
    const request = buildRequest()
    if (!request) {
      setStep('details')
      return
    }

    const fingerprint = getCreationFingerprint(request)
    if (logicalAttempt.current?.fingerprint !== fingerprint) {
      logicalAttempt.current = {
        fingerprint,
        idempotencyKey: createIdempotencyKey(isStaff ? 'staff-create' : 'create'),
        startedAtEpochMs: Date.now(),
      }
    }
    const attempt = logicalAttempt.current

    submissionInFlight.current = true
    setIsSubmitting(true)
    setSubmitError(null)
    try {
      let created: ReservationDetail
      try {
        created = await submitOnce(request, attempt.idempotencyKey, isStaff)
      } catch (error) {
        if (!(error instanceof ApiError) || error.status !== 0) {
          throw error
        }

        setIsReconciling(true)
        const currentReservation = await findCreatedReservation(
          request,
          session?.role === 'Backoffice',
          attempt.startedAtEpochMs,
          session?.nic,
        )
        if (!currentReservation) {
          setSubmitError({
            title: 'Current state checked',
            message: 'No matching reservation is currently visible on the server. You may retry; the original request identifier will be reused so a delayed first request cannot create a duplicate.',
            outcomeUnknown: true,
          })
          return
        }
        created = currentReservation
      }

      setSavedReservation(created)
      setStep('saved')
      announceReservationChange(created)
      window.scrollTo({ top: 0, behavior: 'smooth' })
    } catch (error) {
      setSubmitError(describeCreationError(error))
    } finally {
      setIsReconciling(false)
      setIsSubmitting(false)
      submissionInFlight.current = false
    }
  }

  function handleStationChange(nextStationId: string) {
    setStationId(nextStationId)
    setSlotId('')
    setSlots([])
    setSlotError(null)
    setSubmitError(null)
    setIsLoadingSlots(Boolean(nextStationId))
  }

  return (
    <>
      <PageHeader
        eyebrow="Energy reservations"
        title={isStaff ? 'Create a reservation for a Prosumer' : 'Create reservation'}
        description="The API makes the final account, schedule, overlap, booking-window, and capacity decisions."
      />

      <CreationProgress step={step} isStaff={isStaff} />

      {step === 'saved' && savedReservation ? (
        <SavedReservationSummary
          reservation={savedReservation}
          selectedProsumer={selectedProsumer}
          selectedStation={selectedStation}
          selectedSlot={selectedSlot}
          returnTo={returnTo}
        />
      ) : isLoadingStations ? (
        <LoadingState label="Loading active stations…" />
      ) : stationError ? (
        <ApiErrorState
          error={stationError}
          onRetry={() => {
            setIsLoadingStations(true)
            setStationError(null)
            setStationReloadToken((value) => value + 1)
          }}
          resourceName="stations"
        />
      ) : stations.length === 0 ? (
        <EmptyState
          title="No active stations available"
          description="A reservation cannot be created until the API returns an active station."
          action={<Link className="btn btn-outline-secondary" to={returnTo}>Back to reservations</Link>}
        />
      ) : step === 'details' ? (
        <section className="card border-0 shadow-sm" aria-label="Reservation booking details">
          <div className="card-body p-4">
            {submitError && <CreationErrorAlert error={submitError} />}

            {isStaff && (
              <div className="mb-4">
                <StaffProsumerPicker
                  selected={selectedProsumer}
                  onSelect={(prosumer) => {
                    setSelectedProsumer(prosumer)
                    setSubmitError(null)
                  }}
                />
              </div>
            )}

            <fieldset>
              <legend className="h6 mb-3">{isStaff ? '2.' : '1.'} Choose station and slot</legend>
              <div className="row g-3">
                <div className="col-12 col-lg-6">
                  <label htmlFor="create-station" className="form-label">Active station</label>
                  <select
                    id="create-station"
                    className="form-select"
                    required
                    value={stationId}
                    onChange={(event) => handleStationChange(event.target.value)}
                  >
                    <option value="">Choose a station</option>
                    {stations.map((station) => (
                      <option key={station.id} value={station.id}>
                        {station.name} — {station.address}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="col-12 col-lg-6">
                  <label htmlFor="create-slot" className="form-label">Available slot</label>
                  <select
                    id="create-slot"
                    className="form-select"
                    required
                    disabled={!stationId || isLoadingSlots || Boolean(slotError)}
                    value={slotId}
                    onChange={(event) => {
                      setSlotId(event.target.value)
                      setSubmitError(null)
                    }}
                  >
                    <option value="">{isLoadingSlots ? 'Loading slots…' : 'Choose a slot'}</option>
                    {slots.map((slot) => (
                      <option key={slot.id} value={slot.id}>
                        {formatSlotRange(slot.startTimeUtc, slot.endTimeUtc)} · {formatEnergy(slot.availableCapacityKwh)} available
                      </option>
                    ))}
                  </select>
                  {stationId && !isLoadingSlots && !slotError && slots.length === 0 && (
                    <div className="form-text">No future available slots were returned for this station.</div>
                  )}
                  {Boolean(slotError) && (
                    <div className="alert alert-warning mt-2 mb-0" role="alert">
                      <p className="mb-2">Available slots could not be loaded.</p>
                      <button
                        type="button"
                        className="btn btn-sm btn-outline-dark"
                        onClick={() => {
                          setIsLoadingSlots(true)
                          setSlotError(null)
                          setSlotReloadToken((value) => value + 1)
                        }}
                      >
                        Retry slots
                      </button>
                    </div>
                  )}
                </div>
              </div>
            </fieldset>

            <fieldset className="mt-4">
              <legend className="h6 mb-3">{isStaff ? '3.' : '2.'} Enter required quantity</legend>
              <div className="row g-3">
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
                    onChange={(event) => {
                      setRequestedEnergyKwh(event.target.value)
                      setSubmitError(null)
                    }}
                    aria-describedby="requested-energy-help"
                  />
                  <div id="requested-energy-help" className="form-text">
                    {selectedSlot
                      ? `The slot currently reports ${formatEnergy(selectedSlot.availableCapacityKwh)} available. The API rechecks capacity when saving.`
                      : 'Quantity is required by the slot allocation model.'}
                  </div>
                </div>
              </div>
            </fieldset>
          </div>
          <div className="card-footer bg-white border-top p-3 d-flex flex-wrap justify-content-end gap-2">
            <Link className="btn btn-outline-secondary" to={returnTo}>Cancel</Link>
            <button
              type="button"
              className="btn btn-success"
              disabled={isLoadingSlots}
              onClick={handleReview}
            >
              Review reservation
            </button>
          </div>
        </section>
      ) : (
        <section className="card border-0 shadow-sm" aria-labelledby="reservation-review-title">
          <div className="card-body p-4">
            <h2 id="reservation-review-title" className="h5 mb-2">Review reservation</h2>
            <p className="text-body-secondary mb-4">
              Confirm the intended owner, slot, and quantity. Saving still requires the API's current validation.
            </p>
            {submitError && <CreationErrorAlert error={submitError} />}
            {isReconciling && (
              <div className="alert alert-info" role="status">
                The first response was not received. Reusing the same request identifier to safely reconcile with the API…
              </div>
            )}
            <dl className="detail-grid mb-0">
              <ReviewDetail
                label="Prosumer"
                value={isStaff
                  ? selectedProsumer
                    ? `${selectedProsumer.fullName} (${selectedProsumer.nic})`
                    : 'Not selected'
                  : `${session?.fullName ?? 'Current Prosumer'} (${session?.nic ?? ''})`}
              />
              <ReviewDetail label="Station" value={selectedStation?.name ?? stationId} />
              <ReviewDetail label="Station address" value={selectedStation?.address ?? 'Not supplied'} />
              <ReviewDetail
                label="Slot"
                value={selectedSlot
                  ? formatSlotRange(selectedSlot.startTimeUtc, selectedSlot.endTimeUtc)
                  : slotId}
              />
              <ReviewDetail label="Requested energy" value={formatEnergy(Number(requestedEnergyKwh))} />
              <ReviewDetail
                label="Reported slot availability"
                value={selectedSlot ? formatEnergy(selectedSlot.availableCapacityKwh) : 'Not available'}
              />
            </dl>
          </div>
          <div className="card-footer bg-white border-top p-3 d-flex flex-wrap justify-content-end gap-2">
            <button
              type="button"
              className="btn btn-outline-secondary"
              disabled={isSubmitting}
              onClick={() => {
                setStep('details')
                setSubmitError(null)
              }}
            >
              Back to edit
            </button>
            <button
              type="button"
              className="btn btn-success"
              disabled={isSubmitting}
              onClick={() => void handleConfirm()}
            >
              {isReconciling ? 'Reconciling…' : isSubmitting ? 'Saving…' : 'Save reservation'}
            </button>
          </div>
        </section>
      )}
    </>
  )
}

interface CreationProgressProps {
  step: CreationStep
  isStaff: boolean
}

function CreationProgress({ step, isStaff }: CreationProgressProps) {
  const current = step === 'details' ? 1 : step === 'review' ? 2 : 3
  return (
    <ol className="creation-progress list-unstyled d-flex flex-wrap gap-2 mb-4" aria-label="Reservation creation progress">
      {['Booking details', 'Review', 'Saved'].map((label, index) => (
        <li
          key={label}
          className={`creation-progress-item ${current >= index + 1 ? 'is-active' : ''}`}
          aria-current={current === index + 1 ? 'step' : undefined}
        >
          <span>{index + 1}</span> {index === 0 && isStaff ? 'Prosumer and booking' : label}
        </li>
      ))}
    </ol>
  )
}

function CreationErrorAlert({ error }: { error: CreationErrorPresentation }) {
  return (
    <div className={`alert ${error.outcomeUnknown ? 'alert-warning' : 'alert-danger'}`} role="alert">
      <h2 className="h6 alert-heading">{error.title}</h2>
      <p className="mb-0">{error.message}</p>
    </div>
  )
}

function ReviewDetail({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  )
}

interface SavedReservationSummaryProps {
  reservation: ReservationDetail
  selectedProsumer: EligibleProsumer | null
  selectedStation: StationSummary | null
  selectedSlot: SlotSummary | null
  returnTo: string
}

function SavedReservationSummary({
  reservation,
  selectedProsumer,
  selectedStation,
  selectedSlot,
  returnTo,
}: SavedReservationSummaryProps) {
  return (
    <section className="card border-0 shadow-sm reservation-saved-summary" aria-labelledby="saved-reservation-title">
      <div className="card-body p-4 p-lg-5">
        <div className="saved-check mb-3" aria-hidden="true">✓</div>
        <div className="d-flex flex-wrap justify-content-between align-items-start gap-3 mb-4">
          <div>
            <p className="page-eyebrow mb-1">Reservation saved</p>
            <h2 id="saved-reservation-title" className="h3 mb-1">
              {formatReservationReference(reservation.id)}
            </h2>
            <p className="text-body-secondary mb-0">
              Requested {formatDateTime(reservation.createdAtUtc)}
            </p>
          </div>
          <StatusBadge status={reservation.status} />
        </div>

        <dl className="detail-grid mb-4">
          <ReviewDetail
            label="Prosumer"
            value={reservation.prosumerFullName
              ? `${reservation.prosumerFullName} (${reservation.prosumerNic})`
              : selectedProsumer
                ? `${selectedProsumer.fullName} (${selectedProsumer.nic})`
                : reservation.prosumerNic}
          />
          <ReviewDetail label="Station" value={reservation.stationName ?? selectedStation?.name ?? reservation.stationId} />
          <ReviewDetail
            label="Slot"
            value={selectedSlot
              ? formatSlotRange(selectedSlot.startTimeUtc, selectedSlot.endTimeUtc)
              : formatSlotRange(reservation.scheduledStartTimeUtc, reservation.scheduledEndTimeUtc)}
          />
          <ReviewDetail label="Requested energy" value={formatEnergy(reservation.requestedEnergyKwh)} />
        </dl>

        <div className="d-flex flex-wrap gap-2">
          <Link
            className="btn btn-success"
            to={buildReservationDetailPath(reservation.id, returnTo)}
          >
            View reservation details
          </Link>
          <Link className="btn btn-outline-secondary" to={returnTo}>
            Return to reservation list
          </Link>
        </div>
      </div>
    </section>
  )
}

async function submitOnce(
  request: CreateReservationRequest,
  idempotencyKey: string,
  isStaff: boolean,
): Promise<ReservationDetail> {
  const controller = new AbortController()
  const timeout = window.setTimeout(() => controller.abort(), 15_000)
  try {
    return isStaff
      ? await reservationApi.createForProsumer(request, idempotencyKey, controller.signal)
      : await reservationApi.createOwn(request, idempotencyKey, controller.signal)
  } finally {
    window.clearTimeout(timeout)
  }
}

async function findCreatedReservation(
  request: CreateReservationRequest,
  canFilterByProsumer: boolean,
  startedAtEpochMs: number,
  actorNic: string | undefined,
): Promise<ReservationDetail | null> {
  const response = await reservationApi.list({
    view: 'All',
    prosumerNic: canFilterByProsumer ? request.targetProsumerNic : undefined,
    page: 1,
    pageSize: 100,
  })
  const match = response.items.find((reservation) =>
    reservation.slotId === request.slotId &&
    reservation.requestedEnergyKwh === request.requestedEnergyKwh &&
    (!request.targetProsumerNic || reservation.prosumerNic === request.targetProsumerNic) &&
    Date.parse(reservation.createdAtUtc) >= startedAtEpochMs - 2_000,
  )

  if (!match) {
    return null
  }

  const detail = await reservationApi.getById(match.id)
  return actorNic && detail.createdByActorNic !== actorNic ? null : detail
}
