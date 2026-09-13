# Dev container changes

Two pending changes to `.devcontainer/`, which Claude cannot edit. Both activate on
a **Rebuild Container**, so do them together, **before phase 3 starts**.

- [ ] **Change A** - persist `dotnet user-secrets` across rebuilds (optional)
- [ ] **Change B** - let the dev container reach Azure SQL (needed for the phase 3 -> 4
      migration flip)

The background for both is in `docs/decisions.md` under *Deployment and operations*.

There is also a **Change C**, below, which is **not pending**: it is a contingency for
reaching Azure SignalR from inside the container, written down during phase 1 and
deliberately not applied. Do it only if a deploy's negotiate misbehaves.

> **Read the corrected B5 before running `init-firewall.sh` by hand.** It previously
> described the script as safe to re-run; inside a running container that can leave you
> with no outbound network.

---

## Change A - persist `dotnet user-secrets` across rebuilds

**Why.** `dotnet user-secrets` writes to
`~/.microsoft/usersecrets/<UserSecretsId>/secrets.json`. That path is not mounted, so
it lives in the container's writable layer and is destroyed by every rebuild. From
phase 3 on it holds the JWT signing key, the seed admin credentials, and - after the
migration flip - the Azure SQL connection string.

**Optional.** The cost of skipping it is re-adding those secrets after each rebuild.

### A1. `.devcontainer/docker-compose.yml` - `app` service volumes

Before:

```yaml
    volumes:
      - ..:/workspace:cached
      - claude-code-config:/home/containerdev/.claude
      - claude-code-bashhistory:/commandhistory
      - nuget-packages:/home/containerdev/.nuget/packages
```

After:

```yaml
    volumes:
      - ..:/workspace:cached
      - claude-code-config:/home/containerdev/.claude
      - claude-code-bashhistory:/commandhistory
      - nuget-packages:/home/containerdev/.nuget/packages
      - dotnet-usersecrets:/home/containerdev/.microsoft
```

### A2. `.devcontainer/docker-compose.yml` - top-level `volumes:` block

Before:

```yaml
volumes:
  claude-code-config:
  claude-code-bashhistory:
  nuget-packages:
  mssql-data:
```

After:

```yaml
volumes:
  claude-code-config:
  claude-code-bashhistory:
  nuget-packages:
  mssql-data:
  dotnet-usersecrets:
```

### A3. `.devcontainer/Dockerfile` - the `mkdir -p` / `chown -R` RUN (around line 61)

**Not optional if you do A1/A2.** A named volume mounted at a path that does not exist
in the image is created **owned by root**, and `dotnet user-secrets set` then fails
with EACCES - the same class of problem as the `.npm-global` note already in that file.

Before:

```dockerfile
RUN mkdir -p /workspace /home/${USERNAME}/.claude /home/${USERNAME}/.nuget/packages \
      /home/${USERNAME}/.npm-global /home/${USERNAME}/.npm && \
  chown -R ${USERNAME}:${USERNAME} /workspace /home/${USERNAME}/.claude \
      /home/${USERNAME}/.nuget /home/${USERNAME}/.npm-global /home/${USERNAME}/.npm
```

After:

```dockerfile
RUN mkdir -p /workspace /home/${USERNAME}/.claude /home/${USERNAME}/.nuget/packages \
      /home/${USERNAME}/.npm-global /home/${USERNAME}/.npm /home/${USERNAME}/.microsoft && \
  chown -R ${USERNAME}:${USERNAME} /workspace /home/${USERNAME}/.claude \
      /home/${USERNAME}/.nuget /home/${USERNAME}/.npm-global /home/${USERNAME}/.npm \
      /home/${USERNAME}/.microsoft
```

### A4. Verify after the rebuild

```bash
ls -ld /home/containerdev/.microsoft
# must be owned by containerdev, not root

touch /home/containerdev/.microsoft/.wtest && rm /home/containerdev/.microsoft/.wtest && echo OK
```

**Timing.** The rebuild that activates this also wipes the old writable layer. Do it
before storing any secrets and nothing is ever lost.

---

## Change B - let the dev container reach Azure SQL

**Why.** Needed for the phase 3 -> 4 migration flip, when migrations start being applied
from inside the container with `dotnet ef database update` instead of
`Database.Migrate()` at startup. Not needed before that.

### B1. `.devcontainer/init-firewall.sh` - the domain loop (around line 76)

**Careful:** the final entry carries `; do`, so insert the new line *above* it.

Before:

```bash
for domain in \
    "registry.npmjs.org" \
    "api.anthropic.com" \
    "sentry.io" \
    "marketplace.visualstudio.com" \
    "vscode.blob.core.windows.net" \
    "update.code.visualstudio.com" \
    "dist.nuget.org" \
    "api.nuget.org" \
    "globalcdn.nuget.org"; do
```

After:

```bash
for domain in \
    "registry.npmjs.org" \
    "api.anthropic.com" \
    "sentry.io" \
    "marketplace.visualstudio.com" \
    "vscode.blob.core.windows.net" \
    "update.code.visualstudio.com" \
    "dist.nuget.org" \
    "api.nuget.org" \
    "YOURSERVER.database.windows.net" \
    "globalcdn.nuget.org"; do
```

### B2. Do **not** add it to `REQUIRED` (around line 69)

`REQUIRED=(...)` domains abort the whole script on a DNS failure, which would leave the
firewall half-configured and the container with no network. Azure SQL stays optional:
a failed resolve only warns and stays blocked. Leave that line untouched.

### B3. No port rule needed

The allow rule is `iptables -A OUTPUT -m set --match-set allowed-domains dst -j ACCEPT`
- all ports to allowed IPs. Port 1433 is covered automatically.

### B4. Azure side (portal, one-time)

SQL **server** -> Security -> Networking -> add a firewall rule for **your host's public
IP**. This is separate from "Allow Azure services", which covers the Web App, not your
laptop. The rule needs updating whenever your ISP reassigns your address.

### B5. Test without a rebuild

> **Corrected 2026-09-13 (phase 1).** This section previously said the script "destroys
> and recreates its ipset on each run, so it is safe to run repeatedly". That is true
> only from a **fresh container start**. Re-running it inside a **running** container
> can take the container off the network. Read the warning below before running it.

**Why re-running is not automatically safe.** `iptables -F` (line 9) deletes rules but
**does not reset chain policies**. After any previous successful run the `OUTPUT` policy
is already `DROP`, so the moment the script flushes, you have `OUTPUT DROP` with zero
allow rules. The script re-adds only DNS, SSH and loopback (lines 28-38) and then, at
line 44, runs:

```bash
gh_ranges=$(curl -s https://api.github.com/meta)
```

That is outbound TCP 443, which nothing permits yet, so it returns empty and the script
exits at its own `ERROR: Failed to fetch GitHub IP ranges` guard — **leaving the
container with no outbound network.** On container start this never happens, because a
fresh network namespace has policy `ACCEPT`.

**Restore the precondition the script assumes, then run it:**

```bash
sudo iptables -P INPUT ACCEPT; sudo iptables -P OUTPUT ACCEPT; sudo iptables -P FORWARD ACCEPT
sudo bash /workspace/.devcontainer/init-firewall.sh
```

This leaves the firewall open for the few seconds the script takes, which is acceptable
as a deliberate, supervised action. **If it ever does abort mid-way, the recovery is the
same three `-P ... ACCEPT` commands**, or stopping and starting the container.

Then:

```bash
timeout 5 bash -c 'cat < /dev/null > /dev/tcp/YOURSERVER.database.windows.net/1433' \
  && echo REACHABLE || echo BLOCKED
```

### B6. A rebuild is still required to make it stick

`devcontainer.json` runs `sudo /usr/local/bin/init-firewall.sh` as `postStartCommand`,
and the Dockerfile has `COPY init-firewall.sh /usr/local/bin/` (around line 91). **The
script that runs on container start is the copy baked into the image**, so editing
`.devcontainer/init-firewall.sh` has no effect until the image is rebuilt.

### B7. Known caveats

- **Leave the SQL server's connection policy at `Default`.** From outside Azure that
  means Proxy - all traffic through the regional gateway on 1433, which is the IP this
  allowlist resolves. Under **Redirect**, clients connect to backend nodes on ports
  11000-11999 at addresses the ipset has never seen, and this approach stops working.
- **Gateway IPs rotate.** The script resolves once per container start, so a rotation
  mid-session presents as "it timed out and I changed nothing." Fix: restart the
  container.

---

## Change C (contingency only) - let the dev container reach Azure SignalR

**Do not do this pre-emptively.** Phase 1 deliberately skipped it: the deployed app is
the definitive test of Azure SignalR, and it proved the service works. This entry exists
so that *if* a future deploy's negotiate misbehaves, the bisection step is already
written down.

**What it buys.** Negotiate has three outcomes (see the table in `README.md`). Two of
them - "connection string not read" and "read but no server connection" - look alike
from a browser and have unrelated causes. Reaching the service from inside the container
separates "the connection string or the resource is wrong" from "App Service
configuration or WebSockets is wrong", which the deployed app alone cannot do.

**What it does not buy.** It cannot test App Service's WebSockets setting, TLS
termination, or whether the setting landed in the *Application settings* tab rather than
the *Connection strings* tab. Those are Azure-side and only a deploy exercises them.

### C1. `.devcontainer/init-firewall.sh` - the domain loop

Exactly as B1, and with the same warning: insert **above** the final entry, which carries
the `; do`. The hostname is the `Endpoint=https://<name>.service.signalr.net` value from
the Azure SignalR connection string - currently `signalr-meetingrooms.service.signalr.net`.

```bash
    "api.nuget.org" \
    "signalr-meetingrooms.service.signalr.net" \
    "globalcdn.nuget.org"; do
```

**Do not add it to `REQUIRED`**, for the reason in B2: a DNS failure there aborts the
script, and per the corrected B5 that can leave the container with no network.

### C2. Apply and check

Follow the corrected procedure in **B5** - reset the chain policies first, then run the
script. Then:

```bash
timeout 5 bash -c 'cat < /dev/null > /dev/tcp/signalr-meetingrooms.service.signalr.net/443' \
  && echo REACHABLE || echo BLOCKED
```

No port rule is needed; the allow rule matches all ports to allowed IPs, and the SDK
connects over `wss://` on 443.

### C3. Run the app with the real connection string

Start it from **your own terminal**, not through Claude, so the connection string never
enters a transcript:

```bash
Azure__SignalR__ConnectionString='<paste>' ASPNETCORE_URLS=http://0.0.0.0:5000 \
  dotnet run --project src/backend/MeetingRooms.Api --no-launch-profile
```

Success is `POST /hubs/schedule/negotiate?negotiateVersion=1` returning a `url`
containing `.service.signalr.net` plus an `accessToken`. Allow a few seconds after
startup: the SDK must establish a server connection first, and until it does, negotiate
returns the cold-start 500 described in `README.md`.

### C4. Caveats

- **This is temporary unless you rebuild.** Per B6, the script that runs on container
  start is the copy baked into the image. Fine for a contingency test; if it turns out
  you want it permanently, fold it in alongside Change B, which needs a rebuild anyway.
- **Endpoint IPs rotate.** The script resolves DNS once per run, so a rotation
  mid-session presents as "it worked, then stopped". Re-run the script.

## How the pieces activate

- `postStartCommand` fires on container **start**, not on reopening the VS Code window.
  If the container never stopped, nothing re-runs.
- Editing `docker-compose.yml` has **no effect on the running container** - mounts are
  fixed at container creation. The current container keeps working unchanged until it
  is recreated.
- Changing the Dockerfile or `init-firewall.sh` needs a full **image rebuild**; changing
  only `docker-compose.yml` volumes needs a **recreate**. "Rebuild Container" in the Dev
  Containers extension covers both.
