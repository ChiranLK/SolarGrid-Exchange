import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../../api/apiClient'
import { slotApi } from '../../api/slotApi'
import { stationApi } from '../../api/stationApi'
import { ApiErrorState } from '../../components/ApiErrorState'
import { ErrorState } from '../../components/ErrorState'
import { LoadingState } from '../../components/LoadingState'
import { PageHeader } from '../../components/PageHeader'
import { initialSlotValues, toSlotInput, validateSlot, type SlotFormValues } from './slotFormModel'
import type { SlotSummary, Station } from './stationTypes'

export function SlotFormPage() {
  const { stationId, slotId } = useParams()
  const navigate = useNavigate()
  const [station, setStation] = useState<Station | null>(null)
  const [slot, setSlot] = useState<SlotSummary | null>(null)
  const [values, setValues] = useState<SlotFormValues>(initialSlotValues())
  const [errors, setErrors] = useState<string[]>([])
  const [loadError, setLoadError] = useState<unknown>(null)
  const [saveError, setSaveError] = useState<unknown>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!stationId) return
    const controller = new AbortController()
    Promise.all([
      stationApi.getById(stationId, controller.signal),
      slotId ? slotApi.getById(slotId, controller.signal) : Promise.resolve(null),
    ])
      .then(([stationResult, slotResult]) => {
        setStation(stationResult)
        setSlot(slotResult)
        setValues(initialSlotValues(slotResult ?? undefined))
        setLoadError(null)
        document.title = `${slotId ? 'Edit' : 'Create'} slot | SolarGrid Exchange`
      })
      .catch((error: unknown) => { if (!controller.signal.aborted) setLoadError(error) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [stationId, slotId])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!stationId || (slotId && slot?.stationId !== stationId)) return
    const validationErrors = validateSlot(values)
    setErrors(validationErrors)
    if (validationErrors.length) return
    setSaving(true)
    setSaveError(null)
    try {
      const request = toSlotInput(values)
      if (slotId) await slotApi.update(slotId, request)
      else await slotApi.create(stationId, request)
      navigate(`/stations/${encodeURIComponent(stationId)}`, { state: { notice: slotId ? 'Slot updated.' : 'Slot created.' } })
    } catch (error) {
      setSaveError(error)
    } finally {
      setSaving(false)
    }
  }

  const validationDetails = saveError instanceof ApiError && saveError.problem?.errors
    ? Object.entries(saveError.problem.errors).flatMap(([field, messages]) => messages.map((message) => `${field}: ${message}`))
    : []

  return (
    <>
      <div className="mb-3"><Link to={stationId ? `/stations/${encodeURIComponent(stationId)}` : '/stations'}>← Back to station</Link></div>
      <PageHeader eyebrow="Backoffice" title={slotId ? 'Edit energy slot' : 'Create energy slot'} description="Enter UTC times. The slot must fit the station schedule (Asia/Colombo) without overlapping another slot." />
      {loading ? <LoadingState label="Loading slot form…" /> : loadError ? <ApiErrorState error={loadError} resourceName={slotId ? 'slot' : 'station'} /> : slotId && slot?.stationId !== stationId ? <ErrorState title="Slot and station do not match" message="Open this slot from its own station page." /> : !station?.isActive && !slotId ? <ErrorState title="Station is inactive" message="Activate the station before creating a slot." /> : (
        <form className="card border-0 shadow-sm" onSubmit={submit} noValidate><div className="card-body p-4">
          <p>Station: <strong>{station?.name}</strong></p>
          {slot && <p className="text-body-secondary">Current available capacity: {slot.availableCapacityKwh} kWh. Existing reservations are protected.</p>}
          {saveError !== null && <div className="mb-3">{saveError instanceof ApiError && saveError.status === 409
            ? <ErrorState title="Slot change conflicts with current data" message={saveError.message} />
            : <ApiErrorState error={saveError} resourceName="slot" />}{validationDetails.length > 0 && <ul className="alert alert-danger mt-2 mb-0">{validationDetails.map((message) => <li key={message}>{message}</li>)}</ul>}</div>}
          {errors.length > 0 && <div className="alert alert-danger" role="alert"><ul className="mb-0">{errors.map((error) => <li key={error}>{error}</li>)}</ul></div>}
          <div className="row g-3">
            <div className="col-12 col-md-6"><label className="form-label" htmlFor="slot-start">Start (UTC)</label><input id="slot-start" className="form-control" type="datetime-local" required value={values.startUtc} onChange={(event) => setValues((current) => ({ ...current, startUtc: event.target.value }))} /></div>
            <div className="col-12 col-md-6"><label className="form-label" htmlFor="slot-end">End (UTC)</label><input id="slot-end" className="form-control" type="datetime-local" required value={values.endUtc} onChange={(event) => setValues((current) => ({ ...current, endUtc: event.target.value }))} /></div>
            <div className="col-12 col-md-6"><label className="form-label" htmlFor="slot-capacity">Total capacity (kWh)</label><input id="slot-capacity" className="form-control" type="number" min="0.01" step="any" required value={values.totalCapacityKwh} onChange={(event) => setValues((current) => ({ ...current, totalCapacityKwh: event.target.value }))} /></div>
          </div>
          <button className="btn btn-success mt-4" type="submit" disabled={saving}>{saving ? 'Saving…' : slotId ? 'Save slot' : 'Create slot'}</button>
        </div></form>
      )}
    </>
  )
}
