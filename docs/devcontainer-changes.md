# Dev container changes

Changes to `.devcontainer/`, which Claude cannot edit. This file is the record of what has
been applied and what is still outstanding.

**Status as of 2026-09-13 (the phase 2/3 boundary): everything needed is applied and
verified.** One optional item remains, and it is Azure-side rather than container-side.

---

## Applied 2026-09-13, in one rebuild

All four edits went in together and activated on a single **Rebuild Container**. Verified
afterwards with the checks recorded under each.

### 1. `dotnet user-secrets` survives rebuilds  *(was "Change A")*

**Why.** `dotnet user-secrets` writes to `~/.microsoft/usersecrets/<UserSecretsId>/`. That
path was on the container's writable layer, which every rebuild destroys. From phase 3 on it
holds the JWT signing key and the seed admin credentials.

- `.devcontainer/docker-compose.yml` — `dotnet-usersecrets:/home/containerdev/.microsoft`
  added to the `app` service volumes, and `dotnet-usersecrets:` to the top-level block.
- `.devcontainer/Dockerfile` — `/home/${USERNAME}/.microsoft` added to the `mkdir -p` and
  `chown -R`. **Not optional:** a named volume mounted where the image has no directory is
  created **root-owned**, and `dotnet user-secrets set` then fails with EACCES. Creating the
  path in the image first means the fresh volume inherits `containerdev` ownership. This is
  the same reason `.npm-global` and `.nuget` are pre-created.

*Verified:* directory owned by `containerdev`, write test passes.

### 2. `node_modules` and the npm cache move off the host bind mount

**Why.** `/workspace` is a bind mount to the Mac, so `node_modules` was 92 MB of small files
being written through the host filesystem. Moving it to a named volume keeps it out of the
host tree entirely and off the slower I/O path. `~/.npm` came along for free: without it,
every rebuild re-downloads the whole tree from the registry.

- `.devcontainer/docker-compose.yml` — `frontend-node-modules:/workspace/src/frontend/node_modules`
  and `npm-cache:/home/containerdev/.npm`, plus both names in the top-level block.
- `.devcontainer/Dockerfile` — `/workspace/src/frontend/node_modules` added to the
  `mkdir -p`, for the ownership reason in §1. `chown -R … /workspace` already covered it.

**The step that is easy to miss:** a new named volume starts **empty** and *shadows* the host
directory rather than replacing it. The host copy must be deleted **before** the rebuild, or
you end up with an empty `node_modules` in the container and the original 92 MB orphaned and
invisible on the Mac. Afterwards, repopulate with `npm ci --ignore-scripts` — which installs
the committed lockfile exactly, so the tree is identical rather than re-resolved.

*Verified:* `/proc/mounts` shows `ext4`, not `/run/host_mark/Users`; owned by `containerdev`;
`npm ci` and `npm run build` both succeed. The rebuilt bundle had **identical asset hashes**
to the pre-rebuild build, which is a clean reproducibility check on the lockfile.

**Consequence:** the container is now the only place `node_modules` exists. Opening this repo
on the Mac outside the container leaves editor tooling with nothing to resolve against.

**`bin/` and `obj/` were deliberately left on the bind mount.** Only ~6.7 MB across four
projects, and moving them needs either a hardcoded path per project — which phase 5's test
project would break — or an `ArtifactsPath` change relocating every build output immediately
before the graded phases. Not worth the risk for the size.

### 3. Azure SignalR reachable from the container  *(was "Change C", a contingency)*

Promoted from contingency to applied. The original reason to defer was "do not do unneeded
work", but the container was being rebuilt anyway, so the marginal cost was one line — and
it removes a rebuild from the critical path if phase 6 misbehaves.

- `.devcontainer/init-firewall.sh` — `"signalr-meetingrooms.service.signalr.net"` added to
  the domain loop, **above** the final entry, which carries the `; do`.

**What it buys.** Negotiate has three outcomes (see the table in `README.md`); two of them —
"connection string not read" and "read but no server connection" — look identical from a
browser and have unrelated causes. Reaching the service from here separates "the connection
string or the resource is wrong" from "App Service configuration is wrong".

**What it does not buy.** It cannot test App Service's WebSockets setting, TLS termination,
or whether a setting landed in the wrong portal tab. Those are Azure-side; only a deploy
exercises them.

*Verified:* port 443 reachable.

### 4. Azure SQL reachable from the container  *(the container half of "Change B")*

- `.devcontainer/init-firewall.sh` — `"sql-meetingrooms-test-task.database.windows.net"`
  added to the same loop, same position.

Taken now for the same reason as §3: `init-firewall.sh` is `COPY`d into the image
(`Dockerfile:91`), so the running copy is baked in and a later edit would cost another
rebuild. **The migration flip itself is deferred** — see `docs/decisions.md` — but the
container side is now done, so it costs no rebuild whenever it happens.

*Verified:* port 1433 reachable. Reachable is not the same as usable — Azure will still
refuse the login until the outstanding item below is done.

### Neither new domain was added to `REQUIRED`

`REQUIRED=(…)` on line 69 aborts the whole script on a DNS failure, which would leave the
firewall half-configured and the container with no network. Both new domains stay optional:
a failed resolve only warns and stays blocked for that container's lifetime. If a probe ever
reports BLOCKED, restart the container to re-resolve — that is expected behaviour, not a
broken config.

---

## Outstanding

### Azure SQL server firewall rule

SQL **server** → Security → Networking → add a firewall rule for **your host's public IP**.
This is separate from "Allow Azure services", which covers the Web App, not your laptop.

**Confirmed needed, 2026-09-14.** The dev container and the Mac leave through the same address,
and Azure names it in the error when either tries to connect:

```
Cannot open server 'sql-meetingrooms-test-task' requested by the login.
Client with IP address '<your public IP>' is not allowed to access the server.
```

Wanted for two things, neither urgent: the deferred migration flip, and inspecting the deployed
database by hand - `dotnet ef migrations list --connection ...` from here, or DataGrip/Rider
from the Mac. Still deferred by choice, which also avoids its upkeep: the rule breaks whenever
the ISP reassigns the address.

Two caveats that still apply when you do it:

- **Leave the server's connection policy at `Default`.** From outside Azure that means
  Proxy — all traffic through the regional gateway on 1433, which is the IP this allowlist
  resolves. Under **Redirect**, clients connect to backend nodes on ports 11000-11999 at
  addresses the ipset has never seen, and this approach stops working.
- **Gateway IPs rotate.** The script resolves once per container start, so a rotation
  mid-session presents as "it timed out and I changed nothing". Fix: restart the container.

---


### Certificate revocation endpoints, for connecting to Azure SQL from here

Only needed alongside the rule above, and only for connections made **from the container**.

```
crl3.digicert.com:80  BLOCKED
ocsp.digicert.com:80  BLOCKED
```

`Microsoft.Data.SqlClient` checks certificate revocation during the TLS handshake. The root it
needs is installed (`DigiCert_Global_Root_G2.pem`), but neither revocation endpoint is on the
allowlist, so the fetch hangs until it gives up - somewhere between 25 and 60 seconds. A normal
connection timeout expires first, and the failure is reported as
`error: 35 - An internal exception was caught`, whose inner exception is
`Win32Exception (258): The connection attempt timed out` - naming neither certificates nor the
firewall. Raw TCP to port 1433 connects in 50 ms throughout, which makes the message actively
misleading.

- **Workaround, no rebuild:** `TrustServerCertificate=True` **and** `Connection Timeout=60`.
  Trusting the certificate decides the outcome once the fetch gives up; the long timeout is what
  survives the wait. Verified 2026-09-14: with both, a deliberately wrong login reached the
  server and was rejected as `Login failed for user`, which is the whole path working.
- **Proper fix:** add `crl3.digicert.com` and `ocsp.digicert.com` to the domain loop in
  `.devcontainer/init-firewall.sh`, which costs a rebuild and removes both the stall and the
  need to skip validation.

Neither affects the deployed application, whose egress is unrestricted, nor the local `db`
container, which is reached without TLS.

---

## Reference: running `init-firewall.sh` by hand

> **Re-running it inside a live container is not automatically safe.** It is safe from a
> **fresh container start**, where chain policies are `ACCEPT`. Inside a running container it
> is not.

**Why.** `iptables -F` (line 9) deletes rules but **does not reset chain policies**. After any
previous successful run the `OUTPUT` policy is already `DROP`, so the moment the script
flushes you have `OUTPUT DROP` with zero allow rules. The script re-adds only DNS, SSH and
loopback, and then at line 45 runs:

```bash
gh_ranges=$(curl -s https://api.github.com/meta)
```

That is outbound TCP 443, which nothing permits yet, so it returns empty and the script exits
at its own `ERROR: Failed to fetch GitHub IP ranges` guard — **leaving the container with no
outbound network.** On container start this never happens, because a fresh network namespace
has policy `ACCEPT`.

**Restore the precondition the script assumes, then run it:**

```bash
sudo iptables -P INPUT ACCEPT; sudo iptables -P OUTPUT ACCEPT; sudo iptables -P FORWARD ACCEPT
sudo bash /workspace/.devcontainer/init-firewall.sh
```

This leaves the firewall open for the few seconds the script takes, which is acceptable as a
deliberate, supervised action. **If it ever aborts mid-way, the recovery is the same three
`-P … ACCEPT` commands**, or stopping and starting the container.

**Editing the script requires a rebuild to take effect.** `devcontainer.json` runs
`sudo /usr/local/bin/init-firewall.sh` as `postStartCommand`, and the Dockerfile has
`COPY init-firewall.sh /usr/local/bin/` — the script that runs on container start is the copy
baked into the image.

---

## Reference: what survives what

| What | Lives in | Window reopen | Container restart | **Rebuild** |
|---|---|:---:|:---:|:---:|
| Repo files, incl. `.devcontainer/*` | bind mount `/workspace` | yes | yes | yes |
| `~/.claude`, `~/.nuget/packages`, `~/.microsoft`, `~/.npm`, `node_modules`, `/commandhistory`, `mssql-data` | named volumes | yes | yes | yes |
| `~/.dotnet/tools`, anything else in `$HOME` | container writable layer | yes | yes | **no** |
| iptables rules | kernel netns | yes | re-run by `postStartCommand` | re-run |

Named volumes are removed only by `docker compose down -v` or deleting them by hand — not by
Rebuild Container. Claude Code session transcripts live in `~/.claude/projects/`, so they
survive a rebuild and can be reopened with `claude --resume`.

**The design rule this implies:** prefer the repo over the container. Anything under
`/workspace` survives unconditionally. That is why the `dotnet-ef` tool is a **local** tool
manifest (`.config/dotnet-tools.json`, committed) rather than a global install — see
`docs/decisions.md`.

## How the pieces activate

- `postStartCommand` fires on container **start**, not on reopening the VS Code window. If
  the container never stopped, nothing re-runs.
- Editing `docker-compose.yml` has **no effect on the running container** — mounts are fixed
  at container creation.
- Changing the Dockerfile or `init-firewall.sh` needs a full **image rebuild**; changing only
  `docker-compose.yml` volumes needs a **recreate**. "Rebuild Container" covers both.
- Every named volume needs **two** entries: the mount on the service, and a declaration in
  the top-level `volumes:` block. Referencing one without declaring it makes Compose refuse
  to start the project.
