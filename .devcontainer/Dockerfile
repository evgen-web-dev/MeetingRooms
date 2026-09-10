# .NET SDK straight from Microsoft's registry (Ubuntu 24.04 "Noble"). Native: no
# install script, no feature, nothing from ghcr.io. This IS the base image.
FROM mcr.microsoft.com/dotnet/sdk:10.0

ARG TZ
ENV TZ="$TZ"
ARG USERNAME=containerdev
# The real pin lives in docker-compose.yml (build.args) - this default only
# applies to a bare `docker build` with no args, and is deliberately floating
# so such a build still works. Do NOT pin here as well: two sources for one
# value means the stale one eventually lies about what's installed.
ARG CLAUDE_CODE_VERSION=stable

# Defensive: SDK images build as root, but assert it in case a tag ever changes.
USER root

# Only what's actually needed: firewall tooling (iptables/ipset/ip/dig/aggregate),
# curl + ca-certificates (the Claude installer AND init-firewall.sh's GitHub-IP fetch
# both use curl), git, and a couple of basics. No zsh/fzf/git-delta - those were
# ergonomics and are gone. ICU already ships in the .NET image, so no libicu here.
RUN apt-get update && apt-get install -y --no-install-recommends \
  ca-certificates curl git sudo less procps jq nano xz-utils \
  iptables ipset iproute2 dnsutils aggregate \
  && apt-get clean && rm -rf /var/lib/apt/lists/*

# Node.js for the frontend (src/frontend). Official tarball, checksum-verified -
# not nvm (fetches at runtime, blocked by the firewall, and exists only as a shell
# function invisible to RUN steps and Claude's Bash tool) and not NodeSource's
# curl|bash. Lands in /usr/local/bin, so node/npm are on PATH in every shell.
# Downloaded at BUILD time (before the firewall runs), so nodejs.org needs no allowlist entry.
ARG NODE_VERSION=24.21.0
RUN ARCH="$(dpkg --print-architecture)" && \
    case "$ARCH" in \
      arm64) NODE_ARCH=arm64 ;; \
      amd64) NODE_ARCH=x64 ;; \
      *) echo "Unsupported arch: $ARCH" >&2; exit 1 ;; \
    esac && \
    curl -fsSLO "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-${NODE_ARCH}.tar.xz" && \
    curl -fsSLO "https://nodejs.org/dist/v${NODE_VERSION}/SHASUMS256.txt" && \
    grep " node-v${NODE_VERSION}-linux-${NODE_ARCH}.tar.xz\$" SHASUMS256.txt | sha256sum -c - && \
    tar -xJf "node-v${NODE_VERSION}-linux-${NODE_ARCH}.tar.xz" -C /usr/local --strip-components=1 --no-same-owner && \
    rm "node-v${NODE_VERSION}-linux-${NODE_ARCH}.tar.xz" SHASUMS256.txt && \
    node --version && npm --version

# Ubuntu 24.04 ships a user/group at UID/GID 1000; reclaim it for the non-root
# 'containerdev' user. Written to handle the image having it or not.
RUN if getent passwd 1000 >/dev/null 2>&1; then userdel -r "$(getent passwd 1000 | cut -d: -f1)" 2>/dev/null || true; fi && \
  if getent group 1000 >/dev/null 2>&1; then groupdel "$(getent group 1000 | cut -d: -f1)" 2>/dev/null || true; fi && \
  groupadd --gid 1000 ${USERNAME} && \
  useradd --uid 1000 --gid 1000 -m -s /bin/bash ${USERNAME}

ENV DEVCONTAINER=true

# Persist interactive shell history across rebuilds (handy for auditing what ran).
RUN mkdir /commandhistory && touch /commandhistory/.bash_history && \
  chown -R ${USERNAME} /commandhistory && \
  echo "export PROMPT_COMMAND='history -a' && export HISTFILE=/commandhistory/.bash_history" >> /home/${USERNAME}/.bashrc

# .npm-global exists because npm's default global prefix is /usr/local (root-owned),
# so `npm i -g` and some npx paths fail with EACCES as the non-root user.
RUN mkdir -p /workspace /home/${USERNAME}/.claude /home/${USERNAME}/.nuget/packages \
      /home/${USERNAME}/.npm-global /home/${USERNAME}/.npm && \
  chown -R ${USERNAME}:${USERNAME} /workspace /home/${USERNAME}/.claude \
      /home/${USERNAME}/.nuget /home/${USERNAME}/.npm-global /home/${USERNAME}/.npm

WORKDIR /workspace

# .NET behavior only - DOTNET_ROOT and dotnet-on-PATH already come from the base image.
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1
ENV DOTNET_GENERATE_ASPNET_CERTIFICATE=false
ENV NUGET_PACKAGES=/home/${USERNAME}/.nuget/packages
ENV NPM_CONFIG_PREFIX=/home/${USERNAME}/.npm-global
ENV PATH=$PATH:/home/${USERNAME}/.dotnet/tools:/home/${USERNAME}/.local/bin:/home/${USERNAME}/.npm-global/bin
ENV EDITOR=nano

# Set HOME explicitly: `USER` alone doesn't reliably set it, and the installer writes
# to $HOME/.local/bin - without this it could land in the wrong place and fail.
ENV HOME=/home/${USERNAME}

# Claude Code via its own native installer, run as the non-root user so it installs
# into /home/containerdev/.local/bin (not root's home). Self-contained binary - brings its own
# runtime, so this image needs no Node. The installer verifies the downloaded *binary*
# against a SHA-256 manifest; the install script itself is fetched unverified, so the
# trust root is Anthropic (claude.ai) same as the npm package it replaces.
USER ${USERNAME}
RUN curl -fsSL https://claude.ai/install.sh | bash -s "${CLAUDE_CODE_VERSION}"

# Firewall script: root-owned, containerdev allowed to run just this one script via sudo.
USER root
COPY init-firewall.sh /usr/local/bin/
RUN chmod +x /usr/local/bin/init-firewall.sh && \
  echo "${USERNAME} ALL=(root) NOPASSWD: /usr/local/bin/init-firewall.sh" > /etc/sudoers.d/${USERNAME}-firewall && \
  chmod 0440 /etc/sudoers.d/${USERNAME}-firewall
USER ${USERNAME}