import { useEffect, useState } from 'react'
import { api } from '../api/client'
import { ErrorNote, PageHeading, card } from '../ui'
import { formatDayAndRange } from '../time'

interface MyBooking {
  slotId: number
  roomId: number
  roomName: string
  startUtc: string
  endUtc: string
  bookedAtUtc: string
}

interface MyBookings {
  timeZoneId: string
  bookings: MyBooking[]
}

/** The caller's own bookings, earliest slot first. Carries no booker identity: the caller is it. */
export default function MyBookings() {
  const [data, setData] = useState<MyBookings | null>(null)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    void api
      .get<MyBookings>('/api/bookings/me')
      .then(setData)
      .catch((failure: unknown) => setError(failure))
  }, [])

  return (
    <div className="space-y-5">
      <PageHeading>My bookings</PageHeading>
      <ErrorNote error={error} />

      {data === null ? (
        <p className="text-sm text-zinc-500">Loading…</p>
      ) : (
        <ul className={`divide-y divide-zinc-200 ${card}`}>
          {data.bookings.map((booking) => (
            <li key={booking.slotId} className="flex flex-wrap items-baseline justify-between gap-2 px-4 py-3">
              <span className="font-medium text-zinc-900">{booking.roomName}</span>
              <span className="text-sm text-zinc-600">
                {formatDayAndRange(booking.startUtc, booking.endUtc, data.timeZoneId)}
              </span>
            </li>
          ))}

          {data.bookings.length === 0 && (
            <li className="px-4 py-6 text-sm text-zinc-500">
              Nothing booked yet. Bookings cannot be cancelled, so this list only grows.
            </li>
          )}
        </ul>
      )}
    </div>
  )
}
