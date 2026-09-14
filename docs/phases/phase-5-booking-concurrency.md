# Phase 5 — Booking, the atomic conditional update, and the concurrency test

**Satisfies:** assignment #5 (booking with a deliberate, explained concurrency mechanism) and #6
(an automated concurrency test in the repository, runnable by the reviewer). This is the graded
core; everything before it exists to make this one write correct.

## Context

Phase 4 ended with rooms and slots in Azure SQL: four demo rooms, a rolling 14-day grid of
60-minute windows per room, admin room CRUD, and a schedule read that reports free versus booked.
`Slot.BookedByUserId` is nullable and **nothing has ever written it** — deliberately, including
the seeders.

That is the precondition this phase needs. One slot is exactly one row, keyed by a
server-generated surrogate, so the claim can be a single conditional `UPDATE` whose outcome the
database decides under a row lock. The client posts `{ slotId }` and never a time, so no clock
skew and no `datetime2` precision can produce two rows meaning the same window.

This phase also inherits three things phase 4 explicitly carried forward, and closes all three:
the retry-after-commit case, the missing test for the trailing `Z` on a schedule response, and
the question of how CI relates to a test that needs a real SQL Server.

Settled while planning this phase, and folded into `docs/decisions.md` before the branch merges:

1. **Retry-after-commit resolves by re-reading, and a caller's own booking answers 201.**
2. **A slot that has *ended* cannot be booked; one still running can** — the predicate is
   `EndUtc > nowUtc`, not `StartUtc > nowUtc`.
3. **The failure-path read has a stated resolution order**, because zero rows affected now has
   four possible explanations.
4. **`nowUtc` is read once and truncated to whole seconds** before the write.
5. **CI runs the unit project only**; the concurrency test runs locally and by the reviewer.
6. **No pagination on the bookings lists**, retiring the prediction that this is where pagination
   would earn its keep.
7. **The test host's connection string is derived, never hardcoded**, and points at a separate
   catalog.
8. **No server-computed `isBookable` flag** on the schedule response.
9. **Mapping stays hand-written**, re-examined against the trigger `docs/decisions.md` set rather
   than assumed.

Each is argued where it belongs below.

## Out of scope

Real-time broadcast, per-room hub groups and the `IScheduleNotifier` port (phase 6 — the hub
stays empty). Every screen (phase 7); no frontend work at all. The README's architecture and
concurrency prose (phase 8), other than the "Running the concurrency test" section, which the
README already marks `TODO (phase 5)`. Cancelling, rescheduling or modifying a booking, which is
out of scope permanently (`docs/requirements.md` §4).

## Packages

One ask, in the test project only: **`Microsoft.AspNetCore.Mvc.Testing` 10.0.12**, matching the
10.0.x line every other Microsoft package in the solution sits on. The new project also repeats
the three already pinned in `tests/MeetingRooms.UnitTests` — `xunit.v3.mtp-off` 3.2.2,
`xunit.runner.visualstudio` 3.1.5, `Microsoft.NET.Test.Sdk` 17.14.1. Nothing is added to any
production project.

---

## The shape of the work

### Layering

```
Application     BookingErrorCodes, SlotClaimOutcome, booking DTOs,
                IBookingService + BookingService, three additions to ISlotRepository
Infrastructure  SlotRepository.TryClaimAsync + the two booked-slot reads
Api             BookingsController, one validator, three ErrorStatusCodeMapper rows,
                a public entry point for the test host
tests           MeetingRooms.ConcurrencyTests - the mandated test, plus the slot
                lifecycle and three assertions earlier phases could not reach
root            docker-compose.yml, so a reviewer outside this container has a database
```

No new Domain type. `Slot` already carries everything the claim needs.

### The write

```csharp
var claimed = await _dbContext.Set<Slot>()
    .Where(slot => slot.Id == slotId
                && slot.BookedByUserId == null
                && slot.EndUtc > nowUtc)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(slot => slot.BookedByUserId, userId)
        .SetProperty(slot => slot.BookedAtUtc, nowUtc), cancellationToken);
```

**The race.** The loser blocks on the winner's row lock, re-reads the committed row once the lock
clears, fails the `BookedByUserId IS NULL` predicate, and updates zero rows. The check and the
write are one statement, so no interleaving lets both observers see `NULL`. Correct under READ
COMMITTED with or without RCSI — which Azure SQL enables by default. It would **not** hold under
SNAPSHOT, which raises update-conflict 3960 instead; this path deliberately uses the default.

**This is not a check-then-act.** The application never decides whether the slot is free. By the
time `claimed` comes back the engine has already resolved the race under the row lock. The count
*reports* which outcome occurred; it does not cause it. That is precisely the distinction
assignment requirement #5 draws when it rules out "check if free, then insert" as two separate
unprotected steps.

**`EndUtc`, not `StartUtc`.** A slot with thirty minutes left is still a usable half-hour of a
meeting room, so the rule is that a slot stops being bookable when it is *over*, not when it
starts. One extra predicate in the same `WHERE` clause; it changes nothing about the concurrency
argument, because it is evaluated under the same row lock as the rest.

### The failure path, and its resolution order

Zero rows affected now has four explanations, so the read that phase 4's delete already needed to
tell 404 from 409 does more work here. It runs **only** on the failure path:

| Row state | Outcome | HTTP |
|---|---|---|
| booked, `BookedByUserId` is the caller | `AlreadyClaimedByCaller` | **201** |
| booked, by someone else | `AlreadyBooked` | 409 `SlotAlreadyBooked` |
| free, but `EndUtc <= nowUtc` | `HasEnded` | 409 `SlotHasEnded` |
| no row | `NotFound` | 404 `SlotNotFound` |

**Why the caller's own booking is a success.** `EnableRetryOnFailure` is required for Azure SQL,
and a retrying execution strategy replays an operation when a transient fault lands *after* the
commit but before the acknowledgement. The replayed conditional `UPDATE` then finds the slot
booked by its own winning write, matches zero rows, and would tell the caller who actually won
409. The invariant holds — nothing double-books — but the response lies, and it lies to the one
caller who is entitled to a truthful yes. Re-reading costs one query on a path that was already
taking one.

**What this buys and what it costs.** Booking becomes **idempotent per user**: a deliberate second
POST from the same account also returns 201. The application cannot distinguish a replayed request
from a repeated one without an idempotency key, and an idempotency key is machinery this scope
does not justify. The trade is deliberate — a false 409 to the winner is a worse lie than a
truthful "you have this slot" to someone who asked twice.

**The consequence for the test, which is the part that is easy to miss.** If the concurrency test
fired N requests from **one** user, the N−1 losers would each find their own id on the row and
report as winners. The mandated test therefore uses **N distinct users**. This is written down
here because discovering it while debugging a red test would cost an hour and invite the wrong
fix.

**Ordering is stated rather than incidental.** A slot can be both booked and ended; ownership is
checked first, then booked-by-another, then ended. `nowUtc` is read **once** from `TimeProvider`
and passed to both the `UPDATE` and the re-read, so the statement that refused the claim and the
explanation given for it cannot disagree.

### `BookedAtUtc` and the rounding trap

`datetime2(0)` **rounds** to the nearest second rather than truncating — noted in
`SlotEntityTypeConfiguration` during phase 4 as something that would matter here, and it does. If
the service passed `TimeProvider.GetUtcNow().UtcDateTime` straight through, the value echoed in
the 201 response could sit up to 500 ms away from what the column actually holds.

So the service truncates to whole seconds before the write:

```csharp
var now = _timeProvider.GetUtcNow().UtcDateTime;
var nowUtc = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
```

A value already at `.000` rounds to itself, so stored and echoed agree by construction rather
than by luck. There is no BCL truncation helper for `DateTime`; the ticks arithmetic is the
idiom.

### DTOs

```csharp
public sealed record BookSlotRequest(int SlotId);
public sealed record BookSlotResponse(int SlotId, DateTime BookedAtUtc);

public sealed record MyBookingResponse(
    int SlotId, int RoomId, string RoomName,
    DateTime StartUtc, DateTime EndUtc, DateTime BookedAtUtc);

public sealed record AdminBookingResponse(
    int SlotId, int RoomId, string RoomName,
    DateTime StartUtc, DateTime EndUtc, DateTime BookedAtUtc, string BookedByEmail);

public sealed record MyBookingsResponse(string TimeZoneId, IReadOnlyList<MyBookingResponse> Bookings);
public sealed record AllBookingsResponse(string TimeZoneId, IReadOnlyList<AdminBookingResponse> Bookings);
```

**The zone is published once per response**, exactly as `ScheduleResponse` does it. A "my
bookings" screen may never call the schedule endpoint, and a client holding its own copy of
`Europe/Kyiv` is how a generator and a formatter drift apart.

**`BookedByEmail` appears on the admin shape only.** `docs/requirements.md` §5 says non-admin
users see no booker identity; the user's own list needs no email, because the caller is the
booker.

**No `Location` header on the 201.** Nothing in this API exposes a booking as an addressable
resource — there is no booking entity to address — so a `Location` would have to point at the
slot, which is not what was created. Registration already answers 201 with no `Location` for the
same reason.

### Mapping — the revisit, done rather than deferred

`docs/decisions.md` set an explicit trigger: revisit the no-mapper decision "if a later phase's
DTOs reach eight to ten fields across several types". This phase crosses it — seven fields on
`AdminBookingResponse` alone, across six new types. The re-examination is recorded here so the
decision stays a decision rather than an inherited habit.

**The answer is still no**, and for a new reason rather than the old one. These are not same-shape
maps; they are *flattening projections* — `slot.Room.Name` becomes `RoomName`,
`slot.BookedByUser.Email` becomes `BookedByEmail`. Convention-based mapping does not reach across
a navigation property, so each of those is a configured line either way, and the two remaining
fields are renames. A configured mapper would be the same line count plus a package, plus
`IMapper` in a constructor, plus something to mock that nothing would then cover.

### Repositories

`ISlotRepository` gains three methods:

```csharp
Task<SlotClaimOutcome> TryClaimAsync(int slotId, int userId, DateTime nowUtc, CancellationToken ct);
Task<IReadOnlyList<Slot>> ListBookedForUserAsync(int userId, CancellationToken ct);
Task<IReadOnlyList<Slot>> ListAllBookedAsync(CancellationToken ct);
```

`TryClaimAsync` owns its own write. `ExecuteUpdateAsync` executes immediately and bypasses EF's
change tracker, so there is no `SaveChanges` for `IUnitOfWork` to coordinate and no explicit
transaction — which is also what keeps it clear of the `EnableRetryOnFailure` restriction on
user-initiated transactions.

The two reads are `AsNoTracking()`, ordered by `StartUtc`, and `Include` the room —
`ListAllBookedAsync` also includes the booker. They return entities; `BookingService` maps, so
Infrastructure never builds a wire contract.

**One trade, stated because it is real:** `Include(slot => slot.BookedByUser)` materialises the
whole `AppUser` row — password hash included — to reach one email. It is never serialised, and it
is bounded by the number of bookings on screen, but it is a wider read than the response needs.
The alternative is a projection type in Application that is a wire contract in all but name. At
this scale the `Include` is the smaller cost; if the admin list ever grows a real page size, this
is the line to revisit.

### Use cases

| Method | Returns | Failure |
|---|---|---|
| `BookSlotAsync(request, callerUserId, ct)` | `OperationResult<BookSlotResponse>` | `SlotNotFound`, `SlotAlreadyBooked`, `SlotHasEnded` |
| `GetMyBookingsAsync(callerUserId, ct)` | `MyBookingsResponse` | none possible |
| `GetAllBookingsAsync(ct)` | `AllBookingsResponse` | none possible |

The two reads return their value rather than an `OperationResult` that could only ever succeed —
the same rule `IRoomService.ListRoomsAsync` already follows. A caller of a list endpoint has no
failure branch to handle, and inventing one makes every caller claim a path that does not exist.

`BookSlotAsync` on the `AlreadyClaimedByCaller` path returns the **stored** `BookedAtUtc` from the
re-read, not `nowUtc` — the original claim happened earlier, and reporting the retry's clock would
be a second, quieter version of the lie this whole branch exists to avoid.

### API

```
POST /api/bookings        [Authorize]               201 | 400 | 401 | 404 | 409
GET  /api/bookings/me     [Authorize]               200 | 401
GET  /api/bookings        [Authorize(Roles=Admin)]  200 | 401 | 403
```

`GET /api/bookings` is the admin's "all bookings across users" from assignment #3. It is the
second role-gated read in the application and the first that exists purely because of a role.

One validator, `BookSlotRequestValidator`: `SlotId > 0`. That is the whole payload-only surface —
whether the slot exists, is free, or has ended all need I/O, and by the rule already in use that
makes them error codes rather than validation messages. A body of `{}` binds `SlotId` to 0 and is
rejected as a 400 `ValidationProblemDetails` keyed to the field.

Three new `ErrorStatusCodeMapper` rows: `SlotNotFound` → 404, `SlotAlreadyBooked` → 409,
`SlotHasEnded` → 409. Both conflicts are 409 rather than 400 for the reason already recorded
beside `RoomHasBookedSlots`: the request is well formed and the caller is permitted; it is refused
on account of state.

### The entry point

`Program.cs` gains, at the bottom:

```csharp
public partial class Program { }
```

**A .NET-specific wrinkle with no PHP analogue.** Top-level statements are compiled into a
generated class named `Program` with **internal** accessibility, so
`WebApplicationFactory<Program>` in another assembly cannot name it. Declaring the partial class
merges a public declaration into the generated one. The alternative is `InternalsVisibleTo` in the
`.csproj`; the partial class is the documented idiom and is one line at the point it applies to.

---

## The concurrency test

`tests/MeetingRooms.ConcurrencyTests`, named as `docs/plan.md`'s target layout has it — a reviewer
looking for assignment #6 should find it by name. Three assertions in it are not about
concurrency; they are recorded below as deliberate guests rather than scope creep.

### The host

`BookingApiFactory : WebApplicationFactory<Program>` runs the **real application** — real
controllers, real EF, real SQL Server, real Identity, real JWT. The only things it overrides are
configuration values:

- **The connection string is derived, not written.** `SqlConnectionStringBuilder` is applied to
  whatever `ConnectionStrings__DefaultConnection` already holds, replacing only
  `InitialCatalog` with `MeetingRooms_Tests`. No credential is ever hardcoded, and the same code
  works inside this container and on a reviewer's machine.
- **A separate catalog is not optional.** Bookings cannot be cancelled, so a test run against the
  dev `MeetingRooms` database would permanently consume demo slots — every run eating twenty-odd
  of them, with no way to give them back.
- **`appsettings.Tests.json`** supplies the localhost default the root `docker-compose.yml`
  matches, plus a `Jwt:SigningKey` and a seeded administrator. Those two are **fixtures, not
  secrets**: they are used only by the test host against a local test database, and the real
  values continue to live in `dotnet user-secrets` and App Service application settings. Worth
  stating plainly so nobody reads the committed file as a leak.
- `Database.MigrateAsync()` already runs at startup, so the test catalog is created and migrated
  on first use with no fixture code of its own.

### The mandated test — `SlotBookingConcurrencyTests`

1. Log in as the seeded administrator; `POST /api/rooms` to get a **private room** with its own
   140-slot grid, so runs never interfere with each other or with the demo rooms.
2. `GET /api/rooms/{id}/schedule` and take a slot on a **future day** — no clock boundary can make
   it flaky.
3. Register and log in **20 distinct users**, for the reason argued above. This is the slow part
   of the run — forty requests, each paying Identity's password hashing — so it lives in a shared
   fixture and happens once.
4. Release 20 `POST /api/bookings` at that one slot off a single `TaskCompletionSource`, each with
   its own `HttpClient` and its own bearer token.
5. Assert: exactly **one 201**; **nineteen 409s**, every one carrying `SlotAlreadyBooked` in
   `errorDetails`; **zero 5xx**; and, read back through `AppDbContext`, exactly **one** booked row
   whose `BookedByUserId` is the winner's id.

The fifth assertion matters most. One 201 could in principle coexist with a second write; reading
the row is what turns "the API said so" into "the database says so".

### `SlotLifecycleTests`

The `EndUtc > nowUtc` predicate is time-dependent, and the grid only contains ended slots when the
test happens to run late in the Kyiv working day. Rather than skip conditionally or introduce a
fake clock, these two tests insert a slot **directly through `AppDbContext`**:

- a slot whose window is entirely in the past → `POST /api/bookings` → **409 `SlotHasEnded`**;
- a slot spanning `[now − 30 min, now + 30 min)` → **201**, which is the behaviour this phase
  deliberately allows.

Writing a *slot* row directly is legitimate; the standing rule is that nothing but `TryClaimAsync`
writes `BookedByUserId`, and neither of these does. Both are deterministic at any hour, need no
package, and give the `EndUtc` predicate a mutation probe of its own.

### Three gaps earlier phases recorded as open

Cheap now that a booking can exist, and each one closes a sentence already written down:

- **The trailing `Z`.** Phase 4 found that `datetime2` loses `DateTimeKind` on the round trip and
  fixed it with a value converter, but noted the regression had no test — the loss only happens
  through SQL Server. Asserted here against the **raw JSON string**, because deserialising into a
  `DateTime` is exactly what hides it.
- **`isBooked` / `isBookedByMe` on their true branch.** Phase 4 could only verify the false
  branch, because nothing could book. Now both: true for the booker, and `isBookedByMe` false for
  a different user looking at the same booked slot.
- **`DELETE /api/rooms/{id}` on a room with a booked slot → 409 `RoomHasBookedSlots`.** Phase 4
  proved this at the repository level but left the mapper row unexercised end to end; it is the
  one qualifier on that phase's Done-when list.

### CI

`.github/workflows/deploy.yml` currently runs `dotnet test --configuration Release` across the
whole solution, which would pick this project up automatically and fail — CI has no SQL Server.
The step is scoped to `tests/MeetingRooms.UnitTests` instead, with the reason in a comment.

The reasoning, stated so the narrowing does not read as avoidance: assignment #6 asks for a test
that is **in the repository and runnable by the reviewer**, which this is. Putting a SQL container
and a timing-sensitive test on the *deploy* path buys a green tick at the cost of a deploy that
can be blocked by a slow container on the day of a deadline. Locally, and for the reviewer, a bare
`dotnet test` still runs both projects.

### The root `docker-compose.yml`

One `mcr.microsoft.com/mssql/server:2022-latest` service, port 1433 published, a healthcheck, and
the password `appsettings.Tests.json` expects. It exists so a reviewer outside this container can
run the mandated test with two commands.

**It cannot be exercised from here.** There is no `docker` CLI in the dev container, so this file
is written for a reviewer's host and verified by inspection only. That limitation goes in the
README next to the commands rather than being quietly omitted — inside the container the existing
`db` service already fills the same role, which is why the test suite runs here at all.

---

## Tasks

Each is one commit; the tree builds after every one. Branch `phase/5-booking-concurrency`, from
`develop`.

| # | Commit | Contents |
|---|---|---|
| 1 | `docs: add phase 5 plan` | this file |
| 2 | **(you)** `chore: add the concurrency test project` | csproj with the four packages, the `.slnx` entry under `/tests/`, the project reference to the API |
| 3 | `feat(application): add the booking ports, DTOs and error codes` | `BookingErrorCodes`, `SlotClaimOutcome`, the six DTOs, the three `ISlotRepository` additions, `IBookingService` |
| 4 | `feat(infrastructure): claim a slot with one conditional update` | `TryClaimAsync` and the two booked-slot reads |
| 5 | `feat(application): add the booking use cases` | `BookingService` + DI registration |
| 6 | `feat(api): add the booking endpoints` | `BookingsController`, the validator, three mapper rows |
| 7 | `feat(api): make the entry point reachable from a test host` | `public partial class Program { }` |
| 8 | `test: assert exactly one booking survives concurrent requests` | `BookingApiFactory`, `appsettings.Tests.json`, `SlotBookingConcurrencyTests` |
| 9 | `test: pin the slot lifecycle and three gaps phase 4 could not reach` | `SlotLifecycleTests`, the `Z` assertion, the `isBooked` true branch, the 409 on deleting a booked room |
| 10 | `chore: provide a SQL Server for the concurrency test` | root `docker-compose.yml` |
| 11 | `chore(ci): run only the unit tests before publishing` | `deploy.yml` |
| 12 | `docs: document how to run the concurrency test` | the README section currently marked TODO |
| 13 | `docs: fold phase 5 decisions into decisions.md` | the nine decisions, before the branch merges |
| 14 | `docs: record phase 5 outcome` | the Outcome section, after the deploy, on `develop` |

### Standing decisions to fold into `docs/decisions.md` (task 13)

Under *Booking and concurrency*: the retry-after-commit resolution and the per-user idempotence it
implies; the `EndUtc > nowUtc` rule and why it is `EndUtc`; the four-way resolution order on the
failure path; `nowUtc` read once and truncated to whole seconds. Under *Testing*: CI scoped to the
unit project and why; the derived connection string and the separate catalog; `appsettings.Tests.json`
as a fixture rather than a secret. Under *API surface and errors*: no pagination on the bookings
lists — amending the existing entry, which predicts the opposite — and `timeZoneId` published once
per list response. Under *Architecture and layering*: mapping stays hand-written, with the
flattening-projection argument that replaces the line-count one. Also close the *Still open* entry
that named this phase.

---

## What you do

1. `git checkout -b phase/5-booking-concurrency` from `develop`, and commit this file.
2. The test project (task 2), from the repository root:

```bash
mkdir -p tests/MeetingRooms.ConcurrencyTests
# csproj written by hand, as in phase 4 - the xunit template still pins v2
dotnet add tests/MeetingRooms.ConcurrencyTests reference src/backend/MeetingRooms.Api
dotnet restore && dotnet build
```

   I supply the exact `.csproj` before you run anything, and report what `dotnet list package`
   resolves afterwards, so the versions are approved rather than assumed. Then add the project to
   `MeetingRooms.slnx` under the existing `/tests/` folder.

3. Make each commit when I flag the point.
4. **Nothing to add in the Azure portal.** This phase introduces no new configuration key and no
   new secret — `appsettings.Tests.json` is local to the test project and is never deployed.
5. Merge `phase/5-booking-concurrency` into `develop` with `--no-ff`, then `develop` into `main`,
   which deploys. The three documentation commits already sitting on `develop` ride along.

---

## Risks and fallbacks

| Risk | Fallback |
|---|---|
| `WebApplicationFactory` cannot resolve the entry point or the content root | Task 7 covers the entry point. If the content root still misresolves, `UseContentRoot` on the factory. A fresh clone has no `wwwroot`: that only logs a warning, and no test in this project requests `/` |
| The racing requests do not actually overlap, so the test passes for the wrong reason — `docs/plan.md`'s risk 2 | One `HttpClient` per task, all released off one `TaskCompletionSource`, **and** the mutation probe. A test that cannot be shown to fail on a real defect is ceremony |
| `ExecuteUpdateAsync` will not translate the three-predicate `WHERE` | It is a plain conjunction over two columns and a constant; if EF ever refuses, a parameterised `ExecuteSqlInterpolatedAsync` expresses the same single statement, and the concurrency argument is unchanged |
| First run against `MeetingRooms_Tests` is slow — create, migrate, seed four rooms, top up 560 slots | One-off per database. `docker compose down -v` is the documented reset, and the fixture is shared so it happens once per run, not once per test |
| The CI narrowing silently stops running the unit tests as well | Read the workflow run output after the merge to `main`, not just whether the deploy went green |
| The root compose file cannot be verified from this container | Stated as unverified in the README and in the Outcome, rather than claimed to work |
| Idempotent 201 surprises a reviewer reading the endpoint | It is the documented answer to a named failure mode. The doc comment at the write site and the README both say why a false 409 to the winner is the worse lie |

## Verification

Locally, before the merge:

- `dotnet build` — clean, **zero warnings**, after every commit.
- `dotnet test` — both projects green; the unit suite stays at 17.
- **The mutation probe, which is the phase's exit criterion rather than a passing test.** Delete
  `&& slot.BookedByUserId == null`; `SlotBookingConcurrencyTests` must go **red**, with more than
  one 201 and more than one booked row. Restore; green. Then the second probe: delete
  `&& slot.EndUtc > nowUtc`; `SlotLifecycleTests` must go red on the ended-slot case. Restore.
- Through Scalar with a **User** token: book a free slot → 201 carrying `bookedAtUtc`; the same
  slot again from the same account → **201** (idempotent, as decided); the same slot from a second
  account → 409 `SlotAlreadyBooked`; an unknown slot id → 404 `SlotNotFound`; `slotId: 0` → 400
  keyed to the field; no token → 401 with an empty body. Re-read the schedule: `isBooked: true`,
  and `isBookedByMe` true for the booker and false for the other account. `GET /api/bookings/me`
  lists it with the room name and `timeZoneId`; `GET /api/bookings` → **403**.
- With an **Admin** token: `GET /api/bookings` → 200 including `bookedByEmail`; `DELETE` the room
  that slot belongs to → **409 `RoomHasBookedSlots`**, closing phase 4's one open qualifier.
- Restart the application: no migration applied, no room seeded, the top-up inserting none, and
  **the booking still there**.
- Regression sweep of phases 2, 3 and 4, re-run rather than assumed: `/` serves the React page,
  `/scalar/` and `/openapi/v1.json` load, `/api/nope` is a 404 `ProblemDetails` with no
  `errorDetails`, register/login/`/me` behave, room CRUD and the schedule behave, and the hub
  negotiate still reports in-process SignalR.
- One check beyond the list, as phase 4 did: the generated OpenAPI document marks all three new
  endpoints with the `Bearer` scheme.

On the deployed application:

- It boots; `/health` still reports `databaseReachable: true`.
- Book a slot through Scalar on the deployed URL, see it in both lists, and get a 409 from a
  second account. **This is the first time the mechanism runs against Azure SQL, which has RCSI on
  by default** — the isolation level the design was reasoned against, and the one that could not
  be exercised from this container.
- Everything phases 2–4 built still behaves.

## Done when

- [x] Two simultaneous requests for one slot produce exactly one booking, proven by an automated
      test in the repository that a reviewer can run with two documented commands.
- [x] That test is shown to be load-bearing by a mutation probe, not merely green.
- [x] The winner gets 201, every loser gets 409 `SlotAlreadyBooked`, nobody gets a 5xx, and the
      database holds one booked row.
- [x] A caller who books the same slot twice gets 201 both times, and the stored `BookedAtUtc`
      does not move.
- [x] A slot that has ended is refused with 409 `SlotHasEnded`; a slot still running is bookable.
- [x] `GET /api/bookings/me` and the admin `GET /api/bookings` work, and the admin one is the only
      place a booker's identity appears.
- [x] The schedule's `isBooked` and `isBookedByMe` are proven on their **true** branch.
- [x] A schedule response's instants carry a trailing `Z`, pinned by a test.
- [x] Deleting a room with a booked slot returns 409 through HTTP.
- [x] `dotnet build` zero warnings; both test projects green; CI runs the unit project and the
      deploy still succeeds.
- [x] `docs/decisions.md` carries all nine decisions and no longer lists retry-after-commit as
      open.

*All eleven closed 2026-09-14.*

## Outcome

**Completed and deployed 2026-09-14.** Every task done and every Done-when item closed. Thirteen
planned pre-merge tasks became fifteen commits: task 4 split when writing the service exposed a gap
in the port it had just committed, and task 13 split because correcting a claim in `docs/plan.md` is
a different change from recording a decision.

### Verified

Locally, from the committed tree: `dotnet build` clean at **zero warnings** after every commit;
**17 unit tests** and **6 concurrency tests** green; and the generated SQL read out of EF's command
log rather than assumed — one `UPDATE` carrying all three predicates with no `SELECT` ahead of it,
and a failure read of `SELECT TOP(1) [BookedByUserId], [BookedAtUtc], [EndUtc]`.

**Five probes**, four of which reddened something and one of which corrected a comment:

| probe | result |
|---|---|
| delete `&& slot.BookedByUserId == null` | all twenty requests get 201; the race test fails |
| delete `&& slot.EndUtc > nowUtc` | the expired-slot test fails |
| stagger the racing requests 50 ms apart | the overlap assertion fails |
| delete the UTC value converter | the trailing-`Z` test fails |
| delete `RunContinuationsAsynchronously` | **nothing fails** — see *Learned* below |

Each of the first four reddened **exactly one** test and left the other five green, which also
shows the tests are not coupled to each other.

Through the API on a local run, thirteen assertions over every booking endpoint: a free slot books
(201); the same slot from the same caller books again (201, `bookedAtUtc` **unmoved**); a second
caller gets 409 `SlotAlreadyBooked`; an unknown id 404; `slotId: 0` a 400 keyed to the field; no
token a 401 with an empty body; `GET /api/bookings/me` showing one booking for the booker and none
for anyone else; `GET /api/bookings` 403 for a User and 200 with `bookedByEmail` for an
administrator; the schedule reporting `isBookedByMe` true for the booker and false for another
caller looking at the same booked slot; and deleting that room refused with 409
`RoomHasBookedSlots`.

**On the repository owner's machine, by the reviewer's own path:** `docker compose up -d` followed
by `dotnet test tests/MeetingRooms.ConcurrencyTests`, with no environment variable set, from a
catalog that did not exist. Six of six green in 7.9 seconds — versus ~0.4 in the dev container,
the difference being a cold create-migrate-seed plus SQL Server running under amd64 emulation on
Apple silicon.

On the deployed application: booking works for an administrator and for an ordinary user; an
administrator sees all bookings and their own, an ordinary user only their own; a second caller on
somebody else's slot gets 409; and re-booking one's own slot returns 201. **This is the first time
the mechanism has run against Azure SQL, which enables RCSI by default** — the isolation setting
the design was reasoned against and the one the local SQL Server does not reproduce.

### Deviations from the plan

1. **The `ISlotRepository` additions moved from task 3 to task 4.** Three method signatures without
   their implementation is `CS0535`, which breaks this document's own "the tree builds after every
   commit" rule. A port and its implementation are one logical change anyway.
2. **Task 4 became two commits.** Writing `BookingService` exposed that the outcome enum alone
   cannot answer honestly on the idempotent path — the service could only echo the current
   request's clock. The follow-up returns the stored booking time alongside the outcome.
3. **A result type was written and then removed.** `SlotClaimResult`, a `readonly record struct`
   with three factories, became a named tuple after the owner asked whether it was necessary. It
   was not: a positional record struct has a **public** primary constructor and `default()` bypasses
   any constructor regardless, so its factories could not enforce the pairing they appeared to
   guard, and it carried no behaviour. `RoomService.ResolveRange` was the precedent already in the
   tree.
4. **Task 13 became two commits**, separating the decisions from the correction to `docs/plan.md`.
5. **The CI step also gained a solution-wide `dotnet build`.** The plan only narrowed the test run,
   which would have let a compile break in the concurrency project reach the publish step
   unnoticed — including in the one artifact the assignment is graded on.
6. **Tests that book use a room of their own**, not the shared race room. Bookings cannot be
   undone, so shared state would mean tests consuming each other's slots.
7. **The overlap assertion is not in the plan at all.** It is the largest single addition, and the
   reason is under *What the phase proved*.

### What the phase proved, beyond its checklist

- **A green concurrency test does not establish that the requests overlapped — and neither does the
  mutation probe.** Twenty *serial* requests produce exactly the same one-201-and-nineteen-409s as
  a real race, and with the free-slot predicate removed every `UPDATE` matches whatever the
  ordering, so the probe reddens identically either way. `docs/plan.md` risk 2 named "requests do
  not overlap" as the failure mode and then proposed a mitigation that cannot detect it. The test
  now measures it directly: the last request was issued before the first came back, so all twenty
  were in flight at one instant. The risk entry was corrected rather than left standing.
- **The overlap held on a slower database too.** The owner's run was against emulated amd64 SQL
  Server, which widens the window in which requests can interleave badly. That is stronger evidence
  than the fast local run, not weaker.
- **`ExecuteUpdateAsync` translates exactly as designed**, confirmed by reading EF's command log
  against real SQL Server before any test depended on it.
- **The trailing-`Z` regression is now pinned**, retroactively covering a fix phase 4 shipped with
  no test because the `DateTimeKind` loss only happens on the round trip through SQL Server.

### Learned, and not anticipated by this document

- **A design decision reached into test design, and would have been found the expensive way.**
  Answering a caller who already holds the slot with 201 means N requests from *one* account report
  N winners. The mandated test needs N **distinct** users, and discovering that while debugging a
  red test would have invited exactly the wrong fix.
- **`RunContinuationsAsynchronously` does not serialise the waiters.** Removing it changed nothing,
  because each continuation runs inline only until its HTTP call suspends, after which the
  releasing thread moves to the next. A comment asserting the opposite had already been written;
  it was corrected rather than left to mislead the next reader.
- **`datetime2(0)` rounds the *parameter*, not only the stored value.** The parameter inherits the
  column's type, so an untruncated `nowUtc` would judge a slot ended up to half a second early —
  which makes the truncation in `BookingService` load-bearing rather than cosmetic.
- **A record struct cannot enforce its own invariants.** `default(T)` bypasses every constructor,
  and a positional one's primary constructor is public anyway. Worth knowing before reaching for
  the shape `OperationResult` uses, which is a class and genuinely can refuse to construct itself
  inconsistently.
- **xUnit v3 captures `Console` output**, so a diagnostic probe has to write to a file or take
  `ITestOutputHelper`. An empty v3 test project also builds without `CS5001` — the generated entry
  point arrives before any test does.

### Carried into later phases

- **Phase 6 must broadcast on `Claimed` only, never on `AlreadyClaimedByCaller`.** Both are a
  successful `OperationResult`, so the obvious implementation — broadcast whenever the booking
  succeeded — emits a slot-booked event every time a caller re-posts a booking they already hold,
  when nothing has changed. The outcome that distinguishes them does not currently reach the
  service's return value; carrying it up, or broadcasting from the repository's success branch, is
  a decision phase 6 has to make deliberately.
- **Phase 7 inherits a slot state with no flag.** A slot whose window has closed is
  `isBooked: false` and yet unbookable. That is deliberate — the client has `endUtc` and its own
  clock, and a server-computed flag would be stale on serialisation — so the grid must grey those
  out itself rather than expecting the API to say so.
- **Phase 8's README** must draw the distinction phase 4 flagged (the unique `(RoomId, StartUtc)`
  index is seeder integrity only and plays no part in the guarantee) and should also name the two
  things a reviewer is most likely to question: that a repeat booking answers 201, and why the
  overlap assertion exists.
- **Housekeeping:** a `MeetingRooms_Smoke` catalog was left on the dev container's SQL Server by
  the local HTTP pass, alongside `MeetingRooms_Tests`. Neither is the development database and
  both are safe to drop.
- **Every deploy is unavailable for roughly ten seconds**, observed on this one. It is the App
  Service restart plus a cold start, not this phase — which adds no migration at all. The startup
  block runs before `app.Run()`, deliberately, so a restart presents as unavailable rather than as
  serving requests against a schema that may not exist yet. `SlotGridTopUp` reads every slot in the
  current window on each start, rooms × 140 rows, and is the part that would grow.
