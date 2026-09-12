# Implementation plan

Settled during the planning session of 2026-09-11/12. This is the execution
reference: context, sequence, risks, and the reasoning behind the central design
choice.

**How the project docs relate.**

| Doc | Answers | Authority |
|---|---|---|
| `assignment.md` | what was asked for | authoritative; nothing overrides it |
| `docs/requirements.md` | what we will build, concretely | derived from the assignment + planning |
| `docs/decisions.md` | how we build it | binding technical record |
| `docs/plan.md` (this file) | in what order, and why | execution reference |

---

## Context

The assignment's single non-negotiable property is that a time slot is never
double-booked under concurrent requests, with booking status propagated to all
viewers in real time via Azure SignalR.

Azure is already provisioned — Web App, SQL Database and SignalR Service, all in
Sweden Central — and a minimal API with `/health` already deploys from
`.github/workflows/deploy.yml`. **Deployment is therefore a solved problem, not a
final phase.** Every phase ends with a redeploy.

Deadline: Monday midday. The governing constraint on every choice is *the simplest
design that satisfies the requirement*.

## Starting point (as of 2026-09-12)

- `src/backend/MeetingRooms.Api` — one web project, net10.0, whose only package is
  `Microsoft.AspNetCore.OpenApi`. `Program.cs` serves `wwwroot`, exposes `/health`,
  and falls back to `index.html`. No EF, no auth, no database access yet.
- `MeetingRooms.slnx` references that one project.
- Dev database: SQL Server container, host `db`, via the
  `ConnectionStrings__DefaultConnection` environment variable.
- The dev container's outbound firewall allows GitHub, npm, NuGet and Anthropic
  only. **Neither Azure SQL nor `*.service.signalr.net` is reachable from here** —
  both are first exercised after a deploy.

## Target layout

```
src/backend/MeetingRooms.Domain          entities, Roles constants
src/backend/MeetingRooms.Application     ports, services, Result, error codes
src/backend/MeetingRooms.Infrastructure  AppDbContext, EF configs, repositories, Identity, migrations
src/backend/MeetingRooms.Api             controllers, hub, DI extensions, error mapping, wwwroot
src/frontend                             Vite + React + TS + Tailwind, built into wwwroot
tests/MeetingRooms.ConcurrencyTests      assignment requirement #6
```

---

## The booking design

The data model and the concurrency mechanism were treated as one decision, because
they are one decision. The full reasoning is kept here; `docs/decisions.md` records
the outcome.

### Schema

```
Rooms (Id, Name, Capacity)

Slots (Id, RoomId FK, StartUtc datetime2(0), EndUtc datetime2(0),
       BookedByUserId FK NULL, BookedAtUtc datetime2(0) NULL)
  UNIQUE (RoomId, StartUtc)   -- seeder integrity, NOT the concurrency guarantee
  INDEX  (BookedByUserId)     -- "my bookings"
```

There is no `Bookings` table. A booking has no state independent of the slot — no
lifecycle, no cancellation, no attendees, no price — so a separate entity would be a
1:1 table whose only effect is a join.

### Mechanism: atomic conditional update

One slot is one row, guaranteed by the primary key. Booking folds the business
condition into the `WHERE` clause:

```csharp
var rows = await _db.Slots
    .Where(s => s.Id == slotId && s.BookedByUserId == null)
    .ExecuteUpdateAsync(set => set
        .SetProperty(s => s.BookedByUserId, userId)
        .SetProperty(s => s.BookedAtUtc, nowUtc), ct);
// rows == 1 -> 201 Created     rows == 0 -> 409 SlotAlreadyBooked
```

**The race.** The loser blocks on the winner's row lock, re-reads the committed row
once the lock clears, fails the `BookedByUserId IS NULL` predicate, and updates zero
rows. The check and the write are one statement, so no interleaving lets both
observers see `NULL`. Correct under READ COMMITTED, with or without RCSI — which
Azure SQL enables by default. It would *not* hold under SNAPSHOT isolation, which
raises update-conflict 3960 instead; this path deliberately uses the default.

**Not a check-then-act.** The application never decides whether the slot is free. By
the time `rows` is returned the race is already resolved — the engine resolved it
under the row lock. `rows` reports which outcome occurred; it does not cause it.
This is precisely the distinction assignment requirement #5 draws when it rules out
"check if free, then insert" as two separate unprotected steps.

**Drift immunity.** The client posts `{ slotId }`, a server-generated surrogate key.
No client-supplied value participates in booking identity, so no clock skew, no time
picker glitch, and no `datetime2` sub-second precision can ever produce two rows
meaning the same slot. This is the reason the key is a surrogate rather than
`(RoomId, StartUtc)`.

**Consequence for the repository port.** `ExecuteUpdateAsync` executes immediately
and bypasses EF's change tracker, so nothing on the booking path throws and
`IUnitOfWork` is not involved — there is no `SaveChanges` to coordinate:

```csharp
Task<bool> TryClaimAsync(int slotId, int userId, DateTime nowUtc, CancellationToken ct);
```

**Honest limitation, to be stated in the README.** The invariant lives in one
statement's `WHERE` clause rather than in a standing database constraint, so it binds
every path through `SlotRepository.TryClaimAsync` — which must remain the *only*
write path to `BookedByUserId`. A separate `Bookings` table with `UNIQUE (SlotId)`
would move the invariant into the schema, where it would bind all code paths forever,
at the cost of a 1:1 table carrying no state of its own. For this scope, one write
path plus an automated concurrency test is the proportionate trade.

**Stated boundary.** If bookings ever gain state of their own — cancellation,
rescheduling, or one booking spanning several slots — a booking becomes an entity and
this design splits into two tables. It has none of that state in scope.

---

## Requirement map

| # | Requirement | Satisfied by | Lives in |
|---|---|---|---|
| 1 | Azure account | done | — |
| 2 | Azure resources | done; repo evidence is a README section naming the resources, region, and app-setting *keys* (never values) | `README.md` |
| 3 | Auth + roles | Identity + JWT bearer; `Roles.User` / `Roles.Admin` constants; `[Authorize(Roles=…)]`; role + admin seeder | `Domain/Roles.cs`, `Infrastructure/Identity/`, `Api/Controllers/AuthController.cs` |
| 4 | Resources + schedule | `Room` + `Slot` entities, slot seeder, admin room CRUD, `GET /api/rooms/{id}/schedule` — one indexed seek, no join | `Domain/Entities`, `Application/Services/RoomService`, `Api/Controllers/RoomsController.cs` |
| 5 | Booking concurrency | the atomic conditional update above; `SlotAlreadyBooked` → 409; rationale documented at the write site and in the README | `Application/Services/BookingService.cs`, `Infrastructure/Persistence/Repositories/SlotRepository.cs` |
| 6 | Automated concurrency test | xUnit + `WebApplicationFactory` against real SQL Server; N tasks released off one `TaskCompletionSource`; asserts exactly one 201, N−1 409, zero 5xx, one booked row | `tests/MeetingRooms.ConcurrencyTests` |
| 7 | Real-time | `IScheduleNotifier` port, `ScheduleHub` with one group per room, broadcast **after** the claim returns 1; `AddAzureSignalR()` when its connection string is present | `Application/Interfaces`, `Api/Hubs/ScheduleHub.cs`, `src/frontend` |
| 8 | Deployment | existing workflow plus a node build step emitting into `wwwroot`; schema applied by `Database.Migrate()` at startup | `.github/workflows/deploy.yml`, `Api/Program.cs` |
| 9 | Repo + process | atomic commits, `CLAUDE.md`, phase docs, XML doc comments where the *why* is not obvious | `docs/phases/`, `README.md` |
| 10 | Submission | README header carrying both links | `README.md` |

---

## Phase sequence

Every phase ends with a deploy.

| Phase | Goal | Reqs |
|---|---|---|
| 1 | Solution skeleton: four projects, per-layer DI extension methods, Result + error codes, global exception handler + ProblemDetails, Scalar, `decisions.md` folded in. Plus an empty hub, purely to smoke-test Azure SignalR `negotiate` in Azure on day one. | 9, (7) |
| 2 | Frontend shell + CI build: Vite/React/TS/Tailwind building into `wwwroot`, one page calling `/health`. Small on purpose — it retires the pipeline risk early. | 8 |
| 3 | Persistence + identity: EF Core/SQL Server, `AppDbContext`, Identity, first migration, migrate-on-startup, role + admin seeding, register/login issuing JWT. | 3 |
| 4 | Rooms + slots: entities, slot-grid seeder, admin room CRUD, schedule read showing free vs booked. | 4 |
| 5 | **Booking + the atomic conditional update + the automated concurrency test.** The graded core. | 5, 6 |
| 6 | Real-time: per-room hub groups, notifier port, broadcast after the claim succeeds, client subscription. | 7 |
| 7 | Frontend screens: login, room list, schedule grid, booking, live updates, admin views. | 3, 4, 7 |
| 8 | Docs + polish: README (links, architecture, concurrency rationale, how to run the test), doc comments, final deploy. | 9, 10 |

Phases 1–5 are the technical core; phase 7 is the visible deliverable. **If the
schedule slips, cut breadth inside phases 4 and 7** — fewer admin screens, fewer room
fields — rather than compressing phase 5.

Between phases 3 and 4 sits the migration flip described in `docs/decisions.md`.

---

## Risks, ranked

1. **The booking write on EF + Azure SQL.** No partial credit here. The mechanics
   around it are what bite: `EnableRetryOnFailure` is required for Azure SQL and
   forbids user-initiated transactions unless wrapped in the execution strategy.
   *Mitigated by:* one write, no explicit transaction, and a mutation probe on the test.
2. **Making the concurrency test genuinely concurrent and reviewer-runnable.** It
   silently passes for the wrong reason if it uses an in-memory or SQLite provider,
   if the requests do not overlap, or if a single `HttpClient` serialises them.
   *Mitigated by:* real SQL Server via a root `docker-compose.yml`, requests released
   off one `TaskCompletionSource`, and the mutation probe.
3. **Azure SignalR cannot be tested from this container.** First real proof is
   post-deploy. Sharp edges: WebSockets on the Web App, the hub's JWT arriving as a
   query-string parameter rather than a header, and `negotiate` failures presenting
   as generic connection errors. *Mitigated by:* the empty-hub negotiate smoke test in
   phase 1. *Fallback:* in-process SignalR on the single instance, a config-only change.
4. **Migrations reaching Azure SQL.** No outbound route from here and no Azure CLI.
   *Mitigated by:* `Database.Migrate()` at startup, safe because the plan is single
   instance; the flip to manual migration after phase 3.
5. **Plumbing cost of Identity + Clean Architecture in a first C# project.** A
   schedule risk, not a correctness one, and the most likely thing to eat Sunday.
6. **Frontend build into `wwwroot` through CI.** Vite base path, Tailwind config,
   `npm ci && npm run build` ordering ahead of `dotnet publish`.
   *Mitigated by:* doing it in phase 2 while it is still one page.
7. **Time semantics.** UTC stored, Europe/Stockholm displayed, DST boundaries. Low
   probability, high embarrassment.

## Verification

- **Every phase:** `dotnet build`, `dotnet test`, run locally against the `db`
  container, then redeploy and exercise `/health` plus whatever the phase touched.
- **Phase 5 specifically — the mutation probe.** Delete `&& s.BookedByUserId == null`
  from the `Where` clause; the concurrency test must go red. Restore it; green. That
  check, not a passing test on its own, is the phase's exit criterion.
- **Phase 6:** two browsers on the same room; booking in one updates the other with
  no refresh, on the deployed app — Azure SignalR cannot be exercised from this
  container.
