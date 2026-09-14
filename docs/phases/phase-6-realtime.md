# Phase 6 — Real-time schedule updates

**Satisfies:** assignment #7 — booking status propagated to all viewers in real time via Azure
SignalR. Resolved into behaviour by `docs/requirements.md` §6.

## Context

Phase 1 put an **empty** `ScheduleHub` behind `/hubs/schedule`, purely so the deployed app could
prove on day one that Azure SignalR `negotiate` works. Phase 5 delivered the booking write. Between
them there is a hole: nothing joins a group, nothing broadcasts, and the hub has no members. This
phase closes it end to end.

It also retires `docs/plan.md` risk 3 — *Azure SignalR cannot be tested from this container; first
real proof is post-deploy*. Two corrections to that framing, both learned since it was written:

- The **logic** — groups, the notify rule, re-subscription — is fully exercisable here against
  in-process SignalR, which is the correct local answer and which the Vite proxy already forwards
  (`ws: true`, phase 2). Only the Azure *transport* needs a deploy.
- `signalr-meetingrooms.service.signalr.net` was added to the container allowlist at the phase 2/3
  boundary (`docs/devcontainer-changes.md` §3). That separates "the connection string is wrong"
  from "App Service is wrong"; it still cannot exercise App Service's WebSockets setting.

Three things earlier phases carried into this one, all closed here:

1. **Phase 5:** broadcast on `Claimed` only, never on `AlreadyClaimedByCaller`. The obvious
   implementation — broadcast whenever the booking succeeded — emits an event every time a caller
   re-posts a booking they already hold, when nothing has changed.
2. **Phase 1:** the client must tolerate a failed **initial** negotiate. A cold start after a deploy
   answers `500 "Azure SignalR Service is not connected yet"`, and `withAutomaticReconnect()` covers
   reconnection, not the first `start()`.
3. **Phase 2:** the dev-server proxy already carries `ws: true` on `/hubs`, so no further
   configuration is needed for local development.

Settled while planning this phase, and folded into `docs/decisions.md` before the branch merges:

1. **The room id for the group comes from a separate read**, not from the claim statement.
   `TryClaimAsync` has a zero diff this phase.
2. **A failed broadcast never fails a committed booking.** The adapter catches and logs.
3. **The port takes no `CancellationToken`**, deliberately, against the grain of every other async
   method here.
4. **A query-string access token is accepted only under `/hubs`.**
5. **Group membership does not survive a reconnect**, so the client re-subscribes *and* refetches.
6. **The event carries no booker identity**, with the consequence stated rather than patched.
7. **Room create/update/delete does not broadcast.**
8. **The hub's JSON naming policy is set explicitly**, rather than inherited from a default.

Each is argued where it belongs below.

## Out of scope

Every screen (phase 7). The probe panel this phase adds is scaffolding for the verification below
and is **deleted by phase 7**, not grown into it.

The component-library question — shadcn/ui, or a frontend skill — is **phase 7 planning**, not a
phase 6 decision, and it has a prerequisite worth probing early: `.devcontainer/init-firewall.sh`
allowlists GitHub, npm, NuGet, Anthropic, VS Code and the two Azure hosts. **`ui.shadcn.com` is not
on it**, and that is the host the shadcn CLI fetches component source from at `add` time. Unblocking
it costs an `init-firewall.sh` edit plus a rebuild — a `docs/devcontainer-changes.md` entry, and
work only you can do.

Also out: live updates to the room *list* (§6 is about slot status); cancelling a booking, which is
out of scope permanently (`docs/requirements.md` §4); the README's real-time prose (phase 8).

## Packages

**One ask: `@microsoft/signalr` 10.0.11** in `src/frontend`, as a `dependency` rather than a
`devDependency` — it ships inside the bundle. The version matches the 10.0.x server line the
solution already sits on.

Nothing is added to any .NET project. `Microsoft.Azure.SignalR` 1.33.1 has been in the API since
phase 1, and SignalR itself is part of the ASP.NET Core shared framework. `tests/MeetingRooms.UnitTests`
gains a **project reference** to `MeetingRooms.Application` — a project reference, not a package.

---

## The shape of the work

### Layering

The rule from `docs/decisions.md` is that no layer below `Api` references SignalR. That is what the
port is for, and it is the standard ports-and-adapters split:

```
Application   IScheduleNotifier            (the port: "tell the room")
              BookingService               (calls it, knows nothing about transports)
Api           ScheduleHub, IScheduleClient (the transport and its wire contract)
              SignalRScheduleNotifier      (the adapter: implements the port over IHubContext)
```

The adapter lives in `Api` because `Api` is the only project that may name `Microsoft.AspNetCore.SignalR`.
`Application` is compiled without any knowledge that SignalR exists, which is also what makes the
notify rule unit-testable with a hand-written fake.

### The port — `Application/Interfaces/IScheduleNotifier.cs`

```csharp
Task SlotBookedAsync(int roomId, int slotId);
```

Two decisions are carried on this one signature.

**Primitives, not a payload type.** The shape that goes on the wire is a transport concern and lives
next to the hub. `Application` says *what happened*; `Api` decides how it is spelled to a browser.

**No `CancellationToken`, deliberately.** Every other async method in this codebase takes one, so its
absence needs a reason: by the time this is called the booking is **already committed**, and the
usual token to pass is the *request's* — which is cancelled when the caller's HTTP connection drops.
Forwarding it would mean that a booker closing their tab at the wrong moment leaves every other
viewer stale for an event that really happened. The XML doc says exactly this, so the next reader
does not "fix" it.

The interface also states its contract: **implementations must not throw.**

### `ISlotRepository.GetRoomIdAsync` — and why the claim statement is not touched

The broadcast goes to a per-room group, so it needs the slot's `RoomId`. The claim does not return
it: `ExecuteUpdateAsync` compiles to one `UPDATE … WHERE` and reports a row count, nothing more.

Three ways to get it were considered, and the write path's stability decided it:

| Option | Why not |
|---|---|
| Fold the read into `TryClaimAsync` | Same round trips, but it edits the method the README and phase 5's doc quote verbatim. |
| Raw `UPDATE … OUTPUT INSERTED.RoomId` | One round trip instead of two, at the cost of hand-maintained SQL on the graded path and rewriting the documented rationale for `ExecuteUpdateAsync`. |
| **The client sends `roomId`** | Rejected on principle. `BookSlotRequest` carries a server-generated id and nothing else; a client-supplied room id would be unverified data steering a broadcast, and a mismatched one would deliver the event to the wrong room's viewers. |

So: a new read-only port method, a primary-key seek projecting one column, called **only when the
outcome is `Claimed`**. `TryClaimAsync` has a zero diff this phase — the graded statement is exactly
the text phase 5 verified.

It returns `int?`. Null is unreachable in practice — the claim just succeeded, and a room with a
booked slot cannot be deleted (409 `RoomHasBookedSlots`) — and the caller **skips the broadcast**
rather than throwing. This is the one place the codebase's usual "broken invariant → throw" habit is
wrong: an exception here would turn a committed, irreversible booking into a 500 for the person who
actually got the room.

### The call site — `Application/Services/BookingService.cs`

Immediately before the existing `return outcome switch`:

```csharp
if (outcome is SlotClaimOutcome.Claimed)
{
    var roomId = await _slotRepository.GetRoomIdAsync(request.SlotId, cancellationToken);

    if (roomId is not null)
    {
        await _notifier.SlotBookedAsync(roomId.Value, request.SlotId);
    }
}
```

`Claimed` alone. `AlreadyClaimedByCaller` is a replay of a booking that already happened — the row
did not change, so nothing happened to announce. Both are a successful `OperationResult`, which is
precisely why "broadcast on success" is the wrong reading and why the unit tests below pin it.

The switch expression is untouched; the guard sits above it.

### The hub — `Api/Hubs/ScheduleHub.cs`

```csharp
[Authorize]
public sealed class ScheduleHub : Hub<IScheduleClient>
{
    public static string RoomGroup(int roomId) => $"room-{roomId}";

    public Task SubscribeToRoom(int roomId) => Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId));

    public Task UnsubscribeFromRoom(int roomId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomGroup(roomId));
}
```

**`Hub<IScheduleClient>` rather than `Hub`** — a .NET idiom worth naming, since it has no JavaScript
analogue. The generic parameter is an interface describing what the *server can call on a client*;
SignalR generates the proxy, so `SlotBooked` is checked by the compiler on the server side instead of
being a string literal in two files that can silently drift. The client half is still a string, in
`connection.on('SlotBooked', …)` — half a contract is the most the platform can give here, and it is
the half that changes most often.

`RoomGroup` is `static` and `public` so the adapter composes group names through the same function
the hub joins them with. A group name is a plain string with no registry behind it: a typo in one of
two places is a broadcast nobody receives and no error anywhere.

**No database dependency in the hub, on purpose.** Subscribing to a room that does not exist is
harmless — nothing is ever published to that group — and any authenticated caller may read any
room's schedule, so membership needs no check beyond `[Authorize]`. Adding an existence check would
put a repository and a scoped `DbContext` inside a long-lived connection object to prevent nothing.

### Authenticating the hub — `Api/DependencyInjectionExtensions.cs`

`docs/decisions.md` already fixed the mechanism; this writes it:

```csharp
bearerOptions.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];

        if (!string.IsNullOrEmpty(accessToken)
            && context.Request.Path.StartsWithSegments("/hubs"))
        {
            context.Token = accessToken;
        }

        return Task.CompletedTask;
    }
};
```

A browser cannot set headers on a WebSocket upgrade, so the SignalR JS client appends the token to
the query string instead. **Scoped to `/hubs`** because a query string is the worst place to carry a
credential — it lands in access logs, referrers and proxy logs — and there is no reason the REST API
should ever accept one.

Worth stating for the deployed case: under Azure SignalR the WebSocket terminates at the **service**,
not at the app, so the app authenticates the caller at `negotiate` — where the JS client sends a
normal `Authorization` header — and the service then carries those claims. This wiring is what makes
the in-process path work. Both are wired because both are used: in-process locally, Azure in Azure.

### The adapter, and what happens when the broadcast fails — `Api/Realtime/SignalRScheduleNotifier.cs`

`IHubContext<ScheduleHub, IScheduleClient>` is how code *outside* a hub sends to one; the hub class
itself is only instantiated per invocation and is not something to inject. Sends to
`ScheduleHub.RoomGroup(roomId)`, wrapped in a try/catch that logs at error and **swallows**.

The booking is committed and cannot be undone. The two available answers are:

- **swallow and log** — the caller gets their 201, and other viewers are stale until they refetch;
- **let it bubble** — the caller gets a 500 describing a booking that exists and that they own.

The second lies in the more damaging direction: the response would contradict the database about the
one operation this whole project is graded on. So the first, with the staleness recorded as a known
consequence rather than hidden.

**The catch lives in the adapter, not at the call site.** "The transport can fail" is knowledge that
belongs to the layer that owns the transport; `BookingService` should not be writing defensive code
about a port whose contract already says it does not throw.

### What the payload says, and what it deliberately does not

```csharp
public sealed record SlotBookedEvent(int RoomId, int SlotId);
```

- **No booker identity.** `docs/requirements.md` §5 — a caller learns that a slot is taken, and
  separately whether they took it. An event naming the booker would leak to every viewer of the room
  exactly what the schedule endpoint refuses to disclose.
- **No `isBooked: true` field.** The event name *is* the status, bookings cannot be cancelled, and a
  boolean that is always true is a field waiting to be misread.
- **`RoomId` is on the payload even though the group already implies it.** A client subscribed to
  several rooms would otherwise have to infer which one an event belongs to from the subscription it
  arrived on, which the JS client does not tell it.

**The consequence, stated rather than patched:** a booker with a *second* tab open on the same room
sees the slot flip to booked, but not to *booked by me*, until that tab refetches. The fix would be
an identity field the visibility rule forbids, or per-user targeting for a cosmetic difference in a
rare case. Phase 7 inherits this line, and the honest answer there is a refetch, not a wider event.

**Naming on the wire is set explicitly.** SignalR's JSON protocol is configured independently of
MVC's and does not inherit the API's camelCase, so `AddJsonProtocol` sets the naming policy rather
than trusting a default that cannot be verified from here — the failure mode otherwise is
`e.SlotId` arriving where the client reads `e.slotId`, at runtime, with no error.

### The client — `src/frontend/src/realtime/scheduleConnection.ts`

The reusable half, which phase 7 keeps:

- `HubConnectionBuilder` on `/hubs/schedule` with `accessTokenFactory` — same origin, so no base URL
  anywhere in the app, exactly as `/api` is called today.
- `withAutomaticReconnect([0, 2000, 5000, 10000])`.
- **An explicit retry around the first `start()`**, which is phase 1's carry: automatic reconnect
  does not cover the initial connect, and the first negotiate after a deploy can legitimately 500
  for several seconds while the app establishes its server connection to Azure SignalR.
- **`onreconnected` → re-`invoke('SubscribeToRoom')` *and* refetch the schedule.** Group membership
  is per *connection*, not per user, so it does not survive a reconnect — and events that fired
  during the gap are simply gone. Re-subscribing alone fixes the first hole and leaves the second;
  the refetch is what makes the screen correct rather than merely live again.

### The probe panel — `src/frontend/src/App.tsx`

The health and negotiate panels stay; phase 2's outcome argued for keeping them and they remain the
fastest diagnosis of a deployment. Below them, a panel that exists **only to make the verification
below runnable**: email/password login → room dropdown → that room's slots with a **Book** button →
live updates.

The token is held in component state, not `localStorage`. `docs/decisions.md` puts the JWT in
`localStorage`, but that decision belongs to phase 7's auth context; this page is scaffolding, and
scaffolding should not be the thing that first implements a standing decision.

---

## Tests

`tests/MeetingRooms.UnitTests` gains a project reference to `MeetingRooms.Application` and
`BookingServiceNotificationTests`, with hand-written fakes for `ISlotRepository` and
`IScheduleNotifier` — no mocking package, which is the same call phase 5 made about test
dependencies.

1. `Claimed` → **exactly one** notification, carrying the room id the repository reported.
2. `AlreadyClaimedByCaller` → **no** notification, and still a successful result. This is phase 5's
   carry and the whole reason these tests exist: the wrong implementation is the obvious one.
3. `AlreadyBooked`, `HasEnded`, `NotFound` → no notification.
4. A notifier that **throws** → the booking still succeeds. Belt and braces: the adapter catches too,
   but the service must not be depending on that to be correct.

**No end-to-end hub test.** It would need the SignalR .NET client package and a connection over the
test server, and it is timing-sensitive — while the routing it would prove is exactly what the
two-browser check and probe 2 below cover. `docs/decisions.md` keeps its narrow testing scope.

---

## Tasks

Each is one commit; the tree builds after every one. Branch `phase/6-realtime`, from `develop`.

| # | Commit | Contents |
|---|---|---|
| 1 | `docs: plan phase 6` | this file |
| 2 | **(you)** `chore: add the SignalR client` | `npm install @microsoft/signalr@10.0.11` in `src/frontend` — `package.json` + lockfile |
| 3 | `feat(application): add the schedule notifier port` | `IScheduleNotifier` |
| 4 | `feat(api): broadcast slot bookings to per-room hub groups` | `ScheduleHub`, `IScheduleClient`, `SlotBookedEvent`, `SignalRScheduleNotifier`, `AddRealtime` registration + JSON naming policy |
| 5 | `feat(api): accept the hub's access token from the query string` | `OnMessageReceived`, scoped to `/hubs` |
| 6 | `feat(application): notify the room when a slot is claimed` | `GetRoomIdAsync` on the port and in `SlotRepository`, the guard in `BookingService` |
| 7 | `test: pin which claim outcomes notify` | project reference, fakes, `BookingServiceNotificationTests` |
| 8 | `feat(web): subscribe to a room's live schedule updates` | `scheduleConnection.ts` + the probe panel |
| 9 | `docs: fold phase 6 decisions into decisions.md` | the eight decisions, before the branch merges |
| 10 | `docs: record phase 6 outcome` | the Outcome section, after the deployed check |

### Standing decisions to fold into `docs/decisions.md` (task 9)

Under *Real-time*: the `Claimed`-only rule and why "broadcast on success" is wrong; the
broadcast-failure policy and the staleness it accepts; the missing `CancellationToken` and its
reason; the query-string token scoped to `/hubs`, plus the note that Azure SignalR authenticates at
negotiate instead; groups not surviving a reconnect, hence re-subscribe **and** refetch; the room id
read as a separate call, leaving `TryClaimAsync` untouched; the payload's contents and the
second-tab `isBookedByMe` limitation; room CRUD not broadcasting; the explicit JSON naming policy.
Under *Frontend*: `@microsoft/signalr`, and the probe panel as deliberate scaffolding. Under
*Testing*: the notification unit tests, and why there is no end-to-end hub test.

---

## What you do

1. `git switch -c phase/6-realtime develop`, and commit this file.
2. Task 2, from `src/frontend`: `npm install @microsoft/signalr@10.0.11`. I report what the lockfile
   resolves — including transitive dependencies — before you commit, so the tree is approved rather
   than assumed.
3. Make each commit when I flag the point.
4. **Nothing to add in the Azure portal.** `Azure__SignalR__ConnectionString` has been set since
   phase 1 and WebSockets are already on; this phase introduces no new configuration key and no new
   secret.
5. Merge `phase/6-realtime` into `develop` with `--no-ff`, then `develop` into `main`, which deploys.
   `4a6ff8e docs: record phase 5 outcome` is sitting on `develop` and rides along.
6. Run the two-browser check on the deployed URL and tell me what you see, so the Outcome records a
   result rather than an expectation.

---

## Risks and fallbacks

| Risk | Fallback |
|---|---|
| Azure SignalR misbehaves in production | Clear `Azure__SignalR__ConnectionString` → in-process SignalR on the single instance. Configuration only, no code change — the fallback phase 1 built deliberately |
| App Service WebSockets blocked or off | SignalR negotiates down to Server-Sent Events or long polling on its own; the feature degrades in latency, not in correctness. README records WebSockets as **on** |
| The hub answers 401 and the client cannot connect | Almost always the query-string token: check the path predicate matches `/hubs`, and that `MapInboundClaims = false` still holds. In-process locally is the place to debug it, not Azure |
| Payload arrives with unexpected property casing | Task 4 sets the naming policy explicitly rather than relying on a default; if it still disagrees, read one frame in the browser's network tab — the JSON is plain text |
| A slow broadcast holds the booking request open | Add a timeout inside the adapter, which already owns the failure policy. Not built in advance: `SendAsync` does not wait for client acknowledgement, so the exposure is the server-to-service hop only |
| The probe panel grows into a screen | It is named scaffolding here and deleted in phase 7. If it starts acquiring routing or an auth context, that is phase 7 work happening early and out of order |
| Group membership silently lost after a reconnect, leaving a live-looking but dead screen | Probe 3 exists precisely to catch this, and it is the failure mode most likely to pass a casual demo |

## Verification

Locally, before the merge. In-process SignalR is the correct local answer, and the dev proxy already
forwards the WebSocket:

```bash
dotnet build                                      # clean, zero warnings
dotnet test tests/MeetingRooms.UnitTests          # 17 + the new notification tests
dotnet test tests/MeetingRooms.ConcurrencyTests   # must stay at 6 green - the write path is untouched
dotnet run --project src/backend/MeetingRooms.Api # :5000
npm --prefix src/frontend run dev                 # :5173
```

- Two browser tabs on `http://localhost:5173`, logged in as **different** users, same room: booking
  in one flips the slot in the other **with no refresh**.
- Book the same slot again from the winner's own account → 201, and **no** second event anywhere.
- The negotiate panel still reports in-process SignalR; `/health` still reports
  `databaseReachable: true`.
- Regression sweep, re-run rather than assumed: register/login/`/me`, room CRUD, the schedule read,
  and a booking through Scalar all behave as phase 5 left them.

**Three probes, in phase 5's spirit — a green run proves less than a reddened one:**

1. Delete the `if (outcome is SlotClaimOutcome.Claimed)` guard → the `AlreadyClaimedByCaller` test
   must go **red**. Restore.
2. Watch room B in both tabs, book in room A → **nothing** arrives. This is what proves per-room
   scoping rather than a broadcast to everyone; without it, a green two-browser check is equally
   consistent with `Clients.All`.
3. Stop the API, let the client reconnect, then book from the other tab → the reconnected tab must
   still update. This proves the `onreconnected` re-subscription, and it is the one that fails
   silently if forgotten.

On the deployed application — the only thing that exercises Azure SignalR and App Service WebSockets:

- The negotiate panel reports an Azure endpoint with `accessToken: present`. A 500 on the very first
  request after the deploy is the documented cold-start window; the initial-start retry is exactly
  what must absorb it.
- Two browsers, two accounts, same room, one books → the other updates with no refresh.
- Everything phases 2–5 built still behaves.

## Done when

- [ ] Booking a slot updates every other viewer of that room immediately, locally **and** on the
      deployed app, with no refresh.
- [ ] Nothing is broadcast for a replayed booking, proven by a test that is shown to go red.
- [ ] Nothing is broadcast to viewers of a different room, proven by probe 2.
- [ ] A reconnected client still receives updates, proven by probe 3.
- [ ] A broadcast failure leaves the booking a 201.
- [ ] The hub requires authentication, and a query-string token is accepted only under `/hubs`.
- [ ] No layer below `Api` references SignalR, and `TryClaimAsync` is byte-for-byte what phase 5
      verified.
- [ ] `dotnet build` zero warnings; all three test projects green.
- [ ] `docs/decisions.md` carries the eight decisions; `docs/plan.md` risk 3 amended with what
      actually retired it.
