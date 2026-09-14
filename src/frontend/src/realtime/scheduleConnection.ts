import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'

/** Mirrors `Api/Hubs/SlotBookedEvent.cs`. Carries no booker identity, by design. */
export interface SlotBookedEvent {
  roomId: number
  slotId: number
}

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

export interface ConnectToRoomOptions {
  roomId: number
  /** Called on every (re)connect, so a refreshed token is picked up without rebuilding. */
  accessTokenFactory: () => string
  onSlotBooked: (event: SlotBookedEvent) => void
  /**
   * Called after a reconnect, once the room group has been rejoined. **Refetch the schedule
   * here** - see the note on re-subscription below.
   */
  onResubscribed: () => void
  onStatusChange?: (status: ConnectionStatus) => void
}

export interface RoomSubscription {
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
 * Connects to the schedule hub and joins one room's group.
 *
 * Two things here are not obvious and are the reason this lives in a module rather than in a
 * component:
 *
 * 1. **Group membership belongs to the connection, not to the user.** A reconnect produces a new
 *    connection, which is in no groups at all - so the room has to be rejoined every time.
 * 2. **Rejoining is not enough.** Events that fired while the connection was down are gone; the
 *    server does not replay them. Only a refetch makes the screen correct again, which is what
 *    `onResubscribed` is for.
 */
export async function connectToRoom({
  roomId,
  accessTokenFactory,
  onSlotBooked,
  onResubscribed,
  onStatusChange,
}: ConnectToRoomOptions): Promise<RoomSubscription> {
  const connection = new HubConnectionBuilder()
    // Same origin: the SPA is served by the API, so no base URL anywhere in the client. In
    // development Vite proxies /hubs to :5000 with `ws: true`.
    .withUrl('/hubs/schedule', { accessTokenFactory })
    .withAutomaticReconnect(reconnectDelaysMs)
    .configureLogging(LogLevel.Warning)
    .build()

  // Wrapped in a block rather than passed straight through, so the handler's return value is
  // discarded. TypeScript permits returning a value where void is expected, and the SignalR client
  // reads any returned value as a result for an invocation - logging
  // "Result given for 'slotbooked' method but server is not expecting a result". Found by running
  // a client whose handler was a one-line arrow.
  connection.on('SlotBooked', (event: SlotBookedEvent) => {
    onSlotBooked(event)
  })

  connection.onreconnecting(() => onStatusChange?.('reconnecting'))

  connection.onreconnected(async () => {
    await connection.invoke('SubscribeToRoom', roomId)
    onStatusChange?.('connected')
    onResubscribed()
  })

  connection.onclose(() => onStatusChange?.('disconnected'))

  onStatusChange?.('connecting')
  await startWithRetry(connection)

  await connection.invoke('SubscribeToRoom', roomId)
  onStatusChange?.('connected')

  return { stop: () => connection.stop() }
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
