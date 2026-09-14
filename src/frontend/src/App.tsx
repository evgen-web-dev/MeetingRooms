import { NavLink, Outlet } from 'react-router'
import { useAuth } from './auth/AuthContext'
import { LiveScheduleProvider, useLiveSchedule } from './realtime/LiveSchedule'

/**
 * The authenticated shell: navigation, the session's hub connection, and wherever the router is
 * pointing. Everything inside it is behind `RequireAuth`, which is why the provider can assume a
 * token exists.
 */
export default function AppLayout() {
  return (
    <LiveScheduleProvider>
      <Shell />
    </LiveScheduleProvider>
  )
}

const connectionStyles: Record<string, string> = {
  offline: 'bg-zinc-300',
  connecting: 'bg-amber-400',
  reconnecting: 'bg-amber-400',
  connected: 'bg-green-500',
  disconnected: 'bg-red-500',
}

function Shell() {
  const { user, isAdmin, logOut } = useAuth()
  const { status } = useLiveSchedule()

  const link = ({ isActive }: { isActive: boolean }) =>
    `rounded-md px-2.5 py-1.5 text-sm font-medium ${
      isActive ? 'bg-zinc-900 text-white' : 'text-zinc-600 hover:bg-zinc-100'
    }`

  return (
    <div className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white">
        <div className="mx-auto flex max-w-3xl flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3">
          <span className="font-semibold tracking-tight text-zinc-900">Meeting Rooms</span>

          <nav className="flex items-center gap-1">
            <NavLink to="/rooms" className={link}>
              Rooms
            </NavLink>
            <NavLink to="/bookings" className={link}>
              My bookings
            </NavLink>
            {isAdmin && (
              <NavLink to="/admin/bookings" className={link}>
                All bookings
              </NavLink>
            )}
          </nav>

          <div className="ml-auto flex items-center gap-3 text-sm text-zinc-500">
            {/* Live rather than decorative: amber here is the first sign that a schedule on screen
                has stopped updating itself. */}
            <span className="flex items-center gap-1.5" title={`Realtime: ${status}`}>
              <span className={`inline-block size-2 rounded-full ${connectionStyles[status]}`} />
              {status}
            </span>

            <span className="hidden sm:inline">{user?.email}</span>

            <button type="button" onClick={logOut} className="font-medium text-zinc-700 hover:underline">
              Log out
            </button>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-3xl px-4 py-8">
        <Outlet />
      </main>

      <footer className="mx-auto max-w-3xl px-4 pb-10 text-xs text-zinc-400">
        <NavLink to="/diagnostics" className="hover:underline">
          Diagnostics
        </NavLink>
        <span className="px-2">·</span>
        <a href="/scalar/v1" className="hover:underline">
          API reference
        </a>
      </footer>
    </div>
  )
}
