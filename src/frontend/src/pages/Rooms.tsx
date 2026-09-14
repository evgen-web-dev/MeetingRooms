import { useCallback, useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { ErrorNote, PageHeading, card, dangerButton, input, primaryButton, secondaryButton } from '../ui'

interface Room {
  id: number
  name: string
  capacity: number
}

/**
 * The room list, with the administrator's create, edit and delete on the same page rather than
 * behind separate admin routes. Four rooms and three operations do not justify a section of their
 * own, and an admin editing a room is looking at the list either way.
 */
export default function Rooms() {
  const { isAdmin } = useAuth()

  const [rooms, setRooms] = useState<Room[]>([])
  const [error, setError] = useState<unknown>(null)
  const [loading, setLoading] = useState(true)
  const [editing, setEditing] = useState<Room | null>(null)
  const [draft, setDraft] = useState({ name: '', capacity: '8' })

  const load = useCallback(async () => {
    try {
      setRooms(await api.get<Room[]>('/api/rooms'))
    } catch (failure) {
      setError(failure)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const create = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)

    try {
      // Creating a room generates its slots for the current window immediately, server-side.
      await api.post<Room>('/api/rooms', { name: draft.name, capacity: Number(draft.capacity) })
      setDraft({ name: '', capacity: '8' })
      await load()
    } catch (failure) {
      setError(failure)
    }
  }

  const save = async (room: Room) => {
    setError(null)

    try {
      await api.put<Room>(`/api/rooms/${room.id}`, { name: room.name, capacity: room.capacity })
      setEditing(null)
      await load()
    } catch (failure) {
      setError(failure)
    }
  }

  const remove = async (room: Room) => {
    setError(null)

    // A room holding a booking is refused with 409 RoomHasBookedSlots - bookings are never
    // cancelled, so deleting the room out from under one would be a cancellation by another name.
    if (!confirm(`Delete ${room.name}? Its free slots go with it.`)) return

    try {
      await api.delete(`/api/rooms/${room.id}`)
      await load()
    } catch (failure) {
      setError(failure)
    }
  }

  return (
    <div className="space-y-6">
      <PageHeading>Rooms</PageHeading>

      <ErrorNote error={error} />

      {isAdmin && (
        <form onSubmit={(event) => void create(event)} className={`flex flex-wrap items-end gap-3 p-4 ${card}`}>
          <div className="grow">
            <span className="mb-1 block text-xs font-medium text-zinc-600">New room</span>
            <input
              required
              maxLength={100}
              placeholder="Name"
              value={draft.name}
              onChange={(event) => setDraft({ ...draft, name: event.target.value })}
              className={input}
            />
          </div>

          <div className="w-28">
            <span className="mb-1 block text-xs font-medium text-zinc-600">Capacity</span>
            <input
              required
              type="number"
              min={1}
              value={draft.capacity}
              onChange={(event) => setDraft({ ...draft, capacity: event.target.value })}
              className={input}
            />
          </div>

          <button type="submit" className={primaryButton}>
            Add room
          </button>
        </form>
      )}

      {loading ? (
        <p className="text-sm text-zinc-500">Loading…</p>
      ) : (
        <ul className={`divide-y divide-zinc-200 ${card}`}>
          {rooms.map((room) =>
            editing?.id === room.id ? (
              <li key={room.id} className="flex flex-wrap items-center gap-3 px-4 py-3">
                <input
                  value={editing.name}
                  onChange={(event) => setEditing({ ...editing, name: event.target.value })}
                  className={`grow ${input}`}
                />
                <input
                  type="number"
                  min={1}
                  value={editing.capacity}
                  onChange={(event) => setEditing({ ...editing, capacity: Number(event.target.value) })}
                  className={`w-24 ${input}`}
                />
                <button type="button" onClick={() => void save(editing)} className={primaryButton}>
                  Save
                </button>
                <button type="button" onClick={() => setEditing(null)} className={secondaryButton}>
                  Cancel
                </button>
              </li>
            ) : (
              <li key={room.id} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
                <div>
                  <Link to={`/rooms/${room.id}`} className="font-medium text-zinc-900 hover:underline">
                    {room.name}
                  </Link>
                  <p className="text-sm text-zinc-500">Seats {room.capacity}</p>
                </div>

                <div className="flex items-center gap-2">
                  <Link to={`/rooms/${room.id}`} className={secondaryButton}>
                    Schedule
                  </Link>

                  {isAdmin && (
                    <>
                      <button type="button" onClick={() => setEditing(room)} className={secondaryButton}>
                        Edit
                      </button>
                      <button type="button" onClick={() => void remove(room)} className={dangerButton}>
                        Delete
                      </button>
                    </>
                  )}
                </div>
              </li>
            ),
          )}

          {rooms.length === 0 && <li className="px-4 py-6 text-sm text-zinc-500">No rooms yet.</li>}
        </ul>
      )}
    </div>
  )
}
