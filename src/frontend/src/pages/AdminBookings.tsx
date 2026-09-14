import { useEffect, useState } from 'react'
import { api } from '../api/client'
import { ErrorNote, PageHeading, card } from '../ui'
import { formatDayAndRange } from '../time'

interface AdminBooking {
  slotId: number
  roomId: number
  roomName: string
  startUtc: string
  endUtc: string
  bookedAtUtc: string
  bookedByEmail: string
}

interface AllBookings {
  timeZoneId: string
  bookings: AdminBooking[]
}

/**
 * Every booking across every user - the administrator capability assignment #3 asks for, and the
 * only read in this application that discloses one user's identity to another.
 */
export default function AdminBookings() {
  const [data, setData] = useState<AllBookings | null>(null)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    void api
      .get<AllBookings>('/api/bookings')
      .then(setData)
      .catch((failure: unknown) => setError(failure))
  }, [])

  return (
    <div className="space-y-5">
      <PageHeading>All bookings</PageHeading>
      <ErrorNote error={error} />

      {data === null ? (
        <p className="text-sm text-zinc-500">Loading…</p>
      ) : (
        <div className={`overflow-x-auto ${card}`}>
          <table className="w-full text-left text-sm">
            <thead className="border-b border-zinc-200 text-xs text-zinc-500">
              <tr>
                <th className="px-4 py-2 font-medium">Room</th>
                <th className="px-4 py-2 font-medium">Slot</th>
                <th className="px-4 py-2 font-medium">Booked by</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-200">
              {data.bookings.map((booking) => (
                <tr key={booking.slotId}>
                  <td className="px-4 py-2 font-medium text-zinc-900">{booking.roomName}</td>
                  <td className="px-4 py-2 text-zinc-600">
                    {formatDayAndRange(booking.startUtc, booking.endUtc, data.timeZoneId)}
                  </td>
                  <td className="px-4 py-2 text-zinc-600">{booking.bookedByEmail}</td>
                </tr>
              ))}
            </tbody>
          </table>

          {data.bookings.length === 0 && <p className="px-4 py-6 text-sm text-zinc-500">No bookings yet.</p>}
        </div>
      )}
    </div>
  )
}
