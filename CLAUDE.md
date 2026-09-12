# Working agreement

## Start of a session
Read `docs/plan.md` and the current phase doc in `docs/phases/` before anything
else. They carry the state and the reasoning this file does not.

## Environment
- Everything runs inside the dev container. Never assume host access.
- Dev database: SQL Server, host `db`, database `MeetingRooms`, user `sa`.
  The connection string is already in `ConnectionStrings__DefaultConnection`.
  Never print it, never hardcode it in a file.
- Outbound network is firewalled to an allowlist. If a fetch fails, say so
  rather than working around it.

## What you must NOT do
- No git commits, pushes, tags, or remote changes. I do all git operations myself.
- No `gh`, no Azure CLI, no deployment, no secrets.
- No adding NuGet or npm packages without asking first - including by editing a
  .csproj, Directory.Packages.props, or package.json directly.
- No editing `.devcontainer/`, `.claude/`, or anything under `.git/`.

## How we work
- Explain the approach before writing code. I want to understand the decision.
- I'm new to C#/.NET; my background is PHP, JavaScript, and WordPress. Flag when
  something is a .NET-specific idiom rather than a general pattern.
- Don't agree with me by default. If my reasoning is wrong, say so.
- Give me diffs, not prose summaries of changes.

## Non-negotiable correctness requirement
A time slot must never be double-booked, including under concurrent requests.
Any booking implementation must name its concurrency mechanism explicitly -
unique index plus violation handling, rowversion, or explicit locking - and
explain what happens when two requests race. "It probably won't happen" is not
an answer.

## Planning
Work proceeds in phases. Each phase is planned and approved before
implementation starts. When a phase is done, we record what actually happened
before moving on.

Decisions made while planning or running a phase get folded into
`docs/decisions.md` before that phase merges. A phase doc is a record of one
piece of work, not where standing decisions live - otherwise later phases end up
searching old phase docs to find out what was decided.

## Git and branching
- `main` is deployable and is what Azure deploys. It only receives verified
  merges, at phase boundaries.
- `develop` is the integration branch. Phase branches start from it and merge
  back into it.
- One branch per phase, `phase/<n>-<short-name>`, branched from `develop`.
- The phase doc is the branch's first commit, written and approved before any
  implementation starts. Its Outcome section is the branch's last commit.
- Merge a phase branch into `develop` with `--no-ff`. When the phase is verified
  locally, merge `develop` into `main` - that triggers the deploy - then
  smoke-test the deployed app.
- Commits are atomic: one logical change; subject says what, body says why.
- No `Co-Authored-By` trailers.
- Claude proposes commit points and drafts the messages; I run the commands.

## Project docs
- `assignment.md` - the requirements. Authoritative. Nothing overrides it.
- `docs/requirements.md` - the assignment resolved into concrete behaviour.
- `docs/decisions.md` - decisions I have already made. Binding. If you think one
  is wrong, say so and stop - don't work around it silently.
- `docs/plan.md` - the settled plan: design reasoning, phase sequence, risks.
- `docs/phases/` - one doc per phase, written and approved before implementation.
- `docs/devcontainer-changes.md` - pending changes to `.devcontainer/`, which I make
  myself. Claude never edits that directory.
- `docs/reference/` - a previous project of mine, plus notes on it. Gitignored,
  not part of this solution. Never edit it, never add it to a project, never
  assume it compiles here.

## How to use docs/reference/
Start with `general-ideas.md` - it indexes the patterns and points at the source
files that implement them. Read it so your suggestions match conventions I
already know, and open the cited files when you need the real implementation.

Nothing in there is a requirement. Do not adopt a pattern because it's there -
each one has to justify itself for *this* assignment, which is smaller and on a
four-day deadline. That project had no deadline; several of its choices are the
wrong trade here. When a pattern is more machinery than this project needs, say
so instead of building it. When you do reuse something, say what you changed.

The reference code targets PostgreSQL; this project is SQL Server. Exception
codes, type mappings, index features, and concurrency primitives differ - never
carry those across unchanged.

That project did not solve double-booking under concurrency. Everything in the
reference is scaffolding around that problem, not a solution to it.