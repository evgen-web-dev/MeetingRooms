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

## For the reviewer

**Using the deployed application.** Open it and register. An account created this way receives
the `User` role, which is sufficient to browse rooms, view any room's schedule and book a free
slot. Opening a second browser on the same room demonstrates the real-time behaviour: booking in
one updates the other without a refresh. **Admin credentials are supplied with the submission
links**, as self-registration cannot grant `Admin` by design and the seeded account's password is
not held in this repository.

**Verifying the concurrency guarantee.** Two commands and approximately one minute — see
[Running the concurrency test](#running-the-concurrency-test). It needs the .NET SDK and
Docker, and nothing else: no Node, no frontend build, and no configuration of your own. That
section also sets out a **mutation probe**, which demonstrates that the test fails when the
guarantee is removed.

**Reading the implementation.** Three files, in a suggested order:

| | |
|---|---|
| `src/backend/MeetingRooms.Infrastructure/Persistence/Repositories/SlotRepository.cs` | `TryClaimAsync` — the entire no-double-booking mechanism is one `WHERE` clause, and the reasoning is in the XML comment above it |
| `tests/MeetingRooms.ConcurrencyTests/SlotBookingConcurrencyTests.cs` | twenty simultaneous requests at one slot, and what is asserted about them |
| `CLAUDE.md` and `docs/` | how this was built with Claude Code — the working agreement, the binding decisions, and one document per phase, each written *before* its implementation and closed with an honest outcome |

Prose versions of the first two are below, under [Concurrency](#concurrency) and
[Architecture](#architecture).

## Running the concurrency test

The test required by assignment #6 is `tests/MeetingRooms.ConcurrencyTests`. It drives the **real
application** over HTTP through `WebApplicationFactory` — real controllers, real EF Core, real
Identity, real JWT — against a **real SQL Server**, and fires twenty simultaneous
`POST /api/bookings` at one slot from twenty different accounts. An in-memory or SQLite provider
would make the guarantee untestable, since it is the database's row lock that enforces it.

### Prerequisites

The test requires only the following.

- **.NET SDK 10.0.100 or later.** There is no `global.json`, so any 10.x SDK works.
- **Docker**, running — Docker Desktop on Windows or macOS, Docker Engine on Linux.
- **No Node, and no frontend build.** `wwwroot` is not in the repository, and the test drives
  the API directly rather than through a browser, so it never needs one.
- **No secrets, and no configuration of your own.**
  `tests/MeetingRooms.ConcurrencyTests/appsettings.Tests.json` supplies the signing key, the
  seeded administrator and a `localhost` connection-string fallback. They are fixtures for a
  throwaway container, not credentials — the real ones live in `dotnet user-secrets` and in App
  Service application settings.

### Two commands, from the repository root

The same two on every platform — the forward slashes work in PowerShell and `cmd` as well:

```bash
docker compose up -d --wait
dotnet test tests/MeetingRooms.ConcurrencyTests
```

The first starts a throwaway SQL Server on `localhost:1433` and, thanks to `--wait`, returns only
once it reports healthy — so the test cannot race a database that is still booting. The second
creates and migrates a `MeetingRooms_Tests` catalog on it, registers its own users and rooms, and
runs the suite. A successful run ends:

```text
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6
```

`docker compose down` removes the container again; nothing is persisted between runs.

The tests never touch a development database — the host derives its connection string from
whatever is configured and replaces only the catalog. That is not tidiness: bookings cannot be
cancelled, so a run against the development database would permanently consume demo slots.

<details>
<summary><b>Troubleshooting</b> — port 1433, Apple silicon, older Compose versions</summary>

| Symptom | Cause, and what to do |
|---|---|
| `Ports are not available` / `address already in use: 1433` | Something already listens there — on Windows, most often a locally installed SQL Server. Change the host side of the mapping in `docker-compose.yml` to `"14333:1433"`, then point the tests at the new port with an environment variable rather than editing the fixture (see below). |
| The first run takes several minutes, on Apple silicon | The SQL Server image has no arm64 build, so `platform: linux/amd64` runs it under emulation. The healthcheck allows a 40-second start period for exactly this. It happens once, on the first pull and boot. |
| `unknown flag: --wait` | Docker Compose older than v2.1.1. Run `docker compose up -d`, then wait until `docker compose ps` reports the service `healthy` before testing. |
| A connection error on the first `dotnet test` | The container was not ready yet. Re-run it; `--wait` is what prevents this. |
| `docker: command not found`, or a daemon error | Docker Desktop is not running. On Windows it also needs its WSL 2 backend enabled. |

**Overriding the port or the server.** `BookingApiFactory` takes whatever connection string is
configured and replaces only the catalog, so an environment variable is honoured and no file has
to be edited:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost,14333;User Id=sa;Password=MeetingRooms_Local_1;TrustServerCertificate=True"
```

```bash
export ConnectionStrings__DefaultConnection="Server=localhost,14333;User Id=sa;Password=MeetingRooms_Local_1;TrustServerCertificate=True"
```

**Inside this repository's dev container, skip `docker compose` entirely.**
`ConnectionStrings__DefaultConnection` already points at the `db` service and overrides the
fallback in `appsettings.Tests.json`. The container has no Docker CLI, which is why
`docker-compose.yml` is written for a reviewer's host and has been verified by inspection rather
than by being run.

</details>

### What the race asserts

Exactly one request receives `201 Created`; the other nineteen receive `409 Conflict` carrying
`SlotAlreadyBooked`; none receives a 5xx; and the database ends holding one booked row, whose
booker is the caller who was told they won. It also asserts the requests genuinely **overlapped**
— the last was issued before the first came back — because twenty *serial* requests produce an
identical one-and-nineteen result and would otherwise pass for the wrong reason. Twenty distinct
accounts, rather than one caller repeating itself, because a caller who already holds a slot is
answered `201` by design.

### The mutation probe

A passing test is evidence only if it can also be shown to fail. Removing the guarantee
demonstrates that it can:

1. Open `src/backend/MeetingRooms.Infrastructure/Persistence/Repositories/SlotRepository.cs`.
2. In `TryClaimAsync`, delete the line `&& slot.BookedByUserId == null`.
3. Run the test again. All twenty requests now receive `201`, and the test fails.
4. Restore the line. It passes.

A test that cannot be shown to fail on a real defect establishes nothing, so this demonstration —
rather than the passing run — is what the booking phase treated as its exit criterion.

The other five tests in the project pin the slot lifecycle (a closed window is refused, one still
under way is bookable) and three things that could not be asserted until a booking could exist:
that schedule instants reach the wire as UTC with a trailing `Z`, that `isBooked` and
`isBookedByMe` are correct without disclosing who booked, and that deleting a room holding a
booking is refused with `409 RoomHasBookedSlots`.

## Concurrency

A slot is never double-booked because **the booking is one statement** — an atomic conditional
update, with the business condition inside the `WHERE` clause. The whole guarantee is
`SlotRepository.TryClaimAsync`:

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
load-mutate-save — a read and then a write, which is the shape the assignment rules out.

**When two requests race**, both statements arrive at the same row. One takes the row lock and
commits. The other blocks on that lock, re-reads the committed row once it clears, fails
`BookedByUserId IS NULL`, and updates zero rows. The check and the write are one statement, so
no interleaving lets both see `NULL`. The winner's statement reports one row → **`201 Created`**;
every loser's reports zero → **`409 Conflict`**, carrying `SlotAlreadyBooked`. Never a 5xx, and
nothing silently overwritten.

**This is not "check if free, then book".** The application never decides whether the slot is
free: by the time a row count comes back the database has already settled the race under the row
lock, and the count *reports* which outcome occurred rather than causing it.

Two supporting properties. It is correct under READ COMMITTED with or without RCSI — which Azure
SQL enables by default — but *not* under SNAPSHOT, which raises update-conflict 3960 instead, so
this path opens no transaction of its own and uses the connection's default level. And no
client-supplied value takes part: the client posts `{ slotId }`, a server-generated surrogate
key, so no clock skew and no `datetime2` rounding can produce two rows meaning the same slot.

### Three points a reviewer is likely to question

- **A repeat booking by the same user answers 201, not 409.** Reporting a loss to the caller who
  actually holds the slot is the one incorrect response this design does not accept. Booking is
  therefore idempotent per user, which is why the mandated test races twenty **distinct**
  accounts rather than one caller twenty times.
- **The unique index is not the guarantee.** `UNIQUE (RoomId, StartUtc)` prevents the slot seeder
  from producing two rows meaning the same hour. It constrains slot *identity*, and has no part
  in booking.
- **The test asserts that the requests overlapped.** Twenty *serial* requests produce an identical
  result, and the mutation probe fails identically in either case, so without that assertion a
  passing run would establish the response codes and not the concurrency.

### The trade-off

The invariant lives in one statement's `WHERE` clause rather than in a standing database
constraint, so it binds every path through `TryClaimAsync` — which must remain the **only** write
path to `BookedByUserId` — rather than binding the schema for all time. A separate `Bookings`
table with `UNIQUE (SlotId)` would move it into the schema, at the cost of a 1:1 table carrying
no state of its own. For this scope, one write path plus a test that can be *shown* to fail is
the proportionate trade. If a booking ever gains state of its own — cancellation, rescheduling,
or spanning several slots — it becomes an entity and this design splits into two tables. None of
that is in scope.

The long form, including what was rejected and why, is in `docs/decisions.md` under
*Booking and concurrency*.

## Architecture

**One Azure Web App serves both halves.** The React bundle is built by Vite into the API
project's `wwwroot` and served as static files by the same application, so there is no CORS
configuration and no cross-origin SignalR negotiate, which removes two common sources of error in
this arrangement.

The backend is four projects, each depending only inwards:

| Project | Owns |
|---|---|
| `MeetingRooms.Domain` | `Room`, `Slot`, `AppUser`, the slot-grid generator, role constants. No dependencies |
| `MeetingRooms.Application` | ports (`ISlotRepository`, `IScheduleNotifier`, …), services, `OperationResult`, error codes, DTOs |
| `MeetingRooms.Infrastructure` | `AppDbContext`, EF configurations, repositories, Identity, migrations, seeders |
| `MeetingRooms.Api` | controllers, the SignalR hub, DI wiring, error-to-`ProblemDetails` mapping, `wwwroot` |

**A booking, end to end:** `BookingsController` → `BookingService` →
`SlotRepository.TryClaimAsync` (the one statement above) → announce. The announcement fires on
the `Claimed` outcome **only** — never on a caller re-posting a booking they already hold, which
succeeds but changes nothing.

**Real time** is one SignalR group per room. A client calls `SubscribeToRoom` /
`UnsubscribeFromRoom` on a single connection held for the whole session, so switching rooms swaps
groups instead of reconnecting. The broadcast happens *after* the claim has committed and carries
`{ roomId, slotId }` and nothing else — no booker identity, consistent with what the schedule
endpoint discloses.

**Time** is stored in UTC everywhere. A schedule response names its display zone once
(`timeZoneId`, `Europe/Kyiv`) and the client renders with `Intl`, so no zone arithmetic happens
in the browser and no zone is stored per row.

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

## API reference

The OpenAPI document is at `/openapi/v1.json`, and an interactive
[Scalar](https://scalar.com) reference at **`/scalar/`** (a bare `/scalar` redirects there).
Both are served in **every environment, including the deployed app** — deliberately, so a
reviewer can exercise the API without cloning the repository. It publishes the API surface
publicly, which is an accepted trade: the document describes endpoints rather than data, and
contains no secrets.

## Azure configuration

All resources are in **Sweden Central**.

| Resource | Name | Notes |
|---|---|---|
| App Service (Web App) | `meetingrooms` | Basic B1, Always On, **single instance**, WebSockets **on**, HTTPS Only **on** |
| Azure SQL server | `sql-meetingrooms-test-task` | `sql-meetingrooms-test-task.database.windows.net`; *Allow Azure services* **on**; connection policy left at **Default** |
| Azure SQL database | `MeetingRooms` | provisioned tier |
| Azure SignalR Service | `signalr-meetingrooms` | |

Single instance is load-bearing rather than incidental: it is what makes applying migrations at
startup safe, and what makes in-process SignalR a real fallback. **HTTPS Only is the platform
setting that performs the HTTP→HTTPS redirect** — the app deliberately does *not* use
`UseHttpsRedirection`, because App Service terminates TLS at its front end and the middleware
can otherwise redirect in a loop.

Application settings, **names only — no values appear in this repository**. The first is
configured in a different place in the portal from the rest, which is the most common way to
get this wrong:

| Setting | Where it lives | What it is for |
|---|---|---|
| `DefaultConnection` | App Service → **Connection strings**, type **SQLServer** | Azure SQL |
| `Azure__SignalR__ConnectionString` | App Service → **Application settings** | Azure SignalR; when absent the app runs SignalR in-process |
| `Jwt__SigningKey` | App Service → **Application settings** | signs access tokens |
| `Seed__AdminEmail`, `Seed__AdminPassword` | App Service → **Application settings** | the seeded admin account |

Locally these come from `dotnet user-secrets`, never from `appsettings.json`.

<details>
<summary><b>Diagnosing the realtime transport</b> — and how Azure SignalR was verified</summary>

On a single instance, **behaviour cannot tell Azure SignalR apart from the in-process
fallback**: both deliver the same events to the same groups. So the service was not inferred
from things working — it was verified by reading the client's socket URL, which under Azure
SignalR is `wss://signalr-meetingrooms.service.signalr.net/client/?hub=…` rather than a path on
the app's own origin. `/diagnostics` reports it.

`POST /hubs/schedule/negotiate?negotiateVersion=1` has three outcomes, needing different fixes:

| Response | Meaning | Fix |
|---|---|---|
| `url` containing `.service.signalr.net`, plus `accessToken` | Azure SignalR is wired | — |
| `connectionId` and `availableTransports` | the connection string was **not read** | check the setting name, and that it is an *Application setting* rather than a *Connection string* |
| `500` — *Azure SignalR Service is not connected yet* | it **was** read; the app has no server connection to the service yet | see below |

The second and third look similar in a browser and have nothing in common as causes. The third
is also the **normal cold-start window**: on startup the SDK opens server connections to the
service and negotiate refuses until one is established, so a request in the first moments after
a deploy gets it and the next one succeeds. Only a *persistent* 500 means a wrong endpoint, a
wrong key, or blocked outbound networking.

**The token reaches the hub differently on each path.** A browser cannot set an `Authorization`
header on a WebSocket, so on the in-process path the access token arrives as an `access_token`
query-string parameter and the JWT handler is configured to read it from there **for hub paths
only**. Under Azure SignalR the socket terminates at the service instead, and the claims come
from the `Authorization` header on the negotiate request — so securing the hub also makes an
anonymous negotiate probe useless as a diagnostic.

</details>

## Running it locally

Everything runs inside the dev container; there is no host setup. From the repository root:

```bash
cd src/frontend && npm ci --ignore-scripts && npm run build   # once, before the first run
cd -
dotnet run --project src/backend/MeetingRooms.Api             # serves on http://localhost:5000
```

The frontend build is what puts a page in `wwwroot`; without it the API works but `/` returns
404. The database connection string comes from the `ConnectionStrings__DefaultConnection`
environment variable, already set in the container and pointing at the `db` service.

The API also needs a **JWT signing key** before it will start — it is validated at startup, so a
missing or too-short key stops the application rather than failing on the first login:

```bash
openssl rand -base64 32                                    # 32 bytes is the minimum accepted
dotnet user-secrets --project src/backend/MeetingRooms.Api set 'Jwt:SigningKey' '<the value>'
```

<details>
<summary><b>Further local setup</b> — the seeded administrator, shell quoting, the macOS port clash, and the Vite dev server</summary>

**A seeded administrator** is optional in Development — without it the app logs a warning and
starts with no admin account — and required in every other environment:

```bash
dotnet user-secrets --project src/backend/MeetingRooms.Api set 'Seed:AdminEmail' 'admin@example.com'
dotnet user-secrets --project src/backend/MeetingRooms.Api set 'Seed:AdminPassword' '<password>'
```

The password has to satisfy ASP.NET Core Identity's default policy: at least six characters, with
an uppercase letter, a lowercase letter, a digit and a non-alphanumeric character. **Use single
quotes** — in bash, and in PowerShell, double quotes expand `$`, and what gets stored is then not
what you typed. These values live in `dotnet user-secrets` locally and in App Service application
settings in Azure; never in `appsettings.json`, and never in git.

**On macOS, port 5000 belongs to AirPlay Receiver.** The container binds it correctly and `curl`
inside the container works, but the forwarded port on the host resolves to Apple's service
instead, so the browser shows nothing useful. Either switch AirPlay Receiver off
(System Settings → General → AirDrop & Handoff), or remap the host side in VS Code's **Ports**
panel → *Change Local Address Port* on 5000.

**For frontend work**, `npm run dev` in `src/frontend` serves on <http://localhost:5173> with hot
reload, proxying `/api`, `/health` and `/hubs` to the API — so run both. Note the dev-server
caveat under *Known limitations*: verify anything real-time against `:5000`.

**Two things cannot be exercised from inside the dev container**, because its outbound firewall
allows only GitHub, npm, NuGet and Anthropic: **Azure SQL** (the local `db` container stands in)
and **Azure SignalR** (`*.service.signalr.net`, where SignalR falls back to running in-process,
which is correct locally).

</details>

## Known limitations

Each of the following is a deliberate decision rather than an oversight.

- **Bookings cannot be cancelled, rescheduled or edited.** A deliberate scope choice: a booking
  has no state of its own, which is what lets one slot be one row. `docs/requirements.md` lists
  the full out-of-scope set.
- **Reconnection gives up after four attempts, roughly 17 seconds.** Inside that window a
  dropped connection recovers on its own — it re-subscribes to the current room and refetches,
  because anything announced while it was down was never delivered. Beyond it the header reads
  `Live updates off — reload`, which surfaces the problem to the user rather than hiding it.
  This never affects the booking guarantee or conflict handling; only recovery from an outage.
- **Live updates are unreliable under `npm run dev`, and correct in the production build.** The
  likely cause is React StrictMode double-invoking the subscription effect, so a teardown from
  the first pass resolves after the second has registered — leaving a connected socket in no
  group. That is a hypothesis with strong circumstantial support, **not a proven root cause**;
  it is written up in `docs/phases/phase-7-screens.md` along with the check that would confirm
  it. Verify anything real-time against `:5000` after `npm run build`.
- **CI does not run the concurrency test** — the GitHub runner has no SQL Server. It builds the
  whole solution, so a compile break in that project still fails the deploy, and the test is run
  locally and by the reviewer with the two commands above.
- **OpenAPI and Scalar are public in every environment**, so a reviewer can exercise the API
  without cloning. Endpoints, not data, and no secrets.

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
