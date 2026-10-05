import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('../../api/apiClient', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/apiClient')>()),
  apiRequest: vi.fn(),
}))

import { ApiError, apiRequest } from '../../api/apiClient'
import { userApi } from '../../api/userApi'
import { describeConfirmation, performAccountAction } from './accountActions'
import {
  describeActionError,
  emptyStaffForm,
  fieldErrorsFromApiError,
  filterUsers,
  getAccountActions,
  stationLabel,
  toCreateStaffRequest,
  validateStaffForm,
  type StaffFormValues,
} from './userAdminModel'
import { account } from './userTestFixtures'

const request = vi.mocked(apiRequest)

const validForm: StaffFormValues = {
  ...emptyStaffForm,
  nic: ' 200000000003 ',
  fullName: ' Test Operator ',
  email: ' operator@example.com ',
  phone: ' 0770000003 ',
  password: 'ValidPass1',
  confirmPassword: 'ValidPass1',
  role: 'GridOperator',
  assignedStationId: '0123456789abcdef01234567',
}

beforeEach(() => {
  request.mockReset()
})

describe('user API contract (methods, paths, payloads)', () => {
  it('lists users with optional server-side role/status filters', () => {
    request.mockResolvedValue([])
    userApi.list()
    expect(request).toHaveBeenLastCalledWith('/users', { signal: undefined })
    userApi.list({ role: 'GridOperator', status: 'Deactivated' })
    expect(request).toHaveBeenLastCalledWith('/users?role=GridOperator&status=Deactivated', { signal: undefined })
  })

  it('reads the pending and deactivation-request queues', () => {
    request.mockResolvedValue([])
    userApi.listPending()
    expect(request).toHaveBeenLastCalledWith('/users/pending', { signal: undefined })
    userApi.listDeactivationRequests()
    expect(request).toHaveBeenLastCalledWith('/users/deactivation-requests', { signal: undefined })
  })

  it('creates staff with a JSON body', () => {
    request.mockResolvedValue(account())
    const body = toCreateStaffRequest(validForm)
    userApi.createStaff(body)
    expect(request).toHaveBeenLastCalledWith('/users', {
      method: 'POST', body: JSON.stringify(body), signal: undefined,
    })
  })

  it('sends activation, deactivation and station assignment as PATCH with an encoded NIC', () => {
    request.mockResolvedValue(account())
    userApi.activate('99123456 7V')
    expect(request).toHaveBeenLastCalledWith('/users/99123456%207V/activate', { method: 'PATCH', signal: undefined })
    userApi.deactivate('200000000004')
    expect(request).toHaveBeenLastCalledWith('/users/200000000004/deactivate', { method: 'PATCH', signal: undefined })
    userApi.assignStation('200000000003', '0123456789abcdef01234567')
    expect(request).toHaveBeenLastCalledWith('/users/200000000003/station', {
      method: 'PATCH', body: JSON.stringify({ stationId: '0123456789abcdef01234567' }), signal: undefined,
    })
  })

  it('keeps the Member 3 eligible-prosumer search unchanged', () => {
    request.mockResolvedValue({ items: [] })
    userApi.searchEligibleProsumers(' ni ')
    expect(request).toHaveBeenLastCalledWith('/users/eligible-prosumers?search=ni&page=1&pageSize=10', { signal: undefined })
  })
})

describe('account actions', () => {
  it.each([
    ['activate', 'activate'],
    ['reactivate', 'activate'],
    ['deactivate', 'deactivate'],
    ['approveDeactivation', 'deactivate'],
  ] as const)('%s calls the %s endpoint then refreshes every view', async (kind, endpoint) => {
    request.mockResolvedValue(account({ fullName: 'Test Prosumer' }))
    const refreshList = vi.fn()
    const refreshCounts = vi.fn()

    const result = await performAccountAction({ kind, nic: '200000000004', fullName: 'Test Prosumer' }, [refreshList, refreshCounts])

    expect(request).toHaveBeenCalledWith(`/users/200000000004/${endpoint}`, { method: 'PATCH', signal: undefined })
    expect(refreshList).toHaveBeenCalledOnce()
    expect(refreshCounts).toHaveBeenCalledOnce()
    expect(result.message).toContain('Test Prosumer')
  })

  it('assigns a station and refreshes', async () => {
    request.mockResolvedValue(account({ role: 'GridOperator', assignedStationId: 'abc' }))
    const refresh = vi.fn()

    await performAccountAction({ kind: 'assignStation', nic: '200000000003', fullName: 'Op', stationId: 'abc' }, [refresh])

    expect(request).toHaveBeenCalledWith('/users/200000000003/station', {
      method: 'PATCH', body: JSON.stringify({ stationId: 'abc' }), signal: undefined,
    })
    expect(refresh).toHaveBeenCalledOnce()
  })

  it.each([401, 403, 409, 500])('does not refresh when the API returns %i', async (status) => {
    request.mockRejectedValue(new ApiError(status, `API ${status}`))
    const refresh = vi.fn()

    await expect(performAccountAction({ kind: 'deactivate', nic: 'x', fullName: 'X' }, [refresh]))
      .rejects.toMatchObject({ status })
    expect(refresh).not.toHaveBeenCalled()
  })

  it('refuses a station assignment without a station and sends nothing', async () => {
    await expect(performAccountAction({ kind: 'assignStation', nic: 'x', fullName: 'X' }, [])).rejects.toThrow('Choose a station')
    expect(request).not.toHaveBeenCalled()
  })

  it('uses destructive confirmation copy for deactivation only', () => {
    const base = { nic: '200000000004', fullName: 'Test Prosumer' }
    expect(describeConfirmation({ ...base, kind: 'deactivate' }).destructive).toBe(true)
    expect(describeConfirmation({ ...base, kind: 'approveDeactivation' }).confirmLabel).toBe('Approve and deactivate')
    expect(describeConfirmation({ ...base, kind: 'activate' }).destructive).toBe(false)
    expect(describeConfirmation({ ...base, kind: 'reactivate' }).message).toContain('200000000004')
  })
})

describe('action availability (UI hint; API stays authoritative)', () => {
  it('offers only the transition valid for the current status', () => {
    expect(getAccountActions(account({ status: 'PendingActivation' }), 'me')).toMatchObject({
      canActivate: true, canReactivate: false, canDeactivate: false,
    })
    expect(getAccountActions(account({ status: 'Deactivated' }), 'me')).toMatchObject({
      canActivate: false, canReactivate: true, canDeactivate: false,
    })
    expect(getAccountActions(account({ status: 'Active' }), 'me')).toMatchObject({
      canActivate: false, canReactivate: false, canDeactivate: true,
    })
  })

  it('hides self-deactivation and limits station assignment to Grid Operators', () => {
    expect(getAccountActions(account({ nic: 'me', role: 'Backoffice' }), 'me').canDeactivate).toBe(false)
    expect(getAccountActions(account({ role: 'GridOperator' }), 'me').canAssignStation).toBe(true)
    expect(getAccountActions(account({ role: 'Prosumer' }), 'me').canAssignStation).toBe(false)
    expect(getAccountActions(account({ role: 'Backoffice' }), 'me').canAssignStation).toBe(false)
  })
})

describe('user list helpers', () => {
  const users = [
    account({ nic: '200000000010', fullName: 'Amal Silva', email: 'amal@example.com' }),
    account({ nic: '991234567V', fullName: 'Nimal Perera', email: 'nimal@example.com', phone: '0711111111' }),
  ]

  it('filters loaded rows by NIC, name, email or phone, case-insensitively', () => {
    expect(filterUsers(users, '').map((user) => user.nic)).toEqual(['200000000010', '991234567V'])
    expect(filterUsers(users, 'NIMAL').map((user) => user.nic)).toEqual(['991234567V'])
    expect(filterUsers(users, '567v').map((user) => user.nic)).toEqual(['991234567V'])
    expect(filterUsers(users, '0711').map((user) => user.nic)).toEqual(['991234567V'])
    expect(filterUsers(users, 'nobody')).toEqual([])
  })

  it('labels stations by name with a safe fallback', () => {
    const stations = [{ id: 'aaaaaaaaaaaaaaaaaaaaaaaa', name: 'North Hub', address: 'A', isActive: true }]
    expect(stationLabel('aaaaaaaaaaaaaaaaaaaaaaaa', stations)).toBe('North Hub')
    expect(stationLabel('bbbbbbbbbbbbbbbbbbbbbbbb', stations)).toBe('Station bbbbbb')
    expect(stationLabel(null, stations)).toBe('Not assigned')
  })
})

describe('staff form', () => {
  it('accepts a valid Grid Operator and builds a trimmed request with the station', () => {
    expect(validateStaffForm(validForm)).toEqual({})
    expect(toCreateStaffRequest(validForm)).toEqual({
      nic: '200000000003',
      fullName: 'Test Operator',
      email: 'operator@example.com',
      phone: '0770000003',
      password: 'ValidPass1',
      role: 'GridOperator',
      assignedStationId: '0123456789abcdef01234567',
    })
  })

  it('never sends the confirmation field and drops the station for Backoffice', () => {
    const body = toCreateStaffRequest({ ...validForm, role: 'Backoffice', address: '  ' })
    expect(body).not.toHaveProperty('confirmPassword')
    expect(body).not.toHaveProperty('assignedStationId')
    expect(body).not.toHaveProperty('address')
  })

  it('reports every invalid field', () => {
    const errors = validateStaffForm({
      ...emptyStaffForm, nic: '123', email: 'bad', password: 'short', confirmPassword: 'other', address: 'a'.repeat(201),
    })
    expect(Object.keys(errors).sort()).toEqual(
      ['address', 'confirmPassword', 'email', 'fullName', 'nic', 'password', 'phone', 'role'],
    )
    expect(errors.nic).toContain('9 digits')
  })

  it('maps ASP.NET validation keys onto form fields', () => {
    const error = new ApiError(400, 'One or more validation errors occurred.', {
      errors: { Nic: ['NIC must be 9 digits followed by V or X, or 12 digits.'], '$.email': ['Enter a valid email address.'], Unknown: ['x'] },
    })
    expect(fieldErrorsFromApiError(error)).toEqual({
      nic: 'NIC must be 9 digits followed by V or X, or 12 digits.',
      email: 'Enter a valid email address.',
    })
    expect(fieldErrorsFromApiError(new Error('x'))).toEqual({})
  })
})

describe('API error presentation', () => {
  it.each([
    [400, 'warning', 'Role must be either Backoffice or GridOperator.'],
    [404, 'warning', 'The specified station does not exist.'],
    [409, 'warning', 'This account is already deactivated.'],
  ] as const)('shows the API message for %i', (status, tone, message) => {
    expect(describeActionError(new ApiError(status, message))).toEqual({ tone, message })
  })

  it('explains 401 as an expired session', () => {
    expect(describeActionError(new ApiError(401, 'x')).message).toBe('Your session has expired. Please sign in again.')
  })

  it('shows 403 as access denied with the API reason', () => {
    expect(describeActionError(new ApiError(403, 'This account is deactivated. Please contact Backoffice.')))
      .toEqual({ tone: 'danger', message: 'Access denied. This account is deactivated. Please contact Backoffice.' })
  })

  it('hides server internals for 5xx and unknown errors', () => {
    expect(describeActionError(new ApiError(500, 'stack trace here')).message).not.toContain('stack')
    expect(describeActionError(new Error('boom')).message).toBe('An unexpected error occurred. Please try again.')
  })

  it('passes through the network message when the API is unreachable', () => {
    expect(describeActionError(new ApiError(0, 'Unable to reach SolarGrid right now.')).message).toBe('Unable to reach SolarGrid right now.')
  })
})
