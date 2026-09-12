# Phase 1 — Solution skeleton

**Satisfies:** assignment #9 (reviewable structure), and de-risks #7 early.

**Goal:** the four-project solution exists with its layer references enforced, every
failure leaves the app as an RFC 7807 response rather than a stack trace, the API is
explorable through Scalar, and we know - from the deployed app, not from hope -
whether Azure SignalR is wired correctly.

## Out of scope

No EF Core, no database, no Identity, no auth, no FluentValidation, no domain
entities. The hub is empty: no groups, no methods, no broadcasts. Those are phases
3-6. Phase 1 is pipeline only.

## Context carried in from planning

- `Scalar.AspNetCore` **2.17.3**, `Microsoft.Azure.SignalR` **1.33.1** - the only two
  packages this phase needs, both on the Api project. Application and Infrastructure
  have nothing to register yet, so `Microsoft.Extensions.DependencyInjection.Abstractions`
  waits for phase 3 and `TimeProvider.System` is registered in the composition root.
- Scalar and the OpenAPI document are served in **all** environments, so a reviewer
  can exercise the API on the deployed URL. Noted in the README as deliberate.
- `/health` becomes a controller, per the controllers-for-endpoints decision. Its URL
  stays `/health` - it is already deployed and already used as the Azure smoke test.

## Tasks

Each is one commit. The tree builds after every one.

| # | Commit | Contents |
|---|---|---|
| 1 | `docs: add phase 1 plan` | this file |
| 2 | `chore: add Domain, Application and Infrastructure projects` | three `classlib` projects under `src/backend/`, `Class1.cs` deleted, added to `MeetingRooms.slnx`, and the five project references wired |
| 3 | `feat(application): add OperationResult and generic error codes` | `Application/Results/OperationResult.cs`, `Application/Errors/GenericErrorCodes.cs` |
| 4 | `feat(api): map error codes to ProblemDetails responses` | `Api/Errors/ErrorStatusCodeMapper.cs`, `ProblemDetailsTypeStatusCodeMapper.cs`, `ErrorCodesProblemDetailsFactory.cs`; `Api/DependencyInjectionExtensions.cs` with `ToProblemDetailsResult` |
| 5 | `feat(api): handle unhandled exceptions as ProblemDetails` | `Api/ExceptionHandlers/AppExceptionHandler.cs`, `AddExceptionHandlersWithProblemDetails()`, `app.UseExceptionHandler()` first in the pipeline |
| 6 | `refactor(api): move the health endpoint into a controller` | `Api/Controllers/HealthController.cs` + `HealthResponse`, `AddControllers()`/`MapControllers()`, and a catch-all so unmatched `/api` routes 404 instead of returning `index.html` |
| 7 | **(you)** `chore: add Scalar.AspNetCore package` | `dotnet add src/backend/MeetingRooms.Api/MeetingRooms.Api.csproj package Scalar.AspNetCore --version 2.17.3` |
| 8 | `feat(api): serve the OpenAPI document and Scalar UI` | `AddOpenApi()`, `MapOpenApi()`, `MapScalarApiReference()` - unconditional |
| 9 | **(you)** `chore: add Microsoft.Azure.SignalR package` | `dotnet add src/backend/MeetingRooms.Api/MeetingRooms.Api.csproj package Microsoft.Azure.SignalR --version 1.33.1` |
| 10 | `feat(api): add schedule hub, using Azure SignalR when configured` | `Api/Hubs/ScheduleHub.cs` (empty), `AddRealtime(configuration)`, `MapHub<ScheduleHub>("/hubs/schedule")` |
| 11 | `feat(web): report the SignalR negotiate result on the placeholder page` | `wwwroot/index.html` gains a second check next to the health one |
| 12 | `docs: add README skeleton` | see "README contents" below - it carries facts that currently exist only in conversation |
| 13 | `docs: record phase 1 outcome` | the Outcome section below, after the deploy |

### Layer references (task 2)

```
Application    -> Domain
Infrastructure -> Application, Domain
Api            -> Application, Infrastructure
```

Nothing else. Domain and Infrastructure are empty after this phase; that is expected -
the point is that the graph is in place and visible from the first commit.

### README contents (task 12)

More than a stub, because several operational facts currently live only in
conversation and would be re-derived by clicking around the Azure portal:

- The deployed URL and the repository URL (assignment #10).
- One-line description of what the app is.
- **Run it locally:** `dotnet run --project src/backend/MeetingRooms.Api`, which
  serves on `http://localhost:5248` under the `http` launch profile, against the
  `db` container.
- **Run the concurrency test:** a placeholder heading until phase 5 fills it in.
- **Azure configuration:** a table of *setting name -> where it lives -> what it is
  for*, with **no values**. It must record that `ConnectionStrings:DefaultConnection`
  comes from the App Service *Connection strings* tab (named `DefaultConnection`,
  type SQL Server) while `Azure__SignalR__ConnectionString` is an *App setting*;
  that `Jwt__SigningKey` and `Seed__AdminEmail` / `Seed__AdminPassword` arrive in
  phase 3; and the baseline - Basic B1, Always On, single instance, WebSockets on,
  HTTPS Only on, "Allow Azure services" on for the SQL server, SQL on a provisioned
  tier.
- Empty headings for **Architecture** and **Concurrency**, filled in phase 8.

### What changes vs. the reference project

- **`OperationResult`** - the reference lets `Success(null)` degrade silently into a
  contentless failure and leaves `Value` readable on the failure path. Ours throws on
  a null success and uses `init` rather than `private set`.
- **`ErrorStatusCodeMapper`** starts with one row, `UnexpectedError -> 500`. Auth, room
  and booking codes arrive with their own phases. The unmapped fallback stays 500: a
  missing mapping is a bug and should be loud.
- **No `UseHttpsRedirection`.** On App Service TLS terminates at the front end, so the
  app sees plain HTTP and the middleware can redirect in a loop. The platform's
  "HTTPS Only" setting does this correctly, before the request reaches us.
- **Scalar is not gated to Development**, unlike the reference.
- **`HealthResponse` lives in the Api project**, not Application - there is no use
  case behind health, so it has no place in the application layer.

### Pipeline order (end state of `Program.cs`)

```csharp
app.UseExceptionHandler();      // first: it must see everything below

app.UseDefaultFiles();          // "/" -> "/index.html"
app.UseStaticFiles();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapControllers();
app.MapHub<ScheduleHub>("/hubs/schedule");

app.Map("/api/{**slug}",        // unmatched API routes 404 as an API...
    () => Results.Problem(statusCode: StatusCodes.Status404NotFound));

app.MapFallbackToFile("index.html");   // ...everything else is a client-side route
```

The catch-all is safe: literal controller routes outrank a catch-all parameter, so
`/api/rooms` still reaches its controller once one exists.

## What you do

1. Run the two package installs at tasks 7 and 9, with the exact versions above.
2. **Enable "HTTPS Only"** on the Web App (portal -> Settings -> Configuration). This
   replaces the `UseHttpsRedirection` middleware we are deliberately not adding.
3. Make each commit when Claude flags the point.
4. After the merge to `main` deploys, open the app and report what the negotiate
   check says.

## Risks and fallbacks

| Risk | Fallback |
|---|---|
| `Scalar.AspNetCore` 2.17.3 or `Microsoft.Azure.SignalR` 1.33.1 does not target net10.0 | restore fails loudly and immediately; pin the newest version that targets net9.0 - a net10.0 project consumes net9.0 libraries fine |
| Azure SignalR negotiate fails on the deployed app | the app still runs: clear `Azure__SignalR__ConnectionString` and it falls back to in-process SignalR, which is a config change and no code change. Revisit in phase 6 |
| Adding three project references breaks the publish step | the workflow publishes the Api project, which pulls referenced projects transitively; the phase's own deploy is the check |
| `MapFallbackToFile` swallowing API 404s | handled in task 6; verified explicitly in Done-when |

## Verification

Locally, before the merge:

- `dotnet build` - clean, zero warnings.
- `GET /health` returns the same JSON shape as today.
- `GET /openapi/v1.json` returns a document containing the health endpoint.
- `/scalar` renders the reference UI (confirm the exact path on first run).
- `GET /api/nope` returns a 404 **ProblemDetails**, not `index.html`.
- `POST /hubs/schedule/negotiate?negotiateVersion=1` returns `connectionId` and
  `availableTransports` - in-process SignalR, which is correct locally, since the dev
  container's firewall cannot reach `*.service.signalr.net`.
- The global exception handler is checked with a **temporary, uncommitted** endpoint
  that throws: it must produce a 500 `ProblemDetails` carrying
  `errorDetails: ["UnexpectedError"]`, no stack trace in the body, and a logged
  exception server-side. Removed before committing - the shipped code has no throw
  endpoint.

## Done when

- [ ] Four projects build clean; the reference graph is exactly as specified above.
- [ ] An unhandled exception produces 500 + ProblemDetails + `["UnexpectedError"]`,
      never a stack trace, and is logged.
- [ ] `/health` is served by `HealthController` and its URL is unchanged.
- [ ] `/api/<unmatched>` returns a 404 ProblemDetails.
- [ ] Scalar and `/openapi/v1.json` load locally **and on the deployed app**.
- [ ] The deployed `POST /hubs/schedule/negotiate?negotiateVersion=1` returns a `url`
      containing `.service.signalr.net` plus an `accessToken` - the proof that Azure
      SignalR is wired. (In-process SignalR returns `connectionId` instead; seeing
      that on the deployed app means the connection string is not being read.)
- [ ] The placeholder page reports both checks.
- [ ] README exists with both links.

**Deploy after this phase:** yes - merge `phase/1-skeleton` into `develop` with
`--no-ff`, then `develop` into `main`, which triggers the deploy.

## Outcome

**Completed and deployed 2026-09-13.** All thirteen tasks done, every Done-when item
closed, including the three that could only close after the deploy.

### Verified

Locally, from a clean rebuild of the committed tree: zero-warning build; the reference
graph exactly as specified (Domain 0 references, Application 1, Infrastructure 2, Api 2);
`/health` unchanged in shape; `/openapi/v1.json` containing exactly one path and one
schema; `/api/nope` a 404 `ProblemDetails`; negotiate returning `connectionId`; an
unhandled exception producing 500 + `errorDetails: ["UnexpectedError"]` with the full
stack logged and nothing leaked into the body.

On the deployed app: `/health`, `/scalar/` and `/openapi/v1.json` all load, `/health` was
exercised through Scalar itself, and negotiate returns an Azure SignalR endpoint plus an
access token — `signalr-meetingrooms.service.signalr.net`. **Azure SignalR is wired**,
which was the whole reason this phase carried an otherwise-empty hub.

### Deviations from the plan

1. **`ProblemDetails` is built by MVC's `ProblemDetailsFactory`.** The plan had us port
   three files from the reference; one of them reproduced a status-code-to-RFC-URI table
   the framework already owns. `ProblemDetailsTypeStatusCodeMapper` and
   `ErrorCodesProblemDetailsFactory` were therefore not created, and a small
   `Api/Errors/ProblemDetailsExtensions.cs` holds the `errorDetails` key so the controller
   path and the exception handler cannot drift. Recorded in `docs/decisions.md`.
2. **`OperationResult` gained two guarantees beyond the plan:** reading `Value` on a
   failed result throws instead of returning `default!`, and `Failure()` rejects an empty
   error list. The second is what makes `Errors[0]` safe at the mapping site.
3. **The local HTTP port is 5000, not the 5248 written above.** `devcontainer.json`
   forwards 5000 and 5173 and nothing forwarded 5248, so the documented local URL was
   unreachable from a browser outside the container. 5173 is Vite's default, so 5000 was
   plainly the intended API port. References to 5248 earlier in this document are
   superseded.
4. **`HealthResponse` lives in `Api/DTOs/`**, and `TimeProvider.System` is registered in
   the composition root with `HealthController` as its first consumer, so the
   registration is not dead code.
5. **Scalar's path is `/scalar/`.** A bare `/scalar` returns a 302 to it.

### What the phase proved, beyond its checklist

- **Both package risks were unfounded, for different reasons.** `Scalar.AspNetCore`
  2.17.3 ships a real `net10.0` target *and* self-hosts its JavaScript bundle at
  `/scalar/scalar.js` rather than loading it from a CDN — which is why the reference UI
  renders inside this firewalled container at all. `Microsoft.Azure.SignalR` 1.33.1 has
  no `net10.0` target, resolves its `net8.0` asset cleanly, and produces no `NU1701`.
- **Route precedence was tested, not assumed.** A temporary `/api/ping` controller
  confirmed that a literal controller route outranks the `/api/{**slug}` catch-all. Had
  that been wrong, every endpoint added in phases 3-7 would have been shadowed.
- **Development does not leak stack traces.** ASP.NET inserts its developer exception
  page automatically in Development; because `UseExceptionHandler()` is the first
  middleware *we* register, ours sits inside it and catches first. Confirmed by
  identical response bodies in both environments.

### Learned, and not anticipated by this document

- **Negotiate has three outcomes, not the two in Done-when.** Alongside "Azure `url` +
  `accessToken`" and "`connectionId`, meaning the connection string was never read",
  there is a plain-text `500 "Azure SignalR Service is not connected yet"`, meaning the
  string *was* read but no server connection exists. The second and third look alike from
  a browser and have unrelated causes.
- **That third outcome is also the normal cold-start window.** The first page load after
  the deploy showed it; every subsequent load succeeded. The SDK opens server connections
  to the service at startup and refuses negotiate until one is established. A single 500
  immediately after a deploy or restart is expected; only a persistent one is a fault.
  The README's diagnosis table says so.
- **Azure SignalR negotiate cannot be exercised offline.** A local probe with a
  syntactically valid but unreachable connection string proves the configuration key is
  read and the Azure branch activates - which was the failure mode most likely to go
  unnoticed - but it can never prove negotiate succeeds. Only a deploy does that.

### Carried into later phases

- **Phase 6:** the real SignalR client must tolerate a failed *initial* negotiate after a
  deploy or restart. `withAutomaticReconnect()` covers reconnection, not the first
  `start()`, so that needs an explicit retry.
- **Phase 3:** `Microsoft.Azure.SignalR` pulled in eleven transitive packages, among them
  `Microsoft.IdentityModel.Abstractions` 6.35.0. JWT bearer authentication brings a much
  newer `Microsoft.IdentityModel` line; an `NU1605` downgrade warning there originates
  here.
- **`docs/devcontainer-changes.md` has two entries still to be written:** a contingency
  for allowlisting `signalr-meetingrooms.service.signalr.net`, and a correction to B5.
  B5 currently states that `init-firewall.sh` is safe to run repeatedly. It is safe from
  a *fresh container start*, where chain policies are `ACCEPT`. Inside a running
  container it is not: `iptables -F` does not reset chain policies, so after a previous
  run the flush leaves `OUTPUT DROP` with no allow rules, and the script then dies at its
  own GitHub-metadata fetch — taking outbound networking with it. Restoring the three
  chain policies to `ACCEPT` before running it is the fix.
- **README:** *Architecture* and *Concurrency* are headings until phase 8; *Running the
  concurrency test* until phase 5.

### Process note

This Outcome section was written after the merges rather than as the phase branch's last
commit, because the deployed negotiate result did not exist until `main` had deployed.
For later phases the workable order is: merge to `develop`, merge to `main` and deploy,
then record the outcome on `develop`.
