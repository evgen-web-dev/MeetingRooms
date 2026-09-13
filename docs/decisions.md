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

## Validation

- **FluentValidation**, with validators applied in **one place — a global MVC action
  filter**, not per endpoint.

  > *Changed during planning.* This previously read "middleware". Middleware runs
  > before MVC model binding, so at that point there is no bound DTO to hand a
  > validator — the interception point that has the action arguments is an
  > `IAsyncActionFilter`. Same "one place, not per endpoint" outcome, correct layer.
  > (A PHP/Laravel false friend: there, middleware sees a parsed request.)

## Persistence

- **SQL Server** — Azure SQL Database in production, a SQL Server container locally.
- **`AppDbContext` always sits behind `IUnitOfWork` / repositories**, never used
  directly from application code. Seeders may be an exception.
- **`IUnitOfWork` is kept but minimal.** It is load-bearing only where a use case
  performs more than one write — registration (create user + assign role). EF already
  wraps a single `SaveChangesAsync` in an implicit transaction, so the explicit
  envelope elsewhere is ceremony.
- Explicit transactions are wrapped in EF's execution strategy, because
  `EnableRetryOnFailure` is required for Azure SQL and otherwise rejects
  user-initiated transactions.
- **`IEntityTypeConfiguration<T>` + `modelBuilder.ApplyConfigurationsFromAssembly`**,
  with zero data annotations on entities.
- **Times are stored UTC as `datetime2(0)`** and displayed in Europe/Stockholm.
  Note: SQL Server's `timestamp` is a synonym for `rowversion`, not a date/time type.

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
  `JwtBearerOptions.Events.OnMessageReceived`.

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

- Room fields beyond name and capacity, if any prove useful.
- Frontend routing structure and screen breakdown — deliberately deferred to phase 7.
- The exact `dotnet ef database update` invocation for the post-phase-3 migration
  flip; a ten-minute detail, not a design decision.
