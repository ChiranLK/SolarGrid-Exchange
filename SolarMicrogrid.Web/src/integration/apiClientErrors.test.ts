import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const request = vi.hoisted(() => vi.fn())

vi.mock('axios', () => ({
  default: { create: () => ({ request }) },
  isAxiosError: () => true,
}))

import { ApiError, apiRequest, setUnauthorizedHandler } from '../api/apiClient'

const storage = new Map<string, string>()
const sessionStorage = {
  getItem: (key: string) => storage.get(key) ?? null,
  setItem: (key: string, value: string) => { storage.set(key, value) },
  removeItem: (key: string) => { storage.delete(key) },
}

beforeEach(() => {
  storage.clear()
  request.mockReset()
  vi.stubGlobal('window', { sessionStorage })
})

afterEach(() => {
  setUnauthorizedHandler(null)
  vi.unstubAllGlobals()
})

describe('central API error integration', () => {
  it('clears an authenticated session and notifies the auth boundary on 401', async () => {
    sessionStorage.setItem('solargrid.auth.session', JSON.stringify({
      token: 'test-token', nic: '', fullName: 'Test', role: 'Backoffice', status: 'Active',
    }))
    const onUnauthorized = vi.fn()
    setUnauthorizedHandler(onUnauthorized)
    request.mockRejectedValue({ response: { status: 401, data: { message: 'Session expired.' } } })

    await expect(apiRequest('/stations')).rejects.toMatchObject({ status: 401, message: 'Session expired.' })
    expect(sessionStorage.getItem('solargrid.auth.session')).toBeNull()
    expect(onUnauthorized).toHaveBeenCalledOnce()
  })

  it.each([403, 404, 409])('preserves the API message and status for HTTP %i', async (status) => {
    request.mockRejectedValue({ response: { status, data: { message: `API response ${status}` } } })
    try {
      await apiRequest('/stations')
      throw new Error('The request unexpectedly succeeded.')
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError)
      expect(error).toMatchObject({ status, message: `API response ${status}` })
    }
  })
})
