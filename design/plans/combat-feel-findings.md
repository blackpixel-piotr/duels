# Combat feel pass 1: Implementation Findings

Companion to `combat-feel-plan.md`. Written during implementation — what
actually happened, where reality diverged from the plan's assumptions, and
every place a number wasn't sourced from a design doc and had to be
flagged rather than invented.

---

## Post-merge bugs, round 2 (user-reported)

Three more issues from live playtesting, none caught by the original
verification pass (which drove one full fight but didn't specifically
probe these):

1. **Lunge fired for ranged/magic attacks too.** `startLunge` was called
   unconditionally in the `attack_swing` handler, so a caster/archer
   physically lurched toward the target while casting/firing — read as
   "attacking with a sword lunge" regardless of which clip actually
   played. Melee closing distance into a swing makes sense; a ranged/magic
   attacker staying at range and firing in place does not. Fixed: gated
   `startLunge` to `style === 'melee'` only.
2. **Ranged/magic `hit_blocked` played `hitA`, a hurt-flinch, for a fully
   negated 0-damage hit** — backwards, and a direct (unnoticed)
   contradiction of this codebase's own existing rule: `flinch()` already
   deliberately excludes tier `'blocked'` for exactly the reason that a
   successful defense shouldn't look like getting hurt. The original
   in-code comment even reasoned through this exclusion correctly, then
   the code defeated it anyway by calling `playOverlay(..., 'hitA', ...)`
   directly instead of going through `flinch()`. Since Maggot King is
   mostly ranged/magic, nearly every prayer-block a player saw against him
   hit this path — which is almost certainly why it read as "no block
   animation" even though the melee `block` pose (confirmed present and
   loading correctly — `st.player.clips.block` truthy, checked live) was
   never actually broken. Fixed: ranged/magic `hit_blocked` now plays no
   body animation at all — VFX (`shield_dome`/`deflect_ward`) + the
   splat's "blocked" ring alone carry the read, matching `flinch()`'s own
   convention. Verified with a synthetic `setVfxEvents` call (bypassing
   the need to actually land a prayer-timed block in a real fight):
   `style:'magic'` → `overlay` stays `null`; `style:'melee'` → `overlay`
   becomes `'block'`.
3. **`cameraMotion`/`vfxQuality` had no UI at all**, dev or otherwise —
   `clientPrefs` existed only as a JS API (`getClientPrefs`/
   `setCameraMotion`/`setVfxQuality`) nothing called. Added a `PREFS`
   toggle button in `BattleScene.razor`, same "always visible, not
   TestScene-gated" convention as the existing `MECH` panel (the only
   other reason no prior panel existed for this: iteration 1 explicitly
   scoped out building any UI, correctly, but a zero-UI dev entry point
   should have shipped alongside the backend rather than after a user had
   to ask where it was).

Lesson: the original verification pass checked that each new mechanism
*fired* (an event was observed, a clip name showed up in `overlaysSeen`)
but not always whether the *result* looked right end-to-end (facing
smoothness, style-appropriateness of a physical effect, animation valence
matching outcome). "It ran without erroring" and "it did the right thing"
are different claims — worth two different checks, not one.

---

## Post-merge bug: facing clamp broke smooth tracking (user-reported)

The user reported that, after this pass, animations looked *worse* overall
(camera improvement aside). Root cause: §1's facing clamp
(`actor.facing += Math.max(-maxStep, Math.min(maxStep, da))`) clamped the
*raw* angle gap, not an eased step. Since `destFacing` is recomputed fresh
every frame (e.g. the enemy re-aims at the player's live position
continuously), the per-frame gap is almost always well under `maxStep`
(~15°/frame at 60fps) — meaning the clamp was a no-op nearly every frame
and `facing` just snapped straight to that frame's target, every frame.
All the smoothing the pre-pass exponential ease provided was gone except
during a genuinely large, sudden reversal — exactly backwards from what
was needed, and it read as twitchy/robotic tracking rather than a
bounded-speed turn.

Fixed by clamping the *eased* step instead: `eased = da * Math.min(1, dt *
14)` (the original ease, unchanged), then `Math.max(-maxStep,
Math.min(maxStep, eased))`. Verified by sampling the enemy's `facing`
across 60 real frames while the player walked: ordinary continuous
tracking now decays geometrically (~30%/frame, matching the original ease
exactly — `0.32° → 0.097° → 0.029° → 0.0088°...`), and the one large jump
observed (during a headless-Chromium frame-hitch, `dt` at its 0.05s cap)
was capped at exactly 45° = 90°/100ms × 50ms — the bounded-turn-speed
behavior working as intended, not a regression.

Lesson: a max-*step* clamp and a max-*speed* clamp are not the same thing
when the target itself moves every frame — clamping the delta directly
only bounds speed when the target is stationary from the actor's
perspective; against a live-recomputed target it deletes the smoothing.
Should have been caught by the original browser verification pass, which
sampled `facing` values but didn't check frame-to-frame *smoothness*
(only that turning happened at all) — a gap in that verification, not
just in the implementation.

---

## Verification

- `dotnet build Duels.sln`: 0 errors, 0 warnings.
- `dotnet test Duels.sln`: **141/141 passing**, unchanged from before this
  pass — including the ~15 tests that assert an exact hitsplat log-message
  string (e.g. `"18:normal:magic"`, `"0:blocked:magic"`) or an exact
  `BossCast` message (`"magic"`). `GameState.AppendHitsplat` reproduces
  those messages byte-for-byte from the same typed values now also used to
  build the companion vfxEvent, rather than changing the log format.
- **Browser verification (Playwright, per the `verify` skill)**: launched
  the real app, new character → T1 dev loadout → Boss Roster → Maggot King,
  engaged the boss (clicked via the documented world→screen projection
  technique) and let a real fight play out to its conclusion. Applied last
  pass's lesson (`vfx-findings.md`) that a screenshot alone had missed a
  real positioning bug — this time checked actual engine state, not just
  pixels:
  - `overlaysSeen` included `swordA` (player melee attack clip) and `cast`
    (Maggot King's magic windup) — confirms `attack_swing` drives
    `attackRoleForStyle` correctly for both the player's synchronous
    hitsplat-site trigger and the boss's `BossCast`/`SpawnProjectileAttack`
    trigger, across two different styles.
  - `lungeKindsSeen` included `lunge` — `presentFx` actually engages on a
    real swing (`forced_move`'s `slide` kind wasn't exercised — Maggot King
    has no knockback mechanic; Hive Matron's Tail Stab would be the one to
    check that path against, not attempted this pass).
  - Live particle count peaked at 34 (iteration 1's own cap was 15) —
    confirms the new `attack_swing`/`impact` manifest rows are firing
    bursts on top of the existing `dust_puff` pool, not just present in
    the manifest unused.
  - Facing clamp produced a clean `π` (not NaN/garbage) after a real
    reversal.
  - Camera spring: with `cameraMotion:'full'`, `camFocus` converged to
    *exactly* the player-enemy midpoint (`(-1.75, -5.25)` for player
    `(-1.75, -3.5)` / enemy `(-1.75, -7)` — arithmetic checked, not just
    eyeballed). Switching to `'off'` via `setCameraMotion` relaxed
    `camFocus` back toward pure player-tracking (within ~0.00002 wu after
    600ms of easing) and `distSpring` back to 1.0 — confirms `'off'`
    genuinely collapses the spring rather than merely shrinking it.
  - `getClientPrefs`/`setCameraMotion` round-tripped through localStorage
    correctly.
  - The fight ended in a real defeat screen ("DEFEATED vs THE MAGGOT KING,
    killed by Poison pool") — confirms the trimmed-down `battleEvent`
    (now only `enemyDeath`/`playerDeath`, flag-driven) still fires
    correctly, unaffected by the CombatLog-parsing removal elsewhere.
  - Zero new console errors — only the two known pre-existing ones
    (`Duels.Web.styles.css` 404, Google Fonts blocked by the proxy).
  - **Not exercised**: `hit_blocked` (would need a real prayer-negated hit
    — this run's prayer wasn't set to match Maggot King's active style at
    the right tick); `forced_move`'s `slide` kind (no knockback mechanic
    on this boss). Both share code paths already exercised by
    `impact`/`attack_swing` (same `AppendHitsplat`/`handleCombatVfxEvent`
    machinery, same manifest-driven particle dispatch), so risk is low,
    but flagging per CLAUDE.md rather than claiming full coverage.

---

## The CombatLog migration was bigger than the brief's own examples

The user's instruction named the obvious cases (sip, Perfect Dodge,
BossCast's style) and asked to grep for siblings. The full sweep found:

1. **Every hitsplat** (`HitsplatPlayer`/`HitsplatNpc`, ~23 `AppendLog` call
   sites across `GameTickService.cs`) colon-encoded damage/tier/style-or-
   weapon into the log message, which `BattleScene.razor` then
   `.Split(':')`'d back apart to drive the attack clip, hit-react flinch,
   and splat. This was actually the *biggest* instance of the anti-pattern,
   bigger than the three the brief named — the player's own attack style
   wasn't even in the log at all; `BattleScene.razor` re-derived it itself
   via `ItemRepo.GetWeapon(WeaponId).AttackType` on every render. All of it
   is now `GameState.AppendHitsplat(onEnemy, dmg, tier, style, weapon)` — a
   single call site every hitsplat routes through, so the log message and
   the vfxEvent payload are guaranteed to agree (they're built from the
   same values), and neither one is derived from the other.
2. **Screen shake** read `LogEntryKind` (not `.Message` — a typed enum
   check, not string-parsing) across `MaxHit`/`SpecHit`/`BossSpecial`.
   The `MaxHit`/`SpecHit` portion is now redundant with `impact`'s own
   `tier` field (already real data) and was migrated; `BossSpecial` is
   kept as an explicit, narrow exception — see the "left as-is" section
   below and backlog.md.

## Design decisions made during implementation

1. **`VfxEvent.Data` widened from `IReadOnlyDictionary<string, double>` to
   `IReadOnlyDictionary<string, object>`.** Iteration 1 (`entity_moved`)
   only ever needed a numeric `dx`/`dz`; this pass's events need strings
   too (`style`, `tier`, `weapon`, `itemId`). Both existing `entity_moved`
   call sites still box `int` deltas into the same dictionary shape — no
   behavior change, just a wider type. `vfx.js`'s reads (`ev.data?.dx`)
   are unaffected since JSON doesn't distinguish int from double anyway.
2. **`AppendHitsplat`'s `attack_swing` emission logic reproduces the OLD
   `alreadyAnimatedAtCast` condition exactly** (`tier is not
   ("poison"/"hazard") && !(!onEnemy && style is "ranged"/"magic")`) —
   this was the trickiest part to get right without a behavior regression:
   an NPC's ranged/magic hitsplat is that attack's *impact*, whose swing
   already played back at the earlier `BossCast`/`SpawnProjectileAttack`
   site (which now fires its own `attack_swing` directly, carrying the
   real `StyleToken(attack.Style)` — no more reading `e.Message` as the
   style). Melee never has a cast phase, so it's still the hitsplat site's
   only trigger, unchanged.
3. **`attack_swing`'s payload gained a `tier` field beyond the plan's
   literal `{entityId, targetId, style}`.** Needed to pick the `'spec'`
   clip (`attackRoleForStyle`'s first check) — the plan's 3-field schema
   was the *intent*, not a hard constraint; noted here rather than
   silently narrowing what the event carries.
4. **`forced_move`'s payload is flat (`fromX`/`fromZ`/`toX`/`toZ`)**, not
   the plan's nested `{from, to}` — simpler given `VfxEvent.Data` is a flat
   dictionary; no nested-object support was worth adding for two points.
5. **The mesh lunge/forced-move slide share one `presentFx` state machine
   per actor** (`startLunge`/`startForcedMoveSlide`/`updatePresentOffset`
   in `toon.js`), not two separate mechanisms — a lunge and a forced-move
   slide can't overlap in practice (a knockback interrupts whatever the
   actor was doing), so one slot suffices. Both are a small additive
   offset applied only at the final `ch.group.position.set` call, per the
   plan's requirement that hit resolution/occupancy never move.
6. **"Ease-out-back" (lunge) is a plain sine arc** (0 → max → 0), not a
   literal back-ease with overshoot — a reasonable, much simpler
   approximation given the lunge is capped at 0.3 tile and ~220ms;
   revisit if a real device playtest finds it reads as timid.
7. **Facing turn-rate**: `MAX_TURN_RAD_PER_S = (π/2)/0.1` — literally the
   brief's own "~90°/100ms," not a design-doc number. **PROVISIONAL**,
   listed in backlog.md. A 180° reversal now takes a bounded ~200ms
   instead of the old ease's long asymptotic tail.
8. **Camera spring's separation-to-zoom mapping is a hand-picked band**
   (neutral at 4 tiles separation, ±15% at `'full'`/±7.5% at `'reduced'`,
   clamped, centered on a 4-tile window) — **PROVISIONAL**, no design-doc
   source, not device-tuned. `'off'` collapses both `pull` and `band` to
   0, which makes the spring's own target *equal* `player.pos` and the
   distance multiplier *equal* 1 — i.e. `'off'` restores the pre-pass
   behavior exactly, not approximately (verified by reading the formula,
   not just eyeballing it).
9. **VFX manifest rows gained an optional `style` filter** (`vfx.js`'s
   `handleEvents` only fires a pooled effect whose row has no `style` or
   whose `style` matches `ev.data.style`) — needed because `attack_swing`/
   `impact`/`hit_blocked` each want a *different* effect per doctrine
   style, not the same effect for every style the way iteration 1's single
   `entity_moved` → `dust_puff` mapping worked. `slash_arc`/`impact_burst`
   became 3 rows apiece (one per style); `hit_blocked` became 3 differently
   *shaped* effects (`blocked_spark`/`deflect_ward`/`shield_dome`), not 3
   colors of the same shape.
10. **New effects reuse the existing dust drift/ForceOverLife machinery
    unchanged** — every pooled effect gets the same slight upward float
    (`ForceOverLife`'s baked-in Y component) whether or not that suits it
    (a "shield dome" floating upward is a minor mismatch). Accepted for
    this pass; a per-effect behavior override is a reasonable follow-up
    once there's a second data point beyond "everything drifts a little."

## Left as-is, and why (per CLAUDE.md: flag, don't silently resolve)

- **Screen shake's `BossSpecial`-kind trigger stays in `BattleScene.razor`,
  unmigrated.** It's a typed `LogEntryKind` read, not `.Message`
  string-parsing — not the anti-pattern this pass targets — but it *is*
  still a CombatLog-derived renderer decision, so it's not fully consistent
  with the new "CombatLog is UI text only" doctrine line either. Migrating
  it properly means deciding which of ~30 `BossSpecial` log lines *per
  boss* (phase transitions vs. minor telegraph flavor text) actually
  deserve a shake — a content/feel judgment, not a mechanical refactor,
  and out of scope for a pass about attack/defend/forced-move presentation.
  Logged in backlog.md.
- **No Settings UI was built** — `clientPrefs` is the localStorage object
  a future M6 screen would read/write, per the user's explicit instruction
  to structure it as that backend now rather than a throwaway flag. `api.
  getClientPrefs`/`setCameraMotion`/`setVfxQuality` are the entry points;
  nothing calls them yet outside a console/dev context.
- **Kenney's particle pack is still unreachable from this sandbox** —
  unchanged since the VFX layer's iteration 1 (`vfx-findings.md` #5,
  backlog #40). All 6 new effects use the same procedural placeholder
  texture; not re-attempted this pass.

## What this pass leaves for later

- A real Settings UI over `clientPrefs` (M6).
- Tuning the lunge/slide/camera-spring numbers against a real device —
  everything numeric here is PROVISIONAL, tracked in backlog.md.
- `BossSpecial`'s shake-trigger migration (needs a content decision first).
- Per-effect behavior overrides in `vfx.js` (drift direction/strength) if
  a shield-dome's upward float ends up reading wrong in practice.
