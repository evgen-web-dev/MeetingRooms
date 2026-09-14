# Decisions

Technical and architectural choices for this project. **Binding.** If something here
looks wrong, say so and stop — don't work around it silently.

Entries are grouped by area rather than by the order they were made. `assignment.md`
stays authoritative: if an entry here conflicts with it, the assignment wins.
`docs/requirements.md` says what we build; `docs/plan.md` says in what order.

---

## Architecture and layering

- **Single Web App**, frontend served from `wwwroot` — no CORS, no cross-origin
  SignalR negotiate.
- **Clean Architecture**, with these references and no others:

  ```
  Domain         → (nothing, except ASP.NET Identity's store abstractions — see below)
  Application    → Domain
  Infrastructure → Application + Domain
  Api            → Application + Infrastructure
  ```

- **Controllers** for endpoints (`AuthController : ControllerBase`), not minimal APIs.
- **Extension methods for service registration**, one `DependencyInjectionExtensions`
  per layer, so `Program.cs` stays a list of module calls rather than a dumping ground.
- **Scalar** as the OpenAPI exploration tool, served in **all** environments
  including the deployed app, so a reviewer can exercise the API without cloning
  anything. This publishes the API surface publicly - nothing secret is in it, and
  the trade is stated in the README.
- **The composition root owns the clock.** `TimeProvider.System` is registered in DI and
  injected wherever time is needed; nothing below `Program.cs` reads `DateTime.UtcNow`
  directly. This is what lets the booking timestamp be controlled from a test without
  reaching for a static.
- **Mapping between entities and DTOs is hand-written. No mapper library.**

  > *Settled during phase 4.* The reference project uses Mapster configured per-property
  > with `.IgnoreNonMapped(true)`, which is the correct way to use it — convention-based
  > mapping is how a new entity property silently starts appearing on the wire. But
  > per-property configuration is one line per property either way, so the line count
  > matches hand-written construction and the package is pure addition on top of it. A
  > constructor call also makes a property rename a compile error rather than a runtime
  > one, keeps `IMapper` out of every constructor, and leaves nothing to mock — the
  > reference project mocks `IMapper` in its unit tests, so its mapping config is covered
  > by nothing. **Revisit if a later phase's DTOs reach eight to ten fields across several
  > types**, which is where a configured mapper starts paying rent.

  > *Revisited in phase 5, because that phase crossed the threshold above: six new types, seven
  > fields on the widest.* Still hand-written, but on a different argument than the line-count one.
  > These are **flattening projections** — `slot.Room.Name` becomes `RoomName`,
  > `slot.BookedByUser.Email` becomes `BookedByEmail` — and convention-based mapping does not reach
  > across a navigation property, so every one of them would be a configured line anyway. The
  > trigger has now fired once and been answered; treat a third occasion as a real signal rather
  > than re-running this argument.

## API surface and errors

- **Result pattern** for expected failures — returned, not thrown. A 500 means
  "nothing caught or classified this", never a control-flow branch.
- **`OperationResult` refuses to lie about itself.** `Success(value)` throws on a null
  value, reading `Value` on a failed result throws instead of returning `default!`, and
  `Failure()` rejects an empty error list. Callers may therefore rely on a failure always
  having `Errors[0]`, and a missed `Succeeded` check fails at the mistake rather than
  somewhere downstream of it.
- **Business errors are string error codes** (`InvalidEmailOrPassword`,
  `SlotAlreadyBooked`); validation errors may be text.
- **Business errors and unhandled exceptions are wrapped in `ProblemDetails`;
  validation errors in `ValidationProblemDetails`**, matching what `[ApiController]`
  produces natively so hand-rolled and framework-generated failures look identical
  to a client.

  > *Settled during phase 1.* The `ProblemDetails` object is built by MVC's own
  > `ProblemDetailsFactory` — reached through `ControllerBase.ProblemDetailsFactory`, or
  > left to the writer to fill in from the exception handler — rather than by a
  > hand-rolled factory plus our own status-code-to-RFC-URI table. The framework already
  > owns that mapping, so reproducing it means keeping a second table in sync forever,
  > and "identical to a client" becomes something we maintain by vigilance instead of by
  > construction. Confirmed empirically: the `type` the framework emits for a 500 is
  > character-for-character the URI the hand-rolled table carried. The same factory
  > supplies `CreateValidationProblemDetails` for the validation filter. No package is
  > required — it is in the `Microsoft.AspNetCore.App` shared framework.

- **An unmapped error code maps to 500, not 400.** A missing row in
  `ErrorStatusCodeMapper` is a bug, and a bug should be loud rather than presenting
  itself to the client as a client error.
- **`errorDetails` carries classified business failures only.** It exists so a client can
  branch on a business condition; a routing miss is not one. Unmatched `/api` routes
  therefore return a plain framework `ProblemDetails` with no error code.
- **Global exception handler** as the safety net.
- **`-Request` / `-Response` DTO naming** — `CreateRoomRequest`, `BookSlotResponse`.
- **Conflict contract:** HTTP 409 + `ProblemDetails` + error code `SlotAlreadyBooked`.
- **Every endpoint is a controller action, with no exceptions** - `/health` included,
  even though it is a diagnostic rather than a resource. Its URL stays `/health`.
- **Unmatched `/api/...` routes return a 404 `ProblemDetails`**, not the SPA's
  `index.html`. `MapFallbackToFile` would otherwise answer them with a 200 and a
  page, which is the wrong answer for an API under review.

- **401 and 403 keep the framework's empty body.** JWT bearer answers an unauthenticated
  request with `WWW-Authenticate` and no payload, and a role miss with a bare 403. The
  status is the contract; overriding correct framework behaviour to add a body is
  machinery. Stated here so the absence reads as a decision.

- **A read that cannot fail returns its value, not an `OperationResult`.** The result type
  exists to carry business failures; wrapping a read with no failure branch makes every
  caller handle a path that does not exist. `IRoomService.ListRoomsAsync` returns
  `IReadOnlyList<RoomResponse>` for this reason, while every other method on that service
  has a real failure and is wrapped.
- **The schedule range is two optional UTC instants**, `fromUtc` and `toUtc`, bound as
  `DateTimeOffset?`. Absent bounds mean the whole current window, resolved server-side, so
  a client never computes where a local day begins in UTC.

  > *Settled during phase 4, and measured rather than assumed.* MVC parses a query-string
  > `DateTime` in an `AdjustToUniversal` style: a trailing `Z` **and** an explicit offset
  > both arrive as `Kind.Utc` (the offset already applied), while a value with neither
  > arrives as `Unspecified` and is taken at face value. `DateTimeOffset` binds all three
  > unambiguously and `.UtcDateTime` needs no normalising.
  >
  > **The trap that survives either choice:** a value sent with *no* offset is interpreted
  > in the **server's** local zone. That is `Europe/Kyiv` in the dev container — the `TZ`
  > variable is inherited from the host through `.devcontainer/docker-compose.yml` — and
  > UTC on App Service, so `?fromUtc=2026-09-15` means two different instants in the two
  > environments. Not worth engineering around: once bound, "the caller sent +03:00" and
  > "the caller sent nothing and the server assumed +03:00" are indistinguishable, so no
  > validator can catch it. Clients send ISO strings with `Z` — which is what
  > `Date.prototype.toISOString` produces — and the parameters are optional anyway.
  >
  > Nothing else in the application reads server-local time: `TimeProvider.GetUtcNow()`
  > plus explicit zone conversion is the rule everywhere, which is precisely why this is
  > the only place the discrepancy can surface.

- **The display zone is named once per schedule response** as `timeZoneId`, and never
  stored per row. Nothing creates a slot from a browser, so there is no per-slot zone to
  record; the client needs to know how to render, and a constant held in both the generator
  and the formatter is how the two drift apart. Not a per-slot UTC offset either — that is
  derived data that changes at a DST boundary and can disagree with itself.
- **Nothing is paginated. The schedule is *bounded*, which is a different claim from
  *deferred*.** A schedule response is at most 140 items — ten slots a day across a
  fourteen-day horizon, with the validator capping an explicit range at the same fourteen
  days — so on the order of 15KB of JSON before compression, and a ceiling that cannot grow
  with use. Pagination protects against unbounded growth, and there is none here. The
  endpoint also already carries the paging primitive its domain actually uses:
  `fromUtc`/`toUtc`. A client rendering one day asks for one day and receives ten slots,
  whereas "page 3" of a calendar means nothing to anyone — slots are addressed by time, not
  by index.

  > *The honest weak spot, worth volunteering rather than waiting to be asked:*
  > `GET /api/rooms` has **no** structural bound — nothing stops an administrator creating
  > ten thousand rooms. It is fine at four, and it is the endpoint that would take paging
  > first if the room count ever justified it. Phase 5's "my bookings" and admin
  > all-bookings lists are the genuinely unbounded ones: they grow monotonically with use
  > and have no natural window, so that is where pagination is expected to earn its place.
  > The reference project's four-type pattern — `PaginatedRequest`, `PageQueryParams`,
  > `PagedResult`, `PaginatedResponse` — is worth borrowing there, along with its two
  > cautions: count on the unpaged query first, and `OrderBy` before `Skip`/`Take` or the
  > page is non-deterministic.

- **The bookings lists are not paginated**, which retires the expectation recorded just above.

  > *Settled in phase 5, when those lists were built.* The prediction was sound in principle — they
  > are the one genuinely unbounded read in the application — but the horizon is fourteen days and
  > the deployed application is a demo, so the admin list is tens of rows. Four new types plus
  > count-before-paging on the phase that carries the graded core is machinery competing with the
  > thing being graded. Recorded as a decision rather than an omission: the endpoint to page first
  > is `GET /api/bookings`, and the reference project's four-type pattern is what to borrow.

- **A booking answers 201 with no `Location` header**, for the reason registration already does: a
  booking is not an addressable resource here — it has no identity of its own — so the only thing
  to point at would be the slot, which is not what was created.
- **A caller who already holds the slot gets 201 as well, not 200.** Either way they hold it, and a
  separate status would make every client branch on a distinction that means nothing to them.
- **`timeZoneId` is named once per bookings-list response too**, not only on a schedule. A "my
  bookings" screen may never call the schedule endpoint, and a client holding its own copy of
  `Europe/Kyiv` is how a generator and a formatter drift apart.
- **No server-computed "bookable" flag on a schedule slot.** A slot whose window has closed is
  `isBooked: false` and yet unbookable; the client has `endUtc` and its own clock, and a
  server-computed flag would be stale the moment it is serialised. Phase 7 greys them out.

## Validation

- **FluentValidation**, with validators applied in **one place — a global MVC action
  filter**, not per endpoint.

  > *Changed during planning.* This previously read "middleware". Middleware runs
  > before MVC model binding, so at that point there is no bound DTO to hand a
  > validator — the interception point that has the action arguments is an
  > `IAsyncActionFilter`. Same "one place, not per endpoint" outcome, correct layer.
  > (A PHP/Laravel false friend: there, middleware sees a parsed request.)

- **Validators carry payload-only rules.** A check belongs in a validator when it can be
  settled from the request alone, needs no I/O, and naming the field leaks nothing. The
  password *policy* is therefore Identity's, not the validator's; the only password rule
  in a validator is a length cap, which is not policy but a bound on how much hashing an
  unauthenticated caller can demand.

## Persistence

- **SQL Server** — Azure SQL Database in production, a SQL Server container locally.
- **`AppDbContext` always sits behind `IUnitOfWork` / repositories**, never used
  directly from application code. Seeders may be an exception.
- **`IUnitOfWork` is kept but minimal.** It is load-bearing only where a use case
  performs more than one write — registration (create user + assign role). EF already
  wraps a single `SaveChangesAsync` in an implicit transaction, so the explicit
  envelope elsewhere is ceremony.
  > *Settled during phase 3.* The port is a single
  > `ExecuteInTransactionAsync(operation, ct)` rather than a begin/commit/rollback trio:
  > the trio throws under `EnableRetryOnFailure`, which refuses user-initiated transactions
  > a retrying execution strategy cannot replay. Folding the strategy, the transaction and
  > the save into one call leaves no way to get that wrong. It commits **only when the
  > returned result succeeded** - `UserManager` saves as it goes, so a failure that is
  > returned rather than thrown would otherwise commit half a use case - which is why the
  > result type is constrained to `OperationResult`.

- Explicit transactions are wrapped in EF's execution strategy, because
  `EnableRetryOnFailure` is required for Azure SQL and otherwise rejects
  user-initiated transactions.
- **`IEntityTypeConfiguration<T>` + `modelBuilder.ApplyConfigurationsFromAssembly`**,
  with zero data annotations on entities.
- **Entity types are reached through `Set<T>()`; the context declares no `DbSet`
  properties of its own.** The assembly scan above already discovers every entity, so a
  property would be a second registration mechanism to remember, and `AppDbContext` never
  leaves Infrastructure — nothing outside two repositories would consume it. Table names
  are explicit via `ToTable`, so no table name depends on a C# property name.

  > *The trade, stated because it is real:* `Set<Foo>()` for a type that is not in the
  > model compiles and throws at runtime, where a missing `DbSet` property would not
  > compile. An unconfigured entity would also have no table and no migration, so it fails
  > immediately in development. Note also that `IdentityDbContext` contributes `Users`,
  > `Roles` and friends regardless, so this keeps the context's *own* surface narrow rather
  > than the whole surface.

- **Instants read back from `datetime2` have their `DateTimeKind` restored by a value
  converter**, declared in `SlotEntityTypeConfiguration` and applied to every instant
  column.

  > *Found in phase 4 by verifying rather than assuming.* `datetime2` stores no zone, so
  > EF materialises these columns as `Unspecified`. The value is correct and the label is
  > gone — invisible in C#, where the two compare equal, and very visible on the wire:
  > `System.Text.Json` writes an `Unspecified` instant with no trailing `Z`, and a browser
  > parsing `"2026-09-14T05:00:00"` reads it as **local** time and shifts every slot by the
  > viewer's offset. The converter lives in the configuration rather than at each mapping
  > site so that no read path can forget it. Its provider-side expression is the identity,
  > so stored values, generated SQL and the schema are all unchanged.

- **The slot horizon is rolling, not fixed at first seeding**, kept current by an
  idempotent top-up that runs at startup after the seeders. Every room's window is
  `[today, today+14)` in the display zone, so a room created later is aligned with the rest
  rather than ending earlier, and nothing goes empty if a review window slips. One read for
  the `(RoomId, StartUtc)` pairs already present, a set difference, one insert for the
  remainder; the unique index is the backstop.
- **No explicit index is declared on `Slot.BookedByUserId`.** EF Core creates an index for
  every foreign key, so declaring the one `docs/plan.md` lists for "my bookings" would be
  documentation rather than schema. Confirmed in the generated migration.
- **A room carries a name and a capacity and nothing else, and its name is not unique.**
  `docs/requirements.md` §2 asks for no more, and a unique name would buy a conflict error
  code plus a failure path on both create and edit for nothing.
- **Times are stored UTC as `datetime2(0)`** and displayed in Europe/Kyiv.
  Note: SQL Server's `timestamp` is a synonym for `rowversion`, not a date/time type.

  > *Amended during phase 4 planning.* The display zone was Europe/Stockholm, chosen
  > alongside the Azure region. The region is where the bytes run and says nothing about
  > what the numbers mean; the rooms are fictional, so the zone's only job is to be
  > legible to whoever opens the deployed application. `assignment.md` is silent on time
  > zones, so this amends `docs/requirements.md` §3 and §7 without touching anything
  > authoritative.

## Time

Storage type is under *Persistence*; this is about meaning.

- **One fixed display zone, `Europe/Kyiv`**, for every user and every room
  (`docs/requirements.md` §7). Rendering in each viewer's own zone was considered and
  rejected: the grid's defining property is that it runs 08:00–18:00, and that stops being
  legible the moment two viewers see different hours for the same slot.
- **The zone is resolved once into a static**, trying the IANA id and then the Windows id
  `FLE Standard Time` before throwing with both names and the likely cause.
  `FindSystemTimeZoneById` reads from the OS on every call, and the failure it throws on a
  host with no time-zone database — or one running under invariant globalization — is
  otherwise an unreadable startup crash.
- **The working day excludes the daylight-saving transition hour by construction.** The EU
  switches at 03:00/04:00 local; 08:00–18:00 never contains that, so the generator can never
  be handed a local time that does not exist (spring forward) or happens twice (autumn
  back) — the two cases `TimeZoneInfo.ConvertTimeToUtc` resolves by rule rather than by
  intent. A slot's UTC instant therefore moves by an hour across a boundary while its local
  hour does not: 08:00 Kyiv is 05:00Z in summer and 06:00Z in winter.
- **Slot generation is pure and takes the zone as a parameter**, which is what makes both
  2026 transition days testable without waiting for them. It is also the only logic in the
  phase covered by unit tests, per *Testing*.
- **Local wall-clock values are built with `DateTimeKind.Unspecified`.** That is required,
  not stylistic: `ConvertTimeToUtc` throws when a value's `Kind` contradicts the zone
  argument, and `Unspecified` is the only kind meaning "a reading to interpret in this
  zone".

## Booking and concurrency

This is the assignment's core; the full reasoning is in `docs/plan.md`.

- **Slots are materialised rows.** `Slots(Id, RoomId, StartUtc, EndUtc,
  BookedByUserId NULL, BookedAtUtc NULL)`.
- **There is no `Bookings` table.** A booking has no state independent of the slot —
  no lifecycle, no cancellation, no attendees — so a separate entity would be a 1:1
  table whose only effect is a join.
- **Mechanism: atomic conditional update.** The business condition lives in the
  `WHERE` clause, so the check and the write are a single statement:

  ```csharp
  _db.Slots.Where(s => s.Id == slotId && s.BookedByUserId == null)
           .ExecuteUpdateAsync(...)   // 1 row -> 201,  0 rows -> 409
  ```

  The loser blocks on the winner's row lock, re-reads the committed row when the lock
  clears, fails the predicate and updates zero rows. Correct under READ COMMITTED with
  or without RCSI (Azure SQL enables RCSI by default) — **not** under SNAPSHOT, which
  would raise update-conflict 3960 instead.
- **The rows-affected count does not prevent the race; it reports it.** The engine
  resolved the race under the row lock before the count came back. This is not a
  check-then-act.
- **The booking key is a server-generated surrogate.** The client posts `{ slotId }`,
  never a time, so no clock skew or `datetime2` precision can produce two rows meaning
  one slot. This is why the key is not `(RoomId, StartUtc)`.
- **`UNIQUE (RoomId, StartUtc)` exists for seeder integrity only** — it plays no part
  in the booking guarantee. Say so in the README; the two constraints must not be
  confused.
- **`SlotRepository.TryClaimAsync(...)` owns its own write.** `ExecuteUpdateAsync`
  executes immediately and bypasses the change tracker, so there is no `SaveChanges`
  for `IUnitOfWork` to coordinate on this path.
- **Accepted limitation:** the invariant lives in one statement's `WHERE` clause
  rather than in a standing constraint, so `TryClaimAsync` must remain the only write
  path to `BookedByUserId`. Stated in the README rather than omitted.
- **Nothing seeds a booking**, in any environment. A seeder writing `BookedByUserId`
  directly would force the sentence above to grow a qualifier, in exchange for a demo
  screen looking fuller. Demo *rooms* are seeded; their slots all start free.
- **Deleting a room with any booked slot is refused** — `RoomHasBookedSlots` → 409, not a
  cascade. `docs/requirements.md` §4 says bookings cannot be cancelled, rescheduled or
  modified, so removing the room out from under one would be a cancellation by another
  name, and a silent one. 409 rather than 403 because the caller is permitted to delete
  rooms; this one is refused on account of state.
- **That refusal is itself an atomic conditional delete**, folding the condition into the
  `WHERE` clause exactly as the booking claim does, so no slot can be booked between
  deciding and deleting. The rows-affected count distinguishes success; a second query runs
  only on the failure path, to tell 404 from 409. Verified against SQL Server: it translates
  to `NOT EXISTS`, and the slots disappear through the database's `ON DELETE CASCADE` —
  `ExecuteDelete` never runs an EF-side cascade, so a `ClientCascade` relationship would
  fail here on a foreign-key violation instead.

- **A caller who already holds the slot is told 201, not 409.** The failure path re-reads the row
  — it has to anyway, to tell 404 from 409 — and reports success when `BookedByUserId` is the
  caller's own.

  > *Settled while planning phase 5, closing the entry this file previously carried under* Still
  > open. `EnableRetryOnFailure` replays an operation when a transient fault lands after the commit
  > but before the acknowledgement. The replayed conditional `UPDATE` then finds the slot booked by
  > its own winning write, matches zero rows, and would tell the caller who actually won that they
  > lost. The invariant holds either way; the response would lie, to the one caller entitled to a
  > truthful yes.
  >
  > **The accepted consequence is that booking is idempotent per user.** A deliberate second
  > request from the same account also succeeds, because nothing distinguishes it from a replay
  > without an idempotency key — machinery this scope does not justify. A false 409 to the winner
  > is the worse lie.
  >
  > **The consequence for the mandated test, which is easy to miss:** N requests from *one* account
  > would each find their own id on the row and report N winners. The concurrency test therefore
  > uses N **distinct** users.

- **The 201 carries the booking time from the row, not from the request that asked.** A replay
  reports the same instant either way; a deliberate second request hours later would otherwise
  claim the booking happened just now. No screen consumes the field — it is the visible proof that
  asking twice did not rebook anything.
- **A slot stops being bookable when it *ends*, not when it starts.** The predicate is
  `slot.EndUtc > nowUtc`, in the same `WHERE` clause: a slot with half an hour left is still half
  an hour of a meeting room. `SlotHasEnded` → 409.
- **Zero rows affected has four explanations, resolved in a stated order:** the caller's own
  booking → 201; booked by anyone else → 409 `SlotAlreadyBooked`; free but expired → 409
  `SlotHasEnded`; no row → 404 `SlotNotFound`. A fifth state — free, unexpired, yet unmatched — is
  unreachable and **throws** rather than returning the least-wrong answer: a booking is never
  removed, `EndUtc` is never updated, and `nowUtc` is fixed for the call, so such a row would have
  been claimed by the `UPDATE`.
- **`nowUtc` is read once from `TimeProvider` and truncated to whole seconds** before it is passed
  down. Once, so the predicate that refuses a claim and the timestamp that records one are the same
  instant. Truncated, because `datetime2(0)` **rounds**: the parameter inherits the column's type,
  so an untruncated value would judge a slot ended up to half a second early and store something
  the response then misreports. There is no BCL truncation helper; the ticks arithmetic is the
  idiom.

## Authentication and identity

- **ASP.NET Core Identity** for users and roles.
- **`UserManager` / `RoleManager` are never touched directly** — they sit behind
  `IUserIdentityService` / `IRoleIdentityService` ports shaped around what the
  application needs.
- **Identity's error codes are never leaked verbatim.** `DuplicateUserName`,
  `DuplicateEmail`, `InvalidUserName` and friends pass through a default-deny map
  before reaching Application, folding account-existence oracles into generic codes.
- **JWT access token only. No refresh tokens** — roughly an 8-hour session, re-login
  on expiry. Scoped decision, stated as such in the README.
- **The Identity user type lives in `Domain`**, which therefore takes a
  `Microsoft.Extensions.Identity.Stores` package reference. The layering rule reads
  "Domain depends on nothing except Identity's store abstractions".

  > *Settled during planning.* `AppUser : IdentityUser<…>` has to live somewhere, and
  > "Domain → nothing" plus ASP.NET Identity cannot both hold literally. The
  > alternative was keeping the Identity user in Infrastructure and having domain
  > entities carry a bare `UserId`; we chose navigation properties that work.

- **Options are bound to validated `IOptions` types** (`JwtOptions`, seeding options)
  with `.ValidateOnStart()`, so the app refuses to boot on bad configuration.
- **Secrets live in Azure App Service Application Settings** (`Jwt__SigningKey`,
  `Seed__AdminEmail`, `Seed__AdminPassword`) and in `dotnet user-secrets` locally.
  Never in `appsettings.json`, never in git. No Key Vault — more machinery than this
  project justifies.

- **Identity keys are `int`**, not the default string GUID: slots reference their booker
  by a foreign key, and the default would put an `nvarchar(450)` column on the table the
  booking path writes to. `AppDbContext` therefore supplies all three
  `IdentityDbContext<AppUser, IdentityRole<int>, int>` arguments; the one-argument form
  compiles and silently gives string keys.
- **`AddIdentityCore`, never `AddIdentity`.** `AddIdentity` calls `AddAuthentication`
  internally and makes Identity's cookie scheme the default, which in a JWT-only API
  outranks the bearer scheme and turns every `[Authorize]` endpoint into a redirect to a
  login page that does not exist.
- **Identity's default password policy is kept unchanged**, so there is one source of
  truth for it. The visible consequence: a weak password comes back as a `ProblemDetails`
  carrying Identity's own codes, not as a field-keyed `ValidationProblemDetails`.
- **Two Identity *user* options are set:** `RequireUniqueEmail`, and an empty
  `AllowedUserNameCharacters`. The user name here is the email address, and Identity's
  default character allow-list is narrower than what an address may legally contain - an
  apostrophe would fail a check the request validator already covers.
- **Claims use short names - `sub`, `email`, `role` - declared once in
  `Application/Auth/AppClaimTypes`** and used by both the issuing adapter and the API host.
  `MapInboundClaims` is off, and `TokenValidationParameters.RoleClaimType` is set to the
  same `role`. Left at its default, `[Authorize(Roles = ...)]` looks for a WS-Federation
  URI, finds none, and answers 403 to a token that visibly contains the right roles.
- **The signing key is base64 and at least 32 bytes, validated at startup.** HS256 with a
  shorter key throws on the first token issued, which is a 500 on someone's first login
  rather than an application that refused to boot.
- **`ClockSkew` is 30 seconds.** Skew absorbs a difference between the issuing and the
  validating clock; here they are the same process, so the five-minute default only
  extends every token's real lifetime.
- **Login reports one error code for every failure, and hashes the submitted password even
  when no account matches.** One code hides which half was wrong; without the hash, the
  response time would answer it anyway.
- **Registration reports `EmailAlreadyRegistered` explicitly.** This diverges from the
  reference project, which folds duplicate-user and duplicate-email into a generic code. A
  registration endpoint cannot hide that an address is taken - the request fails either
  way - so the vague code buys nothing and leaves the form unable to say what to fix.
- **Unmapped Identity codes are dropped, and a result with everything dropped carries
  `UnexpectedError`.** An empty error list is not an option: `OperationResult.Failure`
  rejects one. Mapped codes are de-duplicated, because a duplicate registration fails as
  `DuplicateUserName` *and* `DuplicateEmail` when the user name is the email.
- **Registration returns 201 with no `Location` header and issues no token.** Nothing in
  this API exposes a user as a resource, and the client logs in as a separate step.
- **`GET /api/auth/me` reads claims only and touches no database.** Everything it returns
  is already in the token; reading it back is also how a claim-type mismatch becomes
  visible.

## Real-time

- **Azure SignalR Service** in Azure, via `AddAzureSignalR()` when its connection
  string is present; plain in-process SignalR otherwise. Single instance makes the
  fallback viable.
- **One SignalR group per room.** The hub lives in `Api`; Application triggers
  broadcasts through an `IScheduleNotifier` port, so no layer below `Api` references
  SignalR.
- **Broadcast after the write, never before.** The payload carries slot identity and
  new status only — no personal data.
- The hub authenticates from the JWT, which browsers deliver as an `access_token`
  query-string parameter (WebSockets cannot set headers), read server-side in
  `JwtBearerOptions.Events.OnMessageReceived`. **The handler accepts it only when the path starts
  with `/hubs`**, so the REST API never takes a credential from a query string.

  > *The exposure, stated so it can be defended rather than hoped past.* A query string can leak
  > through browser history, `Referer` headers and access logs. The first two do not apply here —
  > the URL is opened by a script, never navigated to — so the real exposure is **logs**, server and
  > proxy side; it is inside TLS on the wire. On the deployed app the socket terminates at Azure
  > SignalR, so the browser carries the *service's* token there and this application's JWT travels
  > on negotiate as an ordinary `Authorization` header; the query-string path is what the
  > in-process fallback and an SSE downgrade use. The residual risk is real: an eight-hour token in
  > an access log is an eight-hour credential. The proper hardening is a short-lived hub ticket
  > minted at negotiate, which is a second token type and did not fit this scope. Note also that
  > the same JWT already sits in `localStorage` by the decision under *Frontend*, which is the
  > larger hole of the two.

- **Only a real state change is announced.** The notifier fires on `SlotClaimOutcome.Claimed`
  alone, never on `AlreadyClaimedByCaller`. Both are a *successful* `OperationResult`, which is
  exactly why "announce whenever the booking succeeded" is the wrong reading: it compiles, passes
  every HTTP test in the solution, and emits an event each time a caller re-posts a booking they
  already hold — when the row did not move. The guard also short-circuits ahead of the room read,
  so a replay costs no extra round trip either.
- **A failed announcement never fails a committed booking.** `SignalRScheduleNotifier` catches,
  logs at error, and returns; the caller still receives 201. The accepted consequence is that other
  viewers stay stale until they read the schedule again. Letting it bubble instead would answer the
  one caller who *did* get the room with a 500 describing a booking that exists and is theirs —
  the more damaging of the two lies. The catch lives in the adapter because that is the layer which
  owns the transport and has a logger; `IScheduleNotifier` states "implementations must not throw"
  as the contract that makes a single catch sufficient. A second catch in Application would have to
  be **silent**, that layer having no logger without a new package, and a silent swallow is worse
  than the contract it duplicates.
- **`IScheduleNotifier` takes no `CancellationToken`**, against the grain of every other
  asynchronous method here. The token available at the call site is the booking request's, and that
  one is cancelled when the caller's connection drops — so forwarding it would let a booker closing
  their tab withhold the announcement from everybody else, for a write that already happened.
- **The room id is read separately, and the claim statement is untouched.** `TryClaimAsync` reports
  a row count rather than a row, and a per-room group needs the room. A second port method,
  `GetRoomIdAsync`, pays one primary-key seek on the winning path and leaves phase 5's graded
  `UPDATE` byte-for-byte as verified. Rejected: folding the read into the claim (edits the statement
  the README quotes), `UPDATE … OUTPUT INSERTED.RoomId` (hand-maintained SQL on the graded path),
  and taking the room from the request — unverified client data steering a broadcast, where a
  mismatched id delivers the event to another room's viewers.
- **The payload is `{ roomId, slotId }` and nothing else.** No booker identity: `requirements.md`
  §5 says a caller learns that a slot is taken and separately whether they took it, and an event
  naming the booker would broadcast to the whole room precisely what the schedule endpoint
  withholds. No status flag: the event's name is the status, and free-to-booked is the only
  transition there is. `roomId` rides along even though the group implies it, because a client
  watching two rooms receives both through one handler and the client library does not say which
  subscription delivered an event.

  > *Accepted consequence.* A booker's **second tab** shows the slot as booked but not as *theirs*
  > until it refetches. The only fixes are an identity field the visibility rule forbids, or
  > per-user targeting for a cosmetic difference — neither earns its keep.

- **Group membership belongs to the connection, so a reconnect must re-subscribe _and_ refetch.**
  A reconnected client is in no groups at all, and anything announced while it was away was never
  delivered and is never replayed. Re-subscribing alone fixes the first half and silently leaves
  the second, which is the failure most likely to pass a casual demo. Verified rather than
  reasoned: a client that reconnects without re-subscribing receives nothing, and receives events
  again the moment it does.
- **Room create, update and delete do not broadcast.** §6 is about slot status; the room list is
  not live, and an administrator's change is picked up on the next read.
- **The hub's JSON naming policy is pinned explicitly.** SignalR's JSON protocol is configured
  independently of MVC's, so the API's camelCase does not carry over by construction. Measured
  rather than assumed: with `AddJsonProtocol` removed the hub still emits `{ roomId, slotId }`, so
  the call is a pin against the two defaults ever diverging, not a fix for a live defect. Kept
  because what it prevents is silent and runtime-only — `SlotId` arriving where the browser reads
  `slotId`, with nothing failing server-side.

## Frontend

- **React + TypeScript + Tailwind**, built by Vite into the API's `wwwroot`.
- **Vite owns `wwwroot` entirely.** The build emits into
  `src/backend/MeetingRooms.Api/wwwroot` with `emptyOutDir: true`, and **nothing in that
  directory is tracked** — it is build output in its entirety. Static files that must
  ship (favicon, `robots.txt`) go in `src/frontend/public/`, which Vite copies into the
  output, so no hand-maintained file ever lives in a directory a build tool clears.
- **Tailwind 4 through `@tailwindcss/vite`**, not PostCSS. No `tailwind.config.js`, no
  `postcss.config.js`, no `content` globs — the plugin plus `@import "tailwindcss";` in
  one CSS file is the whole wiring, and source detection is automatic. Worth stating
  because Tailwind 3 habits look for a config file that no longer exists.
- **The dev server proxies `/health`, `/api` and `/hubs` to port 5000**, with `ws: true`
  on the hub route. Same-origin in production, proxied in development, so application
  code never carries a base URL for either.
- **`tsc --noEmit` runs before `vite build`.** Vite transpiles per file through
  Rolldown/Oxc and never typechecks, so without that step a type error reaches a deploy
  unannounced. `verbatimModuleSyntax` and `isolatedModules` are set for the same reason:
  they restrict the code to the subset where per-file transpilation and whole-program
  compilation provably agree.
- **No ESLint, no Prettier, no frontend tests.** Out of scope at this deadline; the one
  mandated automated test is the concurrency test.
- **No Redux and no React Query.** Auth state in a React context; everything else is
  local component state. Revisit only if a screen proves it insufficient.
- **The JWT is held in `localStorage`**, with the XSS exposure written into the README
  rather than hidden. In-memory storage is safer but needs refresh tokens, which are
  out of scope.
- **`@microsoft/signalr` 10.0.11**, pinned exactly like every other dependency here rather than
  left on npm's default caret — `npm ci` installs the lockfile either way, but a caret lets a later
  `npm install` drift the client off the 10.0.x server line with nothing in the diff saying why.
  It cost ~60 kB raw on the bundle (222 → 282 kB). Its Node-only transitive packages — `ws`,
  `node-fetch`, `tough-cookie` and friends — are **not** bundled, but the client's dead Node branch
  names them in dynamic `require` calls, so they are greppable in the output and that is expected
  rather than a leak.
- **The phase 6 live panel is scaffolding, and phase 7 deletes it.** It holds its token in
  component state rather than `localStorage`: the decision above belongs to phase 7's auth context,
  and a throwaway probe should not be the first thing to implement a standing decision.
- **A SignalR client handler must not return a value.** TypeScript permits returning one where
  `void` is expected, and the client reads any returned value as a result for an invocation —
  logging `Result given for 'slotbooked' method but server is not expecting a result`.
  `scheduleConnection.ts` therefore wraps the caller's handler in a block, so the class of bug is
  removed rather than avoided by convention. Found by running a client whose handler was a one-line
  arrow.

## Testing

- **The mandated concurrency test** (assignment #6): xUnit + `WebApplicationFactory`
  against a real SQL Server, N requests released off one `TaskCompletionSource`,
  asserting exactly one 201, N−1 409, zero 5xx, and one booked row.
- **A root `docker-compose.yml`** provides that SQL Server so the reviewer runs the
  test with one documented command. No Testcontainers dependency.
- **Plus unit tests on the pure slot-grid generator.** Nothing else — remaining
  verification is manual smoke testing through Scalar, recorded honestly in the phase
  docs.
- **The mutation probe is the real exit criterion** for the booking phase: remove
  `&& s.BookedByUserId == null`, watch the test go red, restore it.

- **A green concurrency test does not establish that the requests overlapped, and neither does the
  mutation probe.** Twenty *serial* requests produce exactly the same one-201-and-nineteen-409s as
  a real race, and with the free-slot predicate removed every `UPDATE` matches whatever the
  ordering — so the probe reddens identically either way. The test therefore **measures** overlap:
  the last request was issued before the first came back, so all twenty were in flight at one
  instant. This is `docs/plan.md` risk 2, and that assertion is the only thing that retires it.

  > *Found in phase 5 by probing rather than reasoning.* Removing
  > `TaskCreationOptions.RunContinuationsAsynchronously` was expected to serialise the waiters and
  > did not — each continuation runs inline only until its HTTP call suspends, after which the
  > releasing thread moves on. A comment claiming otherwise was corrected rather than left
  > standing. Staggering the requests 50 ms apart is what reddens the overlap assertion.

- **The integration tests run against their own catalog, derived rather than written down.**
  `SqlConnectionStringBuilder` takes whatever connection string is configured and replaces only
  `InitialCatalog`. No credential enters the repository, the same code works in the dev container
  and on a reviewer's machine, and the development database cannot be reached by accident —
  which matters because bookings cannot be cancelled, so a run against it would permanently
  consume demo slots.
- **`appsettings.Tests.json` holds fixtures, not secrets.** A signing key and a seeded
  administrator used only by the test host against a local test database, plus a localhost
  connection-string fallback matching the root `docker-compose.yml`. The real values stay in
  `dotnet user-secrets` and App Service application settings.
- **Window-dependent tests insert the slot they need through `AppDbContext`**, rather than skipping
  when the generated grid happens to contain no expired slot. Deterministic at any hour, with no
  substituted clock. Writing a *free slot* row is not a booking — the standing rule is only that
  nothing but `TryClaimAsync` writes `BookedByUserId`.
- **Which claim outcomes announce is pinned by unit tests over hand-written fakes.**
  `BookingServiceNotificationTests` covers `Claimed` (exactly one announcement, carrying the room
  the repository reported), `AlreadyClaimedByCaller` (none, and no room read either), the three
  refusals, and the unreachable null-room-id branch. No mocking package: the port has five members
  and two of them matter. The probe is the point — rewriting the guard as "announce whenever the
  booking succeeded" reddens exactly one test.
- **No end-to-end hub test in the solution.** One would need the SignalR .NET client package and a
  connection over the test server, and it is timing-sensitive on a four-day deadline. The routing
  it would prove was exercised instead by a throwaway client against the running application, and
  by the two-browser check — both recorded in the phase 6 document rather than committed.
- **CI runs the unit project only.** `.github/workflows/deploy.yml` still builds the whole solution,
  so a compile break anywhere fails there; but the concurrency project needs a real SQL Server the
  runner does not have. Assignment #6 asks for a test in the repository that the reviewer can run,
  which it is, with two documented commands. A database container and a timing-sensitive test on
  the *deploy* path would buy a green tick at the cost of a deploy a slow container can block.

## Deployment and operations

- **Azure baseline:** App Service Basic B1, Always On, single instance, WebSockets
  enabled; Azure SQL on a provisioned tier. Single instance is what makes
  migrate-on-startup safe and in-process SignalR a real fallback.
- **Migrations run via `Database.Migrate()` at startup through phase 3**, then flip to
  being applied manually from the dev container. The flip needs:
  - the Azure SQL connection string in `dotnet user-secrets` under a **distinct key**
    (`ConnectionStrings:AzureSql`) — the container's
    `ConnectionStrings__DefaultConnection` environment variable outranks user-secrets
    and would otherwise silently redirect the migration to the local database;
  - `<server>.database.windows.net` added to `.devcontainer/init-firewall.sh`, plus
    the host's public IP allowed on the Azure SQL server;
  - the server's connection policy left at **Default/Proxy** — under Redirect, clients
    connect to backend nodes on ports 11000–11999 and the allowlist stops working;
  - `.config/dotnet-tools.json` committed, pinning `dotnet-ef`;
  - a startup `GetPendingMigrationsAsync()` guard that refuses to start outside
    Development when migrations are pending, so "I forgot" fails loudly.

  > *Deferred 2026-09-13, at the phase 2/3 boundary.* The flip is **no longer scheduled**
  > for the phase 3 → 4 boundary. `Database.Migrate()` at startup stays in place until it
  > is actively replaced, which is safe for the reason already recorded above: the plan is
  > a single App Service instance. Revisit only if phases 3-5 finish with time to spare -
  > under a Monday-midday deadline, the flip is machinery competing with the graded core.
  >
  > The **dev-container half is already done**, so the flip costs no container rebuild
  > whenever it happens: `sql-meetingrooms-test-task.database.windows.net` is in
  > `init-firewall.sh` and verified reachable. What remains is Azure-side and code-side -
  > the portal firewall rule for the host IP, the `ConnectionStrings:AzureSql` secret, and
  > the pending-migrations startup guard.
  >
  > **One correction to the list above:** `.config/dotnet-tools.json` pinning `dotnet-ef`
  > is *not* only a prerequisite of the flip. Phase 3 needs the tool to author the first
  > migration at all (`dotnet ef migrations add`), so it is a phase 3 deliverable
  > regardless. It must be a **local** tool manifest rather than a global install:
  > `~/.dotnet/tools` lives on the container writable layer and does not survive a rebuild,
  > whereas the manifest is in the repo and the tool package restores into the
  > `~/.nuget/packages` named volume.
- **No `UseHttpsRedirection` middleware.** App Service terminates TLS at its front
  end, so the app sees plain HTTP; without forwarded-headers configuration the
  middleware can redirect in a loop. The platform's **HTTPS Only** setting performs
  the redirect before the request ever reaches us.
- **The dev container does not persist `~/.microsoft` or globally-installed dotnet
  tools.** `dotnet user-secrets` values and a global `dotnet-ef` survive a container
  restart but are lost on a rebuild. `.config/dotnet-tools.json` plus
  `dotnet tool restore` covers the tool; secrets have to be re-added by hand.
- **Roles and demo rooms are seeded in every environment**; the admin account is read
  from configuration and fails loudly outside Development if absent.
- **Package installs are performed by the repository owner, not by Claude.** Claude
  supplies exact commands with pinned versions (the 10.0.x line, matching SDK 10.0.401
  and the existing `Microsoft.AspNetCore.OpenApi 10.0.12`) and verifies afterwards via
  `dotnet restore` / `dotnet build`.
- **The frontend is built by the deploy workflow, not by an MSBuild target.**
  `npm ci --ignore-scripts && npm run build` runs **before** `dotnet publish`, because
  MSBuild globs `wwwroot` at publish time and would otherwise package an empty one. It
  stays out of the `.csproj` so a local `dotnet build` never requires a node toolchain.
- **npm dependencies are pinned exactly and installed behind a release-age cooldown.**
  `npm install --ignore-scripts --min-release-age 3`: exact versions in `package.json`, a
  committed `package-lock.json`, and a three-day cooldown that keeps anything published in
  the last 72 hours out of the tree — the window in which a compromised publish is usually
  caught and unpublished. Three days rather than seven because React 19.3.0 and Vite 8.3.0
  were themselves three to four days old and a longer cooldown cannot resolve them; the
  cooldown is why `enhanced-resolve` and `nanoid` sit one patch behind latest.
  `--ignore-scripts` costs nothing here: the only package in the tree declaring a lifecycle
  script is `fsevents`, which is macOS-only, because every native toolchain involved
  (Rolldown, Tailwind's Oxide, TypeScript 7) ships per-platform binaries as optional
  dependencies instead. CI runs `npm ci`, which installs the locked tree by integrity hash
  and neither re-resolves versions nor re-applies the cooldown — so the tree deployed is
  the tree reviewed. `npm audit` reported zero vulnerabilities; `npm audit signatures`
  could not run, because the container's firewall blocks `tuf-repo-cdn.sigstore.dev`.
- **Startup applies migrations, then seeds roles, then the administrator** - in that
  order, because a role cannot be seeded into a database with no tables and an
  administrator cannot be granted a role that does not exist. Every step is idempotent, so
  a restart applies nothing.
- **`/health` reports database reachability** as well as whether a connection string is
  configured, which separates "absent" from "present and wrong" without reading Azure
  logs. It goes through a port rather than injecting `AppDbContext` into a controller, and
  carries a three-second timeout of its own so that a database that is down cannot make
  the health endpoint hang behind the retry strategy.

---

## Resolved during planning

Previously open, now closed:

- **Playwright for the automated test?** No. The assertion is "exactly one row
  exists" and the stimulus is N simultaneous HTTP requests; a browser adds a
  rendering engine and a node toolchain between us and that assertion. xUnit +
  `HttpClient`.
- **Concurrency approach chosen on predicted load?** Wrong axis. Load does not
  determine correctness — every candidate mechanism is correct at one request per
  second and at a thousand. The data model decided it.
- **Where do JWT options live?** Azure App Service Application Settings in
  production, `dotnet user-secrets` locally.
- **When does frontend work start?** Split: the build pipeline goes early (phase 2,
  while it is one page), the screens go late (phase 7, once the API contract is
  stable). Building screens against an API still being designed means writing them
  twice.
- **Unit tests at all?** Yes, but narrowly — the mandated concurrency test plus the
  slot-grid generator.
- **Frontend architecture deferred?** Decided early instead, because it was cheap: no
  Redux, no React Query, auth in context. Routing details remain a phase-7 concern.

## Still open

- Frontend routing structure and screen breakdown — deliberately deferred to phase 7.
- The exact `dotnet ef database update` invocation for the post-phase-3 migration
  flip; a ten-minute detail, not a design decision.

*Closed by phase 4:* room fields beyond name and capacity — there are none, and the name is
not unique (see *Persistence*).

*Closed by phase 5:* the retry-after-commit case. A caller who already holds the slot is answered
201 rather than 409, which makes booking idempotent per user — see *Booking and concurrency* for
the reasoning and for what it forces on the mandated test. Also closed: whether the bookings lists
are paginated (they are not — see *API surface and errors*) and how CI relates to a test that needs
a real SQL Server (it does not run it — see *Testing*).
