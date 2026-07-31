# Hive Matron rework — plan (IMPLEMENTED)

A player-directed redesign of the Hive Matron fight. It supersedes parts of
Boss Bible §2 (`duels-boss-designs.md`) — those deviations are in §6, and the
Bible has been updated to match (its §2 now carries a "Melee rework" note). All
four owner decisions (§7) are implemented; see `hive-matron-fixes-findings.md`
for the as-built notes. **All numbers are PROVISIONAL** (no doc source) and
tagged for a tuning pass — start with the melee vulnerability (+30%) and the
Needle Spit damages if the fight feels off.

## 1. Why

Three things about her currently "don't make sense," and they share one root:
**her whole kit is choreographed for a melee-pressure player, but the melee
half was never made to work, so at range it all performs to an empty room.**

- The **dash** ("dashes back to reset spacing") fires after every 3rd attack
  regardless of distance and has no attack behind it — so she retreats from a
  ranged player who isn't chasing. Pointless.
- The **weave** (her signature "melee is possible but must be danced") is
  unplayable: the engine forbids attacking on a move tick, and her continuous
  flee (tested) means a melee player can never reach her. (backlog #40)
- The **drones** spawn at fixed east/west angles around her and then freeze
  (station-keep sees them already at orbit radius → they never move), so they
  block nothing and do nothing. (backlog #36)

## 2. Design goals (from the design owner)

1. **Melee should be viable — in fact she is *weak* to melee.** The weave
   becomes the rewarded, intended way to fight her, not a death sentence.
2. The **dash becomes a real, telegraphed retreating attack** — "Needle Spit."
   It must demand **both** a prayer (Range) **and** a positional dodge, and it
   must have a **visual tell before she leaps**.
3. The **needle floor pattern** is the player's tile **+ its 4 cardinal
   neighbours** (a "+"), so the only safe step is **diagonal**.
4. The **drones actually body-block the melee approach** — they orbit
   *between her and the player*, not behind her, and can be baited out of the
   lane with movement (per the Bible's own "or bait them out of formation").

## 3. The three changes

### A. "Needle Spit" — replaces the silent dash

Rotation change (this is the "rewrite the sequence" the owner flagged): pull
the dash out of the silent `RecordAttackAndMaybeDash` reposition and make it a
telegraphed attack step.

- **Trigger.** Keep the every-3rd-attack cadence, but only when the player is
  within her preferred band (≤ `PreferredRangeMax`, i.e. she has a reason to
  make space). A far, kiting player never triggers it — killing the "retreat
  from nobody" nonsense.
- **Tell (≥2 ticks).** A wing-flare telegraph (doctrine-Range-colored rim
  glow / windup pose) + log line "The Hive Matron rears back, wings screaming
  — NEEDLE SPIT!" so a leap is always readable before it happens.
- **Resolve.** She leaps 3 tiles directly away **and** fires the needle spread
  at the player's tile-at-cast.
- **Floor pattern.** The player's tile + N/S/E/W (a "+"). Safe tiles are the 4
  diagonals → the dodge is a diagonal step.
- **Pray + dodge, both matter (recommended two-layer read).** The needle hit
  is **Range-typed** on the 5 tiles: praying Range negates the *needle*
  damage. But a struck tile also drops a 1-tick **venom nick** (small,
  unprayable) — so praying alone saves most of it, dodging avoids it cleanly,
  and doing both is the mastery play. *(Alternative simpler read: Range-typed
  only, so pray OR dodge each fully works — open question Q1.)*
- **Numbers (PROVISIONAL):** needle direct 18 (Range, "Medium"); venom-nick
  4; leap distance 3 tiles; telegraph 2 ticks.

### B. Melee viability + melee weakness

To make the weave the rewarded line:

- **Drop the continuous flee.** `ProcessSpacingAiMovement`'s "step away every
  tick the player is within `PreferredRangeMin`" is deleted. Her *only* way to
  make space becomes the telegraphed Needle Spit — which the player can read,
  dodge, and stay on her through. (Retires the tested
  `SpacingAi_StepsAwayWhenPlayerCloserThanPreferredRange` behavior — replaced
  by a Needle-Spit test.)
- **Keep the Tail Stab "stands" rule** (already shipped): weaving in→hit→out
  is safe; *holding* melee two stationary ticks still eats a Tail Stab. That's
  the "danced, not held" tension, now actually reachable.
- **Melee weakness.** Melee damage dealt *to her* is amplified by a new
  `MeleeVulnerabilityPercent` (PROVISIONAL **+30%**), mirroring how Chitin
  Guard *reduces* ranged/magic — the same `ApplyBossDamageReduction` seam,
  inverted. This is what makes "weak to melee" real, and pairs with Chitin
  Guard (ranged/magic down periodically) so the fight actively *pushes* you
  into the weave.
- **Attack-on-arrival (open question Q2).** Even with the above, one engine
  rule still bites: you can't swing on the tick you step in, so a hit lands on
  the *second* adjacent tick. With Tail Stab now counting only *stationary*
  ticks that's survivable, but the cleanest weave ("step in and hit same
  tick") needs a global rule change (allow a melee swing on a move tick that
  ends in range). That touches all melee vs. all bosses — flagged, not
  assumed.

### C. Drones body-block the approach lane

- **Spawn between her and the player.** Instead of fixed east/west angles,
  spawn (and keep) the two drones on the boss→player vector at
  `OrbitRadius`, fanned ±1 tile so they flank the lane you'd walk up.
- **Track the lane.** Each tick, re-target the drones onto the current
  boss→player line (they *orbit to stay in front of you*), so circling the
  boss drags them out of position — "bait them out of formation with
  movement," per the Bible.
- **Actually block.** Their live tiles join the player's pathing obstacle set
  (`IsBlocked` / `NextStepToward`), so you must path *around* them or kill
  them (3 HP each) to open a melee lane. This is the real backlog #36 fix.
- **Guard rails:** drones never occupy the player's or boss's tile; if the
  lane is wall-pinned they settle on the nearest free lane tile. Ranged play
  is unaffected (you're not walking the lane).

## 4. PROVISIONAL numbers (all need a tuning pass; none are doc-sourced)

| Thing | Value | Notes |
|---|---|---|
| Needle Spit direct dmg | 18 | Range-typed, "Medium" band |
| Needle venom-nick | 4/1 tick | unprayable, only if you didn't dodge |
| Needle telegraph | 2 ticks | standard tell lead |
| Leap distance | 3 tiles | unchanged from old dash |
| Melee vulnerability | +30% | melee dmg dealt to her |
| Drone lane fan | ±1 tile | flanks the approach lane |

## 5. Verification plan

- Harness: the `melee` brain (currently dies in ~6s landing 1 hit) should now
  **win** — the acceptance test for "melee is viable." Add a `melee-weave`
  brain that reads the Needle-Spit tell and diagonally dodges.
- Unit tests: Needle-Spit "+" pattern + diagonal-safe-tile; pray-Range vs
  venom-nick two-layer; melee-vulnerability multiplier; drone-blocks-a-lane /
  bait-out-with-movement; dash-does-not-fire-when-player-is-far.
- Browser: watch it via the Playtest autopilot (add a melee autopilot line, or
  confirm the ranged line still reads cleanly with the new leap tell).

## 6. Deviations from Boss Bible §2 (reconcile the doc after approval)

- **Identity shift.** The Bible frames her as *teaches spacing*, drop niche
  *best-in-slot ranged weapon*. Making her **melee-weak / melee-rewarding**
  tilts her toward a melee fight. Worth a deliberate call: keep the ranged
  drop (spacing still matters — Dart Volley, Needle Spit, Chitin Guard) or
  re-theme the drop. **(Open question Q3.)**
- The **dash** is redefined from a silent reposition to a telegraphed attack.
- **Drones** gain real collision (the Bible implies it; it was never built).

## 7. Owner decisions (RESOLVED — implementing to these)

- **Q1 → both.** Needle Spit is two-layer: Range-typed needle (pray Range
  reduces) **plus** an unprayable venom nick on a struck tile, so you must pray
  *and* diagonally dodge to take zero.
- **Q2 → change the rule.** Melee will be allowed to swing on a move-tick that
  ends in range ("attack on arrival") — current melee "doesn't feel good."
  Applied so the weave is snappy.
- **Q3 → keep the ranged-weapon drop**, just stop calling her *the spacing
  teacher* in the Bible — she throws ranged needles, so a ranged drop stays in
  theme.
- **Q4 → keep Chitin Guard** (it's the pressure that pushes you into the weave).

## 8. Open questions for the owner (original, now resolved above)

- **Q1.** Needle Spit: two-layer (pray *and* dodge both matter — recommended)
  or single-layer Range AoE (pray *or* dodge)?
- **Q2.** Do we also change the global "no attack on a move tick" rule to make
  the weave feel frictionless, or accept the current one-tick delay (weave
  still works, just less snappy)?
- **Q3.** Keep her ranged-weapon drop niche, or re-theme given the melee tilt?
- **Q4.** Should Chitin Guard stay (ranged/magic down) as the "get in and
  weave" pressure, now that melee is the reward? (I think yes — they pair.)
