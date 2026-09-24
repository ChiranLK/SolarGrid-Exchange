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

  let response: Response
  try {
    response = await fetch(buildUrl(path), { ...requestInit, headers })
  } catch {
    throw new ApiError(0, 'Unable to reach the SolarGrid API. Check that it is running and try again.')
  }

  const payload = await readPayload(response)
  if (!response.ok) {
    const problem = isApiProblem(payload) ? payload : undefined
    const message = getErrorMessage(response.status, problem)

    if (response.status === 401 && !anonymous) {
      clearSession()
      unauthorizedHandler?.()
    }

    throw new ApiError(response.status, message, problem)
  }

  return payload as T
}

function buildUrl(path: string): string {
  const normalizedPath = path.startsWith('/') ? path : `/${path}`
  return `${environment.apiBaseUrl}${normalizedPath}`
}

async function readPayload(response: Response): Promise<unknown> {
  if (response.status === 204) {
    return undefined
  }

  const text = await response.text()
  if (!text) {
    return undefined
  }

  try {
    return JSON.parse(text) as unknown
  } catch {
    return text
  }
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
