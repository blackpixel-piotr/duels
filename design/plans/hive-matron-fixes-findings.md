# Hive Matron fix pass — findings

Standalone follow-up task (not a milestone), prompted by a report that "the
Hive Matron fight doesn't seem right." Goal: analyse the fight, fix concrete
issues, script a "perfect" fight per the Boss Bible, and stand up a dev arena
so boss fights can be playtested without launching the browser and grinding to
the boss. This file is what actually happened; the durable open items are in
`backlog.md` (#40, #41).

## What was built: a headless combat sim harness (`tools/Duels.SimHarness`)

The reusable deliverable. A console app that drives the **real**
`GameTickService` against the **real** embedded `npcs.json`/`items.json`, with
deterministic infra stubs (instant tick source, in-memory state repo, null
event bus) and a deterministic RNG (always-hit by default, or `--seed N`). It
prints a tick-by-tick table (boss/player HP, positions, distance, prayer,
active mechanic flags, and the combat-log deltas) plus a one-line verdict —
the whole fight resolves in milliseconds.

```
dotnet run -c Release --project tools/Duels.SimHarness -- <bossId> <brain> [--ticks N] [--seed S]
  bossId : maggot_king | hive_matron | mirrorhide | bloodtithe
  brain  : idle | naive | melee | perfect
```

Player inputs come from a pluggable `IPlayerBrain` (`SimContext` gives it the
same observable state + commands the Blazor UI has). Shipped brains: `idle`
(stationary dummy — audit raw boss script), `naive` (prays Range, never
dodges — the un-mastered baseline), `melee` (a diagnostic weave probe), and
`perfect` (the scripted clean Hive Matron kill). Built in Release to suppress
`GameState`'s `#if DEBUG` `[PROJ]` diagnostics; the harness has its own trace.

One small production seam was added for it: `GameTickService.TickOnceAsync` — a
thin public wrapper over the private `ProcessTick` the real loop already uses
(the unit tests previously reached it via reflection). No behaviour change.

## Bugs found and fixed

1. **Phase-2 banner named the wrong boss.** The transition line was hardcoded
   `"★ The Maggot King convulses…"` in `ProcessBossScript`, so it fired
   verbatim in the Hive Matron, Mirrorhide and Bloodtithe fights (all three
   use the independent-timer `ProcessBossScript` path, not the master script).
   This is the most likely "doesn't make sense" symptom — a Hive Matron fight
   announcing the Maggot King mid-fight. Fixed with a data-driven
   `FlavorDef.PhaseTwoBanner` (Hive Matron's uses the Bible's own Phase-2 name,
   "Frenzy") plus a generic name-based fallback (`"★ {name} shifts into a
   higher gear — Phase 2 begins!"`) so no boss can ever borrow another's name.

2. **Sting Lob's venom pool used Maggot King's brood flavor.** Her glob reuses
   the shared hazard state machine, whose land/poison/pool lines were
   hardcoded (`"The ground ERUPTS beneath you"`, `"The writhing mass poisons
   you!"`, `"Acrid slime burns at your feet."`) — brood imagery for a wasp
   queen's venom. Fixed via `FlavorDef.HazardLand/HazardPoison/HazardPool`
   (null falls back to the original text, so Maggot King is unchanged); Hive
   Matron now reads "Venom splashes across your tile", etc. This respects the
   CombatLog-is-UI-text-only rule (flavor strings are data, never a renderer
   or gameplay source).

3. **Tail Stab punished a player merely *passing through* adjacency.** The
   Bible says Tail Stab answers a player who "**stands** adjacent for 2
   consecutive ticks," and describes the weave as "step in → hit (1 tick) →
   step out." The code counted *any* adjacent tick, including ticks the player
   moved through adjacency, so a clean weave ate a Tail Stab on its strike
   tick. Fixed: the adjacency counter now only increments on a tick the player
   ends **stationary** while adjacent (`!movedThisTick`). Regression test added
   (`TailStab_DoesNotFire_WhilePlayerWeavesThroughAdjacency`) — it fails under
   the old any-adjacent-tick logic.

`FlavorDef` is a new optional record on `BossScript` (all fields optional, all
null-defaulting to the prior Maggot King text). Only Hive Matron populates it
so far; Mirrorhide/Bloodtithe get the correct generic phase-banner fallback and
could get authored flavor in a later content pass.

## The real "doesn't feel right": the melee weave is unplayable (flagged, not fixed)

The harness's `melee` probe made this concrete: an honest melee attempt against
Hive Matron lands ~1 hit per 3 Tail Stabs and dies in ~6 seconds. Her signature
"weave" — the entire reason the fight exists ("melee is possible but must be
danced… feels fantastic once learned") — does not work. Two interacting causes,
**both of which are genuine design decisions, not clear-cut bugs**, so per
CLAUDE.md they are flagged (backlog #40) rather than silently resolved:

- **Attack-on-arrival.** The combat loop forbids attacking on any tick the
  player moved (`!playerMovedThisTick`), so a melee hit requires being
  *stationary*-adjacent — but the weave assumes the hit lands on the step-in
  tick. This is a global combat-grammar rule affecting all melee, not a
  Hive-Matron quirk.
- **Continuous flee.** Her spacing AI steps away every tick the player is
  inside `PreferredRangeMin`, so open-ground melee can never even reach her;
  cornered, she can't flee and just Tail-Stabs. This flee is *tested*
  (`SpacingAi_StepsAwayWhenPlayerCloserThanPreferredRange`) — a deliberate
  choice — even though it contradicts the Bible's "melee is possible" and its
  "spacing is reset by the **dash** (every 3rd attack)" model.

The Tail Stab "stands" fix (above) is a *prerequisite* for any future weave fix
but is insufficient alone while those two rules stand. Fix menu in backlog #40.

## The "perfect" fight (scripted, `perfect` brain)

Because melee is currently non-viable, the demonstrable perfect fight is the
**ranged spacing** fight — which is also her intended primary counter (she
drops a best-in-slot ranged weapon and teaches spacing). The `perfect` brain:
holds Range against Dart Volley (full negation), Perfect-Dodges every Sting Lob
glob without stepping back into the venom pool, steps perpendicular off every
Pin line to bait the wall-slam and its 4-tick punish window, dumps a special
into that window, and keeps firing straight through Chitin Guard (half-value
ranged still beats trading Tail Stabs).

Result against the fixed build (always-hit RNG): **Hive Matron slain in 47
ticks (~28s), player never below 92/100 HP** — the only damage taken is the DoT
from the very first glob, which a cold opener can't pre-dodge. Phase-2 "frenzy"
banner, the P2 double-chain Pin, and venom flavor all read correctly in the
trace. Re-runnable any time as a smoke test:
`dotnet run -c Release --project tools/Duels.SimHarness -- hive_matron perfect`.

## Follow-up: "Playtest Fight" — watch the autopilot play, in the real game

Second request: a button on the boss pre-fight screen that auto-plays the fight
in the live renderer so the boss script can be watched/analysed without playing
it by hand. Shipped:

- The brain seam (`IPlayerBrain` + `SimContext`) moved from the harness into
  `Duels.Application/AutoPlay/`, so the **same** code drives both the offline
  harness and the live autopilot — no divergence. Added a general
  `AutoPlayBrain` (boss-agnostic: prays the incoming projectile's style, dodges
  globs/pools/Pin lines, kites at range or weaves melee by weapon type, specials
  into punish windows).
- `GameState.AutoPlay` flag (reset every `StartDuel`, opted in by
  `StartDuelCommand.AutoPlay`). `GameTickService.ProcessTick` invokes the brain
  at the very top of the tick — the same `brain.Decide → tick` ordering the
  harness uses — so the prayer it sets is captured by `TickStartProtection` and
  its move/attack are consumed by the normal movement + attack gate. It is a
  pure input source; nothing about tick resolution changed.
- UI: a "▶ PLAYTEST (auto)" button on `PreFightSheet` next to FIGHT, wired
  through `Game.FightBoss(bossId, autoPlay: true)`, plus a pulsing
  "▶ PLAYTEST — autopilot fighting" banner in the battle HUD while it runs.
- The harness gained an `auto` brain (`dotnet run … -- <boss> auto`) exercising
  the exact live autopilot.

**Browser verification caught a real gap.** First live run: the autopilot died
at 1 HP while the boss barely moved. Cause: the dev T2 loadout equips the
*melee* weapon, so the brain tried the (flagged-broken) melee weave. Fix: the
autopilot now prefers a ranged weapon from the action bar when one exists
(`SimContext.PreferredRangedWeaponId`, computed in `GameTickService` from the
loadout) and kites with it — the safest way to let a whole script play out.
Re-verified in-browser: player holds ~96/100 (only the cold-open venom DoT)
while the boss drops steadily, Perfect Dodges and the Pin telegraph visible on
screen — matching the headless perfect run. (Note: the one banner-position CSS
tweak, top→bottom to clear the toast stream, was made after the last live
screenshot and confirmed only by inspection, not re-shot — the app launcher was
too flaky to relaunch reliably at the end of the session.)

## Verification

- `dotnet build` clean (0 warnings). `dotnet test` green: 141 tests
  (21 Domain + 51 Application + 69 Infrastructure), including 3 new Hive Matron
  regressions (phase-2 banner name, venom flavor, weave-through-adjacency).
- Perfect/naive/idle/melee harness runs inspected by hand (traces above).
- Not run: the actual Blazor/Three.js browser build — these changes are all
  sim-layer (C# + one data file + one log-flavor field) and the renderer reads
  none of the changed strings as data, but a browser pass to confirm the
  phase-2 banner and venom lines render in the live combat log was not done.
