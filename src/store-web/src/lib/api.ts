/**
 * API client.
 *
 * Two behaviours matter here and are easy to get wrong:
 *
 * 1. The access token lives in memory only, never in localStorage. A token in localStorage is
 *    readable by any script that manages to run on the page, which turns one XSS hole into a
 *    full account takeover. The refresh token is an HttpOnly cookie the server sets, so page
 *    script cannot read it either.
 *
 * 2. A 401 triggers a single silent refresh and one retry, with all concurrent 401s sharing that
 *    one refresh. Without the sharing, ten parallel requests expiring together would fire ten
 *    refreshes, and token rotation would treat nine of them as replay and revoke the session.
 */

const BASE = '/api'

let accessToken: string | null = null
let refreshPromise: Promise<boolean> | null = null

export function setAccessToken(token: string | null): void {
  accessToken = token
}

export function getAccessToken(): string | null {
  return accessToken
}

export class ApiError extends Error {
  // Declared as fields and assigned in the body rather than as constructor parameter
  // properties: this project has `erasableSyntaxOnly` enabled, which forbids TypeScript
  // syntax that has to be *compiled away* rather than simply stripped.
  readonly status: number
  readonly detail?: string
  readonly correlationId?: string

  constructor(status: number, message: string, detail?: string, correlationId?: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.detail = detail
    this.correlationId = correlationId
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
  correlationId?: string
}

/**
 * Refreshes the access token, coalescing concurrent callers onto one in-flight request.
 */
async function refreshAccessToken(): Promise<boolean> {
  refreshPromise ??= (async () => {
    try {
      const response = await fetch(`${BASE}/auth/refresh`, {
        method: 'POST',
        credentials: 'include',
      })

      if (!response.ok) {
        accessToken = null
        return false
      }

      const data = (await response.json()) as { accessToken: string }
      accessToken = data.accessToken
      return true
    } catch {
      accessToken = null
      return false
    } finally {
      // Cleared in `finally` so a failed refresh does not wedge every later request on a
      // permanently-rejected promise.
      refreshPromise = null
    }
  })()

  return refreshPromise
}

interface RequestOptions extends Omit<RequestInit, 'body'> {
  body?: unknown
  /** Set internally to stop a refreshed request from recursing forever. */
  _retried?: boolean
}

export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { body, _retried, headers, ...rest } = options

  const requestHeaders = new Headers(headers)

  if (body !== undefined && !(body instanceof FormData)) {
    requestHeaders.set('Content-Type', 'application/json')
  }

  if (accessToken) {
    requestHeaders.set('Authorization', `Bearer ${accessToken}`)
  }

  const response = await fetch(`${BASE}${path}`, {
    ...rest,
    headers: requestHeaders,
    // Always sent: the refresh token and the guest-cart id both travel as cookies.
    credentials: 'include',
    body:
      body === undefined
        ? undefined
        : body instanceof FormData
          ? body
          : JSON.stringify(body),
  })

  // One silent refresh, one retry. The server sets X-Token-Expired so an expired token is
  // distinguishable from a genuine authorisation failure, which must not trigger a refresh.
  if (response.status === 401 && !_retried && response.headers.get('X-Token-Expired') === 'true') {
    if (await refreshAccessToken()) {
      return apiFetch<T>(path, { ...options, _retried: true })
    }
  }

  if (response.status === 204) {
    return undefined as T
  }

  if (!response.ok) {
    let problem: ProblemDetails = {}

    try {
      problem = (await response.json()) as ProblemDetails
    } catch {
      // A non-JSON error body (a proxy error page, say) still has to produce a usable message.
    }

    throw new ApiError(
      response.status,
      problem.detail ?? problem.title ?? `Request failed (${response.status})`,
      problem.detail,
      problem.correlationId ?? response.headers.get('X-Correlation-Id') ?? undefined,
    )
  }

  return (await response.json()) as T
}

export const api = {
  get: <T>(path: string, init?: RequestOptions) => apiFetch<T>(path, { ...init, method: 'GET' }),
  post: <T>(path: string, body?: unknown, init?: RequestOptions) =>
    apiFetch<T>(path, { ...init, method: 'POST', body }),
  put: <T>(path: string, body?: unknown, init?: RequestOptions) =>
    apiFetch<T>(path, { ...init, method: 'PUT', body }),
  del: <T>(path: string, init?: RequestOptions) => apiFetch<T>(path, { ...init, method: 'DELETE' }),
}

/** Builds a query string, dropping undefined/null/empty values so URLs stay clean and cacheable. */
export function qs(params: Record<string, string | number | boolean | undefined | null>): string {
  const search = new URLSearchParams()

  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(key, String(value))
    }
  }

  const result = search.toString()
  return result ? `?${result}` : ''
}
