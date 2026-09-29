import { useEffect, useMemo, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { stationApi } from '../../api/stationApi'
import { userApi } from '../../api/userApi'
import { useAuth } from '../../auth/useAuth'
import type { UserRole } from '../../auth/authTypes'
import { userRoles } from '../../auth/authTypes'
import { ApiErrorState } from '../../components/ApiErrorState'
import { ConfirmationDialog } from '../../components/ConfirmationDialog'
import { EmptyState } from '../../components/EmptyState'
import { LoadingState } from '../../components/LoadingState'
import { StatusBadge } from '../../components/StatusBadge'
import type { StationSummary } from '../stations/stationTypes'
import { describeConfirmation, type AccountAction } from './accountActions'
import { FeedbackAlert } from './FeedbackAlert'
import { StationAssignmentDialog } from './StationAssignmentDialog'
import { useAccountAdministration } from './useAccountAdministration'
import { useUserAdmin } from './userAdminContext'
import {
  describeActionError,
  filterUsers,
  formatRole,
  getAccountActions,
  stationLabel,
  type ActionFeedback,
} from './userAdminModel'
import type { AccountStatus, UserAccount } from './userTypes'
import { accountStatuses } from './userTypes'

interface UserListStateProps {
  loading: boolean
  error: unknown
  users: UserAccount[] | null
  search: string
  stations: readonly StationSummary[]
  currentNic?: string
  onRetry: () => void
  onAction: (action: AccountAction) => void
}

/** Table and loading/empty/error states for the user list; exported for rendering tests. */
export function UserListState({
  loading, error, users, search, stations, currentNic, onRetry, onAction,
}: UserListStateProps) {
  if (loading) return <LoadingState label="Loading users…" />
  if (error) return <ApiErrorState error={error} resourceName="users" onRetry={onRetry} />
  if (!users || users.length === 0) {
    return <EmptyState title="No users found" description="No accounts match the selected role and status." />
  }

  const visible = filterUsers(users, search)
  if (visible.length === 0) {
    return <EmptyState title="No matching users" description="No loaded account matches that NIC, name, email or phone." />
  }

  return (
    <div className="card border-0 shadow-sm">
      <div className="table-responsive">
        <table className="table table-hover align-middle mb-0 reservation-table">
          <caption className="visually-hidden">SolarGrid user accounts</caption>
          <thead>
            <tr>
              <th scope="col">NIC</th>
              <th scope="col">Name</th>
              <th scope="col">Contact</th>
              <th scope="col">Role</th>
              <th scope="col">Status</th>
              <th scope="col">Station</th>
              <th scope="col">Deactivation request</th>
              <th scope="col"><span className="visually-hidden">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            {visible.map((user) => {
              const actions = getAccountActions(user, currentNic)
              const base = { nic: user.nic, fullName: user.fullName }
              return (
                <tr key={user.nic}>
                  <td className="text-nowrap">{user.nic}</td>
                  <td>{user.fullName}{user.nic === currentNic && <span className="badge text-bg-light ms-2">You</span>}</td>
                  <td>
                    <div>{user.email}</div>
                    <div className="small text-body-secondary">{user.phone}</div>
                  </td>
                  <td className="text-nowrap">{formatRole(user.role)}</td>
                  <td><StatusBadge status={user.status} /></td>
                  <td>{user.role === 'GridOperator' ? stationLabel(user.assignedStationId, stations) : <span className="text-body-secondary">—</span>}</td>
                  <td>{user.deactivationRequested ? <span className="badge text-bg-warning">Requested</span> : <span className="text-body-secondary">None</span>}</td>
                  <td>
                    <div className="d-flex flex-wrap gap-2 justify-content-end">
                      {actions.canActivate && (
                        <button type="button" className="btn btn-sm btn-success" aria-label={`Activate ${user.fullName}`}
                          onClick={() => onAction({ ...base, kind: 'activate' })}>Activate</button>
                      )}
                      {actions.canReactivate && (
                        <button type="button" className="btn btn-sm btn-outline-success" aria-label={`Reactivate ${user.fullName}`}
                          onClick={() => onAction({ ...base, kind: 'reactivate' })}>Reactivate</button>
                      )}
                      {actions.canAssignStation && (
                        <button type="button" className="btn btn-sm btn-outline-primary" aria-label={`Assign station for ${user.fullName}`}
                          onClick={() => onAction({ ...base, kind: 'assignStation', stationId: user.assignedStationId ?? undefined })}>
                          {user.assignedStationId ? 'Change station' : 'Assign station'}
                        </button>
                      )}
                      {actions.canDeactivate && (
                        <button type="button" className="btn btn-sm btn-outline-danger" aria-label={`Deactivate ${user.fullName}`}
                          onClick={() => onAction({ ...base, kind: 'deactivate' })}>Deactivate</button>
                      )}
                    </div>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      <p className="small text-body-secondary px-3 py-2 mb-0" aria-live="polite">
        Showing {visible.length} of {users.length} accounts
      </p>
    </div>
  )
}

interface LocationNotice {
  notice?: string
}

/** Backoffice list of every account with filters and administration actions. */
export function UserManagementPage() {
  const { session } = useAuth()
  const location = useLocation()
  const { version, notifyChanged } = useUserAdmin()
  const [role, setRole] = useState<UserRole | ''>('')
  const [status, setStatus] = useState<AccountStatus | ''>('')
  const [search, setSearch] = useState('')
  const [users, setUsers] = useState<UserAccount[] | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [loading, setLoading] = useState(true)
  const [retry, setRetry] = useState(0)
  const [stations, setStations] = useState<StationSummary[]>([])
  const [stationsError, setStationsError] = useState<string | null>(null)
  const administration = useAccountAdministration(notifyChanged)
  const notice = (location.state as LocationNotice | null)?.notice
  const [createdNotice, setCreatedNotice] = useState<ActionFeedback | null>(
    notice ? { tone: 'success', message: notice } : null,
  )

  useEffect(() => {
    document.title = 'Users | SolarGrid Exchange'
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    userApi.list({ role: role || undefined, status: status || undefined }, controller.signal)
      .then((response) => { setUsers(response); setError(null) })
      .catch((requestError: unknown) => { if (!controller.signal.aborted) setError(requestError) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [role, status, retry, version])

  useEffect(() => {
    const controller = new AbortController()
    // Station names for the table and the active-station choices for assignment (Member 2 API, read-only).
    stationApi.listAll(controller.signal)
      .then((response) => { setStations(response.items); setStationsError(null) })
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) setStationsError(describeActionError(requestError).message)
      })
    return () => controller.abort()
  }, [])

  const activeStations = useMemo(() => stations.filter((station) => station.isActive), [stations])
  const pending = administration.pendingAction

  function changeRole(value: string) {
    setLoading(true)
    setRole(userRoles.find((item) => item === value) ?? '')
  }

  function changeStatus(value: string) {
    setLoading(true)
    setStatus(accountStatuses.find((item) => item === value) ?? '')
  }

  return (
    <>
      <FeedbackAlert feedback={createdNotice} onDismiss={() => setCreatedNotice(null)} />
      <FeedbackAlert feedback={administration.feedback} onDismiss={administration.dismissFeedback} />

      <form className="card border-0 shadow-sm mb-4" role="search" aria-label="Filter users" onSubmit={(event) => event.preventDefault()}>
        <div className="card-body row g-3 align-items-end">
          <div className="col-12 col-md-5">
            <label className="form-label" htmlFor="user-search">NIC, name, email or phone</label>
            <input id="user-search" type="search" className="form-control" maxLength={100} value={search}
              onChange={(event) => setSearch(event.target.value)} />
          </div>
          <div className="col-12 col-sm-6 col-md-3">
            <label className="form-label" htmlFor="user-role-filter">Role</label>
            <select id="user-role-filter" className="form-select" value={role} onChange={(event) => changeRole(event.target.value)}>
              <option value="">All roles</option>
              {userRoles.map((item) => <option key={item} value={item}>{formatRole(item)}</option>)}
            </select>
          </div>
          <div className="col-12 col-sm-6 col-md-4">
            <label className="form-label" htmlFor="user-status-filter">Status</label>
            <select id="user-status-filter" className="form-select" value={status} onChange={(event) => changeStatus(event.target.value)}>
              <option value="">All statuses</option>
              {accountStatuses.map((item) => <option key={item} value={item}>{item.replace(/([a-z])([A-Z])/g, '$1 $2')}</option>)}
            </select>
          </div>
        </div>
      </form>

      <UserListState
        loading={loading}
        error={error}
        users={users}
        search={search}
        stations={stations}
        currentNic={session?.nic}
        onRetry={() => { setLoading(true); setRetry((value) => value + 1) }}
        onAction={administration.requestAction}
      />

      {pending && pending.kind !== 'assignStation' && (
        <ConfirmationDialog
          isOpen
          {...describeConfirmation(pending)}
          isBusy={administration.isBusy}
          onConfirm={() => { void administration.confirmAction() }}
          onCancel={administration.cancelAction}
        />
      )}
      {pending && pending.kind === 'assignStation' && (
        <StationAssignmentDialog
          key={pending.nic}
          isOpen
          operatorName={pending.fullName}
          currentStationId={pending.stationId ?? null}
          stations={activeStations}
          stationsError={stationsError}
          isBusy={administration.isBusy}
          onConfirm={(stationId) => { void administration.confirmAction(stationId) }}
          onCancel={administration.cancelAction}
        />
      )}
    </>
  )
}
