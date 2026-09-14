/**
 * The one place an HTTP call is made. Two things live here that would otherwise be repeated in
 * every page: attaching the bearer token, and turning this API's failure shape into something a
 * screen can branch on.
 */

/** RFC 7807, plus this API's `errorDetails` extension member carrying business error codes. */
interface ProblemDetailsBody {
  title?: string
  detail?: string
  errorDetails?: string[]
}

/**
 * A failed call. `codes` holds the API's classified business errors - `SlotAlreadyBooked`,
 * `RoomHasBookedSlots` - which is what a screen should react to; the status is the fallback for
 * failures the API did not classify (a routing miss, a validation error).
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly codes: readonly string[],
    message: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }

  is(code: string): boolean {
    return this.codes.includes(code)
  }
}

/*
 * The token and the session-ended handler are module state, written by AuthContext and by nothing
 * else. The alternative - passing a token into every call - was what the phase 6 probe did, and it
 * puts the credential into the signature of every function that touches the API.
 */
let accessToken: string | null = null
let onUnauthorized: () => void = () => {}

export function setAccessToken(token: string | null): void {
  accessToken = token
}

/**
 * Registers what to do when the API refuses a call that *did* carry a token. There are no refresh
 * tokens by decision, so that 401 means the session is over and the only correct answer is to end
 * it locally too.
 */
export function setOnUnauthorized(handler: () => void): void {
  onUnauthorized = handler
}

async function failureOf(response: Response, authenticated: boolean): Promise<ApiError> {
  if (response.status === 401 && authenticated) {
    onUnauthorized()
  }

  try {
    const problem = (await response.json()) as ProblemDetailsBody
    const codes = problem.errorDetails ?? []

    return new ApiError(
      response.status,
      codes,
      codes.length > 0 ? codes.join(', ') : (problem.detail ?? problem.title ?? `HTTP ${response.status}`),
    )
  } catch {
    // 401 and 403 keep the framework's empty body by decision, so there is nothing to parse.
    return new ApiError(response.status, [], `HTTP ${response.status}`)
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const authenticated = accessToken !== null

  const response = await fetch(path, {
    method,
    headers: {
      ...(authenticated ? { Authorization: `Bearer ${accessToken}` } : {}),
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  if (!response.ok) throw await failureOf(response, authenticated)

  // 204 from a delete, and a 201 whose body we do not read.
  if (response.status === 204) return undefined as T

  return (await response.json()) as T
}

export const api = {
  get: <T,>(path: string) => request<T>('GET', path),
  post: <T,>(path: string, body?: unknown) => request<T>('POST', path, body),
  put: <T,>(path: string, body: unknown) => request<T>('PUT', path, body),
  delete: (path: string) => request<void>('DELETE', path),
}

export const message = (error: unknown): string =>
  error instanceof Error ? error.message : String(error)
