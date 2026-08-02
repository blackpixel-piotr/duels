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

## Follow-up 2: the melee rework (implemented)

Player-directed redesign making melee viable and rewarding — full spec +
decisions in `hive-matron-rework-plan.md`. As-built:

- **Melee attack-on-arrival** (`GameTickService` attack gate): a melee swing now
  lands on the tick a move *completes* in range (`meleeArrival`), not a later
  stationary tick. Ranged/magic still defer (kiting unaffected). One existing
  test (`MeleeVsMelee_…FirstContactOnArrivalTick`) updated to the new timing.
- **Melee weakness** (`ApplyMeleeVulnerability`, `MeleeVulnerabilityPercent` =
  +30% PROVISIONAL): Stab/Slash/Crush hits are amplified — the mirror of Chitin
  Guard. Melee now kills her in ~17–26 ticks vs. ranged's ~47.
- **Continuous flee removed** (`ProcessSpacingAiMovement`): she no longer steps
  away every tick you close (that made melee unreachable). She only closes when
  you kite out past her band; her space-making is now the Needle Spit.
- **Needle Spit** (`NeedleSpitDef`, `StartNeedleSpit`/`ResolveNeedleSpit`):
  replaces the silent dash. Every 3rd attack, *if the player is within 5*, she
  telegraphs (log tell + `NeedleSpitTiles` marked for the renderer), then 2
  ticks later leaps 3 back and needles the player's tile + 4 cardinals (dodge
  diagonally). Two-layer: Range-typed volley (pray Range) + unprayable venom
  nick, so zero needs pray *and* dodge. Perfect-Dodge eligible.
- **Drones body-block** (`DroneLaneTile` + `GameState` soft blockers): they now
  spawn on and track the boss→player lane (fanned by spawn index) instead of
  freezing at fixed east/west angles, and their live tiles are soft blockers the
  *player's* pathing must route around (scoped to the player-movement phase, so
  the boss/drones aren't self-blocked). Kill them or circle to bait them aside.

**Acceptance evidence (harness):** the `melee` probe went from *dying in ~6s
landing 1 hit* to *killing the boss* (420→0 in ~17–26 ticks). It still trades to
death because it's a crude probe that flubs ~3 fully-avoidable dodges (a Pin, a
glob, a Tail Stab) — melee is "viable and rewarding but demanding," which is the
intended "danced, not held" identity; a clean player wins. The `auto`/ranged
line is unchanged: still a clean ~28s kill at 96/100 HP.

### Follow-up 3: Needle Spit visuals + poisoned ground (implemented)

- **Venom pools on spit.** `ResolveNeedleSpit` now drops settled venom pools on
  the struck "+" tiles (`GameState.AddPools`, `NeedleSpitDef.PoolTicks` = 6
  PROVISIONAL) — no fresh eruption (the volley was the hit), just lingering
  poison you must then clear. They flow through the existing pool hazard channel,
  so they deal the same per-tick venom and render green like a Sting Lob pool.
  Unit-tested (`…HitsPlayerStandingOnThePlus…` now asserts `IsPool`).
- **"+" floor telegraph.** `BattleScene` sends `ActiveNpc.NeedleSpitTiles` to a
  new `toon.js` `setBattleNeedleSpit`, drawn as a pulsing **ranged-doctrine
  (green) ground quad** on each marked tile (mirrors the hazard-quad renderer,
  its own `needleQuads` map). This is the "she needs a visual indicator before
  she leaps" requirement — the floor "+" plus her leap now read the attack; a
  wing-flare telegraph glow is a nice-to-have still open.

### Follow-up 4: boss-polish pass (Maggot King + Hive Matron) — browser-verified

A visual polish + declutter pass, and this time it **was** verified in a real
browser: `dotnet run` reliably gets reaped in this container, so instead the app
was `dotnet publish`ed and served as static files (`python -m http.server`) —
Duels.Web is standalone WASM, so that just works. Playwright confirmed each item
and captured screenshots.

- **Hit/telegraph text notifications hidden.** The on-screen combat toasts
  (`ToastHost`, `Enabled=false`) and the boss-action `telegraph-bubble`
  (`BattleScene`, `ShowTelegraphBubble=false`) are suppressed — they covered too
  much of the arena. All plumbing intact behind those flags; the log is slated
  to move to a semi-transparent bottom-left chatbox later. Verified: both counts
  0 on screen mid-fight.
- **Needle Spit wing-flare.** She now flares ranged-green (`setActorTelegraphGlow`,
  takes precedence over the style glow) while a Needle Spit winds up — verified
  on screen (green rim on the boss during the "+" telegraph).
- **Pin floor telegraph.** `LineChargeTiles` now streamed to `toon.js`
  `setBattlePinLine`, drawn as a melee-red charge line (Pin had *no* floor
  visual before) — verified (pin quads rendered).
- **Deadly-tile VFX (both bosses, shared renderer).** Hazard quads now breathe
  (opacity + a small scale throb), warnings ramp amber→hot-red and flash on the
  final fuse tick, settled pools are a brighter toxic green with a slow bubble.
  Verified (up to 17 hazard quads live).

### Follow-up 5: real poison-pool VFX — texture + rising particles

The flat glowing quads read as placeholder, so deadly tiles are now proper
surface VFX (browser-verified): pools get a mottled toxic-venom `CanvasTexture`
(dark base, brighter blobs, bubble rings) plus a per-tile `THREE.Points` cloud
of rising bubbles; eruption warnings get a hot cracked-ember texture + rising
ember particles that redden on the final fuse tick; both shared textures slowly
churn; scorch stays flat/safe. Shared renderer, so Maggot King's eruptions/pools
and Hive Matron's venom (Sting Lob + Needle Spit) all upgraded at once. Particle
clouds are created/recycled alongside their hazard quad and torn down with it —
renderer-only, no gameplay coupling. Verified on screen: pools clearly read as
bubbling poison now (5 pools + 5 particle clouds live in the capture).

**Readability note (design "direction" feedback):** the arena ground, the venom
pools, the Needle Spit "+", the drones, and the wing-flare are *all green* — so
the green danger elements have only modest contrast against the green floor
(the red Pin line and amber warnings pop much better). Candidate follow-up: give
venom pools/needle tiles a darker rim or shift them to a more acid yellow-green
so they separate from the arena. Flagged, not changed.

- Numbers are all PROVISIONAL — playtest + tune (melee vuln, needle/nick damage,
  pool duration).

## Verification

- `dotnet build` clean (0 warnings). `dotnet test` green: 141 tests
  (21 Domain + 51 Application + 69 Infrastructure), including 3 new Hive Matron
  regressions (phase-2 banner name, venom flavor, weave-through-adjacency).
- Perfect/naive/idle/melee harness runs inspected by hand (traces above).
- Not run: the actual Blazor/Three.js browser build — these changes are all
  sim-layer (C# + one data file + one log-flavor field) and the renderer reads
  none of the changed strings as data, but a browser pass to confirm the
  phase-2 banner and venom lines render in the live combat log was not done.
