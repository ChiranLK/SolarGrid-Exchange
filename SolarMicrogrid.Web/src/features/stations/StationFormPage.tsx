import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../../api/apiClient'
import { stationApi } from '../../api/stationApi'
import { ApiErrorState } from '../../components/ApiErrorState'
import { LoadingState } from '../../components/LoadingState'
import { PageHeader } from '../../components/PageHeader'
import { StationForm } from './StationForm'
import { initialStationValues, toStationInput, type StationFormValues } from './stationFormModel'
import type { Station } from './stationTypes'

export function StationFormPage() {
  const { stationId } = useParams()
  const navigate = useNavigate()
  const isEdit = Boolean(stationId)
  const [station, setStation] = useState<Station | null>(null)
  const [loading, setLoading] = useState(isEdit)
  const [loadError, setLoadError] = useState<unknown>(null)
  const [saveError, setSaveError] = useState<unknown>(null)
  const [saving, setSaving] = useState(false)
  const [retry, setRetry] = useState(0)

  useEffect(() => {
    document.title = `${isEdit ? 'Edit station' : 'Create station'} | SolarGrid Exchange`
    if (!stationId) return
    const controller = new AbortController()
    stationApi.getById(stationId, controller.signal)
      .then((result) => { setStation(result); setLoadError(null) })
      .catch((error: unknown) => { if (!controller.signal.aborted) setLoadError(error) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [stationId, isEdit, retry])

  async function submit(values: StationFormValues) {
    setSaving(true)
    setSaveError(null)
    try {
      const request = toStationInput(values)
      const saved = stationId
        ? await stationApi.update(stationId, request)
        : await stationApi.create(request)
      navigate(`/stations/${encodeURIComponent(saved.id)}`)
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
      <div className="mb-3"><Link to={stationId ? `/stations/${encodeURIComponent(stationId)}` : '/stations'}>← Back to stations</Link></div>
      <PageHeader eyebrow="Backoffice" title={isEdit ? 'Edit station' : 'Create station'} description="The API validates location, capacities, and operating hours before saving." />
      {loading ? <LoadingState label="Loading station…" /> : loadError ? <ApiErrorState error={loadError} resourceName="station" onRetry={() => { setLoading(true); setRetry((value) => value + 1) }} /> : (
        <>
          {saveError && <div className="mb-3"><ApiErrorState error={saveError} resourceName="station" />{validationDetails.length > 0 && <ul className="alert alert-danger mt-2 mb-0">{validationDetails.map((message) => <li key={message}>{message}</li>)}</ul>}</div>}
          <StationForm key={station?.id ?? 'new'} initialValues={initialStationValues(station ?? undefined)} submitLabel={isEdit ? 'Save changes' : 'Create station'} isSaving={saving} onSubmit={submit} />
        </>
      )}
    </>
  )
}
