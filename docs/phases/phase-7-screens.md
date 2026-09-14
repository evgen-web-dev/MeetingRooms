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

- [x] Every capability in `docs/requirements.md` §1 is reachable from the UI.
- [x] Booking a slot updates other viewers of that room with no refresh, deployed.
- [x] Switching rooms swaps groups on one connection, proven in the Network tab.
- [~] A reconnected client re-subscribes and refetches - **within the ~17 s retry window only**, and
      re-tested only inside it. See the Outcome.
- [x] Admin-only screens are unreachable for a `User`, by nav and by URL.
- [x] The probe panel is gone and the transport diagnosis has a stated home.
- [x] `src/backend` has a zero diff.
- [x] `dotnet build` zero warnings; 23 + 6 tests green; `npm run build` clean.
- [x] `docs/decisions.md` carries this phase's decisions.

## Outcome

**Completed and deployed 2026-09-14.** Every screen `docs/requirements.md` §1 asks for is live at
the deployed URL, real-time works across two browsers, and the Diagnostics page reports Azure
SignalR as the transport with `accessToken: present`.

Ten planned tasks became **fourteen commits**, and the phase cost more wall-clock time than the
1-2 hour budget it was planned against. Both overruns have the same cause, recorded below because
it is the useful part.

### Verified

- `dotnet build` clean, zero warnings. **23** unit and **6** integration tests green - unchanged,
  because `src/backend` has a **zero diff** for the whole phase, provable from
  `git diff --stat 61993b3 -- src/backend` being empty.
- `npm run build` clean (`tsc --noEmit` then `vite build`). The bundle went 282 → 337 kB raw
  (~102 kB gzipped), of which ~54 kB is react-router.
- A **Node SignalR client against the running application**, run repeatedly through the phase and
  finally through the **Vite proxy on :5173**: a booking reaches a subscriber of that room; the
  payload is `{ roomId, slotId }` and nothing else; **after switching rooms on one connection, a
  booking in the room just left is not delivered**; the newly joined room is; a replayed booking
  answers 201 and announces nothing; another user booking a taken slot gets 409 `SlotAlreadyBooked`.
- In two browsers, two accounts, one room, on the **deployed app**: booking propagates with no
  refresh, in both directions. Deep links survive a hard refresh. Admin create, edit and delete
  behave, including the refusal on a room holding a booking. A non-admin sees no admin nav and is
  redirected from `/admin/bookings`.
- The room switch was watched in the browser's Network tab: **one WebSocket, no second negotiate** -
  which is what proves phase 6's carry was closed rather than re-implemented as a reconnect.

### What went wrong, and what it cost

The local verification found a real defect: `withAutomaticReconnect([0, 2000, 5000, 10000])` is a
**finite list of four attempts**, so after ~17 seconds the client gave up permanently and the tab
stayed live-looking and dead until reloaded. That window is shorter than a deploy - phase 5
measured an App Service restart at ~10 s and phase 6 saw a 30-60 s cold start - so it was worth
fixing, and phase 6's own reconnect check had passed only because its outage was 3.6 s.

The fix was two parts: a retry policy that always returns a number, and a guard so `subscribe`
restarts a connection it finds terminally disconnected rather than invoking on it. **The guard
tested `connection.state`, which races with `startWithRetry` awaiting a delay before calling
`start()`** - a second subscribe arriving in that window still sees `Disconnected`, clears the
start promise, and starts the same connection twice. Live updates stopped entirely. Moving the
reset into `onclose` removed that race, and the browser was *still* broken, at which point the
whole fix was **rolled back to `331cbac`** rather than debugged further against a deadline.

The rolled-back tree then failed in the browser too - and worked on the **production bundle served
by the API at :5000**. That is the finding the phase turns on.

### The dev-only defect, stated as the hypothesis it is

With identical source, live updates work in the production build and are unreliable under Vite's
dev server. The likely mechanism is React **StrictMode double-invoking effects**: `useRoomLiveUpdates`
subscribes, is torn down mid-flight, and subscribes again, so the first pass's `unsubscribe` can
resolve *after* the second pass registered its handler - deleting it from the connection's `rooms`
map and sending `UnsubscribeFromRoom` for a room the live subscription still wants. The result is a
connected socket in no group with no handler: no events, and **no console errors**, which is exactly
what was observed.

This is a hypothesis with strong circumstantial support, **not a proven root cause**. Evidence for:
production works and development does not; the server-side probe passes in both, and it is the one
thing that never exercises React's lifecycle. Evidence it is timing-dependent rather than
deterministic: checks 1-4 passed on `:5173` earlier in the same session on the same code.

**What would confirm it:** the WS frame log showing `SubscribeToRoom` followed by
`UnsubscribeFromRoom` on page load, or a guard in the returned unsubscribe that refuses to act when
a newer subscription owns the room - `if (rooms.get(roomId) !== handlers) return` - making the
symptom disappear in development. That guard is the fix to try first, and it was deliberately not
attempted today: two speculative fixes had already been shipped and rolled back, and the deadline
was the wrong place for a third.

### Accepted limitations

- **The reconnect window is four attempts, ~17 seconds.** Inside it, recovery works. Beyond it the
  client gives up and the tab needs a reload; the header says `Live updates off — reload`, which is
  a workaround surfaced to the user rather than a fix. The scenario was deliberately not re-tested
  after the rollback. It never affects the booking guarantee, conflict handling, or live updates in
  normal use - only recovery from an outage.
- **Live updates are unreliable under `npm run dev`.** Development verification of anything
  real-time should be done against `:5000` after `npm run build` until the guard above is added.

### Deviations from the plan

- **Ten code commits rather than eight.** Splitting them so every commit left the tree building put
  the router wiring - `main.tsx` and `App.tsx`, which is also where the probe panel dies - *after*
  the pages it imports, rather than first as the plan listed.
- **Four commits the plan did not list**: two fixes that were rolled back, the shell-readability and
  error-message pass that came out of smoke testing, and a favicon.
- **The whole-horizon fetch decision was made at planning time and held**, and it paid twice: no
  day-boundary arithmetic in the browser, and a live update that is one array edit.

### Learned, and not anticipated by this document

- **A green server-side probe can pass throughout while the application is broken in a browser.**
  The Node client proved the hub, the groups, the payload, room scoping and the proxy - and was
  green during every minute the app did not work. It cannot see React's lifecycle, and that is where
  the defect was. A probe proves the half of the system it touches, and it is worth saying out loud
  which half that is.
- **StrictMode is a test the production build never runs.** It exists to surface exactly this class
  of bug, and a subscription whose teardown is asynchronous is precisely what it targets. The
  lesson is not to disable it: it found a real ordering weakness in the module's ownership model.
- **`pkill -f` matches by pattern, not by who started the process.** Stopping "my" dev servers
  killed the user's, and the resulting `ECONNREFUSED` wall looked like an application fault for
  several minutes.
- **The error body has two shapes and the client must read both.** A validation failure answers
  `ValidationProblemDetails`, whose messages sit under `errors` keyed by field while `title` is the
  framework's generic sentence; `errorDetails` - which the client read exclusively - is only ever
  present on a *business* failure. Too large a capacity therefore surfaced "One or more validation
  errors occurred." and discarded "'Capacity' must be between 1 and 1000."
- **Identity's password codes reach the screen by design**, a consequence `docs/decisions.md`
  recorded in phase 3 and which only became visible when a person used the form.
- **Tailwind 4's preflight sets `cursor: default` on buttons**, following the CSS spec. One base
  rule restores it everywhere; per-component classes would have to be remembered.
- **A status label should name a property of the page, not of the transport.** "Connected" is the
  SignalR client's word; what a reader needs is whether the screen updates itself, and what to do
  when it does not.

### Carried into later phases

- **Fix the dev-mode subscription race**, starting with the ownership guard in `subscribe`'s
  returned unsubscribe. Confirm by the WS frame log rather than by the symptom disappearing.
- **Restore indefinite reconnection** once that is done - the retry policy itself was never shown to
  be wrong, and it was verified across a 55-second outage before the rollback took it out.
- **Phase 8's README** must name the screens, point `/diagnostics` at its new home, and carry what
  phases 5 and 6 sent forward: the query-string token defence, that Azure SignalR was verified by
  the client's socket URL, the unique `(RoomId, StartUtc)` index being seeder integrity only, that a
  repeat booking answers 201, and why the overlap assertion exists. The reconnect limitation above
  belongs there too.
- **Housekeeping:** verification consumed roughly ten slots in *Board Room* and *Focus Room* across
  2026-09-27 and neighbouring days, and registered `p7a@example.test` / `p7b@example.test` plus
  `weak-probe@example.test` in the development database. Bookings cannot be cancelled; the slots are
  gone.
- **`develop` is one commit behind `main`** (`d1dfd38 feat(web): add a favicon`, committed on
  `main`). Fast-forward `develop` before branching phase 8, or that branch starts without it.

