# M0 — Implementation Findings

Companion to `m0-plan.md`. Where the plan is what was intended, this file is
what actually happened during implementation: what shipped, where reality
diverged from the plan's assumptions, and what still needs doing. Written
during/after implementation, not invented up front — one entry per finding.

---

## Workstream B — Definitions

`items.json`/`npcs.json` are 1:1 transcriptions of the old
`InMemoryItemRepository`/`InMemoryNpcRepository` builders (verified
field-by-field during transcription, plus fidelity-spot-check tests); the old
builders are deleted. `invocations.json` ships as the empty-array stub the
plan called for. **Q6 resolved as recommended:** embedded resources, not
`wwwroot` fetches — repositories stay synchronous.

## Workstream A — Tick core: scope finding on input buffering

The formal drift-corrected `TickScheduler`/`ITickSource` landed as planned,
with `GameTickService.Loop`'s body otherwise untouched (same `ProcessTick`,
same sequencing). For input buffering, one rule from UI bible §2/§3.2 turned
out to need real machinery and one didn't:

- **Weapon swaps** ("max one swap per tick; extra taps buffer") were a
  genuine gap — multiple same-tick taps used to all resolve instantly,
  last-wins. Implemented: `GameState.TryClaimWeaponSwapSlot()` /
  `PendingWeaponSwapId`, `WeaponShortcutHandler` defers an overflow tap,
  `GameTickService.ProcessTick` applies it at the top of the next tick.
  **Q4 (buffer depth) resolved as assumed:** depth 1, latest tap wins.
- **The general 150ms end-of-tick action buffer** (attack/spec/sip/move) is,
  on inspection, moot in this codebase: Blazor WASM runs UI event handlers
  and the tick loop on the same single logical thread with cooperative
  `async`/`await` yielding, not real parallelism — a tap either lands before
  `ProcessTick` runs (used this tick) or after (next tick), and
  `QueuedAction` already never drops a tap regardless of timing. There is no
  race here to protect against, so no additional buffering code was added
  for that class. `KickMoveAsync` (the click-to-move latency fix) got a
  small guard against double-advancing a move order right at a tick
  boundary, which is the one place timing-adjacent risk actually existed.

## Workstream C — Persistence: cadence hook landed in a different layer

IndexedDB (`persistence.js` + `IndexedDbSaveStore`) replaces localStorage
behind a new `ISaveStore` and a versioned `SaveEnvelope`, with a one-time
migration off the old `duels_save` localStorage key.

Save-cadence reduction landed differently than first sketched:
`GameTickService` (Application layer) can't construct `SaveData` (a Web-layer
type) itself, so instead of a duel-end callback threaded through the tick
service, `GameService.DispatchAsync` skips persistence for the
high-frequency in-duel commands, and `Game.razor`'s existing `OnTickNotify`
(which already detects the dueling→not-dueling transition for other reasons)
now also persists on duel end plus every 10 ticks as an in-duel safety net.
Net effect matches the plan's intent (duel end + periodic safety interval +
immediate for state-changing commands) without a new cross-layer event path.

## Workstream D — Asset manifest

`asset-manifest.json` + `asset-map.md` cover the current PoC assets
(`steel_sword`, 6-piece ranger set) exactly as they existed in `toon.js`'s
old hard-coded dicts; `AssetMapSyncTests` enforces the two files staying in
step. Service worker `CACHE_VERSION` bumped since `ASSET_URLS` gained the
manifest file.

## Verification caveat — build/test not actually run

This session's sandboxed environment has no local .NET SDK, and the egress
proxy returns a policy denial (403) for `builds.dotnet.microsoft.com` —
installing one wasn't possible here (not an environment we should route
around per the proxy's own guidance). Every change was re-read and manually
traced for correctness (types, call sites, DI wiring, existing test fixtures
updated for the new `GameTickService`/`GameService` constructor parameters),
cross-checked programmatically where possible (JSON validity, item/npc
reference integrity, brace balance), but **`dotnet build Duels.sln` and
`dotnet test` have not actually been run.** Per the cleanup rule, that must
happen — via CI or a session with a working SDK — before this is considered
done.
