/**
 * Every function here takes the zone as an argument. Nothing in this client holds a copy of
 * `Europe/Kyiv`: the schedule and bookings responses both name `timeZoneId`, and two copies of a
 * constant is how a generator and a formatter drift apart (`docs/decisions.md`).
 */

/**
 * The calendar day an instant falls on, *in the display zone*, as `YYYY-MM-DD`.
 *
 * `en-CA` is the idiom rather than a curiosity: it is the locale whose short date format is
 * already ISO-ordered, so the parts come back sortable without assembling them by hand.
 */
export function dayKey(utc: string, timeZoneId: string): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: timeZoneId,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date(utc))
}

/** `Tue 15 Sep` */
export function formatDay(utc: string, timeZoneId: string): string {
  return new Intl.DateTimeFormat('en-GB', {
    timeZone: timeZoneId,
    weekday: 'short',
    day: '2-digit',
    month: 'short',
  }).format(new Date(utc))
}

/** `08:00` */
export function formatTime(utc: string, timeZoneId: string): string {
  return new Intl.DateTimeFormat('en-GB', {
    timeZone: timeZoneId,
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(utc))
}

/** `08:00 – 09:00` */
export function formatRange(startUtc: string, endUtc: string, timeZoneId: string): string {
  return `${formatTime(startUtc, timeZoneId)} – ${formatTime(endUtc, timeZoneId)}`
}

/** `Tue 15 Sep, 08:00 – 09:00` - one line, for the bookings lists. */
export function formatDayAndRange(startUtc: string, endUtc: string, timeZoneId: string): string {
  return `${formatDay(startUtc, timeZoneId)}, ${formatRange(startUtc, endUtc, timeZoneId)}`
}

/** A slot stops being bookable when it *ends* - the same rule the server enforces. */
export const hasEnded = (endUtc: string): boolean => Date.parse(endUtc) <= Date.now()
