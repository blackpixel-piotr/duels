# Combat Feel Pass 2 — Plan

User report: combat feels cheap and not smooth — movement, turning, animation
transitions, animation stalling (getting hit + casting + running on the same
tick don't overlap; the character slides with straight legs), projectiles,
particles. Mandate: fix everything achievable, renderer-led, inside the locked
architecture (C# sim → snapshot → dumb Three.js renderer; 600 ms tick;
semantic vfxEvents; no gameplay in JS).

Pre-planning exploration found concrete root-cause bugs, not just tuning gaps.

## Diagnosed defects

1. **Straight-leg slide (boss/adds).** `applySnapshot` (toon.js) runs on
   *every Blazor render* (any command dispatch), not once per tick. For an
   unchanged target it re-bases `snapFrom` to the current interpolated
   position and restarts the 600 ms lerp window; the gait speed derived from
   the shrinking window span decays toward 0 while the mesh still translates,
   so the locomotion blend space fades to idle mid-move.
2. **Straight-leg slide (player, flagship repro).** The player's
   `discontinuous` teleport flag is dropped (`setBattlePositions` sets only
   `target`), so a Lunge slides up to 2 tiles at run speed with the attack
   overlay damping the legs.
3. **Stuck windup = frozen boss.** The telegraph windup overlay is
   `hold:true` and the mixer `finished` listener never clears held overlays →
   after any telegraph, `actor.overlay` stays set forever: locomotion damp
   stuck at 0/0.35 (frozen legs whenever the boss walks) and the flinch guard
   permanently disables boss flinches.
4. **Same-tick overlap clobbering.** One overlay slot, last-writer-wins
   (`force:true` everywhere) — `attack_swing` + `impact` in one
   `setVfxEvents` array resolve by array order, arbitrarily.
5. **`forced_move` slide unit bug.** Sim sends tile coords; the renderer
   subtracts them from world units (tile × 1.75) → garbage slide offsets.
6. **Stop-start player gait.** Constant-speed pursuit hardcoded to
   2 tiles/tick — a 1-tile step finishes in ~300 ms then idles ~300 ms.
7. **Per-tick NPC gait hitch.** Lerp `frac` clamps at 1 over a fixed 600 ms
   window — late snapshots make NPCs arrive early and dwell.
8. **Duplicate vfxEvents.** BattleScene re-sends the full `VfxEvents` array
   on every render; the sim clears the list only at next tick start, so a
   mid-tick render (prayer flick, tap) re-fires the same swings/splats/
   particles.
9. **Turning.** Frame-rate-sensitive linear ease (`da * min(1, dt*14)`)
   stacked with a hard clamp, duplicated for player/enemy, and the adds copy
   is missing the clamp entirely.
10. **Projectiles are bare spheres.** No trail/glow/streak/spin/impact puff;
    no pooling; per-shot geometry/material never disposed.
11. **Particles are one soft blob.** All manifest effects share a single
    radial-gradient texture; every effect inherits dust's upward drift
    (backlog #44); add-targeted impacts get no particles (only player/enemy
    positions reach `vfx.handleEvents`); boss/adds emit no movement dust.

## Steps

Branch `claude/combat-feel-smoothness-grdwqx`; after each completed step:
build green → commit → fast-forward merge into `claude/text-duel-game-3t4vkf`
→ push. Every invented constant `// PROVISIONAL` + findings + backlog.

- **Step 0 — this file** + findings header.
- **Step 1 — tick-keyed idempotent snapshots + vfxEvent dedup** (fixes 1, 8).
  Send `GameState.FightTicks` (already public) with `setBattlePositions` and
  per-add payloads; `applySnapshot` no-ops when the tick and target are
  unchanged (compare with `!==` — FightTicks resets on rematch); BattleScene
  sends only vfxEvents not already sent for the current tick (index-tracked;
  lossy-by-contract semantics preserved).
- **Step 2 — arrival dwell + stop-start gait** (fixes 6, 7). NPC lerp window
  = measured inter-snapshot interval (EMA α 0.3, clamped [1, 1.4]×TILE_MS)
  instead of constant 600 ms; no extrapolation (rejected: direction-change
  overshoot). Player: idempotent target write keyed on target change; on
  change, adaptive speed `clamp(remainingDist/TILE_MS, 1, 3 tiles/tick)` so a
  1-tile step spans the full tick (continuous chained walking).
- **Step 3 — forced_move unit fix + player discontinuous + hitBig**
  (fixes 2, 5). Multiply event tile coords by TILE; snap player pos when
  `discontinuous`; sim adds `cause = knockback|lunge` to the two emit sites
  (sim names *what* happened); knockback plays the extracted-but-unused
  `hitBig` clip, lunge is slide-only (it accompanies the player's own swing).
- **Step 4 — overlay priority system + windup lifecycle** (fixes 3, 4).
  `OVERLAY_PRIORITY`: death 100 > spec 60 > attack 50 > bigHit 48 >
  windup 45 > block 40 > flinch 30 > eat 25; new plays iff `new >= current`
  (equal replaces — preserves attack-chains-attack and
  swing-interrupts-windup). Windup overlays are kind-stamped and cleared on
  the telegraph falling edge (`active:false` already arrives).
- **Step 5 — additive flinch + kind-dependent damp** (the hit+cast+run
  overlap fix). Additive variants of hitA/hitB via
  `THREE.AnimationUtils.makeClipAdditive` on clip clones, played in a
  separate `additiveFx` slot that composes over locomotion *and* a running
  swing; routed on `impact` when the actor is moving or mid-overlay
  (idle actors keep the stronger full-body flinch). Hard fallback flag if
  the rig contorts under additive (then: no worse than today). Locomotion
  damp becomes per-overlay-kind (`DAMP_BY_KIND`) — notably windup 0.75 with
  the windup overlay's own weight eased down while moving, so a telegraphing
  walking boss keeps moving legs.
- **Step 6 — unified turning** (fixes 9). One shared `turnFacing()`:
  wrap ±π, exponential ease `1-exp(-dt·14)` (frame-rate-independent form of
  the current tuned feel), clamp by existing `MAX_TURN_RAD_PER_S`. Used by
  player, enemy, and adds.
- **Step 7 — projectile visual factory** (fixes 10). Shared
  geometry/texture/material caches (fixes leaks); per-projectile group:
  near-white hot core + additive glow sprite (spawn flash 2×→1 over
  ~120 ms) + velocity-stretched streak sprite + short position-ring-buffer
  trail line + spin; boss projectile gets a subtle sine bob (flight duration
  unknown), the player's cosmetic projectile keeps its known-duration arc;
  despawn impact puff guarded against fight-end removals; `simBacked`
  invariant and cosmetic homing untouched. Glow stays small/additive —
  telegraph-readability doctrine.
- **Step 8 — particle texture families + per-row physics** (fixes 11a,
  closes backlog #44). Cached texture registry: `radial_soft` (existing),
  `smoke`, `spark`, `ring`, `arc`. New optional per-row manifest fields
  (`texture`, `speedMin/Max`, explicit `liftForce`, `startRotation`,
  `endScale`) defaulting to current behavior; retune all rows (dust→smoke,
  slashes→arc, impacts→spark with gravity fall + shrink, dome→ring,
  landing→smoke + new `landing_ring` row).
- **Step 9 — add-targeted particles + enemy movement dust** (fixes 11b/c).
  Pass add positions into `vfx.handleEvents`; sim emits `entity_moved` for
  enemy steps (skipping dash/teleport sites); `dragDust` scoped to
  player-owned slots. Adds deliberately get no dust (budget).
- **Step 10 — impact juice** (renderer-only, all PROVISIONAL). ~90 ms
  decaying emissive hit flash (neutral white — encodes no gameplay, same
  precedent as movement dust); ~120 ms multiplicative `scale.y` squash;
  50 ms hit-stop (mixer timeScale 0.15) on spec/max tiers, frame-driven
  restore — cut if it reads as jank at 600 ms pacing.
- **Step 11 — `getAnimDebug` probe + verification sweep + docs**. Probe
  returns per-actor `{speedSm, overlayRole, overlayKind, locoWeights,
  snapT0, snapTick, facing}` for non-visual Playwright assertions; verify
  skill sweep (slide fix, lunge repro, telegraph cycle, dedup, projectiles,
  particles, leak check); findings completion; backlog updates
  (#44 → Resolved, #43/#46/#49 updated, new items per kept/cut judgment);
  `graphify update .`.
