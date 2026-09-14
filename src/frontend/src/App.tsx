import { Link, NavLink, Outlet, useMatch } from 'react-router'
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

/**
 * The connection state, said in terms of what it means for the person reading it. "connected" is
 * the library's word for a WebSocket being open, which is our concern rather than theirs - what
 * they need to know is whether the page will update itself, and what to do when it will not.
 */
const connectionLabels: Record<string, { dot: string; text: string; title: string }> = {
  offline: { dot: 'bg-zinc-300', text: '', title: '' },
  connecting: {
    dot: 'bg-amber-400',
    text: 'Connecting…',
    title: 'Setting up live updates for this room.',
  },
  connected: {
    dot: 'bg-green-500',
    text: 'Live updates on',
    title: 'Bookings made by other people appear here immediately, with no refresh.',
  },
  reconnecting: {
    dot: 'bg-amber-400',
    text: 'Reconnecting…',
    title: 'The connection dropped. Trying to restore live updates.',
  },
  disconnected: {
    dot: 'bg-red-500',
    text: 'Live updates off — reload',
    title: 'Live updates have stopped. Reload the page to restore them.',
  },
}

function Shell() {
  const { user, isAdmin, logOut } = useAuth()
  const { status } = useLiveSchedule()

  // Shown only where something on screen is actually live. The connection deliberately outlives a
  // room - it is one per session, joining and leaving groups rather than reconnecting - so the
  // status stays "connected" after you navigate away, and announcing live updates on a page that
  // has none is a claim the screen cannot keep.
  const onASchedule = useMatch('/rooms/:roomId') !== null

  const link = ({ isActive }: { isActive: boolean }) =>
    `rounded-md px-2.5 py-1.5 text-sm font-medium ${
      isActive ? 'bg-zinc-900 text-white' : 'text-zinc-600 hover:bg-zinc-100'
    }`

  const connection = connectionLabels[status]!

  return (
    <div className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white">
        <div className="mx-auto flex max-w-5xl flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3">
          <Link to="/" className="font-semibold tracking-tight text-zinc-900 hover:text-zinc-600">
            Meeting Rooms
          </Link>

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

          <div className="ml-auto flex min-w-0 items-center gap-3 text-sm text-zinc-500">
            {/* Hidden while offline, which is not a fault: the socket opens on the first schedule a
                user looks at, so a room list has nothing to report. */}
            {onASchedule && status !== 'offline' && (
              <span className="flex shrink-0 items-center gap-1.5" title={connection.title}>
                <span className={`inline-block size-2 rounded-full ${connection.dot}`} />
                {connection.text}
              </span>
            )}

            {/* Truncated rather than wrapped: an address long enough to matter would otherwise push
                the log-out control off the row. */}
            <span className="hidden max-w-[16rem] truncate sm:inline" title={user?.email}>
              {user?.email}
            </span>

            <button
              type="button"
              onClick={logOut}
              className="shrink-0 font-medium text-zinc-700 hover:underline"
            >
              Log out
            </button>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-5xl px-4 py-8">
        <Outlet />
      </main>

      <footer className="mx-auto max-w-5xl px-4 pb-10 text-xs text-zinc-400">
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
