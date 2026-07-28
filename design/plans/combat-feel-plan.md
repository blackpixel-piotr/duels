# Combat feel pass 1 — plan

Renderer-only per the brief: no sim, tile, or tick-timing changes. Builds
on the VFX layer (`design/plans/vfx-plan.md`) — new semantic events ride
the same `GameState.VfxEvents`/`AppendVfxEvent` pipe iteration 1 shipped,
not a second ad-hoc mechanism.

## Step 0 — animation inventory (verified, not guessed)

Source of truth: `tools/extract_anims.mjs`'s `KEEP` lists (what's actually
baked into `anims1.glb`/`anims2.glb`) cross-checked against `toon.js`'s
`LIB_ROLES` (what's actually wired to a role name the renderer uses).

| Role today | Clip | Notes |
|---|---|---|
| idle | Idle_Loop | |
| swordIdle | Sword_Idle | armed idle stance (`setActorStance`) |
| walk/jog/sprint | Walk_Loop/Jog_Fwd_Loop/Sprint_Loop | forward-only 1D blend space (`updateActorAnim`) |
| punchA/punchB | Punch_Jab/Punch_Cross | unarmed attack |
| swordA/swordB | Sword_Regular_A/B | armed attack combo (alternates via `swingAlt`) |
| spec | Sword_Attack | special-attack flourish |
| throw | OverhandThrow | ranged windup/swing |
| cast | Spell_Simple_Shoot | magic windup/swing |
| hitA/hitB | Hit_Chest/Hit_Head | hit-react, picked by severity |
| hitBig | Hit_Knockback | heavy hit-react (not currently used by `flinch`'s big/small split — see gap below) |
| eat | Consume | flask sip |
| death | Death01 | |

**Extracted but NOT wired to a role** (asset exists, zero pipeline work
needed to use it): `Sword_Block`, `Roll`, `Melee_Hook`.

**Gaps** (flagging per instruction, not improvising):
- No strafe L/R, no backpedal, no turn-in-place clip exists in either
  library file. **Does not block this plan** — §1 below only needs
  rotate-to-face + the existing forward-only gait (same convention as the
  reference OSRS-style games), not directional locomotion blending.
- `flinch()` (`toon.js`) already picks `hitBig`'s conceptual "big hit"
  case by playing `hitB` (`Hit_Head`) for heavy/spec/boss/max tiers —
  `hitBig`/`Hit_Knockback` is mapped in `LIB_ROLES` but never referenced
  by `flinch`'s severity switch today. Not a blocker; noted so §4
  (forced-move) doesn't assume `hitBig` is already play-triggered anywhere
  — it isn't.

## Existing infrastructure this plan builds on, not duplicates

This section exists because several of the brief's asks are already
partially or fully shipped; re-implementing them from scratch would be a
regression, not a feature.

1. **Facing controller (§1) already exists**, symmetrically for player and
   enemy, in `toon.js`'s render `loop()`:
   - Player block (~L1210-1246): `destFacing` = movement direction while
     moving; target direction while stationary + engaged
     (`!holdPosition`); last facing held while disengaged.
   - Enemy block (~L1251-1280): same three-way split, reading
     `interpolateSnapshot`'s `dx/dz` for movement direction instead of the
     player's continuous-pursuit vector.
   - Both ease via `da * Math.min(1, dt * 14)` — an exponential approach,
     not the brief's literal "~90°/100ms" constant-rate turn. **Change**:
     tune this to an actual angular-velocity clamp (max radians/sec) so
     large reversals (e.g. spinning to face a boss that just teleported
     behind the player) read as a bounded-speed turn instead of a
     snappier-than-intended ease at large angle deltas. No new controller.
2. **Attack presentation (§2) partially exists**: `battleEvent`'s
   `playerAttack`/`enemyAttack` cases already resolve an attack role
   (`attackRole()`: spec > style-specific > armed combo > unarmed) and
   play it via `playOverlay`. Ranged/magic already spawn a simple cosmetic
   sphere-projectile mesh for the player's own outgoing attack. **What's
   actually new**: the mesh lunge-toward-target (no such offset layer
   exists — see below) and the slash-arc trail VFX (nothing plays a trail
   today).
3. **Hit-react (§3, unblocked case) already exists**: `flinch()` plays
   `hitA`/`hitB` by severity on `playerHit`/`enemyHit`, and **already
   explicitly excludes `tier === 'blocked'` from flinching** — it's a
   silent no-op today. That silent no-op is precisely §3's gap: nothing
   plays a block reaction or spawns a blocked-hit VFX yet.
4. **No mesh-offset layer exists** for §2's lunge or §4's forced-move
   slide. `actor.ch.group.position` is written directly from
   `actor.pos.wx/wz` every frame — there is no second, additive transform
   a cosmetic effect can drive independently. There's a vestigial,
   never-populated `actor.ch.pivot` reset in `resetBattle` (dead code from
   an earlier iteration) — plan is to repurpose that name for a real
   `lungeOffset`/`forcedMoveOffset` vector rather than invent a second
   mechanism alongside it.
5. **§5's "existing screen-shake setting" does not exist.** `triggerShake`
   (`index.html`) fires unconditionally from three `BattleScene.razor` log
   kinds (MaxHit/SpecHit/BossSpecial) — there is no user-facing toggle,
   and this project has no Settings page/component at all (confirmed:
   no `*Settings*.razor` anywhere in `src/Duels.Web`). Building a full
   Settings surface is out of scope for a renderer-only pass. **Plan**: a
   `localStorage`-backed dev flag (`duels_camera_motion`,
   `full`/`reduced`/`off`), read once at `initBattle`, mirroring how the
   camera/movement debug panels are already dev-only affordances gated
   behind `TestScene` rather than real settings UI. Flag "real Settings
   screen for camera motion + VFX quality" in backlog.md rather than
   building one here.

## 1. Facing controller — tuning, not a new build

- Replace the `da * Math.min(1, dt * 14)` ease (both player and enemy
  blocks) with a max-angular-speed clamp: `da = clamp(destFacing -
  facing, -maxTurn*dt, maxTurn*dt)` where `maxTurn` ≈ `Math.PI/2 / 0.1` (90°
  per 100ms, per the brief) — **PROVISIONAL**: not a design-doc number,
  matches the brief's own stated figure; will flag in findings + backlog.
- No sim changes — confirmed, this reads only `pos`/`target`/`snapTo`/
  `holdPosition`, all already in the renderer's local state.
- Boss uses the literal same code path already (shared block shape) — no
  boss-specific branch to add.

## 2. Attack presentation

- **New vfxEvent**: `attack_swing {entityId, targetId, style}`, appended
  by `GameTickService` at the same point it currently fires
  `BossCast`/hitsplat log entries and the player's `AttackCommand`
  resolution — i.e. sourced from the same tick logic that already knows
  "an attack just started," not a new detection mechanism. This event
  becomes the new trigger for `playOverlay`'s attack-role selection,
  **replacing** `BattleScene.razor`'s current `playerAttack`/`enemyAttack`
  `battleEvent` calls (which are parsed out of `CombatLog` string
  messages) — same visual outcome, cleaner source (semantic event instead
  of string-sniffing a log line), consistent with the VFX architecture's
  "empty array = omitted" per-tick model. **Open question for review**:
  confirm you want this migration (removing the old CombatLog-based
  trigger path) rather than adding `attack_swing` as a second, parallel
  trigger — I'd default to migrating, since keeping both risks
  double-animating a single swing.
- **Mesh lunge**: new `lungeOffset` vector on the actor (see
  infrastructure note above), driven by a small ease-out-back tween
  (0 → 0.3 tile toward `targetId` → back to 0) timed to the weapon's
  cooldown (`GetPlayerWeaponSpeed`/boss attack cadence) so it's always
  fully returned before the next tick's true position write — added to
  `actor.pos` only at the final `ch.group.position.set(...)` call, never
  touching `actor.pos` itself (hit resolution/occupancy stay exactly
  where they are, per the brief).
- **Slash-arc trail**: new `vfx-manifest.json` row (`slash_arc`), one per
  style (doctrine color per style, matching existing `DOCTRINE_HEX`), a
  short-lived quad/ribbon fired from `vfx.js`'s existing pooled-emitter
  pattern (iteration 1's `dust_puff` pool shape reused, not a new
  mechanism) on the same `attack_swing` event.
- **Impact burst**: new `impact` vfxEvent (named in the original VFX
  plan's "future events" list — this is that event, not a new concept),
  fired at hit resolution (same tick as the existing hitsplat log entry),
  positioned at the victim, doctrine color by the attacking style.

## 3. Defend reactions

- **New vfxEvent**: `hit_blocked {entityId, style}`, appended wherever the
  sim currently determines a protection prayer negated a hit (this logic
  already exists — it's what produces today's `tier === 'blocked'`
  hitsplat — this just also names the semantic event instead of leaving
  the renderer to infer "blocked" from a hitsplat tier string).
- `toon.js` gains a `blockRoleForStyle` (mirrors `windupRoleForStyle`'s
  shape): melee → `block` role (newly wired from the already-vendored
  `Sword_Block` clip — zero asset work) + a spark-burst manifest row;
  magic → shield-dome VFX (doctrine blue, ~0.3s) + flinch (reuses
  existing `hitA`); ranged → deflect-ward flash (doctrine green) + flinch.
  "Flinch" here means the existing `hitA` overlay, not a new clip.
- Unblocked hits: **already implemented** (`flinch()`'s existing
  `hitA`/`hitB` split) — this pass adds the `impact` burst (§2) alongside
  it, doesn't change the clip logic.
- Overhead prayer icon: untouched, confirmed — nothing in `hit_blocked`'s
  handling touches `setActorOverhead`/`setBattleOverheads`.

## 4. Forced-move presentation

- **New vfxEvent**: `forced_move {entityId, from, to}`, appended at the
  same call sites that already set the `discontinuous` flag (Lunge,
  Bloodtithe/Mirrorhide knockback — the exact three call sites the VFX
  layer's `entity_moved` suppression already knows about, see
  `vfx-findings.md` #2).
- Renderer: on `forced_move`, skip the normal snap/lerp for that entity
  for the duration of a short (~150ms) arc-slide tween (reusing the
  `lungeOffset`-style additive layer from §2, not a third mechanism) +
  a landing squash (scale pulse on arrival) + a `landing_dust` burst
  (manifest row, visually related to but distinct from iteration 1's
  `dust_puff` — heavier/bigger, one-shot not per-tile).

## 5. Camera spring (toggleable)

- `camFocus` currently eases toward `st.player.pos` only (render loop,
  rate 18). Change: ease toward the midpoint of `st.player.pos` and
  `st.enemy.pos`. Zoom: ease `st.zoomTarget` within a clamped band as a
  function of inter-actor distance (closer together → tighter zoom;
  clamp both ends so it can never exceed the existing `ZOOM_MIN`/
  `ZOOM_MAX`).
- Explicitly NOT touched: `yaw`/`camPitch` stay exactly as user-set —
  confirmed no code path in this section reads or writes either.
- Toggle: `duels_camera_motion` localStorage flag (`full`/`reduced`/
  `off`) per the infrastructure note above — `off` restores today's
  fixed-on-player behavior exactly (bypasses the midpoint/zoom-spring
  code entirely, not just visually near-zero). `reduced` halves the
  midpoint pull and zoom-band width. Also gates the existing
  `triggerShake` calls (today unconditional) — `off`/`reduced` skip or
  scale down the shake call from `BattleScene.razor`.

## 6. VFX manifest additions

New `vfx-manifest.json` rows, all pooled per iteration 1's existing
pattern (pool size = that effect's own concurrency cap), all within the
existing 300-particle global budget:

| effectId | event | notes |
|---|---|---|
| slash_arc | attack_swing | doctrine color by style |
| impact_burst | impact | doctrine color by style |
| blocked_spark | hit_blocked (melee) | |
| shield_dome | hit_blocked (magic) | doctrine blue |
| deflect_ward | hit_blocked (ranged) | doctrine green |
| landing_dust | forced_move | heavier one-shot variant of dust_puff |

Textures: same procedural-placeholder approach as iteration 1
(`design/plans/vfx-findings.md` #5/backlog #40) — Kenney's pack is still
unreachable from this sandbox, unchanged since last pass. Will not
re-attempt the blocked fetch.

## Explicitly out of scope this pass

- Any sim/tile/tick-timing change (brief's own header constraint).
- Strafe/backpedal locomotion (asset gap, and not actually required by
  anything above).
- A real Settings UI screen (flagged to backlog; dev-flag stopgap only).
- Boss-specific lunge/slide tuning beyond the shared mechanism (numbers
  will need per-boss playtesting later, same as every other shared
  system in this codebase).

## Verification plan

`dotnet build`/`dotnet test` (should be unaffected — no C# behavior
changes beyond appending new vfxEvent types, which mirrors the already-
tested `entity_moved` pattern), then the `verify` skill (Playwright)
against a live fight: confirm attack lunge/return, slash trail, block
reactions per style, forced-move slide+dust, and camera spring toggle
on/off/reduced — plus a repeat of the "read the instanced particle buffer
directly" technique from `vfx-findings.md` (screenshot-only verification
already proved insufficient once this pass; use it as the primary check,
not an afterthought). Findings to `design/plans/combat-feel-findings.md`.
