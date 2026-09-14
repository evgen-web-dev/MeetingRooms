import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { useAuth } from '../auth/AuthContext'
import { PageHeading } from '../ui'

/*
 * Phases 1 and 2 built these two panels, and phase 6 kept them for a reason that held up: they are
 * the fastest diagnosis of a deployment, and the transport check is the *only* thing that can show
 * Azure SignalR is carrying the traffic rather than the in-process fallback - on a single instance
 * the two behave identically, so no amount of working real-time distinguishes them.
 *
 * Phase 7 moved them off the home page to here rather than deleting them. Behind RequireAuth,
 * because securing the hub means the transport diagnosis now needs a token.
 */

type Status = 'checking' | 'ok' | 'warn' | 'error'

interface CheckResult {
  status: Status
  text: string
}

/** A SignalR negotiate response, narrowed to the fields that distinguish the three outcomes. */
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

async function checkHealth(): Promise<CheckResult> {
  try {
    const response = await fetch('/health')
    const body = await response.text()

    // Body first, then status. A failing /health is exactly when its body matters: the
    // application's own handler answers ProblemDetails JSON, while the platform answering for an
    // app still inside its startup block returns an HTML page. Discarding it loses the distinction.
    if (!response.ok) throw new Error(`HTTP ${response.status} — ${body.slice(0, 300)}`)

    return { status: 'ok', text: JSON.stringify(JSON.parse(body), null, 2) }
  } catch (e) {
    return { status: 'error', text: `Health check failed: ${message(e)}` }
  }
}

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
      text: ['In-process SignalR — no Azure connection string was read', `transports: ${transports}`].join('\n'),
    }
  }

  return { status: 'error', text: `Unrecognised negotiate response:\n${JSON.stringify(negotiate, null, 2)}` }
}

/**
 * Negotiate is the cheapest proof that SignalR is wired, and it never opens a connection.
 *
 * Called twice, for two different questions. Without a token it asks whether the hub is *secured*:
 * the hub carries [Authorize], so 401 is the correct answer and anything else is the finding. With
 * a token it asks which *transport* is carrying it - the diagnosis an anonymous negotiate can no
 * longer see, because a refusal has no payload to read it from.
 */
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
          'The transport is identified by the authenticated check below.',
        ].join('\n'),
      }
    }

    // A failed negotiate answers in plain text, not JSON: "Azure SignalR Service is not connected
    // yet" is a 500 that means something quite specific, and parsing it as JSON would lose it.
    if (!response.ok) throw new Error(`HTTP ${response.status} — ${body.slice(0, 200)}`)

    return describeTransport(JSON.parse(body) as NegotiateResponse)
  } catch (e) {
    return { status: 'error', text: `Negotiate failed: ${message(e)}` }
  }
}

function Panel({ title, result, children }: { title: string; result: CheckResult; children?: ReactNode }) {
  return (
    <section>
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

export default function Diagnostics() {
  const { accessToken } = useAuth()

  const [health, setHealth] = useState<CheckResult>(pending)
  const [secured, setSecured] = useState<CheckResult>(pending)
  const [transport, setTransport] = useState<CheckResult>(pending)

  useEffect(() => {
    // StrictMode runs effects twice in development, so each check fires twice locally. All three
    // are reads with no side effects, so that is harmless rather than worth guarding.
    void checkHealth().then(setHealth)
    void checkRealtime().then(setSecured)
    void checkRealtime(accessToken()).then(setTransport)
  }, [accessToken])

  return (
    <div className="space-y-6">
      <PageHeading>Diagnostics</PageHeading>

      <Panel title="Backend health" result={health} />
      <Panel title="Hub authentication" result={secured} />

      <Panel title="Realtime transport" result={transport}>
        <p className="mt-2 text-sm text-zinc-500">
          <strong className="font-medium text-zinc-700">Azure SignalR</strong> is the deployed
          target; <strong className="font-medium text-zinc-700">in-process</strong> means the
          connection string was not read at all, which is the expected answer when running locally;{' '}
          <strong className="font-medium text-zinc-700">not connected</strong> means it was read but
          the service is unreachable — the normal cold-start window for the first request after a
          deploy.
        </p>
      </Panel>
    </div>
  )
}
