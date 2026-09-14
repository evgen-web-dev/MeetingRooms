import type { ReactNode } from 'react'
import { ApiError } from './api/client'

/*
 * Hand-written Tailwind rather than a component library: the surface here is a button, an input, a
 * card and a message, and adding shadcn/ui would have cost a firewall change and a container
 * rebuild before a line could be written. These constants exist so the same button does not get
 * spelled three ways across seven screens.
 */

export const card = 'rounded-lg border border-zinc-200 bg-white'

export const input =
  'w-full rounded-md border border-zinc-300 px-3 py-1.5 text-sm outline-none focus:border-zinc-900'

export const primaryButton =
  'rounded-md bg-zinc-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-zinc-700 disabled:cursor-not-allowed disabled:bg-zinc-300'

export const secondaryButton =
  'rounded-md border border-zinc-300 px-3 py-1.5 text-sm font-medium text-zinc-700 hover:bg-zinc-50 disabled:cursor-not-allowed disabled:text-zinc-400'

export const dangerButton =
  'rounded-md border border-red-200 px-3 py-1.5 text-sm font-medium text-red-700 hover:bg-red-50'

export function PageHeading({ children, actions }: { children: ReactNode; actions?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <h1 className="text-2xl font-semibold tracking-tight text-zinc-900">{children}</h1>
      {actions}
    </div>
  )
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-zinc-600">{label}</span>
      {children}
    </label>
  )
}

export function ErrorNote({ error }: { error: unknown }) {
  if (error === null || error === undefined) return null

  return (
    <p className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
      {describeError(error)}
    </p>
  )
}

/**
 * Business error codes are the API's contract for exactly this: a client branching on a condition
 * without parsing prose. Anything unmapped falls through to the code itself rather than to a
 * generic apology - an unfamiliar code on screen is a bug report; "something went wrong" is not.
 */
export function describeError(error: unknown): string {
  if (!(error instanceof ApiError)) return error instanceof Error ? error.message : String(error)

  if (error.is('InvalidEmailOrPassword')) return 'Email or password is incorrect.'
  if (error.is('EmailAlreadyRegistered')) return 'That email address is already registered.'
  if (error.is('SlotAlreadyBooked')) return 'Someone else booked that slot first.'
  if (error.is('SlotHasEnded')) return 'That slot has already ended.'
  if (error.is('SlotNotFound')) return 'That slot no longer exists.'
  if (error.is('RoomNotFound')) return 'That room no longer exists.'
  if (error.is('RoomHasBookedSlots'))
    return 'This room has bookings, so it cannot be deleted. Bookings are never cancelled.'

  // The password policy is Identity's, which reports it as one code per unmet rule - by decision,
  // so the rules live in one place. Matched by prefix rather than by listing the six, so a policy
  // change cannot reintroduce a raw code on screen.
  if (error.codes.some((code) => code.startsWith('Password')))
    return 'Password must be at least 6 characters and include an upper-case letter, a lower-case letter, a digit and a symbol.'

  if (error.status === 403) return 'You do not have access to that.'

  return error.message
}
