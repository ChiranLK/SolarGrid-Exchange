import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../api/apiClient'
import { stationApi } from '../../api/stationApi'
import { useAuth } from '../../auth/useAuth'
import { ApiErrorState } from '../../components/ApiErrorState'
import { ConfirmationDialog } from '../../components/ConfirmationDialog'
import { LoadingState } from '../../components/LoadingState'
import { PageHeader } from '../../components/PageHeader'
import { StatusBadge } from '../../components/StatusBadge'
import type { SlotSummary, StationSummary } from '../stations/stationTypes'
import { createIdempotencyKey, reservationApi } from './reservationApi'
import {
  formatDateTime,
  formatEnergy,
  formatReservationReference,
  formatSlotRange,
} from './reservationFormatters'
import { getSafeReservationReturnTo } from './reservationNavigation'
import type { ReservationAllowedActions, ReservationDetail } from './reservationTypes'

type ReservationAction = 'update' | 'cancel' | 'approve' | 'reject'

export function ReservationDetailPage() {
  const { reservationId = '' } = useParams()
  const { session } = useAuth()
  const [searchParameters, setSearchParameters] = useSearchParams()
  const returnTo = getSafeReservationReturnTo(searchParameters.get('returnTo'))
  const requestedAction = parseAction(searchParameters.get('action'))
  const [reservation, setReservation] = useState<ReservationDetail | null>(null)
  const [activeAction, setActiveAction] = useState<ReservationAction | null>(requestedAction)
  const [isLoading, setIsLoading] = useState(true)
  const [isMutating, setIsMutating] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [mutationError, setMutationError] = useState<string | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)
  const [cancelReason, setCancelReason] = useState('')
  const [rejectReason, setRejectReason] = useState('')
  const [reloadToken, setReloadToken] = useState(0)
  const mutationKeys = useRef(new Map<ReservationAction, string>())
  const isStaff = session?.role === 'Backoffice' || session?.role === 'GridOperator'

  const loadReservation = useCallback(async (signal?: AbortSignal) => {
    if (!reservationId) {
      throw new ApiError(404, 'The reservation reference is missing.')
    }
    return reservationApi.getById(reservationId, signal)
  }, [reservationId])

  useEffect(() => {
    document.title = 'Reservation details | SolarGrid Exchange'
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    loadReservation(controller.signal)
      .then(setReservation)
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
  }, [loadReservation, reloadToken])

  function selectAction(action: ReservationAction | null) {
    setMutationError(null)
    setSuccessMessage(null)
    setActiveAction(action)
    setSearchParameters((current) => {
      const next = new URLSearchParams(current)
      if (action) {
        next.set('action', action)
      } else {
        next.delete('action')
      }
      return next
    }, { replace: true })
  }

  function keyFor(action: ReservationAction): string {
    const existing = mutationKeys.current.get(action)
    if (existing) {
      return existing
    }
    const created = createIdempotencyKey(action)
    mutationKeys.current.set(action, created)
    return created
  }

  async function completeMutation(action: ReservationAction, mutation: () => Promise<ReservationDetail>) {
    setIsMutating(true)
    setMutationError(null)
    setSuccessMessage(null)
    try {
      await mutation()
      mutationKeys.current.delete(action)
      const refreshed = await loadReservation()
      setReservation(refreshed)
      setCancelReason('')
      setRejectReason('')
      selectAction(null)
      setSuccessMessage(`Reservation ${completedActionLabel(action)} successfully.`)
    } catch (requestError) {
      setMutationError(
        requestError instanceof ApiError
          ? requestError.message
          : 'The reservation action could not be completed.',
      )
    } finally {
      setIsMutating(false)
    }
  }

  if (isLoading) {
    return <LoadingState label="Loading reservation details…" />
  }

  if (error || !reservation) {
    return (
      <>
        <Link className="btn btn-link px-0 mb-3" to={returnTo}>← Back to reservations</Link>
        <ApiErrorState
          error={error ?? new ApiError(404, 'The reservation could not be found.')}
          resourceName="reservation"
          onRetry={() => {
            setIsLoading(true)
            setError(null)
            setReloadToken((value) => value + 1)
          }}
        />
      </>
    )
  }

  const actions = reservation.allowedActions

  return (
    <>
      <Link className="btn btn-link px-0 mb-2" to={returnTo}>← Back to reservations</Link>
      <PageHeader
        eyebrow="Reservation details"
        title={formatReservationReference(reservation.id)}
        description={`Requested ${formatDateTime(reservation.createdAtUtc)}`}
        actions={<StatusBadge status={reservation.status} />}
      />

      {successMessage && <div className="alert alert-success" role="status">{successMessage}</div>}
      {mutationError && <div className="alert alert-danger" role="alert">{mutationError}</div>}

      <div className="row g-4">
        <div className="col-12 col-xl-8">
          <section className="card border-0 shadow-sm mb-4" aria-labelledby="booking-information-title">
            <div className="card-body p-4">
              <h2 id="booking-information-title" className="h5 mb-4">Booking information</h2>
              <dl className="detail-grid mb-0">
                <Detail label="Full reference" value={reservation.id} mono />
                <Detail
                  label="Prosumer"
                  value={reservation.prosumerFullName
                    ? `${reservation.prosumerFullName} (${reservation.prosumerNic})`
                    : reservation.prosumerNic}
                />
                <Detail label="Station" value={reservation.stationName ?? reservation.stationId} />
                <Detail label="Station address" value={reservation.stationAddress ?? 'Not supplied'} />
                <Detail label="Slot" value={formatSlotRange(reservation.scheduledStartTimeUtc, reservation.scheduledEndTimeUtc)} />
                <Detail label="Slot reference" value={reservation.slotId} mono />
                <Detail label="Slot availability" value={reservation.slotAvailabilityStatus ?? 'Unavailable'} />
                <Detail label="Requested energy" value={formatEnergy(reservation.requestedEnergyKwh)} />
                <Detail label="Current status" value={reservation.status} />
                <Detail label="Version" value={String(reservation.version)} />
                <Detail label="QR eligibility" value={reservation.qrEligible ? 'Eligible' : 'Not eligible'} />
                <Detail label="Requested at" value={formatDateTime(reservation.createdAtUtc)} />
                <Detail label="Last updated" value={formatDateTime(reservation.updatedAtUtc)} />
              </dl>
            </div>
          </section>

          {activeAction === 'update' && actions.canUpdate && (
            <UpdateReservationForm
              reservation={reservation}
              isBusy={isMutating}
              onCancel={() => selectAction(null)}
              onSubmit={(slotId, requestedEnergyKwh) => completeMutation(
                'update',
                () => reservationApi.update(
                  reservation.id,
                  { slotId, requestedEnergyKwh, expectedVersion: reservation.version },
                  keyFor('update'),
                ),
              )}
            />
          )}

          {activeAction === 'cancel' && actions.canCancel && (
            <ReasonForm
              title="Cancel reservation"
              description="Cancellation releases the held allocation when the API confirms the transition."
              label="Cancellation reason (optional)"
              value={cancelReason}
              isBusy={isMutating}
              submitLabel="Cancel reservation"
              destructive
              onChange={setCancelReason}
              onCancel={() => selectAction(null)}
              onSubmit={() => completeMutation(
                'cancel',
                () => reservationApi.cancel(
                  reservation.id,
                  reservation.version,
                  cancelReason,
                  keyFor('cancel'),
                ),
              )}
            />
          )}

          {activeAction === 'reject' && actions.canReject && (
            <ReasonForm
              title="Reject reservation"
              description="A rejection reason is required and will be stored in the audit history."
              label="Rejection reason"
              value={rejectReason}
              isBusy={isMutating}
              submitLabel="Reject reservation"
              destructive
              required
              onChange={setRejectReason}
              onCancel={() => selectAction(null)}
              onSubmit={() => completeMutation(
                'reject',
                () => reservationApi.reject(
                  reservation.id,
                  reservation.version,
                  rejectReason,
                  keyFor('reject'),
                ),
              )}
            />
          )}

          <AuditSection reservation={reservation} showActorDetails={isStaff} />
        </div>

        <div className="col-12 col-xl-4">
          <ActionAvailability
            actions={actions}
            activeAction={activeAction}
            disabled={isMutating}
            onSelect={selectAction}
          />
        </div>
      </div>

      <ConfirmationDialog
        isOpen={activeAction === 'approve' && actions.canApprove}
        title="Approve reservation?"
        message="The API will revalidate the Prosumer, station, slot, schedule, overlap, and held allocation before approval."
        confirmLabel="Approve reservation"
        isBusy={isMutating}
        onCancel={() => selectAction(null)}
        onConfirm={() => void completeMutation(
          'approve',
          () => reservationApi.approve(
            reservation.id,
            reservation.version,
            keyFor('approve'),
          ),
        )}
      />
    </>
  )
}

interface DetailProps {
  label: string
  value: string
  mono?: boolean
}

function Detail({ label, value, mono = false }: DetailProps) {
  return (
    <div>
      <dt>{label}</dt>
      <dd className={mono ? 'font-monospace text-break' : ''}>{value}</dd>
    </div>
  )
}

interface ActionAvailabilityProps {
  actions: ReservationAllowedActions
  activeAction: ReservationAction | null
  disabled: boolean
  onSelect: (action: ReservationAction) => void
}

function ActionAvailability({
  actions,
  activeAction,
  disabled,
  onSelect,
}: ActionAvailabilityProps) {
  const actionRows = [
    { action: 'update' as const, label: 'Update', allowed: actions.canUpdate, reason: actions.updateUnavailableReason, className: 'btn-outline-success' },
    { action: 'cancel' as const, label: 'Cancel', allowed: actions.canCancel, reason: actions.cancelUnavailableReason, className: 'btn-outline-danger' },
    { action: 'approve' as const, label: 'Approve', allowed: actions.canApprove, reason: actions.approveUnavailableReason, className: 'btn-success' },
    { action: 'reject' as const, label: 'Reject', allowed: actions.canReject, reason: actions.rejectUnavailableReason, className: 'btn-danger' },
  ]

  return (
    <aside className="card border-0 shadow-sm sticky-xl-top action-card" aria-labelledby="available-actions-title">
      <div className="card-body p-4">
        <h2 id="available-actions-title" className="h5 mb-3">Available actions</h2>
        <p className="small text-body-secondary">
          Availability and reasons come from the API for your current identity.
        </p>
        <div className="d-grid gap-3">
          {actionRows.map((row) => (
            <div key={row.action}>
              <button
                type="button"
                className={`btn ${row.className} w-100`}
                disabled={!row.allowed || disabled}
                aria-describedby={!row.allowed ? `${row.action}-unavailable` : undefined}
                aria-pressed={activeAction === row.action}
                onClick={() => onSelect(row.action)}
              >
                {row.label}
              </button>
              {!row.allowed && (
                <p id={`${row.action}-unavailable`} className="small text-body-secondary mt-1 mb-0">
                  {row.reason ?? `${row.label} is not available.`}
                </p>
              )}
            </div>
          ))}
        </div>
      </div>
    </aside>
  )
}

interface ReasonFormProps {
  title: string
  description: string
  label: string
  value: string
  submitLabel: string
  isBusy: boolean
  destructive?: boolean
  required?: boolean
  onChange: (value: string) => void
  onSubmit: () => Promise<void>
  onCancel: () => void
}

function ReasonForm({
  title,
  description,
  label,
  value,
  submitLabel,
  isBusy,
  destructive = false,
  required = false,
  onChange,
  onSubmit,
  onCancel,
}: ReasonFormProps) {
  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    void onSubmit()
  }

  return (
    <form className="card border-0 shadow-sm mb-4" onSubmit={handleSubmit}>
      <div className="card-body p-4">
        <h2 className="h5">{title}</h2>
        <p className="text-body-secondary">{description}</p>
        <label htmlFor="reservation-action-reason" className="form-label">{label}</label>
        <textarea
          id="reservation-action-reason"
          className="form-control"
          rows={4}
          maxLength={500}
          required={required}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      </div>
      <div className="card-footer bg-white p-3 d-flex justify-content-end gap-2">
        <button type="button" className="btn btn-outline-secondary" disabled={isBusy} onClick={onCancel}>Close</button>
        <button
          type="submit"
          className={`btn ${destructive ? 'btn-danger' : 'btn-success'}`}
          disabled={isBusy || (required && !value.trim())}
        >
          {isBusy ? 'Working…' : submitLabel}
        </button>
      </div>
    </form>
  )
}

interface UpdateReservationFormProps {
  reservation: ReservationDetail
  isBusy: boolean
  onSubmit: (slotId: string, requestedEnergyKwh: number) => Promise<void>
  onCancel: () => void
}

function UpdateReservationForm({
  reservation,
  isBusy,
  onSubmit,
  onCancel,
}: UpdateReservationFormProps) {
  const [stations, setStations] = useState<StationSummary[]>([])
  const [slots, setSlots] = useState<SlotSummary[]>([])
  const [stationId, setStationId] = useState(reservation.stationId)
  const [slotId, setSlotId] = useState(reservation.slotId)
  const [energy, setEnergy] = useState(String(reservation.requestedEnergyKwh))
  const [isLoading, setIsLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    stationApi.listActive(controller.signal)
      .then((response) => setStations(response.items))
      .catch(() => setLoadError('Active stations could not be loaded.'))
    return () => controller.abort()
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    stationApi.listAvailableSlots(stationId, controller.signal)
      .then((response) => setSlots(response.items))
      .catch(() => setLoadError('Available slots could not be loaded.'))
      .finally(() => {
        if (!controller.signal.aborted) {
          setIsLoading(false)
        }
      })
    return () => controller.abort()
  }, [stationId])

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const requestedEnergy = Number(energy)
    if (slotId && Number.isFinite(requestedEnergy) && requestedEnergy > 0) {
      void onSubmit(slotId, requestedEnergy)
    }
  }

  const currentSlotIsListed = slots.some((slot) => slot.id === reservation.slotId)

  return (
    <form className="card border-0 shadow-sm mb-4" onSubmit={handleSubmit}>
      <div className="card-body p-4">
        <h2 className="h5">Update reservation</h2>
        <p className="text-body-secondary">Material changes to an approved reservation return it to Pending when accepted by the API.</p>
        {loadError && <div className="alert alert-warning" role="alert">{loadError}</div>}
        <div className="row g-3">
          <div className="col-12 col-lg-6">
            <label htmlFor="update-station" className="form-label">Station</label>
            <select
              id="update-station"
              className="form-select"
              value={stationId}
              onChange={(event) => {
                setIsLoading(true)
                setLoadError(null)
                setStationId(event.target.value)
                setSlotId('')
              }}
            >
              {!stations.some((station) => station.id === reservation.stationId) && (
                <option value={reservation.stationId}>{reservation.stationName ?? 'Current station'}</option>
              )}
              {stations.map((station) => <option key={station.id} value={station.id}>{station.name}</option>)}
            </select>
          </div>
          <div className="col-12 col-lg-6">
            <label htmlFor="update-slot" className="form-label">Slot</label>
            <select
              id="update-slot"
              className="form-select"
              required
              disabled={isLoading}
              value={slotId}
              onChange={(event) => setSlotId(event.target.value)}
            >
              <option value="">Choose a slot</option>
              {stationId === reservation.stationId && !currentSlotIsListed && (
                <option value={reservation.slotId}>
                  Current · {formatSlotRange(reservation.scheduledStartTimeUtc, reservation.scheduledEndTimeUtc)}
                </option>
              )}
              {slots.map((slot) => (
                <option key={slot.id} value={slot.id}>
                  {formatSlotRange(slot.startTimeUtc, slot.endTimeUtc)} · {formatEnergy(slot.availableCapacityKwh)} available
                </option>
              ))}
            </select>
          </div>
          <div className="col-12 col-lg-6">
            <label htmlFor="update-energy" className="form-label">Requested energy (kWh)</label>
            <input
              id="update-energy"
              type="number"
              className="form-control"
              min="0.001"
              step="0.001"
              required
              value={energy}
              onChange={(event) => setEnergy(event.target.value)}
            />
          </div>
        </div>
      </div>
      <div className="card-footer bg-white p-3 d-flex justify-content-end gap-2">
        <button type="button" className="btn btn-outline-secondary" disabled={isBusy} onClick={onCancel}>Close</button>
        <button type="submit" className="btn btn-success" disabled={isBusy || isLoading || !slotId}>
          {isBusy ? 'Updating…' : 'Save changes'}
        </button>
      </div>
    </form>
  )
}

interface AuditSectionProps {
  reservation: ReservationDetail
  showActorDetails: boolean
}

function AuditSection({ reservation, showActorDetails }: AuditSectionProps) {
  return (
    <section className="card border-0 shadow-sm" aria-labelledby="reservation-audit-title">
      <div className="card-body p-4">
        <h2 id="reservation-audit-title" className="h5 mb-3">Activity history</h2>
        {showActorDetails && (
          <dl className="detail-grid mb-4">
            <Detail label="Created by" value={reservation.createdByActorNic} mono />
            <Detail label="Last updated by" value={reservation.updatedByActorNic} mono />
            {reservation.approvedAtUtc && (
              <Detail
                label="Approved"
                value={`${formatDateTime(reservation.approvedAtUtc)} by ${reservation.approvedByActorNic ?? 'unknown actor'}`}
              />
            )}
            {reservation.rejectedAtUtc && (
              <Detail
                label="Rejected"
                value={`${formatDateTime(reservation.rejectedAtUtc)} by ${reservation.rejectedByActorNic ?? 'unknown actor'}${reservation.rejectionReason ? ` · ${reservation.rejectionReason}` : ''}`}
              />
            )}
            {reservation.cancelledAtUtc && (
              <Detail
                label="Cancelled"
                value={`${formatDateTime(reservation.cancelledAtUtc)} by ${reservation.cancelledByActorNic ?? 'unknown actor'}${reservation.cancellationReason ? ` · ${reservation.cancellationReason}` : ''}`}
              />
            )}
            {reservation.completedAtUtc && (
              <Detail
                label="Completed"
                value={`${formatDateTime(reservation.completedAtUtc)} by ${reservation.completedByActorNic ?? 'unknown actor'}${reservation.completedVerificationId ? ` · verification ${reservation.completedVerificationId}` : ''}`}
              />
            )}
          </dl>
        )}
        {reservation.statusHistory.length === 0 ? (
          <p className="text-body-secondary mb-0">No lifecycle events were returned.</p>
        ) : (
          <ol className="audit-timeline mb-0">
            {[...reservation.statusHistory].reverse().map((entry) => (
              <li key={`${entry.version}-${entry.changedAtUtc}`}>
                <div className="d-flex flex-wrap align-items-center gap-2 mb-1">
                  <StatusBadge status={entry.toStatus} />
                  <span className="small text-body-secondary">{formatDateTime(entry.changedAtUtc)}</span>
                </div>
                {entry.fromStatus && <div className="small">Changed from {entry.fromStatus}</div>}
                {showActorDetails && (
                  <div className="small text-body-secondary">
                    {entry.actorRole} · {entry.actorNic} · version {entry.version}
                  </div>
                )}
                {entry.reason && <div className="small mt-1">Reason: {entry.reason}</div>}
              </li>
            ))}
          </ol>
        )}
      </div>
    </section>
  )
}

function parseAction(value: string | null): ReservationAction | null {
  return value === 'update' || value === 'cancel' || value === 'approve' || value === 'reject'
    ? value
    : null
}

function completedActionLabel(action: ReservationAction): string {
  switch (action) {
    case 'update':
      return 'updated'
    case 'cancel':
      return 'cancelled'
    case 'approve':
      return 'approved'
    case 'reject':
      return 'rejected'
  }
}
