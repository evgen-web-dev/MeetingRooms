import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../api/client'
import { useRoomLiveUpdates } from '../realtime/LiveSchedule'
import { ErrorNote, PageHeading, card, primaryButton, secondaryButton } from '../ui'
import { dayKey, formatDay, formatRange, hasEnded } from '../time'

interface ScheduleSlot {
  id: number
  startUtc: string
  endUtc: string
  isBooked: boolean
  isBookedByMe: boolean
}

interface Schedule {
  roomId: number
  roomName: string
  timeZoneId: string
  slots: ScheduleSlot[]
}

/**
 * One room's schedule, a day at a time.
 *
 * **The whole horizon is fetched once and paged by day in memory.** Asking the server for a single
 * day would mean computing where a day begins in Europe/Kyiv *in the browser*, which is the trap
 * `docs/decisions.md` records: a bound sent without an offset is read in the server's own zone, and
 * getting it wrong is an off-by-one-hour that only appears across a daylight-saving boundary. The
 * response is bounded at ~140 slots by construction, so one fetch costs about 15 KB and removes
 * that arithmetic entirely - it also makes a live update a single array edit.
 */
export default function RoomSchedule() {
  const roomId = Number(useParams().roomId)

  const [schedule, setSchedule] = useState<Schedule | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [dayIndex, setDayIndex] = useState(0)

  const load = useCallback(async () => {
    try {
      setSchedule(await api.get<Schedule>(`/api/rooms/${roomId}/schedule`))
    } catch (failure) {
      setError(failure)
    }
  }, [roomId])

  useEffect(() => {
    void load()
  }, [load])

  useRoomLiveUpdates(roomId, {
    // The event says a slot was taken, never by whom, so isBookedByMe is deliberately left alone:
    // a booker's own tab learns that from its 201, and their second tab learns it on the next
    // refetch. Widening the event to carry identity is what §5's visibility rule forbids.
    onSlotBooked: ({ slotId }) => setSchedule((current) => markBooked(current, slotId, false)),

    // Rejoining the group is not enough on its own - anything announced while the connection was
    // down was never delivered and is never replayed.
    onResubscribed: () => void load(),
  })

  const days = useMemo(() => groupByDay(schedule), [schedule])
  const day = days[Math.min(dayIndex, days.length - 1)]

  const book = async (slotId: number) => {
    setError(null)

    try {
      await api.post(`/api/bookings`, { slotId })
      setSchedule((current) => markBooked(current, slotId, true))
    } catch (failure) {
      setError(failure)

      // The refetch is the point: a 409 means the screen was out of date, and the event that would
      // have updated it may have arrived while this request was in flight.
      await load()
    }
  }

  if (schedule === null) {
    return (
      <div className="space-y-4">
        <ErrorNote error={error} />
        {error === null && <p className="text-sm text-zinc-500">Loading…</p>}
      </div>
    )
  }

  return (
    <div className="space-y-5">
      <PageHeading
        actions={
          <Link to="/rooms" className={secondaryButton}>
            All rooms
          </Link>
        }
      >
        {schedule.roomName}
      </PageHeading>

      <div className="flex items-center justify-between gap-3">
        <button
          type="button"
          disabled={dayIndex === 0}
          onClick={() => setDayIndex(dayIndex - 1)}
          className={secondaryButton}
        >
          ◀ Previous
        </button>

        <div className="text-center">
          <p className="font-medium text-zinc-900">
            {day ? formatDay(day.slots[0]!.startUtc, schedule.timeZoneId) : '—'}
          </p>
          <p className="text-xs text-zinc-500">{schedule.timeZoneId}</p>
        </div>

        <button
          type="button"
          disabled={dayIndex >= days.length - 1}
          onClick={() => setDayIndex(dayIndex + 1)}
          className={secondaryButton}
        >
          Next ▶
        </button>
      </div>

      <ErrorNote error={error} />

      <ul className={`divide-y divide-zinc-200 ${card}`}>
        {(day?.slots ?? []).map((slot) => {
          // A slot whose window has closed is unbookable and yet carries no flag saying so, by
          // decision: a server-computed one would be stale the moment it was serialised. The
          // client has endUtc and its own clock.
          const past = hasEnded(slot.endUtc)

          return (
            <li key={slot.id} className="flex items-center justify-between gap-4 px-4 py-2.5">
              <span className={past ? 'text-sm text-zinc-400' : 'text-sm text-zinc-800'}>
                {formatRange(slot.startUtc, slot.endUtc, schedule.timeZoneId)}
              </span>

              {slot.isBooked ? (
                <span className={`text-sm ${slot.isBookedByMe ? 'font-medium text-green-700' : 'text-zinc-500'}`}>
                  {slot.isBookedByMe ? 'Booked by you' : 'Booked'}
                </span>
              ) : past ? (
                <span className="text-sm text-zinc-400">Past</span>
              ) : (
                <button type="button" onClick={() => void book(slot.id)} className={primaryButton}>
                  Book
                </button>
              )}
            </li>
          )
        })}

        {(day?.slots.length ?? 0) === 0 && (
          <li className="px-4 py-6 text-sm text-zinc-500">No slots in this room's window.</li>
        )}
      </ul>
    </div>
  )
}

interface Day {
  key: string
  slots: ScheduleSlot[]
}

/** Groups the horizon into calendar days *of the display zone*, preserving the server's order. */
function groupByDay(schedule: Schedule | null): Day[] {
  if (schedule === null) return []

  const days: Day[] = []

  for (const slot of schedule.slots) {
    const key = dayKey(slot.startUtc, schedule.timeZoneId)
    const last = days[days.length - 1]

    if (last?.key === key) last.slots.push(slot)
    else days.push({ key, slots: [slot] })
  }

  return days
}

function markBooked(schedule: Schedule | null, slotId: number, byMe: boolean): Schedule | null {
  if (schedule === null) return schedule

  return {
    ...schedule,
    slots: schedule.slots.map((slot) =>
      slot.id === slotId ? { ...slot, isBooked: true, isBookedByMe: slot.isBookedByMe || byMe } : slot,
    ),
  }
}
