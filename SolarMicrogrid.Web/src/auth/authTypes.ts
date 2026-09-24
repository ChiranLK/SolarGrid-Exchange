export const userRoles = ['Backoffice', 'GridOperator', 'Prosumer'] as const

export type UserRole = (typeof userRoles)[number]

export interface AuthSession {
  token: string
  nic: string
  fullName: string
  role: UserRole
  status: string
}

export interface LoginRequest {
  email: string
  password: string
}

export interface LoginResponse {
  token: string
  nic: string
  fullName: string
  role: string
  status: string
}

export interface CurrentUserResponse {
  nic: string
  email: string
  role: string
}

export function isUserRole(value: string): value is UserRole {
  return userRoles.some((role) => role === value)
}
