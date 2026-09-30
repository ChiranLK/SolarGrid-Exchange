import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { StationSummary } from '../stations/stationTypes'

interface StationAssignmentDialogProps {
  isOpen: boolean
  operatorName: string
  currentStationId: string | null
  /** Active stations from the existing Member 2 station API. */
  stations: readonly StationSummary[]
  stationsError?: string | null
  isBusy: boolean
  onConfirm: (stationId: string) => void
  onCancel: () => void
}

/** Accessible modal for assigning or changing a Grid Operator's station. */
export function StationAssignmentDialog({
  isOpen,
  operatorName,
  currentStationId,
  stations,
  stationsError,
  isBusy,
  onConfirm,
  onCancel,
}: StationAssignmentDialogProps) {
  const selectRef = useRef<HTMLSelectElement>(null)
  const dialogRef = useRef<HTMLElement>(null)
  const isBusyRef = useRef(isBusy)
  const onCancelRef = useRef(onCancel)
  const [selected, setSelected] = useState(currentStationId ?? '')
  const [touched, setTouched] = useState(false)

  useEffect(() => {
    isBusyRef.current = isBusy
    onCancelRef.current = onCancel
  }, [isBusy, onCancel])

  useEffect(() => {
    if (!isOpen) return
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
    selectRef.current?.focus()
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !isBusyRef.current) onCancelRef.current()
      if (event.key === 'Tab') {
        const focusable = dialogRef.current?.querySelectorAll<HTMLElement>(
          'button:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])',
        )
        if (!focusable?.length) return
        const first = focusable[0]
        const last = focusable[focusable.length - 1]
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault()
          last.focus()
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault()
          first.focus()
        }
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => {
      window.removeEventListener('keydown', handleKeyDown)
      if (previousFocus?.isConnected) previousFocus.focus()
    }
  }, [isOpen])

  if (!isOpen) return null

  const error = touched && !selected ? 'Choose an active station.' : null

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setTouched(true)
    if (selected) onConfirm(selected)
  }

  return (
    <div className="dialog-backdrop" role="presentation" onMouseDown={isBusy ? undefined : onCancel}>
      <section
        ref={dialogRef}
        className="card border-0 shadow-lg dialog-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="station-assignment-title"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <form className="card-body p-4" onSubmit={submit} noValidate>
          <h2 id="station-assignment-title" className="h5">Assign station</h2>
          <p className="text-body-secondary">Choose the active station {operatorName} will operate.</p>
          {stationsError && <div className="alert alert-warning" role="alert">{stationsError}</div>}
          <label htmlFor="station-assignment-select" className="form-label">Station</label>
          <select
            id="station-assignment-select"
            ref={selectRef}
            className={`form-select ${error ? 'is-invalid' : ''}`}
            value={selected}
            disabled={isBusy || stations.length === 0}
            aria-invalid={error ? true : undefined}
            aria-describedby={error ? 'station-assignment-error' : undefined}
            onChange={(event) => setSelected(event.target.value)}
          >
            <option value="">{stations.length === 0 ? 'No active stations available' : 'Select a station'}</option>
            {stations.map((station) => (
              <option key={station.id} value={station.id}>{station.name} — {station.address}</option>
            ))}
          </select>
          {error && <div id="station-assignment-error" className="invalid-feedback">{error}</div>}
          <div className="d-flex justify-content-end gap-2 mt-4">
            <button type="button" className="btn btn-outline-secondary" disabled={isBusy} onClick={onCancel}>Cancel</button>
            <button type="submit" className="btn btn-success" disabled={isBusy || stations.length === 0}>
              {isBusy ? 'Working…' : 'Assign station'}
            </button>
          </div>
        </form>
      </section>
    </div>
  )
}
