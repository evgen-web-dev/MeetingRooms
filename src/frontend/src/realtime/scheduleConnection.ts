import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'

/** Mirrors `Api/Hubs/SlotBookedEvent.cs`. Carries no booker identity, by design. */
export interface SlotBookedEvent {
  roomId: number
  slotId: number
}

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

export interface RoomHandlers {
  onSlotBooked: (event: SlotBookedEvent) => void
  /**
   * Called after a reconnect, once the room group has been rejoined. **Refetch the schedule
   * here** - see the note on re-subscription below.
   */
  onResubscribed: () => void
}

export interface ScheduleConnection {
  /** Joins a room's group. Resolves to the function that leaves it again. */
  subscribe: (roomId: number, handlers: RoomHandlers) => Promise<() => void>
  stop: () => Promise<void>
}

/**
 * Delays for the *first* connect. `withAutomaticReconnect` does not cover `start()`, and the
 * first negotiate after a deploy can legitimately fail: the app opens its server connection to
 * Azure SignalR at startup and answers negotiate with
 * `500 "Azure SignalR Service is not connected yet"` until that is established.
 */
const initialStartDelaysMs = [0, 1000, 2000, 5000, 10000]

const reconnectDelaysMs = [0, 2000, 5000, 10000]

const delay = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

/**
 * **One connection for the whole session**, joining and leaving room groups as the user moves
 * between rooms. Phase 6's probe opened a fresh connection per room, which under Azure SignalR
 * means a negotiate plus a new handshake to the service on every switch - and left
 * `UnsubscribeFromRoom` called by nothing.
 *
 * Two things here are not obvious, and are why this lives in a module rather than in a component:
 *
 * 1. **Group membership belongs to the connection, not to the user.** A reconnect produces a new
 *    connection, which is in no groups at all - so every room has to be rejoined.
 * 2. **Rejoining is not enough.** Events that fired while the connection was down are gone; the
 *    server does not replay them. Only a refetch makes the screen correct again, which is what
 *    `onResubscribed` is for.
 */
export function createScheduleConnection(
  accessTokenFactory: () => string,
  onStatusChange: (status: ConnectionStatus) => void,
): ScheduleConnection {
  const connection = new HubConnectionBuilder()
    // Same origin: the SPA is served by the API, so no base URL anywhere in the client. In
    // development Vite proxies /hubs to :5000 with `ws: true`.
    .withUrl('/hubs/schedule', { accessTokenFactory })
    .withAutomaticReconnect(reconnectDelaysMs)
    .configureLogging(LogLevel.Warning)
    .build()

  const rooms = new Map<number, RoomHandlers>()

  // Wrapped in a block rather than passed straight through, so the handler's return value is
  // discarded. TypeScript permits returning a value where void is expected, and the SignalR client
  // reads any returned value as a result for an invocation - logging
  // "Result given for 'slotbooked' method but server is not expecting a result".
  connection.on('SlotBooked', (event: SlotBookedEvent) => {
    rooms.get(event.roomId)?.onSlotBooked(event)
  })

  connection.onreconnecting(() => onStatusChange('reconnecting'))

  connection.onreconnected(async () => {
    for (const [roomId, handlers] of rooms) {
      await connection.invoke('SubscribeToRoom', roomId)
      handlers.onResubscribed()
    }

    onStatusChange('connected')
  })

  connection.onclose(() => onStatusChange('disconnected'))

  // Started once, lazily, by whichever page subscribes first. Reset on failure so a later
  // subscribe retries rather than awaiting a promise that is already rejected.
  let started: Promise<void> | null = null

  const start = () => {
    started ??= (async () => {
      onStatusChange('connecting')
      await startWithRetry(connection)
      onStatusChange('connected')
    })().catch((error: unknown) => {
      started = null
      throw error
    })

    return started
  }

  return {
    subscribe: async (roomId, handlers) => {
      rooms.set(roomId, handlers)

      await start()
      await connection.invoke('SubscribeToRoom', roomId)

      return () => {
        rooms.delete(roomId)

        // Best effort: leaving a group on a connection that is already gone is a no-op, and a
        // connection that is gone is in no groups anyway.
        if (connection.state === HubConnectionState.Connected) {
          void connection.invoke('UnsubscribeFromRoom', roomId).catch(() => {})
        }
      }
    },

    stop: async () => {
      rooms.clear()
      started = null
      await connection.stop()
    },
  }
}

async function startWithRetry(connection: HubConnection): Promise<void> {
  let lastError: unknown

  for (const waitMs of initialStartDelaysMs) {
    await delay(waitMs)

    try {
      await connection.start()
      return
    } catch (error) {
      lastError = error
    }
  }

  throw lastError
}
