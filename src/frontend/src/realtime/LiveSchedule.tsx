import { createContext, use, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { useAuth } from '../auth/AuthContext'
import { createScheduleConnection } from './scheduleConnection'
import type { ConnectionStatus, RoomHandlers, ScheduleConnection } from './scheduleConnection'

interface LiveValue {
  status: ConnectionStatus | 'offline'
  subscribe: (roomId: number, handlers: RoomHandlers) => Promise<() => void>
}

const LiveScheduleContext = createContext<LiveValue | null>(null)

/**
 * Owns the session's single hub connection. Mounted inside the authenticated layout, so logging
 * out unmounts it and stops the socket.
 *
 * The connection is created on first use rather than on mount: a user who never opens a room
 * schedule never opens a socket, and there is no connection to tear down if they log straight
 * back out.
 */
export function LiveScheduleProvider({ children }: { children: ReactNode }) {
  const { accessToken } = useAuth()
  const [status, setStatus] = useState<ConnectionStatus | 'offline'>('offline')
  const connectionRef = useRef<ScheduleConnection | null>(null)

  // A connection that has been replaced - StrictMode's simulated remount, or a logout and a new
  // login - can still report onclose after its successor is up, which would show "disconnected"
  // over a working socket. Only the current generation is allowed to write the status.
  const generationRef = useRef(0)

  const subscribe = useCallback(
    (roomId: number, handlers: RoomHandlers) => {
      if (connectionRef.current === null) {
        const generation = ++generationRef.current

        connectionRef.current = createScheduleConnection(accessToken, (next) => {
          if (generationRef.current === generation) setStatus(next)
        })
      }

      return connectionRef.current.subscribe(roomId, handlers)
    },
    [accessToken],
  )

  useEffect(
    () => () => {
      generationRef.current += 1
      void connectionRef.current?.stop()
      connectionRef.current = null
      setStatus('offline')
    },
    [],
  )

  const value = useMemo<LiveValue>(() => ({ status, subscribe }), [status, subscribe])

  return <LiveScheduleContext value={value}>{children}</LiveScheduleContext>
}

export function useLiveSchedule(): LiveValue {
  const value = use(LiveScheduleContext)

  if (value === null) throw new Error('useLiveSchedule must be used inside a LiveScheduleProvider')

  return value
}

/**
 * Subscribes to one room for as long as the component is mounted with that id.
 *
 * The handlers are held in a ref so that a re-render - which produces new function identities -
 * does not leave and rejoin the group. Only the room id does that.
 */
export function useRoomLiveUpdates(roomId: number, handlers: RoomHandlers): void {
  const { subscribe } = useLiveSchedule()
  const handlersRef = useRef(handlers)

  handlersRef.current = handlers

  useEffect(() => {
    let cancelled = false
    let leave: (() => void) | undefined

    void subscribe(roomId, {
      onSlotBooked: (event) => handlersRef.current.onSlotBooked(event),
      onResubscribed: () => handlersRef.current.onResubscribed(),
    })
      .then((unsubscribe) => {
        // StrictMode runs this effect twice in development, so the cleanup below has to be able
        // to cancel a subscription that is still being established.
        if (cancelled) unsubscribe()
        else leave = unsubscribe
      })
      .catch(() => {
        // A hub that will not connect is reported by the status indicator; the schedule itself
        // still renders from its fetch, just without live updates.
      })

    return () => {
      cancelled = true
      leave?.()
    }
  }, [roomId, subscribe])
}
