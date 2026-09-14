import { createContext, use, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { api, setAccessToken, setOnUnauthorized } from '../api/client'

/** Matches `MeResponse` - what the caller's own token says about them. */
export interface SessionUser {
  id: number
  email: string
  roles: string[]
}

interface LoginResponse {
  accessToken: string
  /** No refresh tokens by decision, so this is the end of the session, not of a token. */
  expiresAtUtc: string
}

interface AuthValue {
  user: SessionUser | null
  isAdmin: boolean
  /** False until the stored token has been read and, if present, checked. Guards must wait. */
  ready: boolean
  accessToken: () => string
  logIn: (email: string, password: string) => Promise<void>
  register: (email: string, password: string) => Promise<void>
  logOut: () => void
}

const AuthContext = createContext<AuthValue | null>(null)

const tokenKey = 'meetingrooms.accessToken'
const expiryKey = 'meetingrooms.expiresAtUtc'

/** The role name, matching `Domain/Roles.cs`. */
const adminRole = 'Admin'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(null)
  const [ready, setReady] = useState(false)

  // The hub's accessTokenFactory is called on every (re)connect, long after the render that built
  // it, so it reads a ref rather than closing over a value that would go stale.
  const tokenRef = useRef<string | null>(null)

  const clearSession = useCallback(() => {
    localStorage.removeItem(tokenKey)
    localStorage.removeItem(expiryKey)
    tokenRef.current = null
    setAccessToken(null)
    setUser(null)
  }, [])

  const adopt = useCallback(async (token: string, expiresAtUtc: string) => {
    localStorage.setItem(tokenKey, token)
    localStorage.setItem(expiryKey, expiresAtUtc)
    tokenRef.current = token
    setAccessToken(token)

    // Roles come from the API, not from decoding the JWT: no base64 parser in the client, and
    // /api/auth/me exists partly to make a claim-type mismatch visible rather than silent.
    setUser(await api.get<SessionUser>('/api/auth/me'))
  }, [])

  // A 401 on a call that carried a token means the session is over - there is nothing to refresh.
  useEffect(() => {
    setOnUnauthorized(clearSession)
  }, [clearSession])

  useEffect(() => {
    const token = localStorage.getItem(tokenKey)
    const expiresAtUtc = localStorage.getItem(expiryKey)

    // An expired token is discarded without a request: the API would only answer 401, and the
    // login screen is where the user is going either way.
    if (token === null || !isStillValid(expiresAtUtc)) {
      clearSession()
      setReady(true)
      return
    }

    let cancelled = false

    void adopt(token, expiresAtUtc)
      .catch(clearSession)
      .finally(() => {
        if (!cancelled) setReady(true)
      })

    return () => {
      cancelled = true
    }
  }, [adopt, clearSession])

  const logIn = useCallback(
    async (email: string, password: string) => {
      const session = await api.post<LoginResponse>('/api/auth/login', { email, password })

      await adopt(session.accessToken, session.expiresAtUtc)
    },
    [adopt],
  )

  const value = useMemo<AuthValue>(
    () => ({
      user,
      isAdmin: user?.roles.includes(adminRole) ?? false,
      ready,
      accessToken: () => tokenRef.current ?? '',
      logIn,

      // Registration issues no token by decision, so logging in is a second call. Doing it here
      // rather than sending the user to the login form is worth the three lines: creating a second
      // account is exactly what a reviewer does to see a booking arrive in another browser.
      register: async (email: string, password: string) => {
        await api.post('/api/auth/register', { email, password })
        await logIn(email, password)
      },

      logOut: clearSession,
    }),
    [user, ready, logIn, clearSession],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}

export function useAuth(): AuthValue {
  const value = use(AuthContext)

  if (value === null) throw new Error('useAuth must be used inside an AuthProvider')

  return value
}

/**
 * Whether a stored session is worth trying. The API serialises `expiresAtUtc` with a trailing `Z`,
 * so it parses as the instant it names; an unparseable value is treated as expired rather than as
 * valid, because the failure that costs a user something is trusting a token the API will refuse.
 */
function isStillValid(expiresAtUtc: string | null): expiresAtUtc is string {
  if (expiresAtUtc === null) return false

  const expiry = Date.parse(expiresAtUtc)

  return !Number.isNaN(expiry) && expiry > Date.now()
}
