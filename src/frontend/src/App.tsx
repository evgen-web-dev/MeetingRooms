import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'

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
    if (!response.ok) throw new Error(`HTTP ${response.status}`)
    return { status: 'ok', text: JSON.stringify(await response.json(), null, 2) }
  } catch (e) {
    return { status: 'error', text: `Health check failed: ${message(e)}` }
  }
}

/** Negotiate is the cheapest proof that SignalR is wired, and the only one available
 *  before a real client connects. It never opens a connection. */
async function checkRealtime(): Promise<CheckResult> {
  try {
    const response = await fetch('/hubs/schedule/negotiate?negotiateVersion=1', { method: 'POST' })
    const body = await response.text()

    // A failed negotiate answers in plain text, not JSON, so read the body as text first
    // and surface it: "Azure SignalR Service is not connected yet" is a 500 that means
    // something quite specific, and parsing it as JSON would lose it.
    if (!response.ok) throw new Error(`HTTP ${response.status} — ${body.slice(0, 200)}`)

    const negotiate = JSON.parse(body) as NegotiateResponse

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
        pipeline reaches the deployed app.
      </p>

      <Panel title="Backend health" result={health} />

      <Panel title="Realtime transport" result={realtime}>
        <p className="mt-2 text-sm text-zinc-500">
          Negotiate has three outcomes, and they need different fixes:{' '}
          <strong className="font-medium text-zinc-700">Azure SignalR</strong> is the deployed
          target; <strong className="font-medium text-zinc-700">in-process</strong> means the
          connection string was not read at all;{' '}
          <strong className="font-medium text-zinc-700">not connected</strong> means it was read
          but the service is unreachable — which is also the normal cold-start window for the
          first request after a deploy.
        </p>
      </Panel>
    </main>
  )
}
