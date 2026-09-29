import { ApiError } from '../../api/apiClient'
import type { StationSummary } from '../stations/stationTypes'
import type { CreateStaffRequest, StaffRole, UserAccount } from './userTypes'
import { staffRoles } from './userTypes'

// ---- Display helpers ---------------------------------------------------------------

/** Client-side text filter over the rows already returned by the API (display only). */
export function filterUsers(users: readonly UserAccount[], search: string): UserAccount[] {
  const term = search.trim().toLowerCase()
  if (!term) return [...users]
  return users.filter((user) =>
    [user.nic, user.fullName, user.email, user.phone].some((value) => value.toLowerCase().includes(term)))
}

export function formatRole(role: string): string {
  return role === 'GridOperator' ? 'Grid Operator' : role
}

export function formatUtcDateTime(value: string | null | undefined): string {
  if (!value) return 'Not recorded'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return 'Not recorded'
  return new Intl.DateTimeFormat('en-GB', {
    dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Colombo',
  }).format(date)
}

export function stationLabel(stationId: string | null, stations: readonly StationSummary[]): string {
  if (!stationId) return 'Not assigned'
  const station = stations.find((item) => item.id === stationId)
  return station ? station.name : `Station ${stationId.slice(-6)}`
}

// ---- Action availability (UI hint only; the API remains authoritative) ----------------

export interface AccountActions {
  canActivate: boolean
  canReactivate: boolean
  canDeactivate: boolean
  canAssignStation: boolean
}

export function getAccountActions(user: UserAccount, currentNic: string | undefined): AccountActions {
  return {
    canActivate: user.status === 'PendingActivation',
    canReactivate: user.status === 'Deactivated',
    canDeactivate: user.status === 'Active' && user.nic !== currentNic,
    canAssignStation: user.role === 'GridOperator',
  }
}

// ---- Staff form -------------------------------------------------------------------

export interface StaffFormValues {
  nic: string
  fullName: string
  email: string
  phone: string
  address: string
  password: string
  confirmPassword: string
  role: StaffRole | ''
  assignedStationId: string
}

export type StaffFormErrors = Partial<Record<keyof StaffFormValues, string>>

export const emptyStaffForm: StaffFormValues = {
  nic: '', fullName: '', email: '', phone: '', address: '',
  password: '', confirmPassword: '', role: '', assignedStationId: '',
}

const nicPattern = /^([0-9]{9}[VvXx]|[0-9]{12})$/
const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** Mirrors CreateStaffUserDto's annotations for immediate feedback; the API re-validates. */
export function validateStaffForm(values: StaffFormValues): StaffFormErrors {
  const errors: StaffFormErrors = {}
  const nic = values.nic.trim()
  const fullName = values.fullName.trim()
  const email = values.email.trim()
  const phone = values.phone.trim()

  if (!nic) errors.nic = 'NIC is required.'
  else if (!nicPattern.test(nic)) errors.nic = 'NIC must be 9 digits followed by V or X, or 12 digits.'
  if (!fullName) errors.fullName = 'Full name is required.'
  else if (fullName.length > 100) errors.fullName = 'Full name cannot be longer than 100 characters.'
  if (!email) errors.email = 'Email is required.'
  else if (email.length > 100 || !emailPattern.test(email)) errors.email = 'Enter a valid email address.'
  if (!phone) errors.phone = 'Phone is required.'
  else if (phone.length > 20) errors.phone = 'Phone cannot be longer than 20 characters.'
  if (values.address.trim().length > 200) errors.address = 'Address cannot be longer than 200 characters.'
  if (values.password.length < 8) errors.password = 'Password must be at least 8 characters.'
  else if (values.password.length > 100) errors.password = 'Password cannot be longer than 100 characters.'
  if (values.confirmPassword !== values.password) errors.confirmPassword = 'Passwords do not match.'
  if (!staffRoles.some((role) => role === values.role)) errors.role = 'Choose Backoffice or Grid Operator.'

  return errors
}

/** Builds the POST /api/users body: trimmed, no confirm field, station only for Grid Operators. */
export function toCreateStaffRequest(values: StaffFormValues): CreateStaffRequest {
  const role = values.role as StaffRole
  const request: CreateStaffRequest = {
    nic: values.nic.trim(),
    fullName: values.fullName.trim(),
    email: values.email.trim(),
    phone: values.phone.trim(),
    password: values.password,
    role,
  }
  const address = values.address.trim()
  if (address) request.address = address
  const stationId = values.assignedStationId.trim()
  if (role === 'GridOperator' && stationId) request.assignedStationId = stationId
  return request
}

// ---- API error presentation --------------------------------------------------------

export interface ActionFeedback {
  tone: 'success' | 'danger' | 'warning'
  message: string
}

/** Turns an API failure into a safe, user-facing message. Only the API's own message is shown. */
export function describeActionError(error: unknown): ActionFeedback {
  if (!(error instanceof ApiError)) {
    return { tone: 'danger', message: 'An unexpected error occurred. Please try again.' }
  }

  switch (true) {
    case error.status === 0:
      return { tone: 'danger', message: error.message }
    case error.status === 401:
      return { tone: 'warning', message: 'Your session has expired. Please sign in again.' }
    case error.status === 403:
      return { tone: 'danger', message: `Access denied. ${error.message}` }
    case error.status === 400 || error.status === 404 || error.status === 409:
      return { tone: 'warning', message: error.message }
    default:
      return { tone: 'danger', message: 'The server could not complete the request. Please try again.' }
  }
}

/** Maps ASP.NET validation errors (e.g. "Nic", "$.email") onto form field names. */
export function fieldErrorsFromApiError(error: unknown): StaffFormErrors {
  if (!(error instanceof ApiError) || !error.problem?.errors) return {}
  const known = Object.keys(emptyStaffForm) as (keyof StaffFormValues)[]
  const result: StaffFormErrors = {}
  for (const [key, messages] of Object.entries(error.problem.errors)) {
    const normalised = key.replace(/^\$\./, '').toLowerCase()
    const field = known.find((name) => name.toLowerCase() === normalised)
    if (field && messages.length > 0) result[field] = messages[0]
  }
  return result
}
