# Meeting Rooms

A meeting-room booking system where a time slot can never be double-booked, including
when several people request the same slot at the same moment, and where booking status
reaches every viewer in real time.

| | |
|---|---|
| **Deployed app** | `https://meetingrooms-abg4a6ftg3gecda8.swedencentral-01.azurewebsites.net/` |
| **Repository** | `https://github.com/evgen-web-dev/MeetingRooms` |

> ASP.NET Core (net10.0) on Azure App Service, with Azure SQL Database and Azure SignalR
> Service. The frontend is React + TypeScript, built by Vite into the API's `wwwroot` and
> served by the same app, so there is no CORS and no cross-origin SignalR negotiate.

---

## Running it locally

Everything runs inside the dev container; there is no host setup.

The frontend is built by Vite into the API's `wwwroot`, so build it once before the first
run. Without this the API works but `/` returns 404, because there is no page to serve:

```bash
cd src/frontend && npm ci --ignore-scripts && npm run build
```

The API also needs a JWT signing key before it will start. It is validated at startup, so a
missing or too-short key stops the application rather than failing on the first login:

```bash
openssl rand -base64 32          # 32 bytes is the minimum the app accepts

cd src/backend/MeetingRooms.Api
dotnet user-secrets set 'Jwt:SigningKey' '<the base64 value>'
```

The seeded administrator is optional in Development - without it the app logs a warning and
starts with no admin account - and required in every other environment:

```bash
dotnet user-secrets set 'Seed:AdminEmail' 'admin@example.com'
dotnet user-secrets set 'Seed:AdminPassword' '<password>'
```

The password has to satisfy ASP.NET Core Identity's default policy: at least six characters,
with an uppercase letter, a lowercase letter, a digit and a non-alphanumeric character. **Use
single quotes** - inside double quotes the shell expands `$`, and what gets stored is not what
you typed.

These values live in `dotnet user-secrets` locally and in App Service application settings in
Azure. They are never in `appsettings.json` and never in git.

Then, from the repository root:

```bash
dotnet run --project src/backend/MeetingRooms.Api
```

That serves on <http://localhost:5000>. The database connection string is supplied by the
`ConnectionStrings__DefaultConnection` environment variable, already set in the container
and pointing at the `db` service (SQL Server, database `MeetingRooms`).

> **On macOS, port 5000 belongs to AirPlay Receiver.** The container binds it correctly and
> `curl` inside the container works, but the forwarded port on the host resolves to Apple's
> service instead, so the browser shows nothing useful. Either switch AirPlay Receiver off
> (System Settings → General → AirDrop & Handoff), or remap the host side: VS Code's
> **Ports** panel → *Change Local Address Port* on 5000.

For frontend work, `npm run dev` in `src/frontend` serves on <http://localhost:5173> with
hot reload, proxying `/api`, `/health` and `/hubs` to the API — so run both.

Two things cannot be exercised from inside the dev container, because its outbound
firewall allows only GitHub, npm, NuGet and Anthropic:

- **Azure SQL** — the local `db` container is used instead.
- **Azure SignalR** (`*.service.signalr.net`) — SignalR falls back to running in-process,
  which is correct locally. See *Diagnosing the realtime transport* below.

## Running the concurrency test

The test assignment #6 asks for is `tests/MeetingRooms.ConcurrencyTests`. It drives the **real
application** over HTTP through `WebApplicationFactory` — real controllers, real EF Core, real
Identity, real JWT — against a **real SQL Server**, and fires twenty simultaneous
`POST /api/bookings` at one slot from twenty different accounts. An in-memory or SQLite provider
would make the guarantee untestable, since it is the database's row lock that enforces it.

Two commands, from the repository root:

```bash
docker compose up -d
dotnet test tests/MeetingRooms.ConcurrencyTests
```

The first starts a throwaway SQL Server on `localhost:1433`. The second creates and migrates a
`MeetingRooms_Tests` catalog on it, registers its own users and rooms, and runs the suite.
`docker compose down` removes it again; nothing is persisted between runs.

The tests never touch a development database — the host derives its connection string from
whatever is configured and replaces only the catalog. That is not tidiness: bookings cannot be
cancelled, so a run against the development database would permanently consume demo slots.

> **Inside the dev container, skip the first command.** `ConnectionStrings__DefaultConnection`
> already points at the `db` service and overrides the fallback in `appsettings.Tests.json`. The
> container has no Docker CLI, so `docker-compose.yml` is written for a reviewer's host and has
> been verified by inspection rather than by being run.

**What the race asserts.** Exactly one request receives `201 Created`; the other nineteen receive
`409 Conflict` carrying `SlotAlreadyBooked`; none receives a 5xx; and the database ends holding
one booked row, whose booker is the caller who was told they won. It also asserts the requests
genuinely overlapped — the last was issued before the first came back — because twenty *serial*
requests produce an identical one-and-nineteen result and would otherwise pass for the wrong
reason.

Twenty distinct accounts rather than one caller repeating itself, because a caller who already
holds a slot is answered `201` by design; twenty requests from one account would report twenty
winners and prove nothing.

**The mutation probe is what makes a green run mean something.** Delete
`&& slot.BookedByUserId == null` from `SlotRepository.TryClaimAsync` and run the suite again: all
twenty requests receive `201` and the test fails. Restore it and it passes. A test that cannot be
shown to fail on a real defect is ceremony, so this is the phase's actual exit criterion rather
than the green run.

The same project also pins the slot lifecycle — a slot whose window has closed is refused, one
still under way is bookable — and three assertions earlier phases could not reach until a booking
could exist: that schedule instants reach the wire as UTC with a trailing `Z`, that `isBooked` and
`isBookedByMe` are right on their true branch without disclosing who booked, and that deleting a
room with a booked slot is refused with `409 RoomHasBookedSlots`.

## API reference

The OpenAPI document is at `/openapi/v1.json` and an interactive
[Scalar](https://scalar.com) reference at **`/scalar/`** (a bare `/scalar` redirects
there).

Both are served in **every environment, including the deployed app**. That is
deliberate: a reviewer should be able to exercise the API without cloning the
repository. It publishes the API surface publicly, which is an accepted trade: the
document describes endpoints rather than data, and contains no secrets.

## Azure configuration

### Resources

All in **Sweden Central**.

| Resource | Name | Notes |
|---|---|---|
| App Service (Web App) | `meetingrooms` | Basic B1, Always On, **single instance**, WebSockets **on**, HTTPS Only **on** |
| Azure SQL server | `sql-meetingrooms-test-task` | full name `sql-meetingrooms-test-task.database.windows.net`; *Allow Azure services* **on**; connection policy left at **Default** |
| Azure SQL database | `MeetingRooms (sql-meetingrooms-test-task/MeetingRooms)` | provisioned tier |
| Azure SignalR Service | `signalr-meetingrooms` | |

Single instance is load-bearing rather than incidental: it is what makes applying
migrations at startup safe, and what makes in-process SignalR a viable fallback.

**HTTPS Only is the platform setting that performs the HTTP→HTTPS redirect.** The app
deliberately does *not* use `UseHttpsRedirection`: App Service terminates TLS at its
front end, so the app sees plain HTTP and, without forwarded-headers configuration, the
middleware can redirect in a loop.

### Application settings

Names only — **no values appear in this repository**. Note that the two are configured
in *different places* in the portal, which is the most common way to get this wrong.

| Setting | Where it lives | What it is for | Since |
|---|---|---|---|
| `DefaultConnection` | App Service → **Connection strings**, type **SQLServer** | Azure SQL connection | phase 1 |
| `Azure__SignalR__ConnectionString` | App Service → **Application settings** | Azure SignalR Service; when absent the app uses in-process SignalR | phase 1 |
| `Jwt__SigningKey` | App Service → **Application settings** | signs access tokens | phase 3 |
| `Seed__AdminEmail` | App Service → **Application settings** | seeded admin account | phase 3 |
| `Seed__AdminPassword` | App Service → **Application settings** | seeded admin account | phase 3 |

Locally these come from `dotnet user-secrets`, never from `appsettings.json`.

### Diagnosing the realtime transport

`POST /hubs/schedule/negotiate?negotiateVersion=1` has three outcomes, and they need
different fixes. The placeholder page reports which one occurred.

| Response | Meaning | Fix |
|---|---|---|
| `url` containing `.service.signalr.net` plus `accessToken` | Azure SignalR is wired | — |
| `connectionId` and `availableTransports` | the connection string was **not read** | check the setting name, and that it is an *Application setting* rather than a *Connection string* |
| `500` — *Azure SignalR Service is not connected yet* | it **was** read; the app has no server connection to the service **yet** | see below — transient and persistent mean different things |

The second and third look similar from a browser but have nothing in common as causes.

The third is also the **normal cold-start window**: on startup the SDK opens server
connections to the service, and negotiate refuses until one is established. A request
arriving in the first moments after a deploy or a restart therefore gets this response
and the next one succeeds. Treat a single 500 straight after a deploy as expected; only
a *persistent* one indicates a wrong endpoint, a wrong access key, or blocked outbound
networking.

## Architecture

**One Azure Web App serves both halves.** The API runs from
`src/backend/MeetingRooms.Api`; the React bundle is built by Vite into that project's
`wwwroot` and served as static files by the same application. Same origin, so there is no CORS
configuration and no cross-origin SignalR negotiate — two of the most common ways this
arrangement goes wrong simply do not arise.

The backend is four projects, each depending only inwards:

| Project | Owns |
|---|---|
| `MeetingRooms.Domain` | `Room`, `Slot`, `AppUser`, the slot-grid generator, role constants. No dependencies |
| `MeetingRooms.Application` | ports (`ISlotRepository`, `IScheduleNotifier`, …), services, `OperationResult`, error codes, DTOs |
| `MeetingRooms.Infrastructure` | `AppDbContext`, EF configurations, repositories, Identity, migrations, seeders |
| `MeetingRooms.Api` | controllers, the SignalR hub, DI wiring, error-to-`ProblemDetails` mapping, `wwwroot` |

**A booking, end to end:** `BookingsController` → `BookingService` →
`SlotRepository.TryClaimAsync` (the one statement described under *Concurrency*) → announce.
The announcement fires on the `Claimed` outcome **only** — never on a caller re-posting a
booking they already hold, which succeeds but changes nothing.

**Real time** is one SignalR group per room. A client calls `SubscribeToRoom` / `UnsubscribeFromRoom`
on a single connection held for the whole session, so switching rooms swaps groups instead of
reconnecting. The broadcast happens *after* the claim has committed, and carries
`{ roomId, slotId }` and nothing else — no booker identity, consistent with what the schedule
endpoint discloses.

**Time** is stored in UTC everywhere. A schedule response names its display zone once
(`timeZoneId`, `Europe/Kyiv`) and the client renders with `Intl`; no zone arithmetic happens in
the browser and no zone is stored per row.

## The screens

Real URLs, not view state: the API serves `index.html` for unmatched routes, so a deep link to
a room survives a hard refresh.

| Route | What it is | Needs |
|---|---|---|
| `/login`, `/register` | email and password; registering grants the `User` role and logs straight in | — |
| `/rooms` | every room; for an admin, inline create, edit and delete | `User` |
| `/rooms/:roomId` | the day schedule, `◀ Tue 15 Sep ▶` across a 14-day horizon. Book a free slot; other viewers of that room see it immediately | `User` |
| `/bookings` | your own bookings | `User` |
| `/admin/bookings` | every user's bookings, with the booker's email — the one read in this API that discloses one user's identity to another | `Admin` |
| `/diagnostics` | database health, and which transport SignalR is actually using | `User` |
| `/scalar/` | the interactive API reference | — |

Admin routes are unreachable for a `User` both from the navigation and by typing the URL. That
is convenience, not the security boundary — every endpoint behind them is gated server-side
with `[Authorize(Roles = …)]`.

## Concurrency

A slot is never double-booked because **the booking is one statement**: an atomic conditional
update — compare-and-set, with the business condition inside the `WHERE` clause. The whole
guarantee is `SlotRepository.TryClaimAsync`:

```csharp
var claimed = await _dbContext.Set<Slot>()
    .Where(slot => slot.Id == slotId
                && slot.BookedByUserId == null
                && slot.EndUtc > nowUtc)
    .ExecuteUpdateAsync(
        setters => setters
            .SetProperty(slot => slot.BookedByUserId, userId)
            .SetProperty(slot => slot.BookedAtUtc, nowUtc),
        cancellationToken);
```

`ExecuteUpdateAsync` is the one EF Core API that bypasses the change tracker: it compiles to a
single `UPDATE … WHERE` and sends it, loading nothing. Everything else EF does is
load-mutate-save — a read followed by a write, which is the shape the assignment rules out.

**What happens when two requests race.** Both statements arrive at the same row. One takes the
row lock and commits. The other blocks on that lock, re-reads the committed row once it clears,
fails `BookedByUserId IS NULL`, and updates zero rows. The check and the write are one
statement, so no interleaving lets both observers see `NULL`.

- The winner's statement reports one row → **`201 Created`**.
- Every loser's reports zero → **`409 Conflict`**, carrying the error code `SlotAlreadyBooked`.
- Nobody receives a 5xx, and nothing is silently overwritten.

**This is not "check if free, then book".** The application never decides whether the slot is
free. By the time a row count comes back the race is already over — the database settled it
under the row lock — and the count *reports* which outcome occurred rather than causing it.

**Isolation level.** Correct under READ COMMITTED with or without RCSI, which Azure SQL enables
by default. It would *not* hold under SNAPSHOT, which raises update-conflict 3960 instead, so
this path opens no transaction of its own and uses the connection's default level.

**No client-supplied value takes part.** The client posts `{ slotId }`, a server-generated
surrogate key. No clock skew, no time-picker glitch and no `datetime2` rounding can produce two
rows meaning the same slot.

### Three things worth answering before they are asked

- **A repeat booking by the same user answers 201, not 409.** A caller who already holds the
  slot is told they hold it; telling the actual winner they lost is the one misreport this
  design refuses to make. Booking is therefore idempotent per user — which is exactly why the
  mandated test races **twenty distinct accounts** rather than one caller twenty times.
- **The unique index is not the guarantee.** `UNIQUE (RoomId, StartUtc)` exists so the slot
  seeder cannot produce two rows meaning the same hour. It constrains slot *identity*, and has
  no part in booking.
- **The test asserts that the requests overlapped**, because twenty *serial* requests produce an
  identical one-201-and-nineteen-409s result, and the mutation probe above reddens the same way
  either way. Without that assertion a green run would prove the response codes and not the
  concurrency.

### The trade-off, stated

The invariant lives in one statement's `WHERE` clause rather than in a standing database
constraint. That binds every path through `TryClaimAsync` — which must remain the **only** write
path to `BookedByUserId` — rather than binding the schema for all time. A separate `Bookings`
table with `UNIQUE (SlotId)` would move the invariant into the schema, at the cost of a 1:1
table carrying no state of its own: no lifecycle, no cancellation, no attendees, no price. For
this scope, one write path plus an automated test that can be *shown* to fail is the
proportionate trade.

The boundary is named rather than left implicit: if a booking ever gains state of its own —
cancellation, rescheduling, or one booking spanning several slots — it becomes an entity and
this design splits into two tables. None of that is in scope.

The long form, including what was rejected and why, is in `docs/decisions.md` under
*Booking and concurrency*.

## Development process

Built with Claude Code. `CLAUDE.md` carries the working agreement; `docs/` carries the
plan, the binding technical decisions, and one document per phase written and approved
before that phase's implementation started.

| Document | Contents |
|---|---|
| `assignment.md` | the original requirements |
| `docs/requirements.md` | the assignment resolved into concrete behaviour |
| `docs/decisions.md` | binding technical decisions |
| `docs/plan.md` | design reasoning, phase sequence, risks |
| `docs/phases/` | one document per phase, each with an outcome recorded after the fact |
