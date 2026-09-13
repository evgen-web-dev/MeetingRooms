# Phase 2 — Frontend shell + CI build

**Satisfies:** assignment #8 (deployment), and retires risk #6 on `docs/plan.md`'s
ranked list — the frontend build reaching `wwwroot` through CI.

**Goal:** the deployed page is served from a CI-built Vite bundle. Vite, React,
TypeScript and Tailwind exist, build into the API's `wwwroot`, and survive the trip
through GitHub Actions onto App Service — proved on the deployed app, while the
frontend is still a single page and a mistake costs minutes rather than a rewrite.

## Out of scope

No routing, no auth context, no login screen, no `@microsoft/signalr` client, no API
client layer, no component library, no ESLint/Prettier config, no frontend tests. All
of that is phase 7, and `docs/decisions.md` deliberately defers routing and screen
breakdown to it.

This fence is the phase. Phase 2 is small **on purpose**: its entire value is
de-risking the pipeline early, and every hour spent on screens here is an hour spent
before the API contract they consume exists. One page, and it exists to prove the
build.

## Context carried in from planning

- **The reference project has no frontend at all.** `docs/reference/BookingApp` is
  backend-only, and `general-ideas.md` says nothing about Vite, React or Tailwind.
  Unlike phase 1, there is no prior art to match here — every choice below stands on
  its own.
- **Scaffolded by hand, not by `npm create vite`.** `create-vite` is at 9.2.1, prompts
  interactively even when given `--template`, and its `react-ts` template ships an
  ESLint setup this phase has just fenced out of scope. Authoring the seven small files
  directly is faster and deterministic, and `vite.config.ts` has to be hand-written
  anyway for the `outDir` and the dev proxy.
- **Tailwind 4 is not Tailwind 3.** No `tailwind.config.js`, no `postcss.config.js`, no
  `content` globs — a `@tailwindcss/vite` plugin entry plus `@import "tailwindcss";` in
  one CSS file, with source detection automatic. Worth flagging because v3 muscle memory
  reaches for a config file that no longer exists.
- **Node 24.21.0 / npm 11.19.0 are already in the container**, `registry.npmjs.org` is
  on the firewall allowlist, and `devcontainer.json` already forwards 5173. Nothing in
  `.devcontainer/` needs to change for this phase.

### Packages

Versions verified against the registry on 2026-09-13. `CLAUDE.md` requires asking before
any npm package is added; `docs/decisions.md` puts the install itself in the owner's
hands.

| Package | Version | Role |
|---|---|---|
| `react`, `react-dom` | 19.3.0 | |
| `vite` | 8.3.0 | |
| `@vitejs/plugin-react` | 6.1.1 | |
| `typescript` | 7.0.2 | see Risks — 5.9.3 is the fallback |
| `tailwindcss`, `@tailwindcss/vite` | 4.3.3 | v4 is a Vite plugin, not PostCSS |
| `@types/react`, `@types/react-dom` | current | types only |

## Tasks

Each is one commit. The tree builds after every one. Branch `phase/2-frontend-shell`,
from `develop`.

| # | Commit | Contents |
|---|---|---|
| 1 | `docs: add phase 2 plan` | this file |
| 2 | **(you)** `chore: add the frontend toolchain` | `src/frontend/package.json` written by Claude, then `npm install`; commit it with the generated `package-lock.json` |
| 3 | `feat(web): build the React shell into wwwroot` | `vite.config.ts`, `tsconfig.json`, `index.html`, `src/main.tsx`, `src/App.tsx`, `src/index.css`; deletes the placeholder `wwwroot/index.html`; `.gitignore` rule |
| 4 | `ci: build the frontend before publishing the API` | `.github/workflows/deploy.yml` |
| 5 | `docs: fold phase 2 decisions into decisions.md` | the standing decisions below, before the branch merges |
| 6 | `docs: record phase 2 outcome` | the Outcome section, after the deploy, on `develop` |

### `vite.config.ts` — the load-bearing file (task 3)

```ts
export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    outDir: '../backend/MeetingRooms.Api/wwwroot',
    emptyOutDir: true,   // required: outDir is outside the Vite root, so Vite
                         // refuses to clear it unless told to explicitly
  },
  server: {
    host: true,          // 0.0.0.0, so the container's forwarded 5173 reaches a host browser
    port: 5173,
    strictPort: true,
    proxy: {
      '/health': 'http://localhost:5000',
      '/api':    'http://localhost:5000',
      '/hubs':   { target: 'http://localhost:5000', ws: true },  // ws for phase 6
    },
  },
})
```

`base` stays at its `/` default — the app is served from the site root.

The proxy is not needed by this phase's single page, which can be exercised from a
plain `npm run build`. It is here because phase 7 is the phase that cannot live without
HMR, and configuring it now costs four lines.

### `wwwroot` changes hands (task 3)

Vite's build emits `index.html` into its `outDir`, which is precisely the tracked
placeholder page carrying phase 1's `/health` and negotiate diagnostics. Rather than
have a build tool overwrite a tracked file on every run, **Vite takes ownership of the
directory**:

- Delete the tracked `src/backend/MeetingRooms.Api/wwwroot/index.html`.
- `.gitignore`: replace the narrow `src/backend/MeetingRooms.Api/wwwroot/assets/` rule
  with `src/backend/MeetingRooms.Api/wwwroot/`. Nothing in that directory is tracked
  again; all of it is build output.
- Future static assets — favicon, `robots.txt` — go in `src/frontend/public/`, which
  Vite copies into the output. That keeps "Vite owns `wwwroot`" true without exceptions,
  and it is the Vite-idiomatic answer rather than a workaround.

**Consequence, to be verified rather than assumed:** a fresh clone that has not run
`npm run build` has no `wwwroot` at all. The app must still boot and `/health` and
`/scalar/` must still work; only `/` goes missing. Covered in Verification.

### The page itself (task 3)

`src/App.tsx` reproduces the placeholder's two panels — backend health, and the
realtime transport with phase 1's three-way diagnosis (Azure SignalR / in-process /
cold-start 500) — styled with Tailwind utilities.

The diagnostics are ported rather than dropped because they have already earned their
keep: that negotiate check is what proved Azure SignalR was wired on day one, and it
stays useful on every subsequent deploy. The Tailwind styling is not decoration either
— a page that renders unstyled is how a mis-wired Tailwind plugin announces itself.

### CI (task 4)

```yaml
      - uses: actions/setup-node@v4
        with:
          node-version: '24'
          cache: npm
          cache-dependency-path: src/frontend/package-lock.json

      - name: Build frontend
        working-directory: src/frontend
        run: npm ci && npm run build

      - name: Publish            # unchanged, but must come AFTER the node step
```

Ordering is the whole point: MSBuild globs `wwwroot` when the publish runs, so the files
have to exist by then. `package-lock.json` must be committed for `npm ci` to work.

The build stays in the workflow rather than in an MSBuild target on the `.csproj`.
`docs/plan.md` specified it that way, and it keeps a node toolchain off the critical path
of every local `dotnet build`.

### Standing decisions to fold into `docs/decisions.md` (task 5)

Under *Frontend*: Vite owns `wwwroot` and nothing in it is tracked; static assets live in
`src/frontend/public/`; Tailwind 4 via the Vite plugin with no config file; the dev
server proxies `/api`, `/health` and `/hubs` to port 5000; no ESLint/Prettier config and
no frontend tests. Under *Deployment and operations*: the frontend is built by the
workflow before `dotnet publish`, not by an MSBuild target.

## What you do

1. `git checkout -b phase/2-frontend-shell` from `develop`.
2. One install, after Claude writes `package.json`:
   `cd /workspace/src/frontend && npm install`
3. Make each commit when Claude flags the point.
4. Merge `phase/2-frontend-shell` into `develop` with `--no-ff`, then `develop` into
   `main`, which deploys. Open the app and report what both panels say.
5. **Then, before phase 3:** the devcontainer rebuild for Changes A and B in
   `docs/devcontainer-changes.md`. The phase 2/3 boundary is the moment for it —
   `node_modules` sits on the `/workspace` bind mount and survives a rebuild.

## Risks and fallbacks

| Risk | Fallback |
|---|---|
| `typescript` 7.0.2 is the native rewrite, released 2026-07-08 and only two months old | pin `typescript@5.9.3`. Vite never typechecks during a build, so the blast radius is the `tsc --noEmit` script alone — the app still builds and still deploys |
| `emptyOutDir: true` wipes something wanted | nothing else lives in `wwwroot` after this phase; static files go in `src/frontend/public/` |
| A missing `wwwroot` breaks a build-less `dotnet run` | verified explicitly below; if it throws, a tracked `src/frontend/public/.gitkeep` gives the build something to emit so the directory always exists |
| The node step lands after `dotnet publish` in the workflow | presents as a deploy serving no page at `/`; the phase's own deploy is the check |
| Vite 8 defaults differ from expectation (rolldown, template changes) | the config is hand-authored rather than scaffolded, so there are no interactive prompts and no surprise dependencies to unpick |

## Verification

Locally, before the merge:

- `cd src/frontend && npm run build` — exits 0, `tsc --noEmit` clean, emits
  `wwwroot/index.html` plus `wwwroot/assets/*`.
- `dotnet build` — clean, zero warnings.
- `dotnet run --project src/backend/MeetingRooms.Api`, then `http://localhost:5000/`:
  the React page renders, **visibly Tailwind-styled**, the health panel is green, and the
  realtime panel reports *in-process SignalR* — the correct local answer, since the
  container's firewall cannot reach `*.service.signalr.net`.
- Regression, because the SPA fallback sits next to them: `/scalar/` and
  `/openapi/v1.json` still load, and `/api/nope` is still a 404 `ProblemDetails` rather
  than the React page.
- Delete `wwwroot` and `dotnet run` again: the app boots, `/health` returns 200, `/`
  returns 404. Confirms a missing web root is survivable rather than fatal.
- `npm run dev` on 5173 alongside the API on 5000: the page loads in a host browser over
  the forwarded port and the health panel is green **through the proxy**.

On the deployed app:

- `/` serves the React page, styled, health panel green.
- The realtime panel shows an Azure SignalR endpoint with `accessToken: present`. A
  cold-start 500 on the very first load after a deploy is expected — phase 1 established
  that — so reload once before treating it as a fault.
- `/scalar/` still loads, and the browser console shows no 404s for hashed assets.

## Done when

- [x] `src/frontend` builds with one command and emits into the API's `wwwroot`.
- [x] `wwwroot` holds no tracked files; the placeholder page is gone and its two checks
      live in the React page.
- [x] The page is visibly Tailwind-styled, proving the plugin is wired rather than merely
      installed.
- [x] The app still boots, and `/health` still answers, with no `wwwroot` present.
- [x] `/scalar/`, `/openapi/v1.json` and the `/api/<unmatched>` 404 are unchanged.
- [x] `npm run dev` on 5173 reaches the API on 5000 through the proxy.
- [x] The workflow builds the frontend **before** `dotnet publish`.
- [x] The **deployed** page is the CI-built bundle, with both panels reporting correctly
      and Azure SignalR still wired. This is the exit criterion — a green local build
      retires nothing.

*All eight closed 2026-09-13. One item needs the qualifier the Outcome expands on: item 4
holds for a **fresh** build with no `wwwroot`, and not for a tree built with `wwwroot` and
then stripped of it — those are different failures with different causes.*

**Deploy after this phase:** yes — merge `phase/2-frontend-shell` into `develop` with
`--no-ff`, then `develop` into `main`, which triggers the deploy.

## Outcome

**Completed and deployed 2026-09-13.** All six tasks done plus one unplanned commit, every
Done-when item closed, and the exit criterion met: the deployed page is served from a
CI-built Vite bundle.

### Verified

Locally, from the committed tree: `npm run build` clean (0.38 kB html, 6.92 kB css,
222 kB js); `dotnet build` with zero warnings; `/` serving the hashed bundle with correct
MIME types; the compiled CSS containing real Tailwind utilities rather than an empty file;
`/health`, `/openapi/v1.json` and `/scalar/` unchanged; `/api/nope` still a 404
`ProblemDetails`; a deep route still falling back to `index.html`; negotiate returning
in-process `connectionId`, correct locally. In a browser: the page rendered styled, both
panels reported correctly, the console was clean, and React DevTools showed a **production**
build — confirming the bundle was being exercised rather than the dev server. `npm run dev`
on 5173 reached the API through the proxy for `/health`, the hub negotiate and the `/api`
404.

On the deployed app: the GitHub Actions run succeeded with `Build frontend` ahead of
`Publish`; the page rendered styled with no console errors; React DevTools again reported a
production build; `/health`, `/openapi/v1.json` and `/scalar/` all loaded; and the realtime
panel showed **Azure SignalR with an access token on the very first load after the deploy**.

### Deviations from the plan

1. **`tsconfig.json` needed two options this document did not name.** The first
   `npm run build` failed with TS5097 (`allowImportingTsExtensions` required for
   `import App from './App.tsx'`) and TS2882 (`import './index.css'` has no declaration
   until `types: ["vite/client"]` brings Vite's ambient module declarations into scope).
   Both are configuration gaps rather than bugs — `vite build` alone would have produced a
   working bundle — but they were caught by the `tsc --noEmit` step, which is a modest
   argument for the step existing.
2. **A fifth commit updated the README.** Moving the page into `wwwroot` silently
   invalidated the local-run instructions: a fresh clone running `dotnet run` gets a
   working API and a 404 at `/`, because nothing has built a page. The same commit records
   the macOS port clash below.
3. **The `MakeDir` fallback in the risk table was not needed** and was deliberately not
   added — see below.

### What the phase proved, beyond its checklist

- **The arm64 → x64 lockfile crossing works.** The lockfile was resolved in this container
  on linux-arm64 and installed by `npm ci` on a linux-x64 runner, which had to select
  different per-platform binaries for Rolldown, Tailwind's Oxide and TypeScript out of the
  optional dependencies. This was the phase's one genuinely untested step and it passed
  first time.
- **Tailwind 4 needs no configuration file at all.** Plugin plus one `@import` produced
  correct utilities; there is no `tailwind.config.js` or `postcss.config.js` in the repo.
- **`--ignore-scripts` costs nothing on this tree.** `fsevents` is the only package
  declaring a lifecycle script and it is macOS-only; every native toolchain ships
  per-platform binaries as optional dependencies instead. `npm audit` reported zero
  vulnerabilities.

### Learned, and not anticipated by this document

- **A missing `wwwroot` fails in the opposite direction from the one assumed.** A genuinely
  fresh build with no `wwwroot` boots fine: `/health` 200, `/scalar/` 200, `/` 404, no
  exception. But a tree built *with* `wwwroot` and then stripped of it crashes at
  `WebApplication.CreateBuilder` — **before any middleware runs** — with
  `DirectoryNotFoundException` from `StaticWebAssetsLoader`, which reads the
  `.staticwebassets.runtime.json` manifest generated at build time and constructs a file
  provider for every content root it names. Not `UseStaticFiles`, and Development-only. No
  fix was added: the failing state requires deleting a directory after building and is not
  reachable by accident, so an MSBuild `MakeDir` target would be machinery guarding nothing.
- **A stale server process can make every check lie.** The browser showed nothing at
  `http://localhost:5000` while `curl /health` inside the container returned 200. This was
  first diagnosed as macOS AirPlay Receiver occupying port 5000 on the host. **That
  diagnosis was wrong**, and the correction matters more than the original claim: the
  process answering on 5000 was a survivor of the *missing-`wwwroot`* test above, which had
  booted with no web root and so served `/health` at 200 and `/` at 404 for its entire life.
  Restoring the directory on disk could not change that - a static file provider is built at
  startup. The proof is that replacing the *container* process fixed it immediately, on the
  same port and the same host. One bug - the verification-hygiene one below - not two.
  *(The AirPlay note in the README stays. It is real macOS behaviour and a reviewer with
  AirPlay Receiver enabled would hit it; it simply is not what happened here.)*
- **The cold-start negotiate 500 did not reproduce.** Phase 1 established that the first
  request after a deploy can arrive before the SDK has opened a server connection to the
  service. This deploy's first load was already green. The window is real but not
  guaranteed, so the README's guidance stands as "expect it, do not panic" rather than "it
  always happens".
- **Verification hygiene:** `setsid cmd &` followed by `kill -- -$!` does not stop the
  process. `setsid` places the child in a *new* process group whose id is the child's own
  pid, so the negative-pid kill matches nothing. This left stale servers holding 5000 and
  5173, which then masked a failed rebind and made a stale process look like a fresh one.
  Kill by the pid actually listening on the port.

### Carried into later phases

- **Phase 3:** the `docs/devcontainer-changes.md` rebuild for Changes A and B is now due —
  this is the phase 2/3 boundary it was scheduled for. `node_modules` sits on the
  `/workspace` bind mount and survives a rebuild.
- **Phase 6:** the dev-server proxy already carries `ws: true` on `/hubs`, so the hub works
  through HMR without further configuration. Phase 1's note still stands: the client must
  tolerate a failed *initial* negotiate, which `withAutomaticReconnect()` does not cover.
- **Phase 7:** static assets go in `src/frontend/public/`, never in `wwwroot`, which is
  untracked build output cleared by `emptyOutDir` on every build.
