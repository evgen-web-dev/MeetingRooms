# Phase 3 — Persistence + identity

**Satisfies:** assignment #3 (auth + roles). Lays the persistence foundation phases 4–5
build on, and is the last phase before the graded core.

## Context

Phases 1 and 2 built a deployable shell: four projects, `OperationResult`, the
`ProblemDetails` error contract, a global exception handler, Scalar, an empty SignalR hub,
and a CI-built React page. **Nothing in the repository touches a database yet** — there is
no EF Core reference, no `AppDbContext`, no user, no token.

Phase 3 is where the app stops being a shell. It brings in EF Core + SQL Server, ASP.NET
Core Identity, the first migration, migrate-and-seed on startup, and register/login issuing
a JWT. Phase 4 (rooms and slots) and phase 5 (the booking write, the graded core) both sit
on top of `AppDbContext`, `IUnitOfWork`, and a user id in a claim — so every choice here is
load-bearing for them, and any of it that is wrong gets discovered in the phase that cannot
afford surprises.

Settled at the phase 2/3 boundary and confirmed before planning:

- The first migration carries **Identity tables only**. `Room` and `Slot` arrive with phase
  4's migration, keeping the phase boundary in `docs/plan.md` intact.
- **FluentValidation lands now.** Register and login are the first payloads worth rejecting,
  so the global action filter and the `ValidationProblemDetails` contract get proved while
  there are only two DTOs to redo if the shape is wrong.
- The dev container work is **done** (`docs/devcontainer-changes.md`): `~/.microsoft` is a
  named volume, so `dotnet user-secrets` now survives a rebuild.
- The **migration flip is deferred** (`docs/decisions.md`, 2026-09-13). `Database.Migrate()`
  at startup stays, and phase 4 no longer inherits a flip.

## Out of scope

Rooms, slots, booking, the schedule read, the hub's per-room groups, frontend auth screens
(phase 7), refresh tokens, password reset, email confirmation, account lockout, admin user
management, and the migration flip. Also out of scope: a 403 proof — phase 3 has no
admin-only endpoint to be forbidden from, so 403 is first exercised by phase 4's room CRUD.
`/api/auth/me` returning `["Admin"]` for the seeded admin is phase 3's evidence that the
role claim survives the round trip.

---

## Packages

Versions verified against nuget.org on 2026-09-13. All Microsoft 10.0.x packages match the
SDK (10.0.401) and the existing `Microsoft.AspNetCore.OpenApi 10.0.12`.
`docs/decisions.md` puts the install itself in the owner's hands.

| Project | Package | Version | Why |
|---|---|---|---|
| Domain | `Microsoft.Extensions.Identity.Stores` | 10.0.12 | `IdentityUser<int>` — the one dependency the layering rule allows Domain |
| Infrastructure | `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.12 | provider |
| Infrastructure | `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 10.0.12 | `IdentityDbContext`, EF stores |
| Infrastructure | `Microsoft.IdentityModel.JsonWebTokens` | **8.19.2** | `JsonWebTokenHandler` for issuance — see the NU1605 note |
| Api | `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.12 | validation side |
| Api | `Microsoft.EntityFrameworkCore.Design` | 10.0.12 | `dotnet ef` needs it in the **startup** project; `PrivateAssets="all"` |
| Api | `FluentValidation.DependencyInjectionExtensions` | 12.1.1 | pulls `FluentValidation` 12.1.1; validators live in Api |
| tool | `dotnet-ef` | 10.0.12 | local manifest, not a global install |

**8.19.2 is deliberate, not the latest (8.22.0).** `Microsoft.AspNetCore.Authentication.JwtBearer`
10.0.12 pins `Microsoft.IdentityModel.Protocols.OpenIdConnect` **8.19.2**; matching it keeps
one IdentityModel line in the graph instead of lifting half of it to 8.22.0 for nothing.

**On the NU1605 that phase 1 predicted:** `Microsoft.Azure.SignalR` 1.33.1 → `Azure.Identity`
1.11.4 → `Microsoft.IdentityModel.Abstractions` 6.35.0, while JwtBearer wants the 8.19.2
line. Highest-wins resolves that to 8.19.2 and there is **no downgrade warning** — the
warning only appears if something adds a *direct* reference below 8.19.2. So: no pin below
8.19.2 anywhere. If a warning appears anyway, the fix is an explicit direct reference at the
higher version, not `NoWarn`.

FluentValidation 12 is a major version (min .NET 8; removes `CascadeMode.StopOnFirstFailure`,
`Transform`, `InjectValidator`). Checked against the 12.0 upgrade guide and the v12 source:
`AddValidatorsFromAssembly` and `Cascade(CascadeMode.Stop)` both survive, which is everything
this phase uses.

---

## The shape of the work

### Layering

```
Domain          AppUser : IdentityUser<int>, Roles constants
Application     ports (IUserIdentityService, IRoleIdentityService, IAccessTokenService,
                IUnitOfWork), AuthService, DTOs, AuthErrorCodes, JwtOptions, SeedOptions
Infrastructure  AppDbContext, UnitOfWork, Identity port implementations, the Identity
                error-code map, JsonWebTokenService, seeders, migrations
Api             AuthController, JWT bearer wiring, validation filter + validators,
                the OpenAPI bearer scheme, claims helper
```

New files follow the reference project's layout closely enough that
`docs/reference/BookingApp` remains readable as prior art. Four things depart from it, each
for a stated reason below: the transaction shape, the claim types, the duplicate-email code,
and dropping the refresh-token subsystem (already a standing decision).

### Identity configuration

```csharp
services.AddIdentityCore<AppUser>()
    .AddRoles<IdentityRole<int>>()
    .AddEntityFrameworkStores<AppDbContext>();
```

**`AddIdentityCore`, never `AddIdentity`.** *(.NET-specific trap.)* `AddIdentity` calls
`AddAuthentication` internally and sets the default scheme to Identity's **cookie** scheme.
In a JWT-only API that silently outranks the bearer scheme and every `[Authorize]` endpoint
starts answering with a redirect to a login page that does not exist. `AddIdentityCore` adds
`UserManager`, the password hasher and the validators, and no authentication scheme at all.

No `SignInManager`: `UserManager.CheckPasswordAsync` is the whole login check, and lockout is
explicitly out of scope in `docs/requirements.md`.

**Identity's default password policy is kept as-is** (6+, digit, lower, upper,
non-alphanumeric). Zero configuration and one source of truth; the validator does not
restate it — see *Validation* below.

### `AppDbContext` and the execution strategy

```csharp
public sealed class AppDbContext : IdentityDbContext<AppUser, IdentityRole<int>, int>
```

Three generic arguments, not one — `IdentityDbContext<AppUser>` would quietly give every
table `nvarchar(450)` string keys, and `docs/plan.md`'s schema has `BookedByUserId` as an
int FK.

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"),
        sql => sql.EnableRetryOnFailure()));
```

`EnableRetryOnFailure` is required for Azure SQL (`docs/decisions.md`) — and it is what
forces the transaction shape below.

### `IUnitOfWork` — a deliberate divergence from the reference

The reference exposes `BeginTransactionAsync` / `CommitAsync` / `RollbackAsync` and expects
every caller to write the try/catch envelope. **That shape is incompatible with
`EnableRetryOnFailure`:** `BeginTransactionAsync` throws

> The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support
> user-initiated transactions.

unless the whole unit of work runs inside `Database.CreateExecutionStrategy().ExecuteAsync(...)`.
So the port is one method that cannot be used wrongly:

```csharp
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="operation"/> inside the provider's execution strategy and one
    /// transaction, saving and committing on success and rolling back on failure. The
    /// delegate may run more than once: a transient fault rolls back and re-executes it.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
```

Registration is the only caller in this phase, and per `docs/decisions.md` the only caller
this project is expected to need: creating the user and assigning its role are two writes,
and without the envelope a failed `AddToRoleAsync` leaves a user with no role — able to log
in, and authorized for nothing.

*(Identity detail this depends on: `UserManager` saves through the store on every call —
`AutoSaveChanges` defaults to true. Both writes go through the same scoped `AppDbContext`,
so they enlist in the same transaction and the envelope holds.)*

Phase 5's booking write does **not** use this: `ExecuteUpdateAsync` is a single statement
that needs no transaction of its own.

### JWT issuance and validation

`JwtOptions` (Application) binds `Jwt` with `.ValidateOnStart()`:

| Key | Lives in | Notes |
|---|---|---|
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenLifetimeMinutes` | `appsettings.json` | not secrets; **480 minutes**, from `docs/requirements.md` §8 |
| `Jwt:SigningKey` | user-secrets locally, App Service Application setting in Azure | never in git |

Validation at startup goes one step past the reference: the key must be **valid base64 and
at least 32 bytes**. HS256 with a shorter key throws at the first token issuance — a runtime
500 on the first login rather than a refusal to boot.

**On the 480 minutes**, re-examined and reaffirmed during this planning session. Short access
tokens (5–15 minutes) are standard practice *because a refresh token exists* to hide the
expiry and to carry the revocation that a JWT structurally cannot. With no refresh token —
`docs/decisions.md`, scoped deliberately — the access token's lifetime **is** the session
length, so shortening it does not buy a smaller blast radius so much as evict a user
mid-task. Two consequences are written into the README rather than left implicit: the token
cannot be revoked before it expires, and phase 6's hub connection authenticates from the same
token, so a short lifetime would surface as real-time silently dying on a page left open.

Claims are written with **short names**, and this is the other deliberate divergence:

```csharp
new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
new Claim(JwtRegisteredClaimNames.Email, user.Email),
// one "role" claim per role
```

```csharp
bearerOptions.MapInboundClaims = false;          // no legacy JWT → WS-Federation remapping
bearerOptions.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true,            ValidIssuer = jwt.Issuer,
    ValidateAudience = true,          ValidAudience = jwt.Audience,
    ValidateIssuerSigningKey = true,  IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],   // never let the token pick
    ValidateLifetime = true,          ClockSkew = TimeSpan.FromMinutes(2),
    NameClaimType = JwtRegisteredClaimNames.Sub,
    RoleClaimType = "role",
};
```

The reference wrote `ClaimTypes.Role` / `ClaimTypes.NameIdentifier`, which are 60-character
WS-Federation URIs baked into every token for no benefit. Short names cost one thing, and it
is the trap worth naming: **`[Authorize(Roles = …)]` resolves roles through
`RoleClaimType`**, so omitting that line produces a token that visibly contains the right
roles and an API that answers 403 to every one of them. Phase 4 would inherit that as a
mystery; `/api/auth/me` in this phase is what proves it.

Token expiry comes from the injected `TimeProvider`, not `DateTime.UtcNow` — the composition
root owns the clock (`docs/decisions.md`); the reference violates this.

### Errors

`AuthErrorCodes` (Application):

| Code | Status | Notes |
|---|---|---|
| `InvalidEmailOrPassword` | 401 | **one literal for both** unknown email and wrong password — the login oracle stays shut |
| `EmailAlreadyRegistered` | 409 | see below |
| `PasswordTooShort`, `PasswordRequiresDigit`, `PasswordRequiresLower`, `PasswordRequiresUpper`, `PasswordRequiresNonAlphanumeric`, `PasswordRequiresUniqueChars` | 400 | Identity's own codes, passed through |

Each gets a row in `Api/Errors/ErrorStatusCodeMapper.cs`, which already exists and already
falls back to 500 for an unmapped code. The Identity strings are declared **once** as
constants in Application so the default-deny map and the status mapper cannot drift.

**`EmailAlreadyRegistered` is explicit, and that departs from the reference,** which folds
`DuplicateEmail` / `DuplicateUserName` / `InvalidUserName` into one vague code to hide
account existence. The reasoning does not survive contact with a registration endpoint: the
request fails whether the code is specific or vague, so the oracle exists either way and the
vague code only leaves phase 7's form unable to say what went wrong. Login is different —
there the collapse genuinely hides which half was wrong, and it stays.

The **default-deny map** is otherwise kept from the reference and keeps its most valuable
property: an Identity code that is not in the map is **dropped entirely**, so a future
Identity version cannot open a leak by adding one. Its one empirical note comes along too —
`AddToRoleAsync` re-runs user validation, so username/email codes can come back from a role
assignment.

**401 and 403 keep the framework's empty body.** JWT bearer answers an unauthenticated
request with 401 + `WWW-Authenticate` and no payload, and a role miss with a bare 403. The
status is the contract, the frontend branches on it, and overriding correct framework
behaviour to add a body is machinery. Recorded in `docs/decisions.md` as a decision, so its
absence does not read as an oversight.

### Validation

FluentValidation validators live in `Api/Validators/Auth/`, registered by assembly scan, and
run through one global `IAsyncActionFilter` (`docs/decisions.md`: one place, not per
endpoint). Ported from the reference with the two fixes `general-ideas.md` names —
`MakeGenericType` results cached in a static `ConcurrentDictionary`, and no `new Regex(...)`
inside a `.Must()`.

The response shape comes from **MVC's own factory**, matching phase 1's decision:

```csharp
var problemDetails = _problemDetailsFactory.CreateValidationProblemDetails(
    context.HttpContext, modelState);            // modelState built from the FV failures
context.Result = new BadRequestObjectResult(problemDetails);
```

Rules are payload-only, per `general-ideas.md`'s test: `RegisterRequest` → email `NotEmpty`
+ `EmailAddress` + `MaximumLength(256)`, password `NotEmpty` + `MaximumLength(128)`;
`LoginRequest` → the same minus the length caps. **The password policy is deliberately not
restated here.** It is Identity's, and mirroring it would mean maintaining the same rules in
two places forever. The consequence, stated rather than hidden: a weak password comes back
as a 400 `ProblemDetails` carrying `PasswordTooShort`, not as a field-keyed
`ValidationProblemDetails`.

### Endpoints

`AuthController` at `api/auth`, explicit route string, `CancellationToken` on every action,
`Task<ActionResult<T>>` return types:

| Endpoint | Success | Failure |
|---|---|---|
| `POST /api/auth/register` | 201 `{ id, email }` | 409 `EmailAlreadyRegistered`, 400 policy codes, 400 validation |
| `POST /api/auth/login` | 200 `{ accessToken, expiresAtUtc }` | 401 `InvalidEmailOrPassword`, 400 validation |
| `GET /api/auth/me` | 200 `{ id, email, roles }` | 401 (no token) |

Registration always assigns `Roles.User` — `docs/requirements.md` has no role choice and no
promote flow, so `RegisterRequest` carries no role field (the reference's does). Register
does **not** auto-issue a token; the client logs in.

`/me` reads the `ClaimsPrincipal` only — **no database round trip**. Everything it returns is
already in the token, and reading it back out is precisely the proof that issuance and
validation agree on claim types. The claims helper it uses
(`ClaimsPrincipalExtensions.GetUserId()`, throwing on a missing or malformed `sub`) is what
phases 4–5 use to attribute a booking.

### OpenAPI and Scalar

**This is convenience, not an enabler** — Scalar can always send an `Authorization: Bearer …`
header typed in by hand, so an auth-gated endpoint is testable without any of it. What the
document changes:

- *Without* a declared scheme, Scalar's auth panel has nothing to populate, so the token is a
  manual header **on every request**, and the published document does not record which
  endpoints need one.
- *With* it, the token is pasted once into the Authorize control and attached to the
  operations that declare the requirement. `AddPreferredSecuritySchemes` only preselects a
  scheme the document already defines, so it does nothing on its own.

Given `docs/decisions.md` commits to a reviewer driving the deployed API without cloning, the
~25 lines earn their place — but this is task 14 and **the first thing to cut** if the phase
runs long.

```csharp
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();   // components
    options.AddOperationTransformer<AuthorizedOperationTransformer>();   // per-operation requirement
});
// ...
app.MapScalarApiReference(options => { /* existing */ options.AddPreferredSecuritySchemes("Bearer"); });
```

The requirement is attached **per operation**, read from the endpoint's `IAuthorizeData`
metadata — not declared once at document level, which would claim `register` and `login` need
a token they explicitly do not.

*Version trap:* .NET 10's `Microsoft.AspNetCore.OpenApi` 10.0.12 depends on
**`Microsoft.OpenApi` 2.12.0**, whose API differs from the 1.x one nearly every blog sample
uses — `document.AddComponent(...)`, `IOpenApiSecurityScheme`, and
`OpenApiSecuritySchemeReference` in the requirement, not `OpenApiReference`. Both confirmed
present in the resolved assembly.

### Startup: migrate, then seed

```csharp
var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await IdentitySeeder.SeedRolesAsync(scope.ServiceProvider);            // all environments
    await IdentitySeeder.SeedAdminAsync(scope.ServiceProvider, app.Environment);
}
```

Order is not incidental: seeding a role into a database with no tables fails, and seeding the
admin before the roles exist assigns nothing. Both seeders are idempotent behind a cheap
existence check and throw with a reason rather than leaving a half-seeded database.

`SeedOptions` (`Seed:AdminEmail`, `Seed:AdminPassword`) is **not** `ValidateOnStart` —
absent credentials must be a warning in Development and a hard failure everywhere else
(`docs/decisions.md`), and `ValidateOnStart` cannot express that distinction. If the admin
already exists, the seeder ensures the role and **does not touch the password**.

Migration failure is left to throw. On App Service that is a crash loop with the reason in
the log stream, which is the correct loud failure: an app that booted without its schema
would answer every endpoint with a 500 and look like a code bug.

### `/health` gains one field

`HealthResponse` already reports whether a connection string is *configured*. Phase 3 adds
`DatabaseReachable`, from `Database.CanConnectAsync()`. Six lines, and after a deploy it
separates "the connection string is missing" from "it is there and wrong" without reading
Azure logs. Still a boolean — no server name, no error text.

---

## Tasks

Each is one commit; the tree builds after every one. Branch `phase/3-persistence-identity`,
from `develop`.

| # | Commit | Contents |
|---|---|---|
| 1 | `docs: add phase 3 plan` | this file |
| 2 | **(you)** `chore: add the persistence and identity packages` | the seven `dotnet add package` commands below, plus `.config/dotnet-tools.json` from `dotnet new tool-manifest` + `dotnet tool install dotnet-ef` |
| 3 | `feat(domain): add the Identity user and the role constants` | `AppUser`, `Roles` (`User`, `Admin`, `AllRoles`) |
| 4 | `feat(infrastructure): add the database context and its registration` | `AppDbContext`, `UnitOfWork`, `AddInfrastructurePersistence`, `UserSecretsId` on the Api csproj |
| 5 | `feat(infrastructure): add the initial Identity migration` | generated `Migrations/` + snapshot |
| 6 | `feat(application): add the auth ports, DTOs and error codes` | `IUserIdentityService`, `IRoleIdentityService`, `IAccessTokenService`, `IUnitOfWork`, `AuthErrorCodes`, `JwtOptions`, `SeedOptions`, request/response records |
| 7 | `feat(infrastructure): implement the Identity ports` | `UserIdentityService`, `RoleIdentityService`, `IdentityErrorCodesDefaultDenyMapper` |
| 8 | `feat(infrastructure): issue JWT access tokens` | `JsonWebTokenService` |
| 9 | `feat(application): add the registration and login use cases` | `AuthService`, `AddApplicationServices` |
| 10 | `feat(api): authenticate and authorize with JWT bearer` | `AddJwtAuthentication`, `UseAuthentication`/`UseAuthorization`, claims helper |
| 11 | `feat(api): add the auth endpoints` | `AuthController`, new `ErrorStatusCodeMapper` rows |
| 12 | `feat(api): validate requests through a global action filter` | `AsyncValidationFilter`, the two validators, `AddAppValidation` |
| 13 | `feat(api): seed roles and the admin account at startup` | seeders + the `Program.cs` migrate/seed block |
| 14 | `feat(api): declare the bearer scheme in the OpenAPI document` | document transformer (scheme) + operation transformer (per-endpoint requirement) + Scalar preferred scheme. Cuttable if the phase runs long |
| 15 | `feat(api): report database reachability from /health` | `HealthResponse` + controller |
| 16 | `docs: fold phase 3 decisions into decisions.md` | the standing decisions below, before the branch merges |
| 17 | `docs: record phase 3 outcome` | the Outcome section, after the deploy, on `develop` |

### Standing decisions to fold into `docs/decisions.md` (task 16)

Under *Authentication and identity*: int keys; `AddIdentityCore` and why; Identity's default
password policy kept, and the validator not restating it; short claim names with
`RoleClaimType = "role"`; base64 signing key validated for length at startup; register
assigns `Roles.User` and issues no token; `EmailAlreadyRegistered` explicit while login keeps
one literal; 401/403 keep the framework's empty body. Under *Persistence*: `IUnitOfWork` is
`ExecuteInTransactionAsync` because `EnableRetryOnFailure` forbids the reference's shape.
Under *Deployment and operations*: `/health` reports database reachability.

---

## What you do

1. `git checkout -b phase/3-persistence-identity` from `develop`.
2. The installs (task 2), from the repository root:

```bash
dotnet add src/backend/MeetingRooms.Domain package Microsoft.Extensions.Identity.Stores --version 10.0.12
dotnet add src/backend/MeetingRooms.Infrastructure package Microsoft.EntityFrameworkCore.SqlServer --version 10.0.12
dotnet add src/backend/MeetingRooms.Infrastructure package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 10.0.12
dotnet add src/backend/MeetingRooms.Infrastructure package Microsoft.IdentityModel.JsonWebTokens --version 8.19.2
dotnet add src/backend/MeetingRooms.Api package Microsoft.AspNetCore.Authentication.JwtBearer --version 10.0.12
dotnet add src/backend/MeetingRooms.Api package Microsoft.EntityFrameworkCore.Design --version 10.0.12
dotnet add src/backend/MeetingRooms.Api package FluentValidation.DependencyInjectionExtensions --version 12.1.1

dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.12
```

   Then I add `PrivateAssets="all"` to the Design reference by hand — `dotnet add package`
   does not.

3. Local secrets, once (they now survive a rebuild). A 32-byte base64 key:

```bash
openssl rand -base64 32
cd src/backend/MeetingRooms.Api
dotnet user-secrets set "Jwt:SigningKey" "<the base64 value>"
dotnet user-secrets set "Seed:AdminEmail" "<email>"
dotnet user-secrets set "Seed:AdminPassword" "<password meeting Identity's default policy>"
```

4. The migration, when I flag task 5:

```bash
dotnet ef migrations add InitialIdentity \
  --project src/backend/MeetingRooms.Infrastructure \
  --startup-project src/backend/MeetingRooms.Api
```

5. Make each commit when I flag the point.
6. **Before merging to `main`:** add `Jwt__SigningKey`, `Seed__AdminEmail` and
   `Seed__AdminPassword` in the portal under App Service → **Application settings** (not
   Connection strings). `README.md` already lists them as "since phase 3". Without them
   `ValidateOnStart` refuses to boot the deployed app.
7. Merge `phase/3-persistence-identity` into `develop` with `--no-ff`, then `develop` into
   `main`, which deploys. Report what `/health` and a login through Scalar say.

---

## Risks and fallbacks

| Risk | Fallback |
|---|---|
| The first real contact with **Azure SQL** happens on deploy — nothing here can reach it. A bad connection string or a missing firewall allowance is a crash loop | `Migrate()` is idempotent, so a corrected setting plus a restart retries cleanly. `/health`'s new flag distinguishes missing from wrong |
| `dotnet ef` runs the startup project to find the context, so the migrate/seed block might execute at design time | It does not: `HostFactoryResolver` aborts at `builder.Build()`, before that code. If a future SDK changes that, add an `IDesignTimeDbContextFactory<AppDbContext>` reading the env var |
| `NU1605` from the two IdentityModel lines | Documented above; resolves to 8.19.2 by highest-wins. If it appears, add an explicit direct reference at the higher version — never `NoWarn` |
| `EnableRetryOnFailure` rejects the transaction | Designed out: `ExecuteInTransactionAsync` is the only transaction path and it goes through the execution strategy |
| `RoleClaimType` left at its default, so roles are present but invisible to `[Authorize]` | `/api/auth/me` in this phase, and the admin login check in Verification, catch it before phase 4 depends on it |
| FluentValidation 12 breaks something the reference relied on | Checked: the APIs used all survive. Fallback is `11.11.0`, which changes no code here |
| Identity's default password policy frustrates a reviewer | It is Identity's documented default, and the seeded admin's password is chosen to satisfy it. Relaxing it is one options lambda if it proves annoying |

---

## Verification

Locally, before the merge:

- `dotnet build` — clean, **zero warnings** (this is where NU1605 would show).
- `dotnet ef migrations list --project src/backend/MeetingRooms.Infrastructure --startup-project src/backend/MeetingRooms.Api`
  — `InitialIdentity` listed and marked applied after the first run. This is the schema check
  that does not need a SQL client, which the container has no such thing as.
- `dotnet run --project src/backend/MeetingRooms.Api`, then through Scalar at `/scalar/`:
  - `POST /api/auth/register` with a fresh email → **201**, `{ id, email }`.
  - the same request again → **409**, `errorDetails: ["EmailAlreadyRegistered"]`.
  - register with `"not-an-email"` → **400 `ValidationProblemDetails`**, `errors` keyed by
    field — the shape `[ApiController]` produces natively.
  - register with `"abc"` as the password → **400 `ProblemDetails`** carrying Identity's
    policy codes. Different shape on purpose; see *Validation*.
  - `POST /api/auth/login` with the wrong password → **401**,
    `["InvalidEmailOrPassword"]`; with an unknown email → **the identical response**, byte
    for byte.
  - login correctly → 200 + token. Paste it into Scalar's auth dialog.
  - `GET /api/auth/me` with no token → **401**, empty body. With the token → 200 and
    `roles: ["User"]`.
  - log in as the **seeded admin** → `/me` returns `roles: ["Admin"]`. This is the role-claim
    proof; without it a `RoleClaimType` mistake stays invisible until phase 4.
- `/health` → `databaseReachable: true`, `connectionStringConfigured: true`.
- Restart the app and watch the log: migrations report nothing to apply and both seeders are
  no-ops. Idempotence is what makes migrate-on-startup safe on every deploy.
- Regression, all unchanged from phase 2: `/` serves the React page, `/openapi/v1.json` and
  `/scalar/` load, `/api/nope` is still a 404 `ProblemDetails` with no error code, and the
  hub negotiate still reports in-process SignalR.

On the deployed app:

- It boots — which alone proves `Jwt__SigningKey` is present and valid and that the Azure SQL
  connection string works, since `ValidateOnStart` and `Migrate()` both run before the first
  request.
- `/health` reports `databaseReachable: true`.
- Register a user through Scalar on the deployed URL, log in, call `/me`.
- Log in as the seeded admin → `roles: ["Admin"]`, proving the `Seed__*` settings landed in
  Application settings rather than Connection strings.
- `/scalar/` shows the **Authorize** affordance and a token pasted there reaches `/me`.

## Done when

- [ ] `InitialIdentity` exists and is applied locally and in Azure.
- [ ] Register → login → `/me` works end to end, locally and deployed.
- [ ] The seeded admin's `/me` reports `["Admin"]`.
- [ ] Unknown email and wrong password are indistinguishable at login.
- [ ] A duplicate registration is a 409 carrying `EmailAlreadyRegistered`.
- [ ] A malformed payload is a `ValidationProblemDetails`; an Identity policy failure is a
      `ProblemDetails` with Identity's codes.
- [ ] `/me` without a token is 401; the body is empty by decision.
- [ ] A second startup applies no migrations and seeds nothing.
- [ ] `dotnet build` has zero warnings.
- [ ] Phase 2's surface — page, Scalar, OpenAPI, the `/api` 404, negotiate — is unchanged.

**Deploy after this phase:** yes — and the three Azure Application settings must exist
*before* the merge to `main`.
