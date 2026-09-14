import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, Navigate, useNavigate } from 'react-router'
import { useAuth } from '../auth/AuthContext'
import { ErrorNote, Field, card, input, primaryButton } from '../ui'

export default function Register() {
  const { user, register } = useAuth()
  const navigate = useNavigate()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  if (user) return <Navigate to="/rooms" replace />

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    setBusy(true)

    try {
      // Registers, then logs in with the same credentials - the API issues no token on
      // registration by decision.
      await register(email, password)
      await navigate('/rooms', { replace: true })
    } catch (failure) {
      setError(failure)
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="mx-auto max-w-sm px-4 py-16">
      <h1 className="text-2xl font-semibold tracking-tight text-zinc-900">Create an account</h1>
      <p className="mt-1 text-sm text-zinc-600">Every new account can view rooms and book slots.</p>

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
            autoComplete="new-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            className={input}
          />
          <span className="mt-1 block text-xs text-zinc-500">
            At least six characters, with an upper-case letter, a lower-case letter, a digit and a
            symbol.
          </span>
        </Field>

        <ErrorNote error={error} />

        <button type="submit" disabled={busy} className={`w-full ${primaryButton}`}>
          {busy ? 'Creating…' : 'Create account'}
        </button>
      </form>

      <p className="mt-4 text-sm text-zinc-600">
        Already registered?{' '}
        <Link to="/login" className="font-medium text-zinc-900 underline">
          Sign in
        </Link>
      </p>
    </main>
  )
}
