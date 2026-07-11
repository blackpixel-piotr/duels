#!/usr/bin/env sh
# Graphify bootstrap for ephemeral cloud/web sessions.
#
# The knowledge graph itself is committed under graphify-out/, but the graphify
# CLI (a pip package) and its Claude skill live outside the repo and are wiped
# when the container is reclaimed. This runs at SessionStart to reinstall them so
# the committed CLAUDE.md directive + PreToolUse hooks actually work.
#
# Safe to run repeatedly and offline: every step is best-effort and never fails
# the session (the PreToolUse hooks no-op when graphify is absent).
set -u

# Install the CLI if it isn't already on PATH (fast no-op once cached).
if ! command -v graphify >/dev/null 2>&1; then
    pip install -q graphifyy >/dev/null 2>&1 || exit 0
fi

# Re-copy the Claude skill (~/.claude/skills/graphify) into this session.
graphify install --platform claude >/dev/null 2>&1 || true

# Refresh the graph if the CLI is present (AST-only, no network/API).
graphify update . >/dev/null 2>&1 || true

exit 0
