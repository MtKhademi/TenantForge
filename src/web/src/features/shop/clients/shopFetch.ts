import { ApiUnavailableError } from '@/features/auth/authTypes'
import { ShopClientError, shopProblemSchema } from '../contracts/shopContract'

type ShopFetchInit = RequestInit & { json?: unknown }

const REQUEST_TIMEOUT_MS = 8_000
const SESSION_STORAGE_KEY = 'tenantforge:auth:session'

/** Throws ShopClientError for any non-2xx response. Shared by every client. */
async function failed(response: Response): Promise<never> {
  const raw: unknown = await response.json().catch(() => ({}))
  const body = typeof raw === 'object' && raw !== null ? raw : {}
  throw new ShopClientError(shopProblemSchema.parse({ ...body, status: response.status }))
}

function currentAccessToken(): string | null {
  try {
    const raw = window.sessionStorage.getItem(SESSION_STORAGE_KEY)
    if (!raw) return null
    const parsed: unknown = JSON.parse(raw)
    if (typeof parsed !== 'object' || parsed === null) return null
    const token = (parsed as { accessToken?: unknown }).accessToken
    return typeof token === 'string' && token.length > 0 ? token : null
  } catch {
    return null
  }
}

function mergeSignals(timeoutSignal: AbortSignal, callerSignal?: AbortSignal): AbortSignal {
  if (!callerSignal) return timeoutSignal
  const controller = new AbortController()
  function abort() {
    controller.abort()
  }
  if (timeoutSignal.aborted || callerSignal.aborted) {
    controller.abort()
    return controller.signal
  }
  timeoutSignal.addEventListener('abort', abort, { once: true })
  callerSignal.addEventListener('abort', abort, { once: true })
  return controller.signal
}

function build(init: ShopFetchInit, token?: string | null): RequestInit {
  const { json, signal, headers, ...rest } = init
  const nextHeaders = new Headers(headers)
  if (json !== undefined) nextHeaders.set('Content-Type', 'application/json')
  if (token) nextHeaders.set('Authorization', `Bearer ${token}`)

  const timeout = AbortSignal.timeout(REQUEST_TIMEOUT_MS)
  return {
    ...rest,
    signal: mergeSignals(timeout, signal ?? undefined),
    body: json === undefined ? rest.body : JSON.stringify(json),
    headers: nextHeaders,
  }
}

async function request(path: string, init: ShopFetchInit, token?: string | null): Promise<Response> {
  try {
    return await fetch(path, build(init, token))
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error
    throw new ApiUnavailableError()
  }
}

/** Authenticated JSON call. Attaches the current bearer token. */
export async function shopFetch(path: string, init: ShopFetchInit = {}): Promise<unknown> {
  const response = await request(path, init, currentAccessToken())
  if (!response.ok) return failed(response)
  if (response.status === 204) return undefined
  return response.json()
}

/** Anonymous JSON call for public storefront routes. Sends no token. */
export async function shopFetchPublic(path: string, init: ShopFetchInit = {}): Promise<unknown> {
  const response = await request(path, init)
  if (!response.ok) return failed(response)
  if (response.status === 204) return undefined
  return response.json()
}

/** Authenticated call that returns raw bytes instead of JSON. */
export async function shopFetchBlob(path: string, init: ShopFetchInit = {}): Promise<Blob> {
  const response = await request(path, init, currentAccessToken())
  if (!response.ok) return failed(response)
  return response.blob()
}

/**
 * Abort-aware delay. Every mock client simulates latency through THIS
 * function and never through a bare `setTimeout`, so a cancelled request
 * rejects immediately instead of resolving into stale UI.
 */
export function delay(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(new DOMException('Aborted', 'AbortError'))
      return
    }
    const id = window.setTimeout(() => {
      signal?.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    function onAbort() {
      window.clearTimeout(id)
      reject(new DOMException('Aborted', 'AbortError'))
    }
    signal?.addEventListener('abort', onAbort, { once: true })
  })
}
