import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { stationApi } from '../../api/stationApi'
import { userApi } from '../../api/userApi'
import type { StationSummary } from '../stations/stationTypes'
import { FeedbackAlert } from './FeedbackAlert'
import { useUserAdmin } from './userAdminContext'
import {
  describeActionError,
  emptyStaffForm,
  fieldErrorsFromApiError,
  formatRole,
  toCreateStaffRequest,
  validateStaffForm,
  type ActionFeedback,
  type StaffFormErrors,
  type StaffFormValues,
} from './userAdminModel'
import { staffRoles } from './userTypes'

interface StaffAccountFormProps {
  values: StaffFormValues
  errors: StaffFormErrors
  stations: readonly StationSummary[]
  stationsLoading: boolean
  stationsError: string | null
  isSubmitting: boolean
  onChange: (field: keyof StaffFormValues, value: string) => void
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
}

interface FieldProps {
  id: keyof StaffFormValues
  label: string
  type?: string
  autoComplete?: string
  maxLength?: number
  required?: boolean
  hint?: string
  values: StaffFormValues
  errors: StaffFormErrors
  disabled: boolean
  onChange: (field: keyof StaffFormValues, value: string) => void
}

function Field({ id, label, type = 'text', autoComplete, maxLength, required = true, hint, values, errors, disabled, onChange }: FieldProps) {
  const error = errors[id]
  const describedBy = [hint ? `${id}-hint` : null, error ? `${id}-error` : null].filter(Boolean).join(' ') || undefined
  return (
    <div className="col-12 col-md-6">
      <label htmlFor={`staff-${id}`} className="form-label">
        {label}{!required && <span className="text-body-secondary"> (optional)</span>}
      </label>
      <input
        id={`staff-${id}`}
        name={id}
        type={type}
        className={`form-control ${error ? 'is-invalid' : ''}`}
        value={values[id]}
        autoComplete={autoComplete}
        maxLength={maxLength}
        required={required}
        disabled={disabled}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy}
        onChange={(event) => onChange(id, event.target.value)}
      />
      {hint && <div id={`${id}-hint`} className="form-text">{hint}</div>}
      {error && <div id={`${id}-error`} className="invalid-feedback">{error}</div>}
    </div>
  )
}

/** Staff account form; exported for rendering tests. */
export function StaffAccountForm({
  values, errors, stations, stationsLoading, stationsError, isSubmitting, onChange, onSubmit,
}: StaffAccountFormProps) {
  const common = { values, errors, disabled: isSubmitting, onChange }
  const isOperator = values.role === 'GridOperator'

  return (
    <form className="card border-0 shadow-sm" onSubmit={onSubmit} noValidate aria-labelledby="staff-form-title">
      <div className="card-body p-4">
        <h2 id="staff-form-title" className="h5 mb-3">New staff account</h2>
        <div className="row g-3">
          <div className="col-12 col-md-6">
            <label htmlFor="staff-role" className="form-label">Role</label>
            <select
              id="staff-role"
              className={`form-select ${errors.role ? 'is-invalid' : ''}`}
              value={values.role}
              required
              disabled={isSubmitting}
              aria-invalid={errors.role ? true : undefined}
              aria-describedby={errors.role ? 'role-error' : undefined}
              onChange={(event) => onChange('role', event.target.value)}
            >
              <option value="">Choose a role</option>
              {staffRoles.map((role) => <option key={role} value={role}>{formatRole(role)}</option>)}
            </select>
            {errors.role && <div id="role-error" className="invalid-feedback">{errors.role}</div>}
          </div>

          {isOperator && (
            <div className="col-12 col-md-6">
              <label htmlFor="staff-assignedStationId" className="form-label">
                Assigned station <span className="text-body-secondary">(optional)</span>
              </label>
              <select
                id="staff-assignedStationId"
                className={`form-select ${errors.assignedStationId ? 'is-invalid' : ''}`}
                value={values.assignedStationId}
                disabled={isSubmitting || stationsLoading}
                aria-invalid={errors.assignedStationId ? true : undefined}
                aria-describedby="assignedStationId-hint"
                onChange={(event) => onChange('assignedStationId', event.target.value)}
              >
                <option value="">{stationsLoading ? 'Loading active stations…' : 'Assign later'}</option>
                {stations.map((station) => (
                  <option key={station.id} value={station.id}>{station.name} — {station.address}</option>
                ))}
              </select>
              <div id="assignedStationId-hint" className="form-text">
                {stationsError ?? 'Only active stations are listed. You can change this later.'}
              </div>
              {errors.assignedStationId && <div className="invalid-feedback">{errors.assignedStationId}</div>}
            </div>
          )}

          <Field id="nic" label="NIC" maxLength={12} autoComplete="off" hint="9 digits followed by V or X, or 12 digits." {...common} />
          <Field id="fullName" label="Full name" maxLength={100} autoComplete="name" {...common} />
          <Field id="email" label="Email" type="email" maxLength={100} autoComplete="email" {...common} />
          <Field id="phone" label="Phone" type="tel" maxLength={20} autoComplete="tel" {...common} />
          <Field id="address" label="Address" maxLength={200} autoComplete="street-address" required={false} {...common} />
          <div className="w-100" aria-hidden="true" />
          <Field id="password" label="Temporary password" type="password" maxLength={100} autoComplete="new-password" hint="At least 8 characters. Share it with the staff member securely." {...common} />
          <Field id="confirmPassword" label="Confirm password" type="password" maxLength={100} autoComplete="new-password" {...common} />
        </div>
        <div className="d-flex flex-wrap justify-content-end gap-2 mt-4">
          <Link to="/users" className="btn btn-outline-secondary">Cancel</Link>
          <button type="submit" className="btn btn-success" disabled={isSubmitting}>
            {isSubmitting ? 'Creating account…' : 'Create account'}
          </button>
        </div>
      </div>
    </form>
  )
}

/** Backoffice creation of Backoffice and Grid Operator accounts (POST /api/users). */
export function CreateStaffPage() {
  const navigate = useNavigate()
  const { notifyChanged } = useUserAdmin()
  const [values, setValues] = useState<StaffFormValues>(emptyStaffForm)
  const [errors, setErrors] = useState<StaffFormErrors>({})
  const [feedback, setFeedback] = useState<ActionFeedback | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [stations, setStations] = useState<StationSummary[]>([])
  const [stationsLoading, setStationsLoading] = useState(true)
  const [stationsError, setStationsError] = useState<string | null>(null)

  useEffect(() => {
    document.title = 'Create staff account | SolarGrid Exchange'
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    stationApi.listActive(controller.signal)
      .then((response) => { setStations(response.items.filter((station) => station.isActive)); setStationsError(null) })
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) {
          setStationsError(`Stations could not be loaded: ${describeActionError(requestError).message} You can assign a station later.`)
        }
      })
      .finally(() => { if (!controller.signal.aborted) setStationsLoading(false) })
    return () => controller.abort()
  }, [])

  function change(field: keyof StaffFormValues, value: string) {
    setValues((current) => ({
      ...current,
      [field]: value,
      ...(field === 'role' && value !== 'GridOperator' ? { assignedStationId: '' } : {}),
    }))
    setErrors((current) => ({ ...current, [field]: undefined }))
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setFeedback(null)
    const validation = validateStaffForm(values)
    setErrors(validation)
    if (Object.keys(validation).length > 0) {
      setFeedback({ tone: 'warning', message: 'Check the highlighted fields and try again.' })
      return
    }

    setIsSubmitting(true)
    try {
      const created = await userApi.createStaff(toCreateStaffRequest(values))
      notifyChanged()
      navigate('/users', {
        replace: true,
        state: { notice: `${formatRole(created.role)} account created for ${created.fullName}.` },
      })
    } catch (requestError) {
      setErrors(fieldErrorsFromApiError(requestError))
      setFeedback(describeActionError(requestError))
      setIsSubmitting(false)
    }
  }

  return (
    <>
      <FeedbackAlert feedback={feedback} onDismiss={() => setFeedback(null)} />
      <StaffAccountForm
        values={values}
        errors={errors}
        stations={stations}
        stationsLoading={stationsLoading}
        stationsError={stationsError}
        isSubmitting={isSubmitting}
        onChange={change}
        onSubmit={(event) => { void submit(event) }}
      />
    </>
  )
}
