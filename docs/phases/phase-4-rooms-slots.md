# Phase 4 — Rooms + slots

**Satisfies:** assignment #4 (resources and their schedule). Creates the rows phase 5's booking
write claims, so every choice here is load-bearing for the graded core.

## Context

Phase 3 ended with a deployed application that authenticates: EF Core on SQL Server, ASP.NET
Core Identity, the first migration, migrate-and-seed at startup, and register/login/`/me`
working locally and in Azure. It has no domain yet — `AppDbContext` maps Identity tables and
nothing else, and `ApplyConfigurationsFromAssembly` still finds zero configurations.

This phase adds the `Room` and `Slot` entities, the server-side slot-grid generator, admin room
CRUD, and the schedule read that shows free versus booked. It is also where
`[Authorize(Roles = ...)]` gets its first real endpoint: phase 3 proved the role wiring with a
temporary attribute and then reverted it, so nothing in the deployed application is role-gated
today.

Phase 5 claims a `Slot` row with one atomic conditional update, and the invariant depends on one
slot being exactly one row with a server-generated key. This phase creates those rows and must
not leave the booking path anything to trip over.

Settled while planning this phase, and folded into `docs/decisions.md` before the branch merges:

1. **Rolling horizon with a startup top-up.** Every room's window is `[today, today+14)` in the
   display zone. An idempotent top-up runs at startup after the seeders and inserts whatever is
   missing for every room, so rooms stay aligned however late one is created, and the deployed
   application never runs dry if the review window slips. This retires the "widen the constant"
   note in `docs/requirements.md` §3.
2. **Deleting a room that has booked slots is refused** — `RoomHasBookedSlots` → 409. "Bookings
   cannot be cancelled, rescheduled or modified" is a stated requirement, so cascading a room
   delete over them would be a backdoor cancellation.
3. **The bookings-list endpoints belong to phase 5**, not here. Until the booking write exists
   nothing can create a booking, so a list endpoint written now returns an empty array that no
   verification can distinguish from a broken query.
4. **The schedule range is UTC instants on the wire**, both parameters optional.
5. **The fixed display zone moves from Europe/Stockholm to Europe/Kyiv.** `assignment.md` is
   silent on time zones, the rooms are fictional, and the only thing the choice buys is
   legibility to whoever opens the deployed application. Amends `docs/requirements.md` §3 and §7.
6. **The zone is published once per schedule response** as `timeZoneId`, never stored per row.
7. **Mapping stays hand-written.** Mapster is not adopted in this phase; see *Mapping* below.

## Out of scope

Booking a slot, the concurrency test, `GET /api/bookings/me` and the admin all-bookings list
(phase 5); per-room hub groups and broadcasts (phase 6); every screen (phase 7). No frontend
work at all — phase 2's page still calls `/health` only. No seeded bookings, for the reason
under *Seeding and the top-up*.

## Packages

One, and it is the test project rather than a library: `dotnet new xunit` for
`tests/MeetingRooms.UnitTests`. The template pins its own versions from the installed SDK; the
exact set is read out of the generated `.csproj` and reported before the commit rather than
assumed. Nothing else is needed — EF Core, Identity and FluentValidation are already referenced,
and the grid generator depends on nothing but the BCL.

---

## The shape of the work

### Layering

```
Domain          Room, Slot, SlotWindow, SlotGrid (the generator), AppTimeZone
Application     IRoomRepository, ISlotRepository, IRoomService + RoomService,
                room/schedule DTOs, RoomErrorCodes, RoomDeleteOutcome
Infrastructure  Room/Slot entity configurations, RoomRepository, SlotRepository,
                RoomSeeder, SlotGridTopUp, the migration
Api             RoomsController, three validators, two ErrorStatusCodeMapper rows,
                one startup step
tests           MeetingRooms.UnitTests — the slot-grid generator
```

### Entities

Annotation-free, as `docs/decisions.md` requires; all mapping lives in the configurations.

```csharp
public sealed class Room
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Capacity { get; set; }

    // Load-bearing, not decoration: room creation inserts the room and its slots through this
    // collection in one SaveChanges, and EF fixes up the generated RoomId itself.
    public ICollection<Slot> Slots { get; } = [];
}

public sealed class Slot
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public int? BookedByUserId { get; set; }
    public AppUser? BookedByUser { get; set; }
    public DateTime? BookedAtUtc { get; set; }
}
```

**One deviation from phase 3's outcome note,** which anticipated `AppUser` gaining a slot
collection: it does not get one. Nothing queries a user *with* their slots — phase 5's "my
bookings" queries `Slots` by foreign key — so a collection navigation would be a property that
exists to be mapped and never read. `Slot.BookedByUser` stays, because phase 5's admin list
projects the booker's email through it.

### The grid generator

The one piece of pure logic in this phase, and the only thing unit-tested.

```csharp
public readonly record struct SlotWindow(DateTime StartUtc, DateTime EndUtc);

public static class SlotGrid
{
    public static readonly TimeOnly DayStart = new(8, 0);
    public static readonly TimeOnly DayEnd   = new(18, 0);
    public static readonly TimeSpan SlotLength = TimeSpan.FromMinutes(60);
    public const int HorizonDays = 14;
    public const int SlotsPerDay = 10;

    public static IReadOnlyList<SlotWindow> Generate(DateOnly firstDay, int days, TimeZoneInfo zone);
}
```

**The .NET-specific part.** The local wall-clock time is built with `DateTimeKind.Unspecified`
and converted with `TimeZoneInfo.ConvertTimeToUtc(local, zone)`. That method *throws* when the
value's `Kind` is `Utc` or `Local` and disagrees with the zone argument — `Unspecified` is the
only kind meaning "a wall-clock reading, interpret it in this zone". There is no PHP `DateTime`
equivalent, where the object carries its own zone and conversion is a method on it.

**Why this is DST-safe rather than lucky.** Ukraine follows the EU transition rules — last Sunday
of March and of October, at 03:00/04:00 local. The 08:00–18:00 window never contains the
transition hour, so the generator can never be handed a local time that does not exist (spring
forward) or that happens twice (autumn back) — the two cases `ConvertTimeToUtc` resolves by rule
rather than by intent. Either side of a transition the day still produces ten 60-minute windows;
only their UTC offset shifts, 05:00Z in summer and 06:00Z in winter. Tests pin both days.

`AppTimeZone` resolves the zone once into a static, because `FindSystemTimeZoneById` reads from
the OS on every call. It tries `Europe/Kyiv`, falls back to the Windows id `FLE Standard Time`,
and throws naming both if neither resolves — five lines that turn a deployment-image surprise
into a legible startup failure. Verified in this container: tzdata 2026c carries `Europe/Kyiv`
as the canonical zone, with `Europe/Kiev` a symlink to it.

### Mapping

The reference project maps with Mapster, configured per-property with `.IgnoreNonMapped(true)`
(`BookingApp.Application/DependencyInjectionExtensions.cs`). That is the correct way to use it —
convention-based mapping is how a new entity property silently starts appearing on the wire — but
per-property configuration means **one config line per property either way**, so the line count
matches hand-written construction and the package is pure addition on top.

The three mappings this phase needs are `Room → RoomResponse` (three properties),
`CreateRoomRequest → Room` (two), and `Slot → ScheduleSlotResponse`, which is not a mapping at
all: `IsBooked` is computed, and `IsBookedByMe` depends on the caller's user id, which is not a
property of the source object. In Mapster that needs `MapContext.Current.Parameters` or an
`AfterMapping` hook — more machinery, less readable than the expression it replaces.

Hand-written construction makes a property rename a compile error rather than a runtime one,
keeps `IMapper` out of every constructor, and leaves nothing to mock in a test — the reference
project mocks `IMapper` in its unit tests, which means its mapping config is covered by nothing.
`docs/reference/general-ideas.md` reaches the same conclusion: *"Hand-written mapping is a
perfectly reasonable choice at small scale."*

**Revisit at phase 5 or 7**, not before: if the booking and admin-list DTOs land at eight or ten
fields across several types, a configured mapper starts paying rent.

### Mapping the entities

`RoomEntityTypeConfiguration` — table `Rooms`, `Name` required `nvarchar(100)`, `Capacity`
required. **No unique index on `Name`:** `docs/requirements.md` does not ask for one, and it
would buy a new error code and two new failure paths, on create and on edit, for nothing.

`SlotEntityTypeConfiguration` — table `Slots`:

```csharp
builder.Property(slot => slot.StartUtc).HasColumnType("datetime2(0)").IsRequired();
builder.Property(slot => slot.EndUtc).HasColumnType("datetime2(0)").IsRequired();
builder.Property(slot => slot.BookedAtUtc).HasColumnType("datetime2(0)");

builder.HasOne(slot => slot.Room).WithMany(room => room.Slots)
    .HasForeignKey(slot => slot.RoomId).OnDelete(DeleteBehavior.Cascade);

builder.HasOne(slot => slot.BookedByUser).WithMany()
    .HasForeignKey(slot => slot.BookedByUserId).OnDelete(DeleteBehavior.Restrict);

builder.HasIndex(slot => new { slot.RoomId, slot.StartUtc }).IsUnique();
```

- The unique index is **seeder and top-up integrity only** and plays no part in the booking
  guarantee. The comment at the declaration says exactly that, because the README has to draw
  the same distinction and the two must never be confused.
- It is also the index the schedule read seeks on: a `(RoomId, StartUtc)` range, no join.
- **No explicit index on `BookedByUserId`.** `docs/plan.md` lists one; EF Core already creates an
  index for every foreign key, so declaring it would be documentation, not schema.
- Cascade from `Rooms` and `Restrict` from `AspNetUsers` both reach `Slots`, which is fine — SQL
  Server only refuses multiple *cascade* paths into one table.
- `datetime2(0)` **rounds** to the nearest second rather than truncating. Irrelevant for grid
  rows, which always sit at `:00`; it matters for phase 5's `BookedAtUtc`.

### Repositories

Use-case shaped, no generic base, `AsNoTracking()` on reads — the reference project's pattern.
They return entities and primitives; `RoomService` maps to DTOs, so Infrastructure never builds a
wire contract.

```csharp
public interface IRoomRepository
{
    Task<IReadOnlyList<Room>> ListAsync(CancellationToken ct);
    Task<Room?> FindByIdAsync(int roomId, CancellationToken ct);
    Task AddAsync(Room room, CancellationToken ct);                 // room + slots, one SaveChanges
    Task<bool> UpdateAsync(int roomId, string name, int capacity, CancellationToken ct);
    Task<RoomDeleteOutcome> DeleteAsync(int roomId, CancellationToken ct);
}

public interface ISlotRepository   // phase 5 adds TryClaimAsync here
{
    Task<IReadOnlyList<Slot>> ListForRoomAsync(int roomId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}
```

**`IUnitOfWork` is not used anywhere in this phase.** `docs/decisions.md` makes it load-bearing
only where a use case performs more than one write; every write here is a single statement or a
single `SaveChanges`, which EF already wraps in an implicit transaction. Room creation inserts
the room and its 140 slots through one `SaveChangesAsync`.

**Delete is an atomic conditional delete** — the idiom phase 5 uses for the booking claim,
rehearsed somewhere that is not the graded core:

```csharp
var deleted = await _dbContext.Set<Room>()
    .Where(room => room.Id == roomId && !room.Slots.Any(slot => slot.BookedByUserId != null))
    .ExecuteDeleteAsync(cancellationToken);

if (deleted == 1) return RoomDeleteOutcome.Deleted;

return await _dbContext.Set<Room>().AnyAsync(room => room.Id == roomId, cancellationToken)
    ? RoomDeleteOutcome.HasBookedSlots
    : RoomDeleteOutcome.NotFound;
```

One statement decides; the second query runs only on the failure path, to tell 404 from 409. A
check-then-delete would leave a window in which a slot is booked between the two — small, and not
the graded invariant, but there is no reason to accept it when the conditional form is the same
length. **The slots go because the database cascades them**, not EF: `ExecuteDelete` issues one
`DELETE` and never runs an EF-side cascade, so a relationship configured `ClientCascade` would
fail here on a foreign-key violation instead.

The edit uses `ExecuteUpdateAsync` the same way, with `rows == 0` meaning `RoomNotFound` — no
load-modify-save round trip.

### Use cases

`docs/plan.md` names one service for this phase, and one is right: every method is room-scoped.

| Method | Returns | Failure |
|---|---|---|
| `ListRoomsAsync` | `IReadOnlyList<RoomResponse>` | none possible |
| `GetRoomAsync` | `OperationResult<RoomResponse>` | `RoomNotFound` |
| `CreateRoomAsync` | `OperationResult<RoomResponse>` | none today; wrapped for the controller's shape |
| `UpdateRoomAsync` | `OperationResult<RoomResponse>` | `RoomNotFound` |
| `DeleteRoomAsync` | `OperationResult` | `RoomNotFound`, `RoomHasBookedSlots` |
| `GetScheduleAsync` | `OperationResult<ScheduleResponse>` | `RoomNotFound` |

`ListRoomsAsync` deliberately returns the list rather than an `OperationResult` that can only
ever succeed. The result type exists to carry business failures; wrapping a read that has no
failure branch is ceremony that makes the controller claim a path it does not have.

`CreateRoomAsync` builds the `Room`, fills `room.Slots` from `SlotGrid.Generate(today,
HorizonDays, AppTimeZone.Instance)` — "today" from the injected `TimeProvider`, converted into
the display zone — and hands the whole graph to the repository.

`GetScheduleAsync` defaults an absent range to the current local day forward 14 days, using the
same zone resolution the generator uses, so the client never has to compute a local midnight. It
maps `IsBooked = slot.BookedByUserId is not null` and
`IsBookedByMe = slot.BookedByUserId == callerUserId`; no booker identity reaches the wire for
anyone, per `docs/requirements.md` §5.

### API

```
GET    /api/rooms                  [Authorize]              200 RoomResponse[]
GET    /api/rooms/{id:int}         [Authorize]              200 | 404
POST   /api/rooms                  [Authorize(Roles=Admin)] 201 + Location | 400
PUT    /api/rooms/{id:int}         [Authorize(Roles=Admin)] 200 | 400 | 404
DELETE /api/rooms/{id:int}         [Authorize(Roles=Admin)] 204 | 404 | 409
GET    /api/rooms/{id:int}/schedule?fromUtc=&toUtc=  [Authorize]  200 | 400 | 404
```

`GET /api/rooms/{id}` earns its place as the target of `CreatedAtAction` on create. The `:int`
route constraint keeps `/api/rooms/abc` out of the action, where it falls through to the
`/api/{**slug}` catch-all and returns a `ProblemDetails` 404 rather than a binding error.

DTOs in `Application/DTOs/Rooms/`, `-Request`/`-Response` named per `docs/decisions.md`:

```csharp
public sealed record RoomResponse(int Id, string Name, int Capacity);
public sealed record CreateRoomRequest(string Name, int Capacity);
public sealed record UpdateRoomRequest(string Name, int Capacity);
public sealed record ScheduleRangeRequest(DateTimeOffset? FromUtc, DateTimeOffset? ToUtc);
public sealed record ScheduleResponse(int RoomId, string RoomName, string TimeZoneId,
                                      IReadOnlyList<ScheduleSlotResponse> Slots);
public sealed record ScheduleSlotResponse(int Id, DateTime StartUtc, DateTime EndUtc,
                                          bool IsBooked, bool IsBookedByMe);
```

**`DateTimeOffset?`, not `DateTime?`, for the range.** MVC binds a query-string `DateTime`
through `DateTimeStyles.RoundtripKind`: `...Z` arrives as `Kind = Utc`, `...+03:00` arrives as
`Kind = Local`, and a bare date arrives as `Unspecified` — three kinds the service would have to
normalise, silently and wrongly if it missed one. `DateTimeOffset` carries the offset explicitly
and `.UtcDateTime` is unambiguous. A value sent with no offset is read as server-local, which is
UTC both in this container and on App Service.

Three validators in `Api/Validators/Rooms/`, picked up by the existing assembly scan and run by
the existing `AsyncValidationFilter`: name non-empty and at most 100 characters, capacity between
1 and 1000, and for the range `ToUtc > FromUtc` with a span of at most 14 days. Create and update
rules are duplicated rather than shared — FluentValidation's `Include` only composes validators
of the same type, and the filter resolves by *concrete* type, so each request type needs its own.

Two new `ErrorStatusCodeMapper` rows: `RoomNotFound` → 404, `RoomHasBookedSlots` → 409.

### Seeding and the top-up

`docs/decisions.md` exempts seeders from the "never touch `AppDbContext` directly" rule, so both
use it directly rather than growing ports with exactly one caller each.

`RoomSeeder` creates four demo rooms if the table is empty — a fixture rather than filler: a
two-person focus room, a six-person huddle, a twelve-person board room and a twenty-person
training room, so capacity is visibly a real field. It creates rooms only; the top-up gives them
slots, which keeps slot creation on a single code path.

`SlotGridTopUp` runs last in the startup block:

```csharp
var today = DateOnly.FromDateTime(
    TimeZoneInfo.ConvertTimeFromUtc(timeProvider.GetUtcNow().UtcDateTime, AppTimeZone.Instance));
var windows = SlotGrid.Generate(today, SlotGrid.HorizonDays, AppTimeZone.Instance);
// one query for the (RoomId, StartUtc) pairs already inside the window,
// then AddRange the missing ones and one SaveChanges. Logs how many it inserted.
```

Idempotent by construction, with the unique index as the backstop, and a no-op on the second
start of the same day. The deployment is a single instance, so there is no concurrent-startup
case to reason about.

**No seeded bookings, deliberately.** `docs/decisions.md` states that `TryClaimAsync` must remain
the only write path to `BookedByUserId`; a seeder writing that column directly would force that
sentence to grow a qualifier, in exchange for a demo screen looking fuller three days before the
endpoint that fills it properly exists. The consequence is stated under *Verification*.

### Tests

The first test project in the repository. `docs/decisions.md` scopes unit testing to "the
mandated concurrency test plus the slot-grid generator" — this is the second of the two, and
standing the project up now means phase 5 does not meet test-project problems and the graded core
on the same day.

`SlotGridTests` covers: 14 days producing 140 windows, ten per day; the day's first window
starting at 08:00 local and its last ending at 18:00 local; windows contiguous, non-overlapping
and exactly 60 minutes; every `DateTime` returned with `Kind == Utc`; a summer day mapping to
05:00Z and a winter day to 06:00Z; **both 2026 DST transition days still producing ten 60-minute
windows**; and a zero or negative day count rejected rather than quietly returning an empty grid.

CI is unaffected — `.github/workflows/deploy.yml` publishes the API project and never runs
`dotnet test`.

---

## Tasks

Each is one commit; the tree builds after every one. Branch `phase/4-rooms-slots`, from
`develop`.

| # | Commit | Contents |
|---|---|---|
| 1 | `docs: add phase 4 plan` | this file |
| 2 | `docs: move the fixed display zone to Europe/Kyiv` | `requirements.md` §3 + §7, `plan.md` risk 7, `decisions.md` persistence entry — the three places naming Stockholm |
| 3 | `docs: make the slot horizon a rolling window` | `requirements.md` §3's horizon paragraph: rolling window plus startup top-up, replacing "widen the constant if review slips" |
| 4 | **(you)** `chore: add the unit test project` | `dotnet new xunit`, the Domain project reference, the `.slnx` entry |
| 5 | `feat(domain): add the room and slot entities` | `Room`, `Slot` |
| 6 | `feat(domain): generate the slot grid in one fixed zone` | `AppTimeZone`, `SlotWindow`, `SlotGrid` **and its tests** — the tests are part of this change, not a follow-up |
| 7 | `feat(infrastructure): map rooms and slots` | the two `IEntityTypeConfiguration`s |
| 8 | `feat(infrastructure): add the rooms and slots migration` | generated migration + snapshot |
| 9 | `feat(application): add the room ports, DTOs and error codes` | `IRoomRepository`, `ISlotRepository`, `RoomErrorCodes`, `RoomDeleteOutcome`, the DTOs |
| 10 | `feat(infrastructure): implement the room and slot repositories` | `RoomRepository`, `SlotRepository`, DI registration |
| 11 | `feat(application): add the room and schedule use cases` | `IRoomService`, `RoomService`, DI registration |
| 12 | `feat(api): add the room and schedule endpoints` | `RoomsController`, three validators, two mapper rows |
| 13 | `feat(api): seed demo rooms and top up the slot grid at startup` | `RoomSeeder`, `SlotGridTopUp`, the `Program.cs` step |
| 14 | `docs: fold phase 4 decisions into decisions.md` | the seven decisions above, before the branch merges |
| 15 | `docs: record phase 4 outcome` | the Outcome section, after the deploy, on `develop` |

### Standing decisions to fold into `docs/decisions.md` (task 14)

Under *Persistence*: the rolling horizon plus startup top-up; the unique `(RoomId, StartUtc)`
index as seeder integrity only, restated now that it exists; no explicit `BookedByUserId` index
because EF indexes foreign keys already. Under *Booking and concurrency*: room delete refused
while any slot is booked, and why that follows from bookings being non-cancellable. Under *API
surface and errors*: the schedule range as optional UTC instants; `timeZoneId` published once per
response rather than stored per row; `OperationResult` not wrapping reads that cannot fail. Under
*Architecture and layering*: mapping stays hand-written, with the condition that would reopen it.
A new *Time* entry: the display zone is Europe/Kyiv, resolved once through a dual-id lookup, and
the grid window excludes the DST transition hour by construction.

---

## What you do

1. `git checkout -b phase/4-rooms-slots` from `develop`.
2. The test project (task 4), from the repository root:

```bash
dotnet new xunit -o tests/MeetingRooms.UnitTests -n MeetingRooms.UnitTests
dotnet add tests/MeetingRooms.UnitTests reference src/backend/MeetingRooms.Domain
```

   This is the phase's only package ask. The template pins its own versions from the installed
   SDK; I read the generated `.csproj` and report exactly what it pinned before you commit it, so
   the versions are approved rather than assumed. Then add the project to `MeetingRooms.slnx`
   under a `/tests/` folder.

3. The migration, when I flag task 8:

```bash
dotnet ef migrations add AddRoomsAndSlots \
  --project src/backend/MeetingRooms.Infrastructure \
  --startup-project src/backend/MeetingRooms.Api
```

4. Make each commit when I flag the point.
5. **Nothing to add in the Azure portal before merging.** Unlike phase 3, this phase introduces
   no new configuration key and no new secret.
6. Merge `phase/4-rooms-slots` into `develop` with `--no-ff`, then `develop` into `main`, which
   deploys. The two phase-3 documentation commits still sitting on `develop` ride along.

---

## Risks and fallbacks

| Risk | Fallback |
|---|---|
| **Time-zone data missing on the App Service image**, so `FindSystemTimeZoneById` throws and the application will not boot — the one new way this phase can break a deploy | The dual-id resolver tries `Europe/Kyiv`, then `FLE Standard Time`, and throws naming both. If the image turns out to carry no ICU data at all, the fallback is a hard-coded `TimeZoneInfo.CreateCustomTimeZone` with the EU transition rule — ugly, and confined to one file |
| DST handling wrong in a way the tests do not catch | The window excludes the transition hour by construction, and both 2026 transition days are pinned. What remains is a *display* error, which phase 7 surfaces |
| `dotnet new xunit` pins a package set that will not restore behind the container firewall | NuGet is on the allowlist and the template targets the installed SDK. If it fails, the generator ships without tests in task 5 and they follow in a `chore` commit once resolved — the generator is not blocked on them |
| `ExecuteDeleteAsync` will not translate the `!room.Slots.Any(...)` subquery | It should become `WHERE NOT EXISTS`. If EF refuses, fall back to an explicit `AnyAsync` check inside a transaction and record the resulting check-then-act window honestly |
| The top-up is slow or surprising at startup | Four rooms × 140 windows is one indexed range read and at most 560 inserts, once per start, on a single instance. If it ever stops being trivial it moves behind a flag |
| Query-string binding of `DateTimeOffset?` misbehaves in Scalar | Caught in the first Scalar pass below. If it binds badly the parameters become two strings parsed in the validator — uglier, but total |

## Verification

Locally, before the merge:

- `dotnet build` — clean, **zero warnings**. Phase 3's startup warning that no
  `IEntityTypeConfiguration` was found should disappear as a side effect of task 7.
- `dotnet test` — green, and run once with `DayEnd` set to 17:00 to watch the count assertions go
  red, so the tests are known to be load-bearing rather than assumed to be.
- `dotnet ef migrations list` — `AddRoomsAndSlots` listed and applied.
- Read the generated migration: `ON DELETE CASCADE` on the room foreign key, `NO ACTION` on the
  user foreign key, a unique index on `(RoomId, StartUtc)`, and `datetime2(0)` columns.
- Through Scalar with a **User** token: `GET /api/rooms` → 200 and the four seeded rooms;
  `POST /api/rooms` → **403**, the first role gate this application has ever enforced for real;
  `GET /api/rooms/{id}/schedule` → 140 slots, all `isBooked: false`, `timeZoneId: "Europe/Kyiv"`,
  each day's first `startUtc` at 05:00Z while EEST is in effect.
- With an **Admin** token: `POST /api/rooms` → 201 with a `Location` header; following it → 200;
  the new room's schedule → its own 140 free slots. `PUT` → 200 carrying the new values.
  `DELETE` → 204, and that room's schedule → 404 `RoomNotFound`. A successful delete is itself
  the cascade proof: without `ON DELETE CASCADE` the child rows would have failed it on a
  foreign-key violation.
- Failure paths: an unknown id → 404 `RoomNotFound`; `capacity: 0` and an empty name → 400
  `ValidationProblemDetails` keyed by field; `toUtc` before `fromUtc`, and a 30-day span → 400;
  no token → 401 with an empty body.
- Restart: no migration applied, the room seeder a no-op, the top-up logging **0 inserted**.
  Idempotence is what makes this safe on every deploy.
- Regression, unchanged from phases 2 and 3: `/` serves the React page, `/scalar/` and
  `/openapi/v1.json` load, register/login/`/me` still work, `/api/nope` is still a 404
  `ProblemDetails` with no error code, and the hub negotiate still reports in-process SignalR.

**Stated limit of this phase.** Nothing here can produce a *booked* slot: the only write path to
`BookedByUserId` arrives in phase 5, and this container has no SQL client with which to insert one
by hand. `isBooked` and `isBookedByMe` are therefore verified on their false branch only. Phase 5
books a slot and re-reads the schedule, which is where the true branch is proven, and the same
applies to the 409 on deleting a booked room.

On the deployed application:

- It boots — which proves the zone resolves on the App Service image, the one new startup
  dependency this phase adds.
- `/health` still reports `databaseReachable: true`.
- The four demo rooms are present with populated grids, proving the seeder and the top-up both
  ran against Azure SQL.
- An admin creates, edits and deletes a room through Scalar on the deployed URL; a User token
  still gets 403 on the same create.
- Slot times line up with 08:00–18:00 when read in Europe/Kyiv.

## Done when

- [ ] `Rooms` and `Slots` exist in Azure SQL with the unique index and the cascade.
- [ ] Four demo rooms and a full 14-day grid per room, locally and deployed.
- [ ] A room created through the API gets its own grid immediately, aligned with every other
      room's window.
- [ ] A restart applies no migration, seeds no room and inserts no slot.
- [ ] Admin-only endpoints answer 403 to a User token and succeed for an Admin token.
- [ ] Deleting an unbooked room is a 204; the 409 path exists and gets its live proof in phase 5,
      when a booked slot can exist.
- [ ] The schedule returns UTC instants plus `timeZoneId`, and honours an explicit range.
- [ ] `dotnet build` zero warnings, `dotnet test` green.
- [ ] Phase 2's and phase 3's surface unchanged.
- [ ] `requirements.md`, `plan.md` and `decisions.md` no longer say Stockholm.
