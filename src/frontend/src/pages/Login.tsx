import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router'
import { useAuth } from '../auth/AuthContext'
import { ErrorNote, Field, card, input, primaryButton } from '../ui'

export default function Login() {
  const { user, logIn } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  // Where the guard sent them from, so a deep link survives the detour through this screen.
  const destination = (location.state as { from?: string } | null)?.from ?? '/rooms'

  if (user) return <Navigate to={destination} replace />

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    setBusy(true)

    try {
      await logIn(email, password)
      await navigate(destination, { replace: true })
    } catch (failure) {
      setError(failure)
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="mx-auto max-w-sm px-4 py-16">
      <h1 className="text-2xl font-semibold tracking-tight text-zinc-900">Meeting Rooms</h1>
      <p className="mt-1 text-sm text-zinc-600">Sign in to book a room.</p>

      <form onSubmit={(event) => void submit(event)} className={`mt-6 space-y-4 p-5 ${card}`}>
        <Field label="Email">
          <input
            type="email"
            required
            autoComplete="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            className={input}
          />
        </Field>

        <Field label="Password">
          <input
            type="password"
            required
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            className={input}
          />
        </Field>

        <ErrorNote error={error} />

        <button type="submit" disabled={busy} className={`w-full ${primaryButton}`}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>

      <p className="mt-4 text-sm text-zinc-600">
        No account?{' '}
        <Link to="/register" className="font-medium text-zinc-900 underline">
          Register
        </Link>
      </p>
    </main>
  )
}
