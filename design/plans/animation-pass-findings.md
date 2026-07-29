# Animation quality pass: Implementation Findings

Companion to `animation-pass-plan.md`. Written during implementation — what
actually happened, verification results, and where reality diverged (or
didn't) from the plan's assumptions.

---

## What shipped

Item 1 (crossfade consolidation) only, exactly as scoped. Item 2
(upper/lower body layering) was explicitly not built — see the plan's own
feasibility findings, now also logged as backlog item 49.

- `TRANSITION_S` constant table added to `toon.js` (module level, next to
  `MAX_TURN_RAD_PER_S`), using the brief's own suggested defaults verbatim:
  `idleWalk: 0.10, walkAttack: 0.12, attackIdle: 0.20, hitReact: 0.08,
  death: 0.15`.
- `playOverlay`'s default `fade` param and its interrupt-fadeout now read
  `TRANSITION_S.walkAttack` instead of two separately-hardcoded `0.08`s.
- `flinch()`'s hit-react fade: `0.05` → `TRANSITION_S.hitReact` (0.08).
- The death `playOverlay` call's fade: `0.06` → `TRANSITION_S.death` (0.15).
- The `mixer`'s `'finished'` handler's return-to-locomotion fade: `0.18` →
  `TRANSITION_S.attackIdle` (0.20).
- `setActorStance`'s hard `.stop()`/`.play()` cut (idle ↔ swordIdle ↔
  spellIdle) replaced with a manual crossfade: the outgoing action is kept
  alive as `node.fadeOutAction` with a running `fadeOutT`/`fadeOutFrom`,
  and `updateActorAnim`'s existing per-frame node-0 weight write (the loop
  that already owns `setEffectiveWeight` on every locomotion node) ramps
  both actions' weights in opposite directions over
  `TRANSITION_S.walkAttack`, then stops and drops the outgoing reference
  once the ramp completes. A stance swap that interrupts an already
  in-flight one snaps the older fade rather than chaining three actions —
  not attempted differently since rapid re-swaps (equip → immediately
  unequip) aren't a real gameplay pattern.
- `resetBattle` (fight restart) also clears any lingering `fadeOutAction`
  reference per actor, so a mid-fade stance swap can't leave a stopped
  action dangling across a duel reset.

No other call site changed. The locomotion blend space's own continuous
per-frame weight easing (`k = 1 - exp(-dt/tau)`) was deliberately left
untouched, per the plan — it's a better mechanism than a fixed-duration
crossfade for a continuous speed variable, and the brief's "idle↔walk
100ms" figure is already documented as matching its existing tau rather
than implemented as a second mechanism.

## Verification

`dotnet build` and `dotnet test` (69 passed) both unaffected, as expected —
this pass is JS-only.

Browser verification via Playwright (per the `verify` skill), driving the
real app: new character → melee style → `DEV: T1 LOADOUT` → Boss Roster →
Maggot King → FIGHT, engaged the boss via the documented world→screen
projection technique. Two things worth flagging about the navigation
itself, not the animation code: the `verify` skill's documented "DUEL
ARENA" flow is stale — the Hub now uses a `BOSS ROSTER` card opening a
`RosterSheet` overlay (`.roster-card`) into a pre-fight screen with a
`FIGHT` button, not a ladder card; the skill doc should be updated
separately (not done here, out of scope for an animation pass, but noting
it since a future session will hit the same stale selector).

Sampled real engine state, not just screenshots (per `vfx-findings.md`'s
established lesson):

- **Stance-swap crossfade**: force-toggled the player's weapon
  (`voxel.setBattleWeapon`, unequip then re-equip `steel_sword`) and
  sampled `actor.loco[0]` every `requestAnimationFrame` across the swap.
  Confirmed the exact designed math: on swap, `role` flips to `swordIdle`
  immediately while `fadeOutAction` holds the previous `idle` action;
  each frame the incoming action's live weight (`getEffectiveWeight()`)
  equals `node.w * t` and the outgoing action's live weight equals
  `fadeOutFrom * (1 - t)` where `t = fadeOutT / TRANSITION_S.walkAttack` —
  cross-checked the sampled numbers against this formula by hand and they
  matched (e.g. incoming `w=0.114` at `t≈0.417` → predicted live weight
  `0.0475`, sampled `0.047`). The fade completed in ~3 sampled frames
  instead of the ~7 frames a 120ms transition would take at 60fps — not a
  bug: headless Chromium's frame time regularly exceeds the render loop's
  own `dt` cap (`Math.min(0.05, clock.getDelta())`, per the `verify`
  skill's "~12fps" note), so each sampled frame advanced the fade by the
  full 50ms cap rather than a real ~16.7ms tick; the ramp math itself is
  frame-rate-independent (driven by accumulated `dt`, not frame count).
  After the ramp, `hasFadeOut` correctly returned to `false` — the outgoing
  action was stopped and dropped, not leaked as a silently-still-playing
  action.
- **One-shot overlay fades**: sampled `actor.overlay` across a live combat
  sequence (a boss hit landing mid-player-attack). Observed a `hitA`
  flinch fade in from weight `0.625` to `1` (`fading: true` → `false`, i.e.
  the THREE-native fade schedule completing), then a same-frame-adjacent
  interrupt into `swordB` (the player's own attack overlay) fading in from
  `0.417` to `1` the same way, then `overlay` clearing to `null` once
  `LoopOnce` finished and the `'finished'` handler's `attackIdle` fade-out
  ran. No hard pop at any transition.
- **Console**: zero new console errors. The only two entries seen
  (`404` for `Duels.Web.styles.css`, `ERR_CONNECTION_RESET` for a Google
  Fonts fetch) are both pre-existing "known noise" per the `verify` skill,
  unrelated to this change.
- Screenshot captured mid-fight for a visual sanity check (not
  diff-compared against a baseline — headless timing makes catching an
  exact stance-swap mid-transition frame unreliable, and the weight
  sampling above already gives ground-truth confirmation the screenshot
  can't).

## Deltas from current behavior (flagged per the brief's own request)

All per the brief's stated defaults, not silently picked:
hit-react fade 50ms → 80ms (slower, still fast), death fade 60ms → 150ms
(noticeably slower), overlay return-to-locomotion fade 180ms → 200ms
(marginal). Playtested via the sequence above; nothing read as sluggish at
these durations.

## Item 2 (upper/lower body layering): not built, logged to backlog

No new findings beyond what the plan already established — see
`animation-pass-plan.md`'s feasibility section and backlog item 49 for the
two blocking findings (full-body-authored clip content; player attacks are
always stationary by sim design) carried forward for a future revisit.
