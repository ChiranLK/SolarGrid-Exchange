import type {
  CurrentUserResponse,
  LoginRequest,
  LoginResponse,
} from '../auth/authTypes'
import { apiRequest } from './apiClient'

export const authApi = {
  login(request: LoginRequest): Promise<LoginResponse> {
    return apiRequest<LoginResponse>('/auth/login', {
      method: 'POST',
      body: JSON.stringify(request),
      anonymous: true,
    })
  },

  me(): Promise<CurrentUserResponse> {
    return apiRequest<CurrentUserResponse>('/auth/me')
  },
}
