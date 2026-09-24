import type { PagedEligibleProsumers } from '../features/users/userTypes'
import { apiRequest } from './apiClient'

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
}
