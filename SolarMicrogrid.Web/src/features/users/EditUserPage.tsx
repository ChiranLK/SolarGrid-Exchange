import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { userApi } from '../../api/userApi'
import { ApiErrorState } from '../../components/ApiErrorState'
import { LoadingState } from '../../components/LoadingState'
import { FeedbackAlert } from './FeedbackAlert'
import { describeActionError, type ActionFeedback } from './userAdminModel'
import { useUserAdmin } from './userAdminContext'
import type { UpdateUserRequest } from './userTypes'

type EditErrors = Partial<Record<keyof UpdateUserRequest, string>>

const emptyValues: UpdateUserRequest = { fullName: '', email: '', phone: '', address: '' }
const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** Validates the editable contact fields before the authoritative API validation. */
function validate(values: UpdateUserRequest): EditErrors {
  const errors: EditErrors = {}
  if (!values.fullName.trim()) errors.fullName = 'Full name is required.'
  else if (values.fullName.trim().length > 100) errors.fullName = 'Full name cannot exceed 100 characters.'
  if (!emailPattern.test(values.email.trim()) || values.email.trim().length > 100) errors.email = 'Enter a valid email address.'
  if (!values.phone.trim()) errors.phone = 'Phone is required.'
  else if (values.phone.trim().length > 20) errors.phone = 'Phone cannot exceed 20 characters.'
  if ((values.address ?? '').trim().length > 200) errors.address = 'Address cannot exceed 200 characters.'
  return errors
}

/** Backoffice editor for an existing account's non-security profile fields. */
export function EditUserPage() {
  const { nic = '' } = useParams()
  const navigate = useNavigate()
  const { notifyChanged } = useUserAdmin()
  const [values, setValues] = useState<UpdateUserRequest>(emptyValues)
  const [errors, setErrors] = useState<EditErrors>({})
  const [feedback, setFeedback] = useState<ActionFeedback | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<unknown>(null)
  const [retry, setRetry] = useState(0)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    document.title = 'Edit user | SolarGrid Exchange'
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    userApi.get(nic, controller.signal)
      .then((user) => {
        setValues({ fullName: user.fullName, email: user.email, phone: user.phone, address: user.address ?? '' })
        setLoadError(null)
      })
      .catch((error: unknown) => { if (!controller.signal.aborted) setLoadError(error) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [nic, retry])

  function change(field: keyof UpdateUserRequest, value: string) {
    setValues((current) => ({ ...current, [field]: value }))
    setErrors((current) => ({ ...current, [field]: undefined }))
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const validation = validate(values)
    setErrors(validation)
    if (Object.keys(validation).length > 0) {
      setFeedback({ tone: 'warning', message: 'Check the highlighted fields and try again.' })
      return
    }

    setSaving(true)
    setFeedback(null)
    try {
      const updated = await userApi.update(nic, {
        fullName: values.fullName.trim(),
        email: values.email.trim(),
        phone: values.phone.trim(),
        address: values.address?.trim() || null,
      })
      notifyChanged()
      navigate('/users', { replace: true, state: { notice: `Details updated for ${updated.fullName}.` } })
    } catch (error) {
      setFeedback(describeActionError(error))
      setSaving(false)
    }
  }

  if (loading) return <LoadingState label="Loading user details…" />
  if (loadError) return <ApiErrorState error={loadError} resourceName="user" onRetry={() => {
    setLoading(true)
    setRetry((value) => value + 1)
  }} />

  const fields: Array<{ field: keyof UpdateUserRequest; label: string; type: string; maxLength: number; required: boolean }> = [
    { field: 'fullName', label: 'Full name', type: 'text', maxLength: 100, required: true },
    { field: 'email', label: 'Email', type: 'email', maxLength: 100, required: true },
    { field: 'phone', label: 'Phone', type: 'tel', maxLength: 20, required: true },
    { field: 'address', label: 'Address', type: 'text', maxLength: 200, required: false },
  ]

  return (
    <>
      <FeedbackAlert feedback={feedback} onDismiss={() => setFeedback(null)} />
      <form className="card border-0 shadow-sm" onSubmit={(event) => { void submit(event) }} noValidate>
        <div className="card-body p-4">
          <h2 className="h5 mb-1">Edit user details</h2>
          <p className="text-body-secondary mb-4">NIC {nic}. Role, status and password are managed separately.</p>
          <div className="row g-3">
            {fields.map(({ field, label, type, maxLength, required }) => (
              <div className="col-12 col-md-6" key={field}>
                <label className="form-label" htmlFor={`edit-${field}`}>{label}{!required && ' (optional)'}</label>
                <input
                  id={`edit-${field}`}
                  className={`form-control ${errors[field] ? 'is-invalid' : ''}`}
                  type={type}
                  maxLength={maxLength}
                  required={required}
                  disabled={saving}
                  value={values[field] ?? ''}
                  onChange={(event) => change(field, event.target.value)}
                />
                {errors[field] && <div className="invalid-feedback">{errors[field]}</div>}
              </div>
            ))}
          </div>
          <div className="d-flex justify-content-end gap-2 mt-4">
            <Link to="/users" className="btn btn-outline-secondary">Cancel</Link>
            <button type="submit" className="btn btn-success" disabled={saving}>{saving ? 'Saving…' : 'Save changes'}</button>
          </div>
        </div>
      </form>
    </>
  )
}
