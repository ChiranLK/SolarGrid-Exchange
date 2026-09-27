import { useState, type FormEvent } from 'react'
import type { StationFormValues } from './stationFormModel'
import { validateStation } from './stationFormModel'

interface Props {
  initialValues: StationFormValues
  submitLabel: string
  isSaving: boolean
  onSubmit: (values: StationFormValues) => void
}

export function StationForm({ initialValues, submitLabel, isSaving, onSubmit }: Props) {
  const [values, setValues] = useState(initialValues)
  const [errors, setErrors] = useState<string[]>([])

  function update(field: keyof Omit<StationFormValues, 'operatingSchedule'>, value: string) {
    setValues((current) => ({ ...current, [field]: value }))
  }

  function updateDay(index: number, changes: Partial<StationFormValues['operatingSchedule'][number]>) {
    setValues((current) => ({
      ...current,
      operatingSchedule: current.operatingSchedule.map((item, position) => position === index ? { ...item, ...changes } : item),
    }))
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const validationErrors = validateStation(values)
    setErrors(validationErrors)
    if (!validationErrors.length) onSubmit(values)
  }

  return (
    <form onSubmit={submit} noValidate className="card border-0 shadow-sm">
      <div className="card-body p-4">
        {errors.length > 0 && <div className="alert alert-danger" role="alert"><strong>Check these fields:</strong><ul className="mb-0">{errors.map((error) => <li key={error}>{error}</li>)}</ul></div>}
        <div className="row g-3">
          <div className="col-12 col-md-6"><label className="form-label" htmlFor="station-name">Name</label><input id="station-name" className="form-control" value={values.name} maxLength={120} required onChange={(event) => update('name', event.target.value)} /></div>
          <div className="col-12 col-md-6"><label className="form-label" htmlFor="station-address">Address</label><input id="station-address" className="form-control" value={values.address} maxLength={250} required onChange={(event) => update('address', event.target.value)} /></div>
          <div className="col-12"><label className="form-label" htmlFor="station-description">Description</label><textarea id="station-description" className="form-control" value={values.description} maxLength={500} rows={3} onChange={(event) => update('description', event.target.value)} /></div>
          <div className="col-12 col-md-6"><label className="form-label" htmlFor="station-latitude">Latitude (°)</label><input id="station-latitude" className="form-control" type="number" step="any" min="-90" max="90" required value={values.latitude} onChange={(event) => update('latitude', event.target.value)} /></div>
          <div className="col-12 col-md-6"><label className="form-label" htmlFor="station-longitude">Longitude (°)</label><input id="station-longitude" className="form-control" type="number" step="any" min="-180" max="180" required value={values.longitude} onChange={(event) => update('longitude', event.target.value)} /></div>
          <div className="col-12 col-md-6"><label className="form-label" htmlFor="station-generation">Generation capacity (kW)</label><input id="station-generation" className="form-control" type="number" step="any" min="0.01" required value={values.energyGenerationCapacityKw} onChange={(event) => update('energyGenerationCapacityKw', event.target.value)} /></div>
          <div className="col-12 col-md-6"><label className="form-label" htmlFor="station-battery">Battery storage capacity (kWh)</label><input id="station-battery" className="form-control" type="number" step="any" min="0" required value={values.batteryStorageCapacityKwh} onChange={(event) => update('batteryStorageCapacityKwh', event.target.value)} /></div>
        </div>
        <h2 className="h5 mt-4">Operating schedule</h2>
        <p className="text-body-secondary small">Set open days and their local 24-hour times. Closed days do not need times.</p>
        <div className="row g-3">
          {values.operatingSchedule.map((item, index) => (
            <div className="col-12 col-lg-6" key={item.dayOfWeek}>
              <div className="border rounded p-3 h-100">
                <div className="form-check mb-2"><input className="form-check-input" type="checkbox" id={`open-${item.dayOfWeek}`} checked={item.isOpen} onChange={(event) => updateDay(index, { isOpen: event.target.checked })} /><label className="form-check-label fw-semibold" htmlFor={`open-${item.dayOfWeek}`}>{item.dayOfWeek} open</label></div>
                {item.isOpen && <div className="row g-2">
                  <div className="col-6"><label className="form-label" htmlFor={`start-${item.dayOfWeek}`}>Opens</label><input className="form-control" id={`start-${item.dayOfWeek}`} type="time" required value={item.openingTime ?? ''} onChange={(event) => updateDay(index, { openingTime: event.target.value })} /></div>
                  <div className="col-6"><label className="form-label" htmlFor={`end-${item.dayOfWeek}`}>Closes</label><input className="form-control" id={`end-${item.dayOfWeek}`} type="time" required value={item.closingTime ?? ''} onChange={(event) => updateDay(index, { closingTime: event.target.value })} /></div>
                </div>}
              </div>
            </div>
          ))}
        </div>
        <div className="mt-4"><button className="btn btn-success" type="submit" disabled={isSaving}>{isSaving ? 'Saving…' : submitLabel}</button></div>
      </div>
    </form>
  )
}
