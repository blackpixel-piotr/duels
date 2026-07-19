#!/bin/bash
# Ensures the .NET 8 SDK is present so `dotnet build`/`dotnet test` work on
# the first try, regardless of which base image this session's sandbox
# happened to boot from. Only runs on Claude Code on the web — local
# checkouts manage their own toolchain.
#
# Why apt-get and not the official dotnet-install.sh: that script pulls from
# Microsoft's download CDN, which is proxy-blocked in some of these sandboxes
# (same restriction as other external hosts) — apt-get hits the distro's own
# mirrors instead and has been reliable here. See .claude/skills/verify/SKILL.md.
set -uo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
    exit 0
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "session-start: dotnet not found, installing dotnet-sdk-8.0 via apt-get..."
    if apt-get update -qq && apt-get install -y -qq dotnet-sdk-8.0; then
        echo "session-start: dotnet-sdk-8.0 installed."
    else
        echo "session-start: WARNING — dotnet-sdk-8.0 install failed; dotnet build/test will not work this session." >&2
    fi
fi

# Warm the NuGet cache so the first `dotnet build` a session runs isn't also
# paying for every package download — safe to re-run, no-ops when current.
if command -v dotnet >/dev/null 2>&1; then
    dotnet restore "$CLAUDE_PROJECT_DIR/Duels.sln" --verbosity quiet \
        || echo "session-start: WARNING — dotnet restore failed (network?); build/test may still work if packages are already cached." >&2
fi
