import type { UserRole } from '../../auth/authTypes'

export interface EligibleProsumer {
  nic: string
  fullName: string
  email: string
}

export interface PagedEligibleProsumers {
  items: EligibleProsumer[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

export const accountStatuses = ['PendingActivation', 'Active', 'Deactivated'] as const

export type AccountStatus = (typeof accountStatuses)[number]

export const staffRoles = ['Backoffice', 'GridOperator'] as const

export type StaffRole = (typeof staffRoles)[number]

/** Safe account view returned by /api/users (UserResponseDto). Never contains a password hash. */
export interface UserAccount {
  nic: string
  fullName: string
  email: string
  phone: string
  address: string | null
  role: UserRole
  status: AccountStatus
  assignedStationId: string | null
  deactivationRequested: boolean
  createdAtUtc: string
}

/** One Prosumer deactivation request (DeactivationRequestResponseDto). */
export interface DeactivationRequest {
  nic: string
  fullName: string
  email: string
  phone: string
  status: AccountStatus
  deactivationRequestedAtUtc: string | null
}

export interface UserListFilter {
  role?: UserRole
  status?: AccountStatus
}

/** Body for POST /api/users (CreateStaffUserDto). */
export interface CreateStaffRequest {
  nic: string
  fullName: string
  email: string
  phone: string
  address?: string
  password: string
  role: StaffRole
  assignedStationId?: string
}

/** Editable profile fields for PUT /api/users/{nic}. */
export interface UpdateUserRequest {
  fullName: string
  email: string
  phone: string
  address?: string | null
}
