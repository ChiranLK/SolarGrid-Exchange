import type { UserAccount } from './userTypes'

/** Synthetic account for tests only; not real NIC or contact data. */
export function account(overrides: Partial<UserAccount> = {}): UserAccount {
  return {
    nic: '200000000004',
    fullName: 'Test Prosumer',
    email: 'prosumer@example.com',
    phone: '0770000004',
    address: null,
    role: 'Prosumer',
    status: 'Active',
    assignedStationId: null,
    deactivationRequested: false,
    createdAtUtc: '2026-09-01T00:00:00Z',
    ...overrides,
  }
}
