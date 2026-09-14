import { Navigate, Outlet, useLocation } from 'react-router'
import { useAuth } from './AuthContext'

/**
 * Both guards are route *elements* wrapping nested routes, rather than a check inside each page.
 * A page that forgets the check is the failure mode worth designing out.
 *
 * Neither is a security boundary - every one of these endpoints is gated server-side by
 * `[Authorize]`, and an admin route reached by URL would still answer 403. This is about not
 * showing a screen that cannot work.
 */
export function RequireAuth() {
  const { user, ready } = useAuth()
  const location = useLocation()

  // Without this, a hard refresh on a deep link redirects to the login screen for the instant it
  // takes to read localStorage and call /api/auth/me.
  if (!ready) return <Pending />

  return user ? <Outlet /> : <Navigate to="/login" replace state={{ from: location.pathname }} />
}

export function RequireAdmin() {
  const { isAdmin, ready } = useAuth()

  if (!ready) return <Pending />

  return isAdmin ? <Outlet /> : <Navigate to="/rooms" replace />
}

const Pending = () => <p className="px-4 py-10 text-sm text-zinc-500">Loading…</p>
