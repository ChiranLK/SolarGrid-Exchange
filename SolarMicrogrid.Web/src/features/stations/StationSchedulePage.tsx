import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { stationApi } from '../../api/stationApi'
import { ApiErrorState } from '../../components/ApiErrorState'
import { LoadingState } from '../../components/LoadingState'
import { PageHeader } from '../../components/PageHeader'
import { ScheduleFields } from './ScheduleFields'
import { initialStationValues, toStationInput, validateStation } from './stationFormModel'
import type { OperatingSchedule, Station } from './stationTypes'

export function StationSchedulePage() {
  const { stationId } = useParams()
  const navigate = useNavigate()
  const [station, setStation] = useState<Station | null>(null)
  const [schedule, setSchedule] = useState<OperatingSchedule[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [loadError, setLoadError] = useState<unknown>(null)
  const [saveError, setSaveError] = useState<unknown>(null)
  const [errors, setErrors] = useState<string[]>([])

  useEffect(() => {
    if (!stationId) return
    const controller = new AbortController()
    stationApi.getById(stationId, controller.signal)
      .then((result) => {
        setStation(result)
        setSchedule(initialStationValues(result).operatingSchedule)
        setLoadError(null)
        document.title = `Schedule: ${result.name} | SolarGrid Exchange`
      })
      .catch((error: unknown) => { if (!controller.signal.aborted) setLoadError(error) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [stationId])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!station || !stationId) return
    const values = { ...initialStationValues(station), operatingSchedule: schedule }
    const validationErrors = validateStation(values)
    setErrors(validationErrors)
    if (validationErrors.length) return
    setSaving(true)
    setSaveError(null)
    try {
      // PUT requires the complete station DTO; refresh unrelated fields before saving the schedule.
      const latestStation = await stationApi.getById(stationId)
      const latestValues = { ...initialStationValues(latestStation), operatingSchedule: schedule }
      await stationApi.update(stationId, toStationInput(latestValues))
      navigate(`/stations/${encodeURIComponent(stationId)}`, { state: { notice: 'Operating schedule saved.' } })
    } catch (error) {
      setSaveError(error)
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <div className="mb-3"><Link to={stationId ? `/stations/${encodeURIComponent(stationId)}` : '/stations'}>← Back to station</Link></div>
      <PageHeader eyebrow="Backoffice" title="Edit operating schedule" description="Schedule times are in Sri Lanka time (Asia/Colombo)." />
      {loading ? <LoadingState label="Loading schedule…" /> : loadError ? <ApiErrorState error={loadError} resourceName="station" /> : station ? (
        <form className="card border-0 shadow-sm" onSubmit={submit} noValidate><div className="card-body p-4">
          {saveError !== null && <div className="mb-3"><ApiErrorState error={saveError} resourceName="station" /></div>}
          {errors.length > 0 && <div className="alert alert-danger" role="alert"><ul className="mb-0">{errors.map((error) => <li key={error}>{error}</li>)}</ul></div>}
          <p className="mb-0">Station: <strong>{station.name}</strong></p>
          <ScheduleFields schedule={schedule} onChange={setSchedule} />
          <button className="btn btn-success mt-4" disabled={saving} type="submit">{saving ? 'Saving…' : 'Save schedule'}</button>
        </div></form>
      ) : null}
    </>
  )
}
