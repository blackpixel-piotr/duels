#!/usr/bin/env bash
# Throttles graphify's PreToolUse nudge hooks (search/read) to fire at most
# once per session instead of on every matching Bash/Grep/Read/Glob call.
#
# graphify's own `hook-guard` (see the installed graphify pip package's
# cli.py, _run_hook_guard) has a built-in once-per-session gate, but only for
# its opt-in --strict DENY path (_mark_session_denied) — the ordinary soft
# nudge (additionalContext reminding the agent to run `graphify query`
# first) fires unconditionally on every matching tool call, which was
# spamming ~40 lines of reminder text into the transcript on every message.
# This wraps that same nudge with the same marker-file pattern graphify uses
# for its strict mode, just gating the nudge instead of a block.
#
# Reads the PreToolUse JSON payload once, extracts session_id, and re-feeds
# the SAME payload to the real `graphify hook-guard <kind>` invocation only
# the first time a given session hits this hook; every later call for that
# session exits 0 with no output — silently allowing the tool call through,
# consistent with graphify's own fail-open design (never blocks on error).
set -uo pipefail

kind="${1:-}"
shift || true

if ! command -v graphify >/dev/null 2>&1; then
    exit 0
fi

payload="$(cat)"

session_id="$(printf '%s' "$payload" | python3 -c '
import json, sys
try:
    d = json.load(sys.stdin)
    sid = d.get("session_id") if isinstance(d, dict) else None
except Exception:
    sid = None
print(sid or "")
' 2>/dev/null)"

# No parseable session id — fail open to the real hook every time (matches
# graphify's own fail-open behavior for malformed/absent stdin) rather than
# silently suppressing the nudge forever.
if [ -z "$session_id" ]; then
    printf '%s' "$payload" | graphify hook-guard "$kind" "$@"
    exit 0
fi

safe_id="$(printf '%s' "$session_id" | tr -c 'A-Za-z0-9_-' '_' | cut -c1-64)"
marker_dir="graphify-out/cache/nudge_sessions"
marker="$marker_dir/${safe_id}.${kind}"

if [ -e "$marker" ]; then
    exit 0
fi

mkdir -p "$marker_dir" 2>/dev/null || true
: > "$marker" 2>/dev/null || true

printf '%s' "$payload" | graphify hook-guard "$kind" "$@"
exit 0
