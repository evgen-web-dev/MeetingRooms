import { useEffect, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { connectToRoom } from './realtime/scheduleConnection'
import type { ConnectionStatus, RoomSubscription } from './realtime/scheduleConnection'

type Status = 'checking' | 'ok' | 'warn' | 'error'

interface CheckResult {
  status: Status
  text: string
}

/** Shape of a SignalR negotiate response, narrowed to the fields that distinguish the
 *  three outcomes. Azure SignalR answers with `url` + `accessToken`; in-process SignalR
 *  answers with `connectionId`. */
interface NegotiateResponse {
  url?: string
  accessToken?: string
  connectionId?: string
  availableTransports?: { transport: string }[]
}

const pending: CheckResult = { status: 'checking', text: 'checking…' }

const statusStyles: Record<Status, string> = {
  checking: 'text-zinc-500',
  ok: 'text-green-700',
  warn: 'text-amber-700',
  error: 'text-red-700',
}

const message = (e: unknown) => (e instanceof Error ? e.message : String(e))

/** Same-origin call, which is the point: it proves the static files and the API are
 *  served by one app. */
async function checkHealth(): Promise<CheckResult> {
  try {
    const response = await fetch('/health')
    const body = await response.text()

    // Body first, then status - the lesson phase 1 learned on negotiate, applied here after a
    // deployed 500 could not be diagnosed after the fact. A failing /health is exactly when its
    // body matters, and the body says which failure it was: the application's own handler answers
    // ProblemDetails JSON carrying errorDetails, while the platform answering for an app that has
    // not finished starting (ASP.NET Core Module 500.3x, during the migrate-and-seed block that
    // runs before app.Run()) returns an HTML page. Discarding it loses the distinction.
    if (!response.ok) throw new Error(`HTTP ${response.status} — ${body.slice(0, 300)}`)

    return { status: 'ok', text: JSON.stringify(JSON.parse(body), null, 2) }
  } catch (e) {
    return { status: 'error', text: `Health check failed: ${message(e)}` }
  }
}

/** Reads the transport out of a negotiate payload: Azure SignalR answers with `url` plus
 *  `accessToken`, in-process SignalR with `connectionId`. */
function describeTransport(negotiate: NegotiateResponse): CheckResult {
  if (negotiate.url) {
    // The access token is deliberately not rendered - only whether one came back.
    return {
      status: negotiate.accessToken ? 'ok' : 'error',
      text: [
        'Azure SignalR',
        `endpoint: ${new URL(negotiate.url).host}`,
        `accessToken: ${negotiate.accessToken ? 'present' : 'MISSING'}`,
      ].join('\n'),
    }
  }

  if (negotiate.connectionId) {
    const transports = (negotiate.availableTransports ?? []).map((t) => t.transport).join(', ')
    return {
      status: 'warn',
      text: [
        'In-process SignalR — no Azure connection string was read',
        `transports: ${transports}`,
      ].join('\n'),
    }
  }

  return {
    status: 'error',
    text: `Unrecognised negotiate response:\n${JSON.stringify(negotiate, null, 2)}`,
  }
}

/** Negotiate is the cheapest proof that SignalR is wired, and the only one available before a
 *  real client connects. It never opens a connection.
 *
 *  Called twice on this page, for two different questions. Without a token it asks whether the
 *  hub is *secured*: since phase 6 the hub carries [Authorize], so 401 is the correct answer and
 *  anything else is the finding. With a token it asks which *transport* is carrying the hub -
 *  the Azure-versus-in-process diagnosis phases 1 and 2 relied on, which an anonymous negotiate
 *  can no longer see, because a refusal has no payload to read it from. */
async function checkRealtime(token?: string): Promise<CheckResult> {
  try {
    const response = await fetch('/hubs/schedule/negotiate?negotiateVersion=1', {
      method: 'POST',
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    })
    const body = await response.text()

    if (response.status === 401 && token === undefined) {
      return {
        status: 'ok',
        text: [
          'Hub is secured — an unauthenticated negotiate is refused (401).',
          'Which transport is carrying it is identified after logging in below.',
        ].join('\n'),
      }
    }

    // A failed negotiate answers in plain text, not JSON, so read the body as text first
    // and surface it: "Azure SignalR Service is not connected yet" is a 500 that means
    // something quite specific, and parsing it as JSON would lose it.
    if (!response.ok) throw new Error(`HTTP ${response.status} — ${body.slice(0, 200)}`)

    return describeTransport(JSON.parse(body) as NegotiateResponse)
  } catch (e) {
    return { status: 'error', text: `Negotiate failed: ${message(e)}` }
  }
}

function Panel({ title, result, children }: { title: string; result: CheckResult; children?: ReactNode }) {
  return (
    <section className="mt-8">
      <h2 className="text-lg font-semibold text-zinc-900">{title}</h2>
      <pre
        className={`mt-2 overflow-x-auto rounded-md bg-zinc-100 p-4 text-sm whitespace-pre-wrap ${statusStyles[result.status]}`}
      >
        {result.text}
      </pre>
      {children}
    </section>
  )
}

export default function App() {
  const [health, setHealth] = useState<CheckResult>(pending)
  const [realtime, setRealtime] = useState<CheckResult>(pending)

  useEffect(() => {
    // StrictMode runs effects twice in development, so both checks fire twice locally.
    // Both are reads with no side effects, so that is harmless rather than worth guarding.
    void checkHealth().then(setHealth)
    void checkRealtime().then(setRealtime)
  }, [])

  return (
    <main className="mx-auto max-w-2xl px-4 py-16">
      <h1 className="text-3xl font-bold tracking-tight text-zinc-900">Meeting Rooms</h1>
      <p className="mt-2 text-zinc-600">
        Frontend shell. The screens arrive in phase 7; this page exists to prove the build
        pipeline reaches the deployed app, and — below — that a booking reaches another browser
        with no refresh.
      </p>

      <Panel title="Backend health" result={health} />

      <Panel title="Realtime transport" result={realtime}>
        <p className="mt-2 text-sm text-zinc-500">
          Anonymous, this asks one question: is the hub secured? Since the hub carries{' '}
          <code>[Authorize]</code>, <strong className="font-medium text-zinc-700">401</strong> is
          the correct answer. The transport diagnosis moved below, where there is a token:{' '}
          <strong className="font-medium text-zinc-700">Azure SignalR</strong> is the deployed
          target; <strong className="font-medium text-zinc-700">in-process</strong> means the
          connection string was not read at all;{' '}
          <strong className="font-medium text-zinc-700">not connected</strong> means it was read
          but the service is unreachable — the normal cold-start window for the first request
          after a deploy.
        </p>
      </Panel>

      <LivePanel />
    </main>
  )
}

/* ------------------------------------------------------------------------------------------
 * Phase 6 probe. Deliberately throwaway: phase 7 replaces this with real screens behind real
 * routing, and deletes it. It exists so the phase's verification is runnable - two browsers on
 * one room, one books, the other updates without a refresh - which is the only way to exercise
 * Azure SignalR and App Service's WebSockets at all.
 *
 * The token is held in component state rather than localStorage. docs/decisions.md does put the
 * JWT in localStorage, but that belongs to phase 7's auth context; scaffolding should not be the
 * thing that first implements a standing decision.
 * ---------------------------------------------------------------------------------------- */

interface Room {
  id: number
  name: string
}

interface ScheduleSlot {
  id: number
  startUtc: string
  endUtc: string
  isBooked: boolean
  isBookedByMe: boolean
}

interface Schedule {
  roomName: string
  timeZoneId: string
  slots: ScheduleSlot[]
}

interface LoginResponse {
  accessToken: string
}

/** RFC 7807, plus this API's `errorDetails` extension member. */
interface ProblemDetailsBody {
  errorDetails?: string[]
}

const connectionStyles: Record<ConnectionStatus | 'offline', string> = {
  offline: 'text-zinc-500',
  connecting: 'text-amber-700',
  reconnecting: 'text-amber-700',
  connected: 'text-green-700',
  disconnected: 'text-red-700',
}

/** Surfaces the error code rather than a status number: SlotAlreadyBooked is the interesting bit. */
async function failureOf(response: Response): Promise<Error> {
  try {
    const problem = (await response.json()) as ProblemDetailsBody
    const codes = problem.errorDetails?.join(', ')

    return new Error(codes ? `${response.status} ${codes}` : `HTTP ${response.status}`)
  } catch {
    return new Error(`HTTP ${response.status}`)
  }
}

async function api<T>(path: string, token: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      Authorization: `Bearer ${token}`,
      ...(init?.body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
  })

  if (!response.ok) throw await failureOf(response)

  return (await response.json()) as T
}

function LivePanel() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [token, setToken] = useState<string | null>(null)
  const [rooms, setRooms] = useState<Room[]>([])
  const [roomId, setRoomId] = useState<number | null>(null)
  const [schedule, setSchedule] = useState<Schedule | null>(null)
  const [status, setStatus] = useState<ConnectionStatus | 'offline'>('offline')
  const [transport, setTransport] = useState<CheckResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  const logIn = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)

    try {
      const response = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password }),
      })

      if (!response.ok) throw await failureOf(response)

      setToken(((await response.json()) as LoginResponse).accessToken)
    } catch (e) {
      setError(message(e))
    }
  }

  useEffect(() => {
    if (!token) return

    let cancelled = false

    // The authenticated half of the negotiate check - the only way left to tell Azure SignalR
    // from the in-process fallback, since both behave identically on a single instance.
    void checkRealtime(token).then((result) => {
      if (!cancelled) setTransport(result)
    })

    void api<Room[]>('/api/rooms', token)
      .then((loaded) => {
        if (cancelled) return

        setRooms(loaded)
        setRoomId((current) => current ?? loaded[0]?.id ?? null)
      })
      .catch((e: unknown) => {
        if (!cancelled) setError(message(e))
      })

    return () => {
      cancelled = true
    }
  }, [token])

  useEffect(() => {
    if (!token || roomId === null) return

    // StrictMode runs this twice in development, so the cleanup below has to be able to stop a
    // connection that is still being opened - hence the flag rather than a bare await.
    let cancelled = false
    let subscription: RoomSubscription | undefined

    const loadSchedule = async () => {
      try {
        const loaded = await api<Schedule>(`/api/rooms/${roomId}/schedule`, token)

        if (!cancelled) setSchedule(loaded)
      } catch (e) {
        if (!cancelled) setError(message(e))
      }
    }

    void (async () => {
      await loadSchedule()

      try {
        const opened = await connectToRoom({
          roomId,
          accessTokenFactory: () => token,

          // isBookedByMe is deliberately left alone: the event says a slot was taken, never by
          // whom. A booker's own tab learns that from its 201, and a second tab of theirs will
          // show the slot as taken but not as theirs until it refetches.
          onSlotBooked: ({ slotId }) =>
            setSchedule((current) =>
              current === null
                ? current
                : {
                    ...current,
                    slots: current.slots.map((slot) =>
                      slot.id === slotId ? { ...slot, isBooked: true } : slot,
                    ),
                  },
            ),

          // Rejoining the group is not enough on its own - anything announced while the
          // connection was down was never delivered and is not replayed.
          onResubscribed: () => void loadSchedule(),

          onStatusChange: (next) => {
            if (!cancelled) setStatus(next)
          },
        })

        if (cancelled) {
          await opened.stop()
          return
        }

        subscription = opened
      } catch (e) {
        if (!cancelled) setError(message(e))
      }
    })()

    return () => {
      cancelled = true
      void subscription?.stop()
    }
  }, [token, roomId])

  const book = async (slotId: number) => {
    if (!token) return

    setError(null)

    try {
      await api<unknown>('/api/bookings', token, { method: 'POST', body: JSON.stringify({ slotId }) })

      setSchedule((current) =>
        current === null
          ? current
          : {
              ...current,
              slots: current.slots.map((slot) =>
                slot.id === slotId ? { ...slot, isBooked: true, isBookedByMe: true } : slot,
              ),
            },
      )
    } catch (e) {
      setError(message(e))
    }
  }

  if (!token) {
    return (
      <section className="mt-8">
        <h2 className="text-lg font-semibold text-zinc-900">Live schedule</h2>
        <form onSubmit={(event) => void logIn(event)} className="mt-2 flex flex-wrap gap-2">
          <input
            type="email"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            placeholder="email"
            className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm"
          />
          <input
            type="password"
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            placeholder="password"
            className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm"
          />
          <button type="submit" className="rounded-md bg-zinc-900 px-3 py-1.5 text-sm text-white">
            Log in
          </button>
        </form>
        {error && <p className="mt-2 text-sm text-red-700">{error}</p>}
      </section>
    )
  }

  // The response names the zone rather than the client holding its own copy - two copies is how
  // a generator and a formatter drift apart.
  const formatSlot = (slot: ScheduleSlot) =>
    schedule === null
      ? slot.startUtc
      : new Intl.DateTimeFormat('en-GB', {
          timeZone: schedule.timeZoneId,
          weekday: 'short',
          day: '2-digit',
          month: 'short',
          hour: '2-digit',
          minute: '2-digit',
        }).format(new Date(slot.startUtc))

  // A slot whose window has closed is unbookable and yet carries no flag saying so - by decision,
  // because a server-computed one would be stale on serialisation. The client has endUtc and its
  // own clock, so it filters here.
  const upcoming = (schedule?.slots ?? []).filter((slot) => Date.parse(slot.endUtc) > Date.now()).slice(0, 12)

  return (
    <section className="mt-8">
      <h2 className="text-lg font-semibold text-zinc-900">Live schedule</h2>

      <div className="mt-2 flex flex-wrap items-center gap-3">
        <select
          value={roomId ?? ''}
          onChange={(event) => setRoomId(Number(event.target.value))}
          className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm"
        >
          {rooms.map((room) => (
            <option key={room.id} value={room.id}>
              {room.name}
            </option>
          ))}
        </select>

        <span className={`text-sm ${connectionStyles[status]}`}>hub: {status}</span>
        <span className="text-sm text-zinc-500">{schedule?.timeZoneId}</span>
      </div>

      {transport && (
        <pre
          className={`mt-2 overflow-x-auto rounded-md bg-zinc-100 p-3 text-xs whitespace-pre-wrap ${statusStyles[transport.status]}`}
        >
          {transport.text}
        </pre>
      )}

      {error && <p className="mt-2 text-sm text-red-700">{error}</p>}

      <ul className="mt-3 divide-y divide-zinc-200 rounded-md border border-zinc-200">
        {upcoming.map((slot) => (
          <li key={slot.id} className="flex items-center justify-between gap-4 px-3 py-2 text-sm">
            <span className="text-zinc-700">{formatSlot(slot)}</span>

            {slot.isBooked ? (
              <span className={slot.isBookedByMe ? 'text-green-700' : 'text-zinc-500'}>
                {slot.isBookedByMe ? 'booked by you' : 'booked'}
              </span>
            ) : (
              <button
                type="button"
                onClick={() => void book(slot.id)}
                className="rounded-md bg-zinc-900 px-3 py-1 text-xs text-white"
              >
                Book
              </button>
            )}
          </li>
        ))}
      </ul>
    </section>
  )
}
