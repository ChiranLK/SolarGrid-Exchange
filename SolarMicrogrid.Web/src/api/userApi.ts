import type {
  CreateStaffRequest,
  DeactivationRequest,
  PagedEligibleProsumers,
  UserAccount,
  UserListFilter,
  UpdateUserRequest,
} from '../features/users/userTypes'
import { apiRequest } from './apiClient'

function userPath(nic: string, action: string): string {
  return `/users/${encodeURIComponent(nic)}/${action}`
}

export const userApi = {
  searchEligibleProsumers(
    search: string,
    page = 1,
    signal?: AbortSignal,
  ): Promise<PagedEligibleProsumers> {
    const parameters = new URLSearchParams({
      search: search.trim(),
      page: String(page),
      pageSize: '10',
    })
    return apiRequest<PagedEligibleProsumers>(
      `/users/eligible-prosumers?${parameters.toString()}`,
      { signal },
    )
  },

  list(filter: UserListFilter = {}, signal?: AbortSignal): Promise<UserAccount[]> {
    const parameters = new URLSearchParams()
    if (filter.role) parameters.set('role', filter.role)
    if (filter.status) parameters.set('status', filter.status)
    const query = parameters.toString()
    return apiRequest<UserAccount[]>(query ? `/users?${query}` : '/users', { signal })
  },

  get(nic: string, signal?: AbortSignal): Promise<UserAccount> {
    return apiRequest<UserAccount>(`/users/${encodeURIComponent(nic)}`, { signal })
  },

  update(nic: string, request: UpdateUserRequest, signal?: AbortSignal): Promise<UserAccount> {
    return apiRequest<UserAccount>(`/users/${encodeURIComponent(nic)}`, {
      method: 'PUT', body: JSON.stringify(request), signal,
    })
  },

  listPending(signal?: AbortSignal): Promise<UserAccount[]> {
    return apiRequest<UserAccount[]>('/users/pending', { signal })
  },

  listDeactivationRequests(signal?: AbortSignal): Promise<DeactivationRequest[]> {
    return apiRequest<DeactivationRequest[]>('/users/deactivation-requests', { signal })
  },

  createStaff(request: CreateStaffRequest, signal?: AbortSignal): Promise<UserAccount> {
    return apiRequest<UserAccount>('/users', {
      method: 'POST', body: JSON.stringify(request), signal,
    })
  },

  activate(nic: string, signal?: AbortSignal): Promise<UserAccount> {
    return apiRequest<UserAccount>(userPath(nic, 'activate'), { method: 'PATCH', signal })
  },

  deactivate(nic: string, signal?: AbortSignal): Promise<UserAccount> {
    return apiRequest<UserAccount>(userPath(nic, 'deactivate'), { method: 'PATCH', signal })
  },

  assignStation(nic: string, stationId: string, signal?: AbortSignal): Promise<UserAccount> {
    return apiRequest<UserAccount>(userPath(nic, 'station'), {
      method: 'PATCH', body: JSON.stringify({ stationId }), signal,
    })
  },
}
