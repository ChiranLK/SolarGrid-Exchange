import axios, { isAxiosError } from 'axios'
import { clearSession, readSession } from '../auth/sessionStorage'
import { environment } from '../config/environment'

export interface ApiProblem {
  status?: number
  message?: string
  title?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly problem?: ApiProblem

  constructor(status: number, message: string, problem?: ApiProblem) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

interface ApiRequestOptions extends RequestInit {
  anonymous?: boolean
}

let unauthorizedHandler: (() => void) | null = null
const httpClient = axios.create({ baseURL: environment.apiBaseUrl })

export function setUnauthorizedHandler(handler: (() => void) | null): void {
  unauthorizedHandler = handler
}

export async function apiRequest<T>(
  path: string,
  options: ApiRequestOptions = {},
): Promise<T> {
  const { anonymous = false, headers: customHeaders, ...requestInit } = options
  const headers = new Headers(customHeaders)
  const session = readSession()

  headers.set('Accept', 'application/json')
  if (requestInit.body && !(requestInit.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json')
  }
  if (!anonymous && session?.token) {
    headers.set('Authorization', `Bearer ${session.token}`)
  }

  let payload: unknown
  try {
    const response = await httpClient.request<T>({
      url: normalizePath(path),
      method: requestInit.method ?? 'GET',
      headers: Object.fromEntries(headers.entries()),
      data: requestInit.body,
      signal: requestInit.signal ?? undefined,
    })
    payload = response.status === 204 ? undefined : response.data
  } catch (error) {
    if (isAxiosError(error) && error.response) {
      const problem = isApiProblem(error.response.data) ? error.response.data : undefined
      const status = error.response.status

      // 401, or a 403 saying the account itself is no longer active (Member 1 account-status
      // messages), ends the session: every further call would be refused the same way.
      if (!anonymous && (status === 401 || (status === 403 && isAccountStatusProblem(problem)))) {
        clearSession()
        unauthorizedHandler?.()
      }

      throw new ApiError(status, getErrorMessage(status, problem), problem)
    }

    throw new ApiError(0, 'Unable to reach the SolarGrid API. Check that it is running and try again.')
  }

  return payload as T
}

function normalizePath(path: string): string {
  return path.startsWith('/') ? path : `/${path}`
}

// Exact texts the API uses when the signed-in account is pending, deactivated or not active.
export const accountStatusMessages = [
  'This account is awaiting activation.',
  'This account is deactivated. Please contact Backoffice.',
  'This account is not active. Please contact Backoffice.',
] as const

function isAccountStatusProblem(problem: ApiProblem | undefined): boolean {
  const message = problem?.message?.trim().toLowerCase()
  return !!message && accountStatusMessages.some((text) => text.toLowerCase() === message)
}

function isApiProblem(value: unknown): value is ApiProblem {
  return typeof value === 'object' && value !== null
}

function getErrorMessage(status: number, problem?: ApiProblem): string {
  if (problem?.message) {
    return problem.message
  }

  const firstValidationMessage = problem?.errors
    ? Object.values(problem.errors).flat()[0]
    : undefined
  if (firstValidationMessage) {
    return firstValidationMessage
  }
  if (problem?.title) {
    return problem.title
  }

  switch (status) {
    case 401:
      return 'Your session is invalid or has expired. Please sign in again.'
    case 403:
      return 'You do not have permission to perform this action.'
    case 404:
      return 'The requested resource could not be found.'
    default:
      return 'The request could not be completed. Please try again.'
  }
}
