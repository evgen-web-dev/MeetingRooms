# Phase 7 — Frontend screens

**Satisfies:** the visible half of assignment #3, #4 and #7 — the capabilities in
`docs/requirements.md` §1, rendered as screens a reviewer can use without Scalar.

## Context

Every capability in §1 already exists as a verified endpoint. What the deployed URL serves is
phase 2's diagnostic shell plus phase 6's throwaway probe panel, so a reviewer opening the app
today sees a debug page rather than a booking system. This phase is the deliverable those five
phases were building toward, and it adds **no backend code at all**: every endpoint and both hub
methods exist, and `MapFallbackToFile` already makes client-side routes survive a hard refresh.

**Budget: 1–2 hours, for phases 7 and 8 together.** That is the governing constraint on every
choice below, and it is recorded rather than implied so the trade-offs read as decisions. Scope is
exactly §1; styling is plain and consistent rather than designed. `README.md` already carries both
submission links, the local-run steps, the concurrency-test commands and the concurrency rationale,
so phase 8 is genuinely small.

Settled by decision at planning time:

- **react-router 8.3.1** — the one new dependency. Deep-linkable rooms, a hard refresh that works,
  and admin guards as components.
- **Hand-written Tailwind, no component library.** `ui.shadcn.com` is still absent from
  `.devcontainer/init-firewall.sh`, so shadcn/ui would cost a firewall edit and a container rebuild
  before a line could be written — the prerequisite phase 6 flagged, now answered with *no*.
- **A day view with date navigation** for a room's schedule.

### Inherited obligations, all closed here

| From | Obligation |
|---|---|
| Phase 6 | One hub connection per session, swapping groups with `SubscribeToRoom`/`UnsubscribeFromRoom` rather than reconnecting per room |
| Phase 6 | Delete the probe panel, and decide deliberately where the transport diagnosis lives |
| Phase 6 | The second-tab limitation is answered by a **refetch**, never by a wider event |
| Phase 5 | Expired slots carry no server flag — the client greys them out from `endUtc` and its own clock |
| Phase 4 | No time-zone constant in the client: `timeZoneId` comes off the response into `Intl` |

## Out of scope

Everything `docs/requirements.md` names as out of scope, unchanged — no cancelling, no editing a
booking, no user management. Also: no frontend tests (a standing decision), no ESLint or Prettier,
and no README rewrite, which is phase 8's.

## Packages

**One ask: `react-router` 8.3.1** in `src/frontend`, as a `dependency`. It pulls one transitive
package, `cookie-es`; its peers are `react`/`react-dom` `>=19.2.7` against the 19.3.0 already here;
it was published 2026-08-28, well clear of the three-day cooldown `docs/decisions.md` mandates.
Nothing is added to any .NET project.

---

## The shape of the work

### The one decision worth arguing: the schedule is fetched whole, then paged by day in memory

A per-day fetch would send `fromUtc`/`toUtc` for "15 September in Europe/Kyiv", which means
computing a zone's day boundary **in the browser** — precisely the trap `docs/decisions.md` records
under *API surface and errors*, where a value carrying no offset is interpreted in the server's
local zone. Getting it wrong is an off-by-one-hour that only appears across a DST boundary, which
is the worst kind of bug to ship on a deadline: invisible today, wrong in October.

Fetching the horizon instead — `GET /api/rooms/{id}/schedule` with no bounds, ~140 slots, ~15 KB,
a ceiling `decisions.md` already establishes cannot grow with use — and grouping client-side with
`Intl.DateTimeFormat(timeZoneId)` removes that arithmetic entirely. It also makes day navigation
pure state, a live update a single array edit, and the reconnect refetch one call. Phase 4 carried
this forward as the expectation; it is both the safer answer and the faster one to write.

The day view is unaffected by this: it is the rendering, not the fetch.

### Routing and layout

```
/login  /register                       public
/rooms  /rooms/:id  /bookings           RequireAuth
/admin/bookings                         RequireAdmin
/diagnostics                            RequireAuth
/                                       → /rooms
```

Declarative mode (`<BrowserRouter><Routes>`), with `RequireAuth`/`RequireAdmin` as wrapper
components rather than a route-data loader — the smallest surface that does the job. `App.tsx`
stops being a page and becomes the layout: nav, `<Outlet/>`, and the hub status.

### Auth

Token **and** `expiresAtUtc` in `localStorage`, the standing decision, with the XSS exposure
already written into the README. An expired stored token is discarded on load without a request.

**Roles come from `GET /api/auth/me`, not from decoding the JWT.** No base64 parser in the client,
and it exercises the endpoint built partly to make a claim-type mismatch visible.

**Any 401 from any call ends the session**, through a module-level `onUnauthorized` hook that
`api/client.ts` exposes and the auth context sets. There are no refresh tokens by decision, so a
401 means the session is over and the only correct response is to return to the login screen.

**Registration logs in with the same credentials immediately.** Registration issues no token by
decision; a reviewer creating a second account for the two-browser check should not have to type
it twice.

### Real-time: one connection per session

`realtime/scheduleConnection.ts` keeps both of its recorded decisions — the explicit retry around
the first `start()`, and the handler wrapped in a block so it cannot return a value — and grows a
`subscribe(roomId)` that returns an unsubscribe. A provider owns exactly one connection for the
session; the schedule page subscribes on mount and unsubscribes when the room changes.

This closes phase 6's first carry. The probe panel reconnected from scratch on every room switch —
correct but wasteful, a negotiate plus a fresh handshake to Azure SignalR each time, and it left
`UnsubscribeFromRoom` called by nothing. **On reconnect: re-subscribe the current room _and_
refetch**, because anything announced while the connection was down was never delivered.

### Rooms, booking, and the admin views

Rooms list for everyone; for an admin, an inline create form, inline edit of name and capacity, and
delete, with 409 `RoomHasBookedSlots` rendered as a plain message. All on one page — separate admin
routes would be surface this budget cannot pay for.

The schedule renders `◀ Tue 15 Sep ▶` over the horizon's days, ten rows, each free / `booked` /
`booked by you`, with past slots greyed and unbookable. Booking posts `{ slotId }` and marks the row
locally on 201; a 409 or 404 shows the error code and refetches — which is also the honest answer
to the second-tab limitation phase 6 handed over.

`/bookings` is the caller's own list; `/admin/bookings` adds `bookedByEmail`, the one read in this
API that discloses one user's identity to another.

### Diagnostics

The health and transport panels **move** to `/diagnostics` rather than being deleted. That is the
deliberate answer phase 6 asked for, and the reasons are that the code exists and works, that it is
the only thing which can show Azure SignalR is carrying the traffic rather than the in-process
fallback, and that `README.md` already documents it. Linked from the footer, behind `RequireAuth`
because the transport diagnosis needs a token now that the hub is secured.

---

## Tasks

Branch `phase/7-screens`, from `develop`.

| # | Commit | Contents |
|---|---|---|
| 1 | `docs: plan phase 7` | this file |
| 2 | **(you)** `chore(web): add react-router` | `package.json` + lockfile |
| 3 | `feat(web): add routing, the auth context and the API client` | `main.tsx`, `App.tsx` layout, `api/client.ts`, `auth/` |
| 4 | `feat(web): add login and registration` | two pages |
| 5 | `feat(web): list rooms, with admin create, edit and delete` | `pages/Rooms.tsx` |
| 6 | `feat(web): show a room's day schedule and book a slot live` | `pages/RoomSchedule.tsx`, the reshaped connection, `time.ts` |
| 7 | `feat(web): list my bookings and, for an admin, everyone's` | two pages |
| 8 | `refactor(web): move the health and transport panels to /diagnostics` | probe panel deleted |
| 9 | `docs: fold phase 7 decisions into decisions.md` | before the branch merges |
| 10 | `docs: record phase 7 outcome` | after the deployed check |

### Standing decisions to fold into `decisions.md` (task 9)

Under *Frontend*: react-router 8.3.1 in declarative mode, with guards as components; the
whole-horizon fetch with client-side day grouping, and the DST reason for it; one hub connection
per session with group swapping; roles read from `/api/auth/me` rather than from a decoded token; a
401 anywhere ending the session; registration logging in immediately; and the probe panel becoming
`/diagnostics` rather than disappearing.

## What you do

1. `git switch -c phase/7-screens develop`, and commit this file.
2. From `src/frontend`: `npm install react-router@8.3.1 --save-exact --ignore-scripts --min-release-age 3`.
   I report what the lockfile resolves before you commit it.
3. Make each commit when I flag the point.
4. **Nothing to add in the Azure portal.** No new configuration key, no new secret.
5. Merge `phase/7-screens` into `develop` with `--no-ff`, then `develop` into `main`, which
   deploys. The two phase-6 docs commits `main` is currently behind ride along.

## Risks and fallbacks

| Risk | Fallback |
|---|---|
| react-router 8 has dropped or renamed declarative mode | Check the installed `.d.ts` before writing routes; `createBrowserRouter` is the same screens with a different entry point |
| The budget runs out mid-phase | Cut in this order, and nothing in §1 is ever cut: the nav's connection indicator, then `/diagnostics` (delete the panels; the README documents a `curl` negotiate instead), then styling polish |
| A room switch silently reconnects instead of swapping groups | Watch the Network tab: one WebSocket and no second negotiate. This is the check that proves phase 6's carry was closed rather than re-implemented |
| A deep link 404s on the deployed app | `MapFallbackToFile` already handles it; if it fails, the cause is the API's `/api/{**slug}` catch-all, not the router |

## Verification

```bash
npm --prefix src/frontend run build              # tsc --noEmit, then vite build
dotnet build                                     # 0 warnings
dotnet test tests/MeetingRooms.UnitTests         # 23
dotnet test tests/MeetingRooms.ConcurrencyTests  # 6
git diff --stat develop -- src/backend           # expected: empty
```

Locally, two browsers on `http://localhost:5173`, two accounts:

- Same room, one books → the other flips with no refresh.
- **Switch rooms in one tab → still one WebSocket, no second negotiate.**
- Room B open in both tabs, book in room A → nothing arrives.
- Stop the API for a few seconds; both tabs recover, and a booking afterwards still propagates.
- Admin: create, edit and delete a room; delete one holding a booking → 409 `RoomHasBookedSlots`.
- A non-admin sees no admin nav, and `/admin/bookings` typed directly redirects.
- Hard-refresh `/rooms/3` → the page loads.
- A past slot in today's grid is greyed, with no Book button.

On the deployed application: log in, book, watch it propagate to a second browser, and confirm
`/diagnostics` reports an Azure SignalR endpoint.

## Done when

- [ ] Every capability in `docs/requirements.md` §1 is reachable from the UI.
- [ ] Booking a slot updates other viewers of that room with no refresh, deployed.
- [ ] Switching rooms swaps groups on one connection, proven in the Network tab.
- [ ] A reconnected client re-subscribes and refetches.
- [ ] Admin-only screens are unreachable for a `User`, by nav and by URL.
- [ ] The probe panel is gone and the transport diagnosis has a stated home.
- [ ] `src/backend` has a zero diff.
- [ ] `dotnet build` zero warnings; 23 + 6 tests green; `npm run build` clean.
- [ ] `docs/decisions.md` carries this phase's decisions.

## Outcome

*To be written after the deployed check.*
