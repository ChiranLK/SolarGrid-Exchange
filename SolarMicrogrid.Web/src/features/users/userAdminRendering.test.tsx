import { renderToStaticMarkup } from 'react-dom/server'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/apiClient'
import { ConfirmationDialog } from '../../components/ConfirmationDialog'
import type { StationSummary } from '../stations/stationTypes'
import { describeConfirmation } from './accountActions'
import { StaffAccountForm } from './CreateStaffPage'
import { DeactivationRequestsState } from './DeactivationRequestsPage'
import { FeedbackAlert } from './FeedbackAlert'
import { PendingActivationsState } from './PendingActivationsPage'
import { StationAssignmentDialog } from './StationAssignmentDialog'
import { emptyStaffForm } from './userAdminModel'
import { UserListState } from './UserManagementPage'
import { account } from './userTestFixtures'

const stations: StationSummary[] = [
  { id: 'aaaaaaaaaaaaaaaaaaaaaaaa', name: 'North Hub', address: 'North Road', isActive: true },
  { id: 'bbbbbbbbbbbbbbbbbbbbbbbb', name: 'South Hub', address: 'South Road', isActive: true },
]

function listHtml(overrides: Partial<Parameters<typeof UserListState>[0]> = {}) {
  return renderToStaticMarkup(
    <UserListState
      loading={false}
      error={null}
      users={[]}
      search=""
      stations={stations}
      currentNic="200000000001"
      onRetry={vi.fn()}
      onAction={vi.fn()}
      {...overrides}
    />,
  )
}

describe('user list states', () => {
  it('shows loading, empty and error states', () => {
    expect(listHtml({ loading: true })).toContain('Loading users')
    expect(listHtml({ users: [] })).toContain('No users found')
    const error = listHtml({ error: new ApiError(500, 'The request could not be completed.') })
    expect(error).toContain('role="alert"')
    expect(error).toContain('Try again')
  })

  it('shows access denied for 403 and a retry for an unreachable API', () => {
    expect(listHtml({ error: new ApiError(403, 'You do not have permission to perform this action.') })).toContain('Access denied')
    expect(listHtml({ error: new ApiError(0, 'Unable to reach the SolarGrid API.') })).toContain('API unavailable')
  })

  it('renders safe account columns, station name and request state without secrets', () => {
    const html = listHtml({
      users: [
        account({ nic: '200000000003', fullName: 'Grid Op', role: 'GridOperator', assignedStationId: stations[0].id }),
        account({ deactivationRequested: true }),
      ],
    })
    expect(html).toContain('<th scope="col">NIC</th>')
    expect(html).toContain('North Hub')
    expect(html).toContain('Grid Operator')
    expect(html).toContain('Requested')
    expect(html).toContain('Showing 2 of 2 accounts')
    expect(html.toLowerCase()).not.toMatch(/password|hash|token/)
  })

  it('offers only valid actions per row, with accessible names', () => {
    const html = listHtml({
      users: [
        account({ nic: '200000000005', fullName: 'Pending Person', status: 'PendingActivation' }),
        account({ nic: '200000000006', fullName: 'Former Person', status: 'Deactivated' }),
        account({ nic: '200000000004', fullName: 'Active Person', status: 'Active' }),
        account({ nic: '200000000001', fullName: 'Me Myself', role: 'Backoffice', status: 'Active' }),
        account({ nic: '200000000003', fullName: 'Grid Op', role: 'GridOperator', assignedStationId: stations[1].id }),
      ],
    })
    expect(html).toContain('aria-label="Activate Pending Person"')
    expect(html).not.toContain('aria-label="Deactivate Pending Person"')
    expect(html).toContain('aria-label="Reactivate Former Person"')
    expect(html).toContain('aria-label="Deactivate Active Person"')
    expect(html).not.toContain('aria-label="Deactivate Me Myself"')
    expect(html).toContain('aria-label="Assign station for Grid Op"')
    expect(html).toContain('Change station')
    expect(html).not.toContain('aria-label="Assign station for Active Person"')
  })

  it('applies the text filter and shows a no-match state', () => {
    const users = [account({ fullName: 'Amal Silva' }), account({ nic: '200000000011', fullName: 'Nimal Perera' })]
    expect(listHtml({ users, search: 'nimal' })).toContain('Showing 1 of 2 accounts')
    expect(listHtml({ users, search: 'nobody' })).toContain('No matching users')
  })
})

describe('pending activation states', () => {
  const props = { loading: false, error: null, onRetry: vi.fn(), onActivate: vi.fn() }

  it('shows loading, a clear empty state and API errors', () => {
    expect(renderToStaticMarkup(<PendingActivationsState {...props} loading users={null} />)).toContain('Loading pending accounts')
    expect(renderToStaticMarkup(<PendingActivationsState {...props} users={[]} />)).toContain('No accounts awaiting activation')
    expect(renderToStaticMarkup(<PendingActivationsState {...props} users={null} error={new ApiError(401, 'Your session is invalid or has expired. Please sign in again.')} />))
      .toContain('Your session is invalid or has expired')
  })

  it('lists pending Prosumers with an Activate button each', () => {
    const html = renderToStaticMarkup(
      <PendingActivationsState {...props} users={[account({ fullName: 'New Prosumer', status: 'PendingActivation' })]} />,
    )
    expect(html).toContain('New Prosumer')
    expect(html).toContain('aria-label="Activate New Prosumer"')
    expect(html).toContain('<caption class="visually-hidden">Accounts awaiting activation</caption>')
  })
})

describe('deactivation request states', () => {
  const props = { loading: false, error: null, onRetry: vi.fn(), onApprove: vi.fn() }

  it('shows empty and error states', () => {
    expect(renderToStaticMarkup(<DeactivationRequestsState {...props} requests={[]} />)).toContain('No deactivation requests')
    expect(renderToStaticMarkup(<DeactivationRequestsState {...props} requests={null} error={new ApiError(409, 'Conflict')} />))
      .toContain('Conflict')
  })

  it('shows safe request details, a machine-readable timestamp and an approve action', () => {
    const html = renderToStaticMarkup(
      <DeactivationRequestsState
        {...props}
        requests={[{
          nic: '200000000004', fullName: 'Leaving Prosumer', email: 'leaving@example.com', phone: '0770000004',
          status: 'Active', deactivationRequestedAtUtc: '2026-09-20T04:30:00Z',
        }]}
      />,
    )
    expect(html).toContain('Leaving Prosumer')
    expect(html).toContain('dateTime="2026-09-20T04:30:00Z"')
    expect(html).toContain('20 Sept 2026')
    expect(html).toContain('aria-label="Approve deactivation for Leaving Prosumer"')
    expect(html).toContain('does not currently support rejecting')
  })
})

describe('create-staff form', () => {
  const baseProps = {
    errors: {}, stations, stationsLoading: false, stationsError: null, isSubmitting: false,
    onChange: vi.fn(), onSubmit: vi.fn(),
  }

  function formHtml(overrides: Partial<Parameters<typeof StaffAccountForm>[0]> = {}) {
    return renderToStaticMarkup(
      <MemoryRouter><StaffAccountForm values={emptyStaffForm} {...baseProps} {...overrides} /></MemoryRouter>,
    )
  }

  it('labels every field and offers only staff roles', () => {
    const html = formHtml()
    for (const id of ['staff-role', 'staff-nic', 'staff-fullName', 'staff-email', 'staff-phone', 'staff-address', 'staff-password', 'staff-confirmPassword']) {
      expect(html).toContain(`for="${id}"`)
    }
    expect(html).toContain('value="Backoffice"')
    expect(html).toContain('value="GridOperator"')
    expect(html).not.toContain('value="Prosumer"')
  })

  it('shows the active-station picker only for Grid Operators', () => {
    expect(formHtml()).not.toContain('staff-assignedStationId')
    expect(formHtml({ values: { ...emptyStaffForm, role: 'Backoffice' } })).not.toContain('staff-assignedStationId')
    const operator = formHtml({ values: { ...emptyStaffForm, role: 'GridOperator' } })
    expect(operator).toContain('staff-assignedStationId')
    expect(operator).toContain('North Hub — North Road')
    expect(operator).toContain('Assign later')
  })

  it('explains a station-loading failure without blocking account creation', () => {
    const html = formHtml({ values: { ...emptyStaffForm, role: 'GridOperator' }, stations: [], stationsError: 'Stations could not be loaded.' })
    expect(html).toContain('Stations could not be loaded.')
    expect(html).toContain('<button type="submit" class="btn btn-success">Create account</button>')
  })

  it('marks invalid fields accessibly and disables submit while sending', () => {
    const html = formHtml({ errors: { nic: 'NIC is required.' }, isSubmitting: true })
    expect(html).toContain('aria-invalid="true"')
    expect(html).toContain('aria-describedby="nic-hint nic-error"')
    expect(html).toContain('NIC is required.')
    expect(html).toContain('Creating account…')
  })
})

describe('dialogs and feedback', () => {
  it('renders an accessible station-assignment dialog listing active stations', () => {
    const html = renderToStaticMarkup(
      <StationAssignmentDialog isOpen operatorName="Grid Op" currentStationId={stations[1].id} stations={stations}
        isBusy={false} onConfirm={vi.fn()} onCancel={vi.fn()} />,
    )
    expect(html).toContain('role="dialog"')
    expect(html).toContain('aria-modal="true"')
    expect(html).toContain('for="station-assignment-select"')
    expect(html).toContain('South Hub')
    expect(html).toMatch(/<option value="bbbbbbbbbbbbbbbbbbbbbbbb" selected="">/)
  })

  it('disables assignment when no active station exists', () => {
    const html = renderToStaticMarkup(
      <StationAssignmentDialog isOpen operatorName="Grid Op" currentStationId={null} stations={[]}
        isBusy={false} onConfirm={vi.fn()} onCancel={vi.fn()} />,
    )
    expect(html).toContain('No active stations available')
  })

  it('renders confirmation dialogs with the action-specific copy', () => {
    const copy = describeConfirmation({ kind: 'deactivate', nic: '200000000004', fullName: 'Active Person' })
    const html = renderToStaticMarkup(<ConfirmationDialog isOpen {...copy} onConfirm={vi.fn()} onCancel={vi.fn()} />)
    expect(html).toContain('Deactivate account?')
    expect(html).toContain('btn-danger')
    expect(html).toContain('aria-modal="true"')
  })

  it('announces success politely and failures assertively', () => {
    expect(renderToStaticMarkup(<FeedbackAlert feedback={{ tone: 'success', message: 'Done.' }} />)).toContain('role="status"')
    const failure = renderToStaticMarkup(<FeedbackAlert feedback={{ tone: 'warning', message: 'Already active.' }} onDismiss={vi.fn()} />)
    expect(failure).toContain('role="alert"')
    expect(failure).toContain('aria-label="Dismiss message"')
    expect(renderToStaticMarkup(<FeedbackAlert feedback={null} />)).toBe('')
  })
})
