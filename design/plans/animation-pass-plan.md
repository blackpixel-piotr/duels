# Animation quality pass: crossfading + upper/lower body layering — plan

## Verify first — findings (checked directly, not assumed)

### Does the current system hard-stop/reset, or is there existing fade?

**Mostly already crossfades. One real hard-cut found.**

- **One-shot overlays** (`playOverlay` — attack/cast/block/hit/death/eat):
  already a genuine crossfade. Starting a new overlay fades the previous
  one out (`actor.overlay.action.fadeOut(0.08)`) while the new action
  fades in (`.fadeIn(fade)`, default `fade = 0.08`). When an overlay
  finishes, the `mixer`'s `'finished'` listener fades it out over `0.18`s
  and clears `actor.overlay`, letting locomotion resume. Verified the
  exact `THREE.AnimationAction` semantics in the vendored
  `three.module.min.js` directly (not assumed from the three.js docs):
  `fadeIn`/`fadeOut` call `_scheduleFading`, a real weight-interpolant
  ramp; `.reset()` runs before any weight/fade calls in `playOverlay`, so
  it can't clobber the scheduled fade that follows it. This is not a
  fake/cosmetic fade — it's the real mechanism.
- **Locomotion blend space** (`updateActorAnim`'s idle/walk/jog/sprint
  triangular blend): never a discrete cut at all — every node's weight is
  continuously eased every frame (`k = 1 - exp(-dt/tau)`, tau 0.07s/0.14s
  depending on speeding-up vs settling), which is a *better* mechanism
  than a fixed-duration crossfade for locomotion (it's velocity-aware).
  The brief's "idle↔walk 100ms" suggestion is already in the right
  neighborhood of the existing tau values. Not touching this system —
  replacing continuous blending with discrete `crossFadeTo` calls would
  be a regression, not a fix.
- **One real hard cut**: `setActorStance` (swaps the locomotion blend
  space's anchor-0 node between `idle`/`swordIdle`/`spellIdle` whenever a
  weapon/style changes) does `n0.action.stop(); a.reset().
  setEffectiveWeight(n0.w).play();` — an instant cut, no fade at all.
  Fixing this in-place isn't a plain `crossFadeTo` call, though — see
  "why not just crossFadeTo" below.
- **Scattered magic numbers, not a tunable table**: the fade durations
  that do exist are hardcoded per call site (`0.08`, `0.05` for hit-react,
  `0.06` for death, `0.18` in the `finished` handler) rather than named
  constants. This is the real gap item 1 closes — not "add fading" but
  "make the fading atomic and tunable in one place."

### Why not just call `crossFadeTo` on the stance swap?

`crossFadeTo`/`fadeIn`/`fadeOut` schedule a weight-interpolant that three.js
advances internally. But the locomotion blend space's own per-frame loop
(`updateActorAnim`) already calls `action.setEffectiveWeight(L[i].w)` on
*every* node *every* frame — and `setEffectiveWeight` calls `stopFading()`
internally (verified in the vendored source), which cancels any pending
fade. The two mechanisms fight: the blend space's per-frame weight write
would cancel a `fadeIn`/`fadeOut` schedule before it ever completed. The
stance swap needs a *manual* crossfade integrated into the same per-frame
loop that already owns node 0's weight — see §1 below.

### Rig: does the skeleton support a clean upper/lower split?

**Yes, cleanly.** Parsed `superhero.gltf`'s node hierarchy directly (not
assumed): standard Unreal-Mannequin-style rig —
`root → pelvis → { spine_01 → spine_02 → spine_03 → { neck_01 → Head,
clavicle_l/r → ... → hand_l/r → fingers }, thigh_l/r → calf_l/r →
foot_l/r → ball_l/r }`. `spine_01` is the obvious, single, well-identified
split joint: everything at or above it (spine chain, arms, neck, head) is
"upper body"; everything below `pelvis` (both leg subtrees) is "lower
body." No naming ambiguity, no missing joint.

### Feasibility of item 2 (upper/lower layering) — the rig says yes, the clip content says not without re-scoping

Parsed the actual animation channels out of `anims1.glb`/`anims2.glb`
directly (every clip, not a sample): **every single extracted clip — all
22 of them, attacks and casts and blocks included — carries rotation
tracks on every leg bone** (`thigh_l/r`, `calf_l/r`, `foot_l/r`,
`ball_l/r`). These aren't neutral/idle-compatible legs sitting still under
an upper-body gesture — `Sword_Attack`, `Spell_Simple_Shoot`,
`Sword_Regular_A/B`, `Punch_Jab/Cross`, `Sword_Block` etc. all have their
own authored leg motion (weight shifts, stance changes) baked in. This is
exactly the condition flagged in the brief: *"if clips were authored as
full-body-only with no consistent lower-body idle across them, this needs
re-scoping, not a code fix."* They are. A naive bone mask (locomotion legs
+ overlay-clip upper body) would *discard* that authored leg weight-shift
and substitute an unrelated walk-cycle stride — which could read as *more*
disconnected than today's full-body one-shot, not less. This can't be
confirmed either way without eyes on a real render frame-by-frame, which
is outside what this pass can verify from here.

Separately, checked whether the situation item 2 exists to solve — "a
cast playing over a run cycle" — is actually reachable. It is not, for the
player: `GameTickService.ProcessTick`'s attack gate requires
`!playerMovedThisTick` (the player must not have moved *this tick* to
attack at all) — the player's own attack/cast/block overlay **only ever
plays while stationary**, by sim design, not "usually." The brief's own
hedge — *"it may turn out unnecessary if most attacks land on stationary
ticks anyway"* — resolves to "all of them, always, for the player." The
boss side is less certain (scripted movement + attack timing could
plausibly overlap for some boss) but wasn't confirmed as a real, common
occurrence across the 4 implemented boss scripts either.

**Recommendation: don't build item 2 this pass.** Both independent
questions the brief asked to gate it on came back "not without more work
first" — the clip content needs re-scoping (real animation-editing work,
not available here) before a mask would even look right, and the
player-side motivating scenario doesn't occur at all under the current
combat model. Logging as a backlog item with both findings attached rather
than either building something likely to look worse, or silently dropping
the ask.

### Mixer delta-time (ruled out as a separate cause)

Both places `mixer.update(dt)` is called use a genuine variable delta,
capped at 0.05s against frame hitches — not a fixed step: the battle loop
uses `Math.min(0.05, st.clock.getDelta())` (`st.clock` is a real
`THREE.Clock`); the equipment-preview loop uses `Math.min(0.05, (now -
pst.lastT) / 1000)` off `performance.now()`. Not a contributing cause.

---

## 1. Crossfade consolidation (implementing this pass)

- New named, tunable duration table (module-level in `toon.js`), using
  the brief's own suggested defaults verbatim (not invented):
  ```js
  const TRANSITION_S = {
      idleWalk: 0.10,   // documents the locomotion blend's own tau (0.07–0.14s) — not a second mechanism, see above
      walkAttack: 0.12, // one-shot overlay fade-IN (attack/cast/block/eat/windup) and the stance-swap crossfade
      attackIdle: 0.20, // overlay fade-OUT on finish, back to locomotion (was 0.18, hardcoded)
      hitReact: 0.08,   // flinch fade-IN (was 0.05, hardcoded) — still fast, "hits shouldn't feel delayed"
      death: 0.15,      // death fade-IN (was 0.06, hardcoded)
  };
  ```
  Flagging the deltas from current behavior explicitly since they're real,
  if small, changes: hit-react fade slows from 50ms→80ms, death fade slows
  from 60ms→150ms, the finished-handler's return-to-locomotion fade goes
  180ms→200ms. All per the brief's own stated defaults, not silently
  picked.
- `playOverlay`'s default `fade` param becomes `TRANSITION_S.walkAttack`;
  the interrupt-fadeout inside it uses the same constant.
- `flinch()`'s explicit `fade: 0.05` → `TRANSITION_S.hitReact`.
- The death `playOverlay` call's `fade: 0.06` → `TRANSITION_S.death`.
- The `mixer`'s `'finished'` handler's `fadeOut(0.18)` →
  `TRANSITION_S.attackIdle`.
- `setActorStance`'s hard cut → a manual crossfade integrated into
  `updateActorAnim`'s existing per-frame node-0 weight write (see "why
  not just crossFadeTo" above): keep the outgoing action alive as
  `node.fadeOutAction`, ramp incoming/outgoing weights in opposite
  directions over `TRANSITION_S.walkAttack`, then stop and drop the
  outgoing reference.

## 2. Upper/lower body layering — not implemented this pass

Per the feasibility findings above. Backlogged with both blocking
findings (full-body-authored clips, player attacks are always stationary)
attached, so a future revisit starts from "what would need to change"
instead of re-deriving this.

## Verification plan

`dotnet build`/`test` unaffected (JS-only). Browser: confirm no console
errors from the new crossfade code, sample `actor.overlay`/locomotion
node weights across a real attack sequence and a weapon-style swap to
confirm no snap/pop and that outgoing actions are actually stopped (no
leaked always-playing actions), screenshot a stance swap mid-transition.
Findings to `design/plans/animation-pass-findings.md`.
