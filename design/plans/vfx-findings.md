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

---

## Post-merge follow-up: bigger/more-frequent movement dust + a live tuning panel

Playtest request: "make ground particles when player is moving bigger...
and more often? any way i can tweak these settings live?" plus, separately,
"add a boss freeze button."

**Bigger/more often**: bumped `dust_puff`'s manifest row (`count` 5→9,
`size` 0.35→0.55, `lifetime` 0.4→0.45s) and gave it its own pool size
(`poolSize: 8` in the manifest row, read via `row.poolSize ?? DUST_POOL_SIZE`
in `vfx.js`'s `makePool` — every other effect keeps the shared default of
3, unaffected) instead of sharing the flat `DUST_POOL_SIZE = 3` constant
finding 7 above described. Movement dust is triggered far more often than
a one-shot combat effect (every tile step, not once per swing), so the old
shared pool size was capping *concurrent* puffs much more aggressively for
this effect than for the others — the fix is a bigger pool specifically
for this row, not a blanket increase everywhere.

**Live tuning**: `createVfxSystem` now exposes `setDustDebug({sizeMult,
countMult, activeSlots})`/`getDustDebug()`, scoped to the `entity_moved`
pool(s). Mutates the already-built `ParticleSystem`s' value-generator
objects in place (`startSize` → a new `IntervalValue`, `emissionBursts[0]
.count` → a new `ConstantValue`) rather than tearing down/rebuilding
systems — the same technique `spawnBurst` already used for `drift`, so
this wasn't a new pattern, just applying the existing one outside the
per-burst hot path. `activeSlots` dials concurrency *within* the pre-built
pool (a cursor cap in `spawnBurst`, `Math.min(pool.activeSlots,
pool.slots.length)`) rather than constructing new `ParticleSystem`s live —
simpler and avoids any `renderer.addSystem`/`group.add` bookkeeping mid-
fight. One real correctness catch found while wiring this up: `maxParticle`
(the `ParticleSystem`'s own hard cap, fixed at construction) was `row.count
* 2` — a live `countMult` above ~2x would have silently truncated the
actual spawned particles below what the slider asked for, with no error or
signal that it happened. Bumped to `row.count * 4` for headroom; the panel
also reads back the *actual* live emission count for the particle-budget
check in `spawnBurst` (previously read the static manifest default, which
would have under-counted budget usage once tuning diverged from it).

A new `VFX ▾` debug panel (`BattleScene.razor`, top row left of `PREFS`,
same always-visible convention as `MECH`) exposes three sliders (dust size
×, count ×, concurrency) plus a Reset, seeded from `voxel.getDustDebug` on
open (same "read, don't assume" reasoning as the `MOVE` panel) rather than
assuming defaults. Deliberately *not* `clientPrefs`-backed like `PREFS`
(`cameraMotion`/`vfxQuality`) — this is dev-tuning-in-the-moment, doesn't
persist across reloads, matching the `TestScene`-only `MOVE`/`CAM` panels'
convention instead (just without their `TestScene` gate).

**Boss freeze button**: `FREEZE ENEMY`/`UNFREEZE` already existed end-to-end
(`EnemyFrozen`/`FreezeEnemyCommand`, gates `ProcessBossScript`/
`ProcessMasterScript` in `GameTickService`) but was gated behind
`GameState.TestScene`, which nothing in the reachable app ever sets —
backlog item 25's exact complaint. Moved it to the same always-visible
convention `MECH`/`PREFS`/the new `VFX` panel already use. `CAM`/`MOVE`
were left `TestScene`-gated — not requested, and backlog #25 already flags
the whole family as needing a real dev gate once non-dev fights ship;
widening only what was asked for keeps that debt from growing further than
necessary. Updated backlog #25 to record this.

**Verified**: `dotnet build`/`test` (69/69, unaffected — the C# side is
untouched; this pass is manifest + JS + one `.razor` file). Playwright
against a live fight: `getDustDebug` read back `{sizeMult:1, countMult:1,
activeSlots:8, maxSlots:8}` on load (matching the new manifest defaults);
calling `setDustDebug({sizeMult:2, countMult:2.5, activeSlots:4})` produced
`{sizeMult:2, countMult:2.556, activeSlots:4}` — the `2.556` isn't drift,
it's `Math.round(9 * 2.5) / 9` (`23/9`), confirming the rounding math
matches the implementation exactly, not just landing close. Separately
confirmed the manifest itself served the new `count:9, size:0.55,
lifetime:0.45, poolSize:8`. The freeze button: confirmed visible outside
`TestScene`, and clicking it flipped both its label (`FREEZE ENEMY` →
`UNFREEZE`) and CSS class (`freeze-btn-on`) — confirms the dispatched
`FreezeEnemyCommand` actually flipped `GameState.EnemyFrozen`, not just a
local UI toggle. (Freezing only gates the *boss's* own actions — the
player's own attacks still land as normal while frozen, which is correct:
the button's contract is "the enemy stops acting," not "combat pauses.")

---

## Post-merge, round 2: the "bigger/more often" tuning overshot — dark smudge + trailing artifacts

User report, after playing with the round-1 tuning above: "theres some
visual artifact spawning now when running related to the vfx but its not
under the player. the dust animation isnt a good enough dust animation.
feels cheap."

**Investigation.** Round 1's numeric verification (getDustDebug round-
tripping correctly) never actually *looked* at the result — it confirmed
the tuning API was wired correctly, not that the tuned values looked good.
This round started by adding a genuine Playwright-visible check: a new
`_debugDustSlots()` probe hook (emitter position + live particles' own
world positions/age/life, mirroring `_debugLiveParticles`'s existing
convention) to rule out an actual position bug before assuming it was a
feel/density problem, then screenshots (with the boss frozen via the new
FREEZE button and `cameraMotion:'off'` so the frame is stable) to see the
effect directly rather than reason about it from data alone — the lesson
from `vfx-findings.md`'s very first round ("a screenshot alone had missed
a real bug") cuts both ways: sampled state alone can also miss a real
*look* problem that only shows up visually.

- **No position-corruption bug found.** `_debugDustSlots()` showed every
  live particle staying within a small, physically-sensible distance of
  its *own* burst's emitter position (growing from ~0.01 units at spawn to
  ~0.15-0.2 by the end of its ~0.35-0.45s life) — exactly what the
  `ForceOverLife` drift + `startSpeed` config should produce. An earlier
  reading of one diagnostic run *appeared* to show the player teleporting
  8+ tiles ahead of the dust within ~200ms — traced to unreliable
  `requestAnimationFrame`-counted timing in headless Chromium (the
  `verify` skill's own "~12fps, GPU-stall" note), not a real discontinuity:
  a follow-up check using real wall-clock `setTimeout` delays instead of
  rAF-frame counts showed the player's rendered position advancing
  smoothly and continuously toward its target, no snap. Recording this
  because it's a real trap in this test harness, not just noise to ignore.
- **Two real, visible problems, confirmed by actually looking at a
  screenshot** (frozen boss, `cameraMotion:'off'`, zoomed/pitched for a
  clear ground-level view — earlier screenshot attempts in this round kept
  missing the ~0.35-0.45s window entirely due to `page.screenshot()`'s own
  round-trip latency; catching it took polling `_debugLiveParticles()` in
  a tight loop and screenshotting the instant it went nonzero):
  1. **The dust rendered as a dark smudge, not light dust.** `colorToken:
     "--border"` (`#4a3d2e`, "sandstone border" — chosen in iteration 1
     specifically as a neutral non-doctrine token, not for its actual
     shade) is a fairly dark brown; multiplied onto the procedural white-
     radial-gradient texture with `NormalBlending` at up to 55% opacity
     against the mid-brightness green ground (`#4a7038`), it read as a
     near-black blob rather than a puff of kicked-up dirt — exactly what
     "feels cheap" would describe. Switched to `--text` (`#d4c5a9`,
     "parchment off-white") — still an existing neutral/non-doctrine
     token (same "token, never raw hex" rule iteration 1 already
     established for this row), just one that's actually the right
     brightness for dust.
  2. **Particles sank visibly below the ground plane.** `PointEmitter`'s
     `initialize()` (read directly in the vendored three.quarks source)
     gives every particle a fully spherical random initial velocity
     direction — roughly half of any burst launches with a *downward* Y
     component — against which the shared upward counter-force
     (`ForceOverLife`'s Y, `0.05` for every effect) was far too weak to
     compensate; `_debugDustSlots()` showed several particles' Y drop to
     small negative values (e.g. `-0.046`) by mid-life, i.e. visibly
     clipping through the floor. Made the lift force data-driven per row
     (`row.liftForce`, default `0.05` — every existing combat-effect row
     keeps today's exact value, unaffected) and raised dust's to `0.55`.
- **Separately, round 1's own density bump (`poolSize` 3→8, `count` 5→9,
  `size` 0.35→0.55, `lifetime` 0.4→0.45s) overshot "more often"** into
  visibly multiple, larger, longer-lived puffs strung out along the
  player's recent path during any fast/long move order — technically each
  one *was* correctly placed at its own historical foot position (see the
  "no position bug" finding above), but several simultaneously-visible
  blobs trailing behind, once the player's continuous on-screen position
  has moved on, is exactly what "an artifact... not under the player"
  describes, even without any actual mispositioning. Dialed back to
  `poolSize: 5`, `count: 7`, `size: 0.5`, `lifetime: 0.35` — still more/
  bigger than the original iteration-1 baseline (3/5/0.35/0.4s), just not
  as far over it as round 1 went.
- **Added a fade-in.** Every particle in a burst spawns on the exact same
  frame (`emissionBursts` fires the whole `count` at `time:0`) and the
  existing alpha curve popped straight to `alphaPeak` with no ramp — an
  instantly-opaque circle reads as a flat sprite, not a puff forming. Made
  the curve data-driven (`row.alphaPeak` default `0.55`, `row.fadeInFrac`
  default `0` — both match every existing row's current 2-keyframe curve
  exactly when unset) and gave dust a quick `0 → alphaPeak` ramp over the
  first 15% of its life before the existing fade-out.

**Verified**: `dotnet build`/`test` (69/69, unaffected — JS + manifest
only). Playwright: `getDustDebug` correctly read back the new baseline
(`poolSize`/`count`/`size` reflected in `maxSlots`/the multiplier math),
`setDustDebug`/reset still round-tripped correctly against the new
baseline. Visual: a screenshot caught mid-burst (see the investigation
above for how) shows a light, soft tan puff sitting directly under the
trailing foot — no dark smudge, no visible ground-clipping, no stray
blobs trailing behind. `_debugDustSlots()` is now a permanent Playwright
probe hook (mirrors `_debugLiveParticles`), kept rather than removed after
this investigation, for the next time a "looks wrong" report needs
ground-truth particle positions instead of guessing from a screenshot.

---

## Post-merge, round 3: dust should drag along with the player, not sit fully planted

User report: "the dust is stationary but it should follow the player a
bit." Consistent with (not a regression from) round 2's fix — `worldSpace:
true` was a deliberate iteration-1 choice ("particles keep drifting from
their spawn point in world space once emitted, rather than following the
[reused, repositioned] emitter object") specifically so a *pooled* emitter
being repositioned for its *next* burst never warps an *earlier*, still-
alive one. That reasoning still holds — the fix here doesn't touch
`worldSpace` or the emitter — it adds a *separate*, additive per-frame drag
on top.

**Implementation**: `toon.js`'s render loop now calls `st.vfx.update(dt,
st.player.pos)` (previously just `update(dt)`) with the player's live
rendered position, already computed earlier in the same loop iteration.
`vfx.js` tracks the previous frame's player position (`lastPlayerPos`,
module-private) and, each frame, nudges every currently-alive `entity_moved`
particle by `(thisFramePlayerDelta) * DUST_FOLLOW_STRENGTH` (`0.35`) —
directly mutating `particle.position` (already world-space per the above),
additive on top of whatever three.quarks' own integration did that frame
inside `renderer.update(dt)`, so it doesn't fight the existing drift/size/
fade behaviors. Scoped to the `entity_moved` pool specifically (reads
`poolsByEvent.get('entity_moved')`), not every effect — combat splashes
(slash/impact/block) aren't emitted from a moving reference point in the
same way and weren't part of the report.

**Why 0.35 (partial, not full)**: a full 1:1 attach would need the
particles reparented to the emitter in local space, which is exactly what
`worldSpace:true` exists to avoid (see above) — and would read as the dust
rigidly glued to the player, not settling behind them. A partial drag
keeps the puff visibly lagging/spreading (still reads as dust, still
settles into place) while no longer being perfectly planted at its exact
spawn point for its whole ~0.35s life. No design-doc source for this
exact fraction — a cosmetic tuning constant, same class as the pre-existing
`driftScale` (`0.9`) it sits next to, not a gameplay number.

**Verified**: `dotnet build`/`test` (69/69 — no C# touched). A live-fight
Playwright check meant to catch this via real movement kept missing the
window (same headless rAF-timing unpredictability round 2 already flagged
— the player's own move order frequently completed *before* the sampling
loop even started, or the specific dust particle being tracked expired/
got replaced by a new burst mid-sample), so verified deterministically
instead: spawned one burst via the real `handleEvents` path, then called
`st.vfx.update` directly with a known, hand-controlled sequence of player
positions (10 steps of +0.5 world units, +5 total) with no real rendering
or timing involved. The particle's average position shifted by `1.749`
units — matching `5 * 0.35 = 1.75` almost exactly (the `0.001` difference
is the particles' own small pre-existing drift/spread, not error). This
is a stronger verification than a lucky screenshot would have been: it
confirms the *exact* coefficient is applied, not just "some plausible-
looking movement happened."
