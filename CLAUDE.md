# Working agreement

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
- No adding NuGet or npm packages without asking first — including by editing a
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
Any booking implementation must name its concurrency mechanism explicitly —
unique index plus violation handling, rowversion, or explicit locking — and
explain what happens when two requests race. "It probably won't happen" is not
an answer.

## Planning
Phase docs live in `docs/phases/` and follow `docs/phases/_template.md`.
Phase docs are written and approved before implementation starts.

## Reference code
`docs/reference/` holds code from a previous project of mine, for reference only.
It is gitignored and not part of this solution — never edit it, never add it to
a project, never assume it compiles here. It targets PostgreSQL; this project is
SQL Server. When reusing a pattern from it, adapt it and tell me what changed.