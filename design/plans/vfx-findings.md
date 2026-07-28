# VFX layer — iteration 1 (movement dust): Implementation Findings

Companion to `vfx-plan.md`. Written during implementation — what actually
happened, where reality diverged from the plan's assumptions, and every
place a number/asset wasn't sourced from a design doc and had to be
flagged rather than invented.

---

## Verification

- `dotnet build Duels.sln`: 0 errors, 0 warnings.
- `dotnet test Duels.sln`: **141/141 passing** (21 Domain, 51 Application,
  69 Infrastructure — unchanged from before this pass). No new tests were
  added — iteration 1 is entirely renderer-side (JS) plus a thin C#
  emission point (`GameTickService`'s already-tested
  `playerMovedThisTick`/movement path) with no new branching logic worth a
  dedicated `GameState`/`GameTickService` unit test; existing movement/tick
  suites (`RangeAndMovementTests`, etc.) exercise the code path the new
  `AppendVfxEvent` call sits inside and would catch a regression there.
- **Browser verification (Playwright, per the `verify` skill)**: launched
  the real app, new character → T1 dev loadout → Boss Roster → Maggot King
  → drove the player around the arena with repeated ground clicks.
  - Confirmed via a debug hook (`vfx._debugLiveParticles()`, a Playwright
    probe mirroring the project's existing `_battles`/`_previews` debug
    handles) that live particle count rises to exactly 10-15 while walking
    (2-3 pooled dust-puff bursts × 5 particles, matching the "3 concurrent
    per entity" pool-size cap) and decays to **0** within ~1s of standing
    still.
  - Confirmed via direct scene-graph inspection (reading the three.quarks
    batch's raw `geometry.attributes.offset` instanced buffer) that spawned
    particles land within ~0.6 world units of the player's live rendered
    position — i.e. at their feet, not at some stale or default location.
  - Confirmed visually: dust puffs render as a soft, low-opacity dark patch
    trailing the player's movement (screenshots taken mid-walk).
  - Zero new console errors. The only two errors present are the project's
    known pre-existing dev-mode noise (`Duels.Web.styles.css` 404, Google
    Fonts blocked by the proxy) — confirmed by URL, not just message text.
  - Existing combat visuals (hitsplats, HP bars, HUD, attack animations)
    all rendered normally throughout — no regression observed.
  - Not exercised: enemy/NPC movement dust (out of scope, player-only this
    iteration), the idle→walk→idle transition on every possible camera
    angle, real-device performance (headless Chromium only, ~12fps per the
    `verify` skill's own notes).

### Two real bugs found and fixed during this verification pass

Both were invisible to the unit-test suite and to `dotnet build` (JS-only,
runtime-observable behavior) — exactly what the browser-verification step
exists to catch.

1. **Pooled emitters reused a stale `matrixWorld`, spawning particles at
   the wrong world position.** three.quarks' `ParticleSystem` only forces
   `emitter.updateWorldMatrix()` once, ever, per instance (its own
   `firstTimeUpdate` flag) — `emit()` otherwise reads whatever
   `matrixWorld` happens to already be cached. Since `vfx.js` repositions
   and restarts the *same 3 pooled instances* for every burst (see the
   pooling design in the plan), every burst after each slot's very first
   one was spawning at a one-frame-stale position. `_debugLiveParticles()`
   still reported correct counts (the burst genuinely fired), which is why
   this wasn't caught by the particle-count telemetry alone — only
   directly reading the instanced `offset` buffer's actual world
   coordinates (compared against `st.player.pos`) surfaced it. Fixed by
   calling `slot.system.emitter.updateWorldMatrix(true, false)`
   immediately after repositioning and before `.restart()` in
   `spawnBurst()` (`vfx.js`).
2. **Additive blending on a dark earth-tone color is close to invisible.**
   `--border` (`#4a3d2e`) is a dark brown; additive blending *adds* the
   particle's RGB onto whatever's behind it, so a dark-near-black color
   contributes almost nothing regardless of size, count, or the fix above
   — this was masking bug #1 in early screenshots even after position was
   corrected. The architecture plan's "fake glow via additive blending"
   note (CLAUDE.md doctrine section) is about effects that *should* glow
   (sparks, magic) — a dust puff is the opposite case (an opaque-ish cloud
   that should read as a translucent smudge, not a light source). Switched
   `vfx-manifest.json`'s `dust_puff` row from `"blend": "additive"` to
   `"blend": "normal"` (the field/branch already existed in `vfx.js`'s
   `buildBurstSystem`, unused until now); confirmed visible in screenshots
   immediately after. `vfx-plan.md`'s architecture note about additive
   blending stands for future glow-type effects — this is a correction to
   iteration 1's own manifest row, not to the general doctrine.

---

## Design decisions made during implementation (not fully specified by the
brief, flagged here per CLAUDE.md rather than resolved silently)

1. **`vfxEvents` lives per-tick-and-cleared on `GameState`, not as an
   ever-growing cursor-read log like `CombatLog`.** The brief said
   "appended by the C# sim per tick... Empty array = omitted," which reads
   as "this tick's array," not a permanent log. `CombatLog`'s append-
   forever-plus-cursor shape exists specifically so a coalesced Blazor
   render never misses a hitsplat; VFX's own doctrine line ("can be
   disabled with zero gameplay impact") means the opposite tradeoff is
   correct here — an occasionally-dropped dust puff under render pressure
   is fine. Reusing the `CombatLog` cursor pattern would have meant either
   an unbounded list or a FIFO-trimmed one with a cursor that has to be
   reconciled against the trim (a subtlety `CombatLog` itself doesn't
   fully handle — its own cursor isn't decremented when `AppendLog` trims
   at 500 entries, though that's a pre-existing latent issue, not
   something this pass touched or needed to fix, since VFX doesn't reuse
   that shape).

2. **Two C# call sites emit `entity_moved`, not one.** The plan only
   named `GameTickService.ProcessTick`'s `playerMovedThisTick` (the
   ordinary per-tick walk step). While wiring it, `KickMoveAsync` — the
   "step on the click, not next tick" immediate-move path fired from
   `BattleScene.razor`'s `OnGroundClick` — turned out to move the
   player's tile through the exact same `SetPlayerTile` mechanism outside
   `ProcessTick`. Leaving it unwired would have meant the very first step
   after a ground-tap produced no dust while every subsequent tick-driven
   step did — a visible inconsistency for "one puff per tile entered,"
   not a new feature. Added the same `AppendVfxEvent` call there,
   per-step within its existing loop (see `GameTickService.cs`).

3. **three.quarks pinned to `0.12.1`, not latest (`0.17.1`).** Verified via
   `registry.npmjs.org/three.quarks`'s per-version `peerDependencies`: our
   vendored `three.module.min.js` is r160 (`0.160.0`); `0.12.1`'s peer
   floor is `>=0.157.0` (satisfied), `0.12.2`'s jumps to `>=0.182.0` (not
   satisfied). This is the newest version actually compatible with this
   repo's three.js, confirmed against the declared peer range — not
   independently smoke-tested against every internal three.js API it
   touches (that's what the browser verification below is for).

4. **Packaging quirk**: `three.quarks@0.12.1`'s `dist/
   three.quarks.esm.min.js` is mislabeled — it's the UMD/global bundle,
   not real ESM (checks `typeof exports`/`typeof define`, falls back to a
   `THREE.QUARKS` global). Vendored the genuine (unminified) `dist/
   three.quarks.esm.js` instead, and rewrote its one bare `from 'three'`
   import to the relative `./three.module.min.js` — matching how every
   other vendored lib in `wwwroot/lib/` already imports three, rather than
   introducing this project's first import map for one file.

5. **Kenney particle-pack textures are blocked, not fetched.** `kenney.nl`
   and `itch.io` both return 403 from this sandbox's egress proxy (org
   policy, confirmed via direct `curl` — the proxy's own README says not
   to route around a 403/407, only report it). No GitHub mirror was
   substituted either: this session's GitHub access is scoped to
   `blackpixel-piotr/duels` only, and guessing an unverified third-party
   mirror for a CC0-attribution asset felt like the wrong tradeoff versus
   a clearly-flagged placeholder. Iteration 1 instead generates a small
   soft-circle sprite at runtime (`vfx.js`'s `makeProceduralDustTexture`,
   a `THREE.CanvasTexture` off a radial-gradient `<canvas>`) — visually
   fine for a puff, explicitly not the sourced asset. Logged in
   `backlog.md` (#40) with the exact swap point (`resolveTexture` in
   `vfx.js`).

6. **Dust color reuses `--border` (sandstone, `#4a3d2e`), not a doctrine
   token.** The doctrine paragraph (`CLAUDE.md`) says VFX "always use
   doctrine color tokens," but the 7 existing ones
   (`--doctrine-{melee,ranged,magic,hazard,safe,poison,bleed}`) are all
   combat-meaning colors. Dust carries no gameplay information — binding
   it to a doctrine color would make it look like it means something it
   doesn't. Read `--border` (an existing neutral/earthy token, not a new
   hex value) instead, satisfying the "token, never raw hex" half of the
   rule while treating the "doctrine" half as scoped to effects that
   actually encode style/state. Flagged per CLAUDE.md's "flag design
   ambiguities as questions; never resolve them silently" rather than
   picking a token silently.

7. **Per-effect emitter pooling doubles as the "concurrent" cap** — not a
   separate counter. `ParticleSystem.restart()` (the call used to fire a
   new burst on a reused instance) kills that instance's own still-alive
   particles, so a fixed pool of 3 `ParticleSystem`s round-robined for
   `dust_puff` naturally enforces "3 concurrent per entity": a 4th event
   arriving while all 3 are still fading reuses (and kills) the oldest of
   the 3, which *is* the cap, not an approximation of it. Simpler than
   tracking a separate live-count per entity and made the "capped at 3
   concurrent" requirement fall out of the pool size rather than needing
   its own enforcement code.

8. **Global particle budget (300) is untested by real load this pass** —
   see `backlog.md` #41. Iteration 1's worst case (3 pooled dust systems ×
   up to 10 particles headroom each) never approaches it, so the
   auto-cull-oldest-across-all-effects path exists but won't actually run
   until a second effect ships.

---

## What iteration 2+ inherits for free

- `vfx-manifest.json` already supports arbitrary additional rows — a new
  event type is a new manifest row plus a new `AppendVfxEvent` call site
  in C#, no `vfx.js` core changes expected (the pooling/budget/quality
  machinery is already generic per-effectId).
- `api.setVfxQuality` exists and is wired through to `vfx.js`'s
  `handleEvents`/`update` short-circuits; only a UI control is missing.
- The "positioned via the interpolation layer" pattern (reading
  `st.player.pos`/`st.enemy.pos` at event-handling time rather than the
  raw sim tile) generalizes to the enemy/NPC side without change — the
  handler already takes a `positions` map keyed by entity id.
