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

```bash
dotnet run --project src/backend/MeetingRooms.Api
```

The `http` launch profile serves on <http://localhost:5000>. The database connection
string is supplied by the `ConnectionStrings__DefaultConnection` environment variable,
already set in the container and pointing at the `db` service (SQL Server, database
`MeetingRooms`).

Two things cannot be exercised from inside the dev container, because its outbound
firewall allows only GitHub, npm, NuGet and Anthropic:

- **Azure SQL** — the local `db` container is used instead.
- **Azure SignalR** (`*.service.signalr.net`) — SignalR falls back to running in-process,
  which is correct locally. See *Diagnosing the realtime transport* below.

## Running the concurrency test

> **TODO (phase 5).** The automated test that fires simultaneous booking requests at one
> slot and asserts exactly one booking is created. This section will carry the
> `docker compose up` command for the SQL Server it runs against and the `dotnet test`
> invocation, so a reviewer can run it with two commands.

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

> **TODO (phase 8).**

## Concurrency

> **TODO (phase 8).** How a slot is never double-booked, what happens when two requests
> race, and the trade-off accepted.

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
