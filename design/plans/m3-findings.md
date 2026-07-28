# M3 — Combat Grammar as Systems + Bosses 2–4: Implementation Findings

Companion to `m3-plan.md`. Written during implementation — what actually
happened, where reality diverged from the plan's assumptions, and every
place a number/mechanic wasn't in a design doc and had to be flagged
rather than invented.

---

## Verification

- `dotnet build Duels.sln`: 0 errors, 0 warnings throughout (checked after
  every workstream).
- `dotnet test Duels.sln`: **141/141 passing** (21 Domain, 51 Application,
  69 Infrastructure — up from M2's 115; new: `HiveMatronTests` (10),
  `MirrorhideTests` (7), `BloodtitheTests` (9), plus fidelity assertions
  added to `DefinitionNpcRepositoryTests`).
- **Browser verification (Playwright, per the `verify` skill)**: launched
  the real app, drove Chromium through the actual new UI (not a synthetic
  harness) — new character → T2 dev loadout → Boss Roster → confirmed all
  4 real bosses (Maggot King, Hive Matron, Mirrorhide, Bloodtithe)
  selectable and the 4 unbuilt ones (Gale Roc, Unblinking, Millstone,
  Grand Duelist) render disabled with "Coming soon" → pre-fight screen
  showed the correct name/flavor text for Hive Matron → FIGHT started a
  real duel against **her specifically** (420/420 HP, not Maggot King) →
  repeated for Mirrorhide (386/480 after ~15 ticks) and Bloodtithe (see
  the bleed-tuning finding below) → re-verified Maggot King still fights
  correctly through the new roster/pre-fight flow (no regression). Zero
  JS console errors beyond the two pre-existing, already-documented ones
  (`Duels.Web.styles.css` 404, Google Fonts blocked by the proxy).
- **Not separately load-tested**: Gale Roc/Unblinking/Millstone/Grand
  Duelist obviously don't exist yet; RETRY against a non-Maggot-King boss
  wasn't explicitly re-verified (uses the same `StartDuelCommand` path,
  low risk, but not eyes-on).

---

## The real scope was bigger than the plan anticipated

`m3-plan.md`'s Workstream A audit correctly identified that Maggot King's
mechanics live directly in `GameTickService` rather than a general toolkit,
but implementing three genuinely different bosses surfaced additional
foundational gaps the plan didn't call out:

- **Arena size was a hardcoded compile-time constant**
  (`GameState.ArenaRadius = 4`), not per-duel data, despite `BossScript`
  already carrying an (unused) `ArenaRadius` field. Hive Matron's 11×11
  arena was impossible without fixing this first. Fixed: `ArenaRadius` is
  now an instance property seeded from `BossScript.ArenaRadius` in
  `StartDuel`; `GameState.InArena` became an instance method (was static);
  the two swarm-corner arrays in `GameTickService` were converted from a
  static field referencing the old constant to a `SwarmCorners(state)`
  helper. The JS renderer had the identical problem — `voxel.js`'s
  `WALK_R`/`ARENA_R` were also hardcoded consts — fixed the same way
  (mutable, set from `initBattle`'s new `opts.arenaRadius`, itself read
  from `GameState.ArenaRadius` in `BattleScene.razor`). None of this was
  in the plan; it was discovered mid-implementation.
- **`Player.MaxHp` is a flat 100** (combat-math-v2, XP/levels retired)
  with no per-boss or per-tier scaling. This isn't a gap M3 needed to
  close, but it's the reason the Bloodtithe bleed finding below reads as
  extreme in absolute numbers — see that section.

---

## What shipped vs. the plan

**Fully implemented, tested, and browser-verified**: Workstream A (shared
systems — Perfect Dodge generalized into `TryPerfectDodge`, punish window
already boss-agnostic at the engine level and just needed real triggers,
forecast messages generalized beyond Maggot King's literal wording,
per-duel arena size), Workstream B (Hive Matron), Workstream C
(Mirrorhide), Workstream D (Bloodtithe), Workstream E (item ingestion),
Workstream F/G (roster + pre-fight screens, SaveData v4, hub wiring).

**Scoping simplifications made along the way** (flagged here, not silently
shipped as full parity with the bible):

1. **Copycat (Mirrorhide, Phase 2)** replays the player's last-used special
   as a single fixed-damage hit rather than porting each of the six wired
   specials' individual effects (burn, sap, pin-delay, etc.) onto the boss.
   The plan's Workstream A.12 anticipated this exact scoping call in
   advance — implemented as planned, not a deviation.
2. **Pin's "stunned 2 ticks against the wall" (Hive Matron)** is
   approximated with the existing `DelayPlayerAttack` primitive (delays
   the player's next attack) rather than a full movement-lock, since no
   player-movement-freeze mechanism exists in the engine and building one
   for a single boss's one attack felt like the wrong trade against this
   milestone's actual scope. Flagged inline in `ResolveLineCharge`.
3. **Drones (Hive Matron) station-keep at their orbit radius rather than
   truly body-blocking melee lanes.** The bible's "body-blocking melee
   approach lanes" implies real lane-collision the player's pathing must
   route around; what shipped is real-HP adds that must be dealt with
   (killed or ignored) near the boss, not full collision geometry. Flagged
   inline in `ProcessAdds`.
4. **Chitin Guard's damage-reduction check, Cloak's untargetable check,
   Reflect's damage-back, and Attunement immunity are all now real, wired
   mechanics** — none of these were simplified away; they're full
   implementations, just calling out that this is the one area where the
   plan's Workstream A underestimated how much new *engine* surface (not
   just data) three new bosses would need beyond the truly-shared
   primitives (damage roll, prayer check, projectile flight) that already
   existed.
5. **Bloodtithe's back-tile bonus/Tithe-aura facing** is quantized to 4
   cardinal directions with a 1-tile/tick turn rate (matches "90° per
   tick" exactly) rather than continuous angles — a deliberate
   simplification given the game's tile-grid nature, not a corner cut.

---

## New PROVISIONAL numbers (per CLAUDE.md's rule, listed here + backlog.md)

The Boss Bible gives **band names** (Light/Medium/Heavy/Severe) for most
attacks but exact numbers for almost nothing beyond a few explicitly-stated
values (turn rates, stack caps, tick counts). Following the precedent
already set by Maggot King's own M1 numbers (18 for Medium, 35 for Heavy —
themselves unflagged translations of the band table, not sourced
individually), the same band-to-number translations were reused across
all three new bosses for consistency rather than inventing fresh values
per boss. Numbers that have **no band name or Global-Combat-Grammar-
derivable formula at all** are flagged PROVISIONAL below (also inline in
`npcs.json`):

- **Rotation cadences/tick spacing** for all three bosses' attack loops —
  the bible never gives Hive Matron/Mirrorhide/Bloodtithe a literal
  tick-by-tick table the way it does for Maggot King. Invented reasonable
  loops consistent with the game's established pacing.
- **Boss HP** for all three (420/480/520) — no boss's HP is sourced
  anywhere in the docs, including Maggot King's own 450.
- **Hive Matron**: Tail Stab's exact damage (35, "Heavy"-band), Sting
  Lob's venom-splash damage (18 land + 4/tick pool — bible gives no exact
  numbers, only "Medium"-ish framing and "10 ticks" duration, which *is*
  sourced), Pin's damage (35), Cloak-analog n/a (she has none), Copycat
  n/a (she has none).
- **Mirrorhide**: Echo Strike/Prism Sweep damage (18/35), Cloak's
  CadenceTicks (20 — "occasionally" isn't a number) and PounceDamage (25),
  Copycat's Damage (35) and CadenceTicks (20).
- **Bloodtithe**: Scythe Arc/Blood Lance damage (35/18), bleed
  DamagePerTick (3/stack — bible says "Light per tick," no exact rate;
  **see the tuning finding below, this is the one that turned out to
  matter in practice**), Crimson Pact's CadenceTicks (20), Harvest's
  DamagePerStack (18) and CadenceTicks (20).

All of the above are commented `// PROVISIONAL` (or the JSON-comment
equivalent) at their source in `npcs.json` and are listed in
`backlog.md`'s new entries (see below) for a human content/tuning pass —
not resolved silently.

---

## A real tuning finding from browser verification, not just a data gap

Browser-testing Bloodtithe (fresh T2-geared, 100 HP player, no Font usage)
produced a defeat in **7.2 seconds (12 ticks)**, "Slain by: Bleed" — not a
direct hit, the stacking bleed DoT itself. Tracing why: his 20-tick Phase-1
rotation puts a real attack (Scythe Arc or Blood Lance, whichever the
position-dependent pick resolves) at ticks 0, 6, and 16 — well within
reach of a fresh player who auto-closes at 2 tiles/tick against his own
1-tile/2-ticks approach — and **each landed hit both deals direct damage
*and* applies/refreshes a bleed stack** (per the bible: "every hit he
lands applies a bleed stack"). Two or three hits landing inside the
observed 12-tick window stacks bleed to 2–3 while continuously refreshing
its 10-tick duration, on top of the Tithe aura's separate 1%-max-HP/tick
drain while adjacent to his front — the combination is lethal to an
undefended 100-HP player well before they'd realistically reach either
Font tile (opposite arena corners from a fresh spawn).

This is not a code defect — the mechanics implement the bible's own text
correctly (bleed-on-hit is generic and confirmed by unit test; the Font
purge/Harvest counter-play is real and unit-tested) — but the **combination
of three unsourced PROVISIONAL numbers** (bleed's 3/tick, plus the fact
his rotation lands 2-3 hits inside a very short opening window given the
100-HP-flat combat model) makes his opening extremely punishing in
practice, arguably more than "sustain, DoT management" implies as a fight
identity. Flagged as backlog item below for a human tuning pass — the fix
space includes lowering bleed's per-stack rate, widening his rotation's
early-fight spacing, or intentionally keeping it lethal-if-ignored (his
whole fantasy is "inevitable" pressure) and expecting the FTUE/tooltip
layer to warn players to path toward a Font early. Not decided here.

---

## Interpretive choices flagged (not invented silently, but not doc-given either)

- **Common/Uncommon loot content for Hive Matron, Mirrorhide, and
  Bloodtithe** — exactly the gap flagged in `m3-plan.md`'s design question
  1, resolved the same way: reused Maggot King's exact Common/Uncommon
  shape and materials (`chitin_fragment`/`rot_gland`/gold), shifted to
  each boss's own tier's gear bridge (Hive Matron stays T1→T2 like Maggot
  King; Mirrorhide/Bloodtithe move to T2→T3). No new item names invented.
  Still pending a real content-authoring pass — added to backlog.md.
- **Held-prayer input (Hive Matron P2's 3-round Dart Volley burst)**
  resolved itself for free, as the plan's design question 2 speculated it
  might: since prayer state already persists until explicitly swapped and
  impact-resolution independently re-checks protection on each of the
  three consecutive-tick impacts, "holding" the correct prayer across all
  three is already the natural behavior with zero new input handling.
  Confirmed by code inspection; not separately unit-tested as its own
  scenario (covered implicitly by the existing prayer-check test coverage
  Maggot King's grub volley already exercises).
- **Locked-boss posters read as "not yet built"** — confirmed via browser
  verification: the four unbuilt bosses render disabled with "Coming
  soon," driven by `INpcRepository.GetTemplate` returning null, not a
  hardcoded flag, so this can never drift out of sync with what's actually
  implemented.
- **Loadout preset chip scoped to the single active bar** — shipped as
  planned; backlog #9 (5 named presets) stays open, untouched.
- **Backlog #15** (Maggot King's Phase 1 Eruption still on an independent
  timer) — **not touched**. Workstream A's extraction ended up NOT
  generalizing the master-script/independent-timer machinery at all (see
  below) — Hive Matron/Mirrorhide/Bloodtithe all use the ordinary
  (non-master-script) `ProcessBossScript`/`ResolveRotationStep` pipeline
  with their own new per-tick mechanic hooks alongside it, never routing
  through `ProcessMasterScript`. Maggot King's P1 Eruption is therefore
  untouched and backlog #15 remains exactly as scoped: parked for the
  pre-M5 audit.
- **Backlog #33** (boss Evasion never exercised by a non-zero value) —
  confirmed still true: none of the three new bosses' `Evasion` differs
  from neutral zero (the bible gives none of them a stated favored/
  disfavored style). Stays open past M3, as the plan's design question 6
  predicted.

---

## A design decision NOT made that the plan flagged as needing one

`m3-plan.md`'s Workstream A.4 proposed unifying player poison + Rotfang's
NPC poison + Bloodtithe's new bleed under one shared stacking-DoT
primitive. **This did not happen.** Implementing Bloodtithe surfaced that
the three DoTs actually have different enough shapes (player poison is a
flat single-instance track with its own 4-tick internal counter; Rotfang's
NPC poison decrements duration to compute a floor-of-stacks damage; the new
player bleed needed "capture damage before ticking" symmetry with Rotfang's
own pattern) that a real unification would have been a risky refactor of
two already-shipped, tested mechanisms for uncertain benefit, not the
"look for one primitive, apply to all three" the plan hoped for. What
shipped instead: `GameState.PlayerBleedStacks`/`PlayerBleedDurationTicksLeft`
is a fourth, symmetric-to-Rotfang's-shape DoT track, additive next to the
other three, not a replacement. Flagging this explicitly since the plan
predicted a unification attempt and none was made — a future milestone
touching DoTs again should treat "four parallel shapes" as the current
state of the world, not assume the plan's proposed unification already
happened.

---

## Small refactors made along the way (behavior-preserving)

- `ExecuteBasicAttackOnAdd`/`ProcessAdds` became non-static (needs
  `Player`/`_damage` for real drone damage rolls) — swarm-fodder behavior
  (`TakeDamage(1)`, "any hit kills") is byte-for-byte unchanged.
- `_swarmThresholdsFired`/`TrySpawnSwarmThreshold` renamed to the more
  general `_thresholdsFired`/`TryFireDroneThreshold` alongside the
  existing name (kept for compatibility) — both HP-threshold-triggered
  spawn systems (swarms, drones) now share one "already fired" set.
- `AddInstance` gained an `AddKind` (Swarm/Drone) and `OrbitRadius` —
  purely additive constructor parameters with defaults, existing swarm
  call sites unchanged.

---

## Not touched (explicitly out of scope for this pass, per `m3-plan.md` §11)

Invocations/raid level/mastery/collection log (M4); Gale Roc, the
Unblinking, Millstone Golem, Grand Duelist (M5+); shop buyback, Bank UX
polish, T3/T4 weapon specials (pre-existing backlog items, not reopened);
5 named loadout presets (#9); `VoxelIcon`/`voxel.js` item-icon migration
(#12); the Distribution decision (#34).
