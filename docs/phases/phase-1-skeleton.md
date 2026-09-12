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
| 12 | `docs: add README skeleton` | links, one-line description, empty architecture and concurrency headings |
| 13 | `docs: record phase 1 outcome` | the Outcome section below, after the deploy |

### Layer references (task 2)

```
Application    -> Domain
Infrastructure -> Application, Domain
Api            -> Application, Infrastructure
```

Nothing else. Domain and Infrastructure are empty after this phase; that is expected -
the point is that the graph is in place and visible from the first commit.

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

<!-- Filled in after the deploy, before phase 2 starts: what actually happened, what
     deviated, what got deferred. This is what the next session reads first. -->
