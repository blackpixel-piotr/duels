# Combat Feel Pass 2 — Findings

Reality vs. combat-feel-2-plan.md, recorded as implementation happens.

## Divergences from the plan

- **No sim change was needed for the tick stamp.** The plan assumed
  `GameState.FightTicks` might need exposing; it was already `public`
  (GameState.cs) — the whole Step 1 sim side was two payload fields in
  `BattleScene.razor`.
- **The vfxEvent dedup (defect 8) was found during plan review, not
  exploration** — `BattleScene.razor` re-sent the full `VfxEvents` array on
  every render with no cursor, and the sim only clears the list at the
  NEXT tick start, so every mid-tick command render re-fired the whole
  tick's swings/splats/particles. Fixed with a `(FightTicks, sent-index)`
  cursor in the razor; safe because Blazor WASM is single-threaded, so a
  tick (clear → emit → `TickFight()`) is atomic with respect to renders.
  The renderer contract is unchanged; lossy-by-contract semantics
  preserved (a coalesced render still just drops events).
- **The Lunge slide needed one extra guard the plan missed**: the Lunge
  special fires `forced_move` + `attack_swing` on the same tick, and both
  wanted the single `presentFx` slot (the swing's cosmetic lunge nudge
  would clobber the traversal slide). `startLunge` now yields to an
  active slide.
- **NPC arrival-dwell fix (Step 2) also corrected the gait-speed
  denominator** — `instSpeed` divided by nominal `TILE_MS` even though the
  lerp window is now the measured EMA; both use the same window now.
  Extrapolation was considered and rejected as planned (direction-change
  overshoot reads worse than a brief dwell).
- **Additive flinch verdict (backlog #49 evaluation): SHIPPED.** The rig
  accepts `makeClipAdditive` deltas cleanly — verified live via Playwright
  (`additiveFlinch: true` while an attack overlay kept playing, no visual
  contortion in screenshots). `ADDITIVE_FLINCH` kill-switch const remains
  in toon.js if a real device ever disagrees. Backlog #49 updated: the
  hit-react slice is closed; the attack/cast-over-locomotion bone-mask
  verdict stands.
- **Player cosmetic projectiles switched to doctrine color tokens** —
  they were ad-hoc amber (`#ffd166`) / ice-blue (`#7ab8ff`); the shared
  comet factory colors them by `DOCTRINE_HEX[style]` like the boss's.
  Deliberate consistency change, noting it since it alters an existing
  look without a bug behind it.
- **Splat lifecycle disposal fixed a pre-existing leak** — damage-splat
  canvas textures/materials (and the perfect-dodge ring's geometry) were
  never disposed; the projectile impact-ring work touched that loop, so
  disposal was added for all splats, not just the new rings.
- **Hit-stop was kept** after live verification (50ms @ 0.15 timescale on
  heavy/spec/max only). Whether it reads as impact or jank on a real
  phone at 600ms pacing is still a playtest question — flagged in backlog
  #51 as keep-or-cut.
- **three.quarks capability check:** the vendored 0.12 build exports
  `RotationOverLife` AND `RenderMode.StretchedBillBoard` (+
  `rendererEmitterSettings.speedFactor/lengthFactor`), so sparks got the
  better velocity-aligned stretch treatment rather than the fallback
  random-rotation-only path the plan hedged on.

## Non-doctrine color flag

The impact hit flash is neutral white (emissive pulse). Same precedent as
movement dust (vfx-findings.md): it encodes no gameplay information —
style/tier reads stay with the splat, particles, and projectile colors,
which are all doctrine-tokened. The projectile glow/trail/ring are
doctrine-colored (`DOCTRINE_HEX[style]`).

## PROVISIONAL constants introduced (full list — backlog #51)

- `applySnapshot` window EMA: clamp [1, 1.4]×TILE_MS, alpha 0.3.
- Player adaptive pursuit speed clamp: [1, 3] tiles/tick.
- `OVERLAY_PRIORITY`: death 100 > spec 60 > attack 50 > bigHit 48 >
  windup 45 > block 40 > flinch 30 > eat 25 (equal replaces equal).
- `OVERLAY_DAMP`: windup 0.75, block/flinch/bigHit 0.5, eat 0.6,
  attack/spec unchanged (0.35/0), death 0; windup overlay weight eased to
  0.55 while moving.
- `ADDITIVE_FLINCH_WEIGHT` 0.6; additive routing threshold 0.4 wu/s.
- `TURN_EASE_RATE` 14 (carried from pass 1, now exact exponential form).
- Projectile dressing: core 0.13, glow 0.55 base scale, 120ms spawn
  flash, 10-point trail, 2.0 wu near-player arrival heuristic for the
  impact ring, ring life 450ms/grow 3.2.
- Impact juice: flash 90ms @ 0.55 emissive; squash 120ms @ 0.08;
  hit-stop 50ms @ 0.15 (heavy/spec/boss/max tiers).
- vfx-manifest.json retune batch (all rows): texture families, speed
  ranges, per-row lift/gravity, spin, endScale — JSON carries no
  comments, so this file is the flag for the whole batch.

## Verification

- `dotnet build` green and `node --check` clean at every step; full test
  suite green at the end (Domain 21, Application 51, Infrastructure 69 —
  141/141).
- Playwright end-to-end suite (verify skill, Maggot King fight with dev
  MECH toggles disabling boss mechanics to keep the fight alive):
  **13/13 checks pass** — probe sanity, zero mid-tick lerp re-bases over
  14 command-spam samples, ordered walk with no mid-walk idle dips
  (speed profile 2.1→4.1 sustained→decay), windup overlay appears on
  telegraph rise and clears on fall, attack overlay survives a same-batch
  impact event (priority), flinch reroutes to the additive layer,
  projectile renders as a dressed comet group and is swept with zero
  geometry-count growth (leak check), hit flash + squash + hit-stop all
  trigger on a max-tier impact, impact particles fire under budget,
  enemy `entity_moved` spawns dust, and no renderer console errors
  (including the projectile `simBacked` invariant).
- The live boss's own telegraph produced a real windup capture
  (`overlayKind: 'windup'`, measured `snapWindow` ≈ 646–675ms — the EMA
  visibly tracking real tick cadence above the nominal 600ms, i.e. the
  exact dwell the Step 2 fix absorbs).

### Verification caveats

- **Boss/add walking was only exercised synthetically.** Maggot King is
  stationary; the walking-boss paths (Bloodtithe's approach, Hive
  Matron's spacing AI, `EmitNpcStepDust` at both sites) compile and are
  unit-test-covered for movement, but the moving-boss gait/dust was
  verified via injected `entity_moved`/telegraph events plus the add
  lerp layer, not a live Bloodtithe fight.
- Knockback (`hitBig` + slide) and Lunge were verified by code path and
  the renderer contract, not a live Hive Matron Tail Stab / T2-weapon
  Lunge — both ride the same `forced_move` handler the probe exercised
  indirectly.
- Headless chromium runs ~12fps; all timing-sensitive checks were
  written against the debug probe (`getAnimDebug`), not screenshots.
- Feel judgments the probe can't settle (hit-stop keep/cut, damp values,
  windup weight, projectile glow size on a phone screen) are listed in
  backlog #51 for on-device playtesting.

## Follow-up: footstep-timed running dust

The pass-2 dust retune (Step 8, smoke texture) still read cheap because the
dust rode the sim's per-tick `entity_moved` event — one lumpy puff per
~600ms tick, unrelated to the stride, one flat layer. Fixed by moving dust
emission into the renderer, timed to the actual gait phase:

- **Renderer owns footfall timing.** `updateFootsteps(st, actor, now)` runs
  in the render loop after `updateActorAnim` for both the player and enemy
  blocks; it emits a burst when `actor.gaitPhase` crosses 0.0 (wrap / launch
  reset) or 0.5 — the two footfalls per stride cycle — gated on
  `speedSm > FOOTSTEP_MIN_SPEED`, not crumbled, and not mid-`slide`.
  `emitFootstepDust` places the puff behind the body and to the alternating
  planted-foot side (analytic from `facing`; foot bones left as a possible
  refinement), passing the forward direction so the existing `spawnBurst`
  drift kicks it backward.
- **vfx.js `footstepDust(wx, wz, dx, dz, ownerId)`** spawns the pooled
  `entity_moved` rows directly (reusing `spawnBurst`), so `dragDust`,
  `setDustDebug`, and the BattleScene VFX debug panel keep working unchanged.
  Early-outs under `quality:'off'`.
- **Sim emissions retired.** The three `entity_moved` emit sites
  (`ProcessTick`'s player-moved block, `KickMoveAsync`, and Step 9's
  `EmitNpcStepDust` + both call sites) are deleted — footfall timing is
  renderer-native, the sim only ever knew tile deltas. No test referenced
  `entity_moved`, so nothing broke; 141/141 still green.
- **Architecture rationale (why this doesn't violate "VFX driven by
  vfxEvents").** That rule exists for *sim-sourced* effects the renderer
  can't otherwise know about (hits, casts). Footfalls are the opposite: the
  stride phase exists *only* in the renderer, so dust timed to it is
  renderer-native presentation reading only interpolation state — the same
  locked-invariant carve-out CLAUDE.md already grants facing and camera
  motion. Encodes no gameplay; disabled with zero gameplay impact via the
  quality path. This is a deliberate, reasoned placement, documented here.
- **Two-layer look.** `dust_puff` retuned to ground-hugging soft smoke
  (lift 0.55→0.08, muted-tan `--text-dim`, count 7→4, tighter spread); new
  `dust_kick` row = small fast dark-dirt (`--border`) flecks with negative
  lift (gravity fall), stretched billboard, shrink — the kicked-debris read.
  Both ride `entity_moved`, so one footstep fires both (multi-row precedent:
  landing_dust + landing_ring).
- **Verification:** dedicated Playwright probe 6/6 — no dust while
  stationary (after the fight-start auto-approach settles), dust spawns and
  stays under budget while walking (player `speedSm` up to 4.1), a synthetic
  gait drive on the (stationary) Maggot King confirms the enemy footstep
  path emits too, no console errors; plus a forced-burst screenshot
  confirming the dust reads as a low ground-hugging tan smear at the feet
  rather than a floating blob. New PROVISIONAL constants
  (`FOOTSTEP_MIN_SPEED` 1.2 wu/s, `FOOT_BACK_OFF` 0.15, `FOOT_LAT_OFF` 0.18,
  gait markers 0/0.5) + the two retuned/added manifest rows folded into
  backlog #51's provisional batch.

### Caveat

The live boss footstep path was exercised by a **synthetic** gait drive
(Maggot King is stationary; the two reachable moving bosses — Bloodtithe's
approach, Hive Matron's spacing — compile through the identical shared
`updateFootsteps` path but weren't run live). `setDustDebug`/`getDustDebug`
now tune/read both `entity_moved` rows (dust_puff + dust_kick) since they
loop the event's pools — harmless for a dev panel, noted for completeness.
