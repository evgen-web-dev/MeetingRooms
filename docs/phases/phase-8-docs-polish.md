# Phase 8 — Docs and polish

**Satisfies:** assignment #9 (*"code must be well-documented and suitable for review"*) and
#10 (*"make sure the source code is well-documented and accessible for review"*), plus the
outstanding half of #5 — `docs/requirements.md` §10 commits the README to explaining the
concurrency mechanism by name, what happens when two requests race, and the trade-off
accepted. That paragraph does not exist yet.

## Context

This is the last phase. Nothing follows it but the submission.

`docs/plan.md` scopes it as *"README (links, architecture, concurrency rationale, how to run
the test), doc comments, final deploy"*. **The doc-comment half is already done.** A sweep of
`src` and `tests` finds zero `TODO` or `FIXME` markers, and the XML documentation on
`SlotRepository.TryClaimAsync`, `BookingApiFactory` and `SlotBookingConcurrencyTests` is the
most thoroughly commented part of the codebase — written at the moment the reasoning was
fresh, which is the only time it is ever written well. So this phase is the README, and one
thing only.

### The problem

`README.md` is 220 lines with two `TODO (phase 8)` markers still in it — *Architecture* and
*Concurrency* — and it is organised around the person who **built** the project rather than
the person who will **review** it. A reviewer's first two questions are *does it work* and
*prove the concurrency claim*; today the file answers those on lines 81 and 203, after sixty
lines of dev-container setup they will never run.

Three phase docs also carry mandatory README content forward:

| From | Must reach the README |
|---|---|
| Phase 5 | the unique `(RoomId, StartUtc)` index is seeder integrity and plays **no part** in the guarantee; a repeat booking answers 201; why the overlap assertion exists |
| Phase 6 | the query-string token defence; that Azure SignalR was verified by reading the client's **socket URL**, not inferred from behaviour |
| Phase 7 | name the screens; `/diagnostics` is the transport panels' documented home; the ~17 s reconnect limitation |

All of that is additive, and the brief is a README that is **not too big**. The resolution is
not compression. It is reordering around the reviewer's path and moving developer depth into
collapsed `<details>` blocks, which GitHub renders natively — one file, nothing lost, nothing
to navigate.

### The reviewer is not on this machine, and may not be on a Mac

Every command in the README today assumes bash, and the only OS-specific note in the file is
about macOS AirPlay. That is a real gap on the one section the assignment *requires* a
reviewer to run. A Windows reviewer typing `docker compose up -d` on a machine with SQL Server
installed hits a port-1433 collision with no guidance; a reviewer on Apple silicon hits a
multi-minute emulated boot with no warning that it is expected and happens once.

The prerequisites are also **smaller than a reviewer will assume**, and saying so is worth a
paragraph: `src/backend/MeetingRooms.Api/wwwroot/` is gitignored and untracked
(`.gitignore:490`), so a fresh clone has no frontend build — and the mandated test drives the
API through `WebApplicationFactory` rather than a browser. **No Node, no frontend build, and
no secret of any kind is needed to run it.** Only the .NET SDK and Docker.

## Repo state, verified before this doc was written

- Branch `develop`, working tree clean. `develop` = `main` + 1 (`d1ccc35 docs: record phase 7
  outcome`). Phase 7's carry *"develop is one commit behind main"* is **closed** — the favicon
  commit is in, and `main` is now the branch that is behind.
- `dotnet build` → 0 warnings, 0 errors.
- `dotnet test tests/MeetingRooms.UnitTests` → 23 passed;
  `dotnet test tests/MeetingRooms.ConcurrencyTests` → 6 passed.
- The only `TODO`s in the repository are `README.md:201` and `README.md:205`.
- `docs/decisions.md` *Still open* holds exactly one item — the exact `dotnet ef database
  update` invocation for the migration flip. Deferred by choice; this phase does not close it.

## Decisions taken at planning time

1. **Docs only. No source changes.** Phase 7 carried a real fix forward — the ownership guard
   for the dev-mode subscription race, and restoring indefinite reconnection — and it is
   deliberately **not** done here. Two speculative fixes were shipped and rolled back in phase
   7, and neither defect touches the booking guarantee, conflict handling, or live updates in
   normal use. Both ship as **stated limitations**. `git diff --stat develop -- src tests`
   must be empty when this branch merges; that is the check, not an intention.
2. **One lean README; depth in `<details>`.** No second document to navigate.
3. **Dual-shell commands for the mandated test and its prerequisites only.** That is what a
   reviewer must run. Local development stays bash with a short Windows note — doubling every
   command in a file we are trying to shrink buys nothing.
4. **Admin credentials are delivered out of band.** The README states that self-registration
   grants `User` and that admin credentials accompany the submission links. No credential
   enters the repository, and a reviewer is told rather than left concluding the admin
   features are unreachable.

## Out of scope

- The dev-mode subscription race and the finite reconnect window (decision 1 above).
- Any change to `src/`, `tests/`, `docker-compose.yml`, or the deploy workflow. The
  port-1433 collision is answered by documentation, not by parameterising the compose file.
- Any new document. Everything lands in `README.md` and `docs/`.

## Packages

None. Nothing is added to any `.csproj` or to `package.json`.

---

## The shape of the work

### Target structure

Roughly 150–170 visible lines, with the rest behind collapsed blocks.

| Section | Purpose | Source |
|---|---|---|
| Title, links, tech line | assignment #10 | exists, keep |
| **For the reviewer** | five-minute path, file pointers, the admin-access note | new |
| **Running the concurrency test** | assignment #6, dual-shell, troubleshooting collapsed | rewritten |
| **Concurrency** | assignment #5, requirements §10 | fills TODO |
| **Architecture** | orientation | fills TODO |
| **The screens** | phase 7 carry | new |
| **Azure configuration** | assignment #2 evidence | exists, trim |
| **Running it locally** | secrets walkthrough and AirPlay note collapsed | exists, trim |
| **Known limitations** | phase 5/6/7 carries, stated as decisions | new |
| **Development process** | assignment #9 | exists, keep |

### The one section that gets more room, not less

*Running the concurrency test* is the only section that grows, because it is the only one the
assignment obliges a reviewer to execute. It gains stated prerequisites, both shells,
`docker compose up -d --wait` so the test cannot race a SQL Server that is still booting, the
expected output so success is recognisable, and a collapsed troubleshooting table covering
the port collision, Apple-silicon emulation, a Compose older than v2.1.1, and this dev
container — where `docker compose` is skipped entirely because
`ConnectionStrings__DefaultConnection` already points at the `db` service.

The port-collision entry is the one that matters on Windows, and the fix is documented as an
**environment variable** rather than an edit to `appsettings.Tests.json`:
`BookingApiFactory` derives its connection string from whatever is configured and replaces
only the catalog, so an override is honoured by design and no fixture has to be touched.

The mutation probe keeps its place as the section's point — a test that cannot be shown to
fail on a real defect is ceremony — and gains the exact edit, named file and string, so the
reviewer does not have to find it.

### Concurrency, and what a reviewer will question

The mechanism is named — an **atomic conditional update**, compare-and-set with the business
condition inside the `WHERE` clause of one `UPDATE` — then the statement itself, the race
narrated from the loser's side, why this is not the check-then-act assignment #5 rules out,
and the isolation caveat (correct under READ COMMITTED with or without RCSI; *not* under
SNAPSHOT, which raises 3960).

Then the three things phase 4 and phase 5 predicted a reviewer would trip on, each one
sentence: a repeat booking by the holder answers **201**, which is why the mandated test uses
twenty *distinct* accounts; the **unique index is not the guarantee**; and the **overlap
assertion** exists because twenty serial requests produce an identical one-and-nineteen
result and the mutation probe cannot tell the difference either.

Closing with the trade-off stated honestly: the invariant lives in one statement's `WHERE`
clause rather than a standing constraint, so it binds every path through `TryClaimAsync` —
which must remain the only write path to `BookedByUserId` — and a `Bookings` table with
`UNIQUE (SlotId)` is the alternative that was weighed and why it lost.

---

## Tasks

Branch `phase/8-docs-polish`, from `develop`.

| # | Commit | Contents |
|---|---|---|
| 1 | `docs: plan phase 8` | this file |
| 2 | `docs: explain the concurrency guarantee in the README` | fills the Concurrency TODO |
| 3 | `docs: describe the architecture and the screens` | fills the Architecture TODO, adds the route table |
| 4 | `docs: make the concurrency test runnable on Windows and macOS` | prerequisites, both shells, troubleshooting |
| 5 | `docs: lead the README with the reviewer's path` | new opening, the trims, `<details>`, Known limitations |
| 6 | `docs: fold phase 8 decisions into decisions.md` | before the branch merges |
| 7 | `docs: record phase 8 outcome` | after the deployed check |

Commits 2–5 are ordered so each leaves a README that reads correctly on its own: the two
TODOs close first, then the section a reviewer must run, then the restructure that moves
everything into its final place.

### Standing decisions to fold into `decisions.md` (task 6)

A new *Documentation* section: the README is the reviewer's document and developer depth
lives in collapsed blocks; dual-shell commands are given for the mandated test only; admin
credentials are delivered out of band and never enter the repository; and the dev-mode
realtime race plus the finite reconnect window ship as **stated limitations** rather than
being fixed against the deadline.

## What you do

1. `git switch -c phase/8-docs-polish develop`, and commit this file.
2. Make each commit when I flag the point.
3. **Before submitting, three portal tasks carried from phase 6**, none of which I can do:
   enable App Service application logging so an incident during review is readable; check the
   Azure SignalR tier's concurrent-connection quota (Free is 20) against a reviewer opening
   several tabs; and check whether the Azure SQL database is serverless with auto-pause,
   which would explain a slow first request after an idle period.
4. Merge `phase/8-docs-polish` into `develop` with `--no-ff`, then `develop` into `main` —
   which deploys, and carries `d1ccc35` along with it.

## Risks and fallbacks

| Risk | Fallback |
|---|---|
| A `docker compose` instruction is wrong and cannot be tested from here — this container has no Docker CLI | Every flag is checked against Compose's documented behaviour and stated conservatively, with the `--wait` fallback written out for older versions. The README already says this file is verified by inspection; that claim stays |
| The README ends up **longer** despite the brief | `wc -l` is in the verification list, and the `<details>` split is what pays for the new sections. If it overruns, the *Running it locally* section is the one that gets cut further — a reviewer running the test does not need it |
| `<details>` renders badly, or a table inside one collapses wrong | Check on GitHub after the merge to `main`. A blockquote is the fallback and costs nothing but vertical space |
| Documenting a limitation invites a reviewer to mark it down | Stating it is still the right call: the alternative is a reviewer finding it themselves, which is worse. Each one is written as a decision with its reason, next to the guarantee it does **not** affect |

## Verification

Nothing executable changes, so verification is proving that, then reading.

```bash
git diff --stat develop -- src tests             # expected: empty
dotnet build                                     # 0 warnings
dotnet test tests/MeetingRooms.UnitTests         # 23
dotnet test tests/MeetingRooms.ConcurrencyTests  # 6
grep -n "TODO" README.md                         # expected: nothing
wc -l README.md
```

Then, by reading:

- Every command, path and quoted string in *Running the concurrency test* checked against the
  files it names, not written from memory.
- Every claim about the deployed app is one already verified in a phase doc — this container
  cannot reach Azure, and phase 8 introduces no new assertion about it.
- No connection string, key, password or admin credential anywhere in the diff.
- The `<details>` blocks render on GitHub, checked on `main` after the merge.

## Done when

- [ ] Both `TODO (phase 8)` markers are gone, and *Concurrency* satisfies
      `docs/requirements.md` §10 — mechanism by name, the race, the trade-off.
- [ ] A reviewer on Windows can run the mandated test from the README alone, including the
      port-1433 case.
- [ ] Prerequisites state that neither Node, nor a frontend build, nor any secret is needed.
- [ ] The phase 5, 6 and 7 README carries are all discharged.
- [ ] *Known limitations* names the reconnect window, the dev-mode race, and no cancellation.
- [ ] `src/` and `tests/` have a zero diff; build and both suites still green.
- [ ] `docs/decisions.md` carries this phase's decisions.
- [ ] Deployed from `main` and smoke-tested; this Outcome section written.

## Outcome

> Written after the deploy.
