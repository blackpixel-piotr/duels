# M1 — Vertical Slice: Implementation Findings

Companion to `m1-plan.md`. Written during implementation — what actually
happened, where reality diverged from the plan, and every assumption made
where the docs didn't specify a number. Updated as work proceeds.

---

## Verification caveat (read first)

This session's sandboxed environment has **no .NET SDK** and the egress
proxy denies `builds.dotnet.microsoft.com` (same constraint hit in M0).
Every file was hand-traced for type/signature correctness, JSON content was
validated for well-formedness and cross-reference integrity, and several
latent bugs were caught this way (see below) — but **`dotnet build` and
`dotnet test` have not actually been run.** This must happen (CI, or a
session with a working SDK) before M1 is considered done. Given the scale of
this milestone (touches ~90% of the codebase), treat the first build as a
real risk surface, not a formality.

## Sequencing deviation

The plan's §11 sequencing (B → A+J → E → C → D → F → G → H, each merged
separately) assumed a session that could compile-check between steps. Since
this session cannot build at all, staging small dependent changes into their
own "green" commits would have meant carrying deliberately-broken
intermediate states with no way to verify they even parse. Instead,
Workstreams **A, B, J, C, D, E were implemented together** as one coherent
combat-core rewrite (they're tightly coupled: the new damage model, item
docStats, boss engine, and flask belt all touch `GameTickService`,
`GameState`, and `NpcTemplate` simultaneously). G (action bar/loadout editor)
and H (death/retry/victory) landed alongside since the HUD and result
screens needed real data to bind to. F (HUD) and I (renderer) are separate
passes layered on top. This is a deviation from the letter of §11 but keeps
the spirit — one working (as far as hand-tracing can tell) system per commit
rather than eight partial ones.

## Scope reductions (flagged, not silent)

- **XP/levels removed entirely, not kept vestigial.** D3 in the plan said to
  keep Attack/Strength/Defence/Hitpoints XP displaying and accruing
  (combat-inert). Given the scale of everything else in this milestone, I
  dropped the vestigial XP system outright — `Player` no longer has
  `AttackXp`/`StrengthXp`/etc., `ExperienceTable.cs` is deleted, and
  `StatsPanel`/`CharacterSheet` no longer show levels. This trades away
  cosmetic progression surface that D3 explicitly wanted preserved. Flagging
  for a real decision: reintroduce as pure cosmetic progression, or confirm
  removal is fine going into M2.
- **Vengeance removed.** Not explicitly listed in Workstream J's removal
  sweep, but it appears nowhere in the boss bible or UI bible's M1 HUD spec
  (§3.2's belt is flasks-only), so it doesn't fit the new action bar/HUD.
  Treated as ladder-era and removed (`VengeanceCommand`/`Handler`,
  `GameState.VengActive`/`VengCooldownRounds`). Flagging since it wasn't an
  explicit J item — if it should return as an invocation-modifier later,
  the design docs already anticipate that (decisions doc: "Vengeance:
  available as a modifier").
- **Weapon-slot special auto-fire on switch removed.** The pre-M1
  `WeaponShortcutHandler` auto-queued a special attack when switching TO a
  weapon that had one. Under the new model there's a dedicated Special
  Attack button (UI bible §3.2), so switching weapons now always queues a
  normal attack; the special fires only from its own button. Cleaner, and
  matches the UI bible, but is a behavior change from the pre-M1 build.
- **Loadout Editor is intentionally minimal**, matching the plan's own
  deferrals (§8, "Deferred from §4.1"): tap-to-bind/tap-to-clear only, no
  drag, no 5 named presets, no per-boss "last used" chips, no per-weapon
  default attack style binding (the domain model supports it —
  `Loadout.SetDefaultStyle` exists — but no UI calls it yet).
- **Item/NPC economy content is data-complete but shop/bank-free.** Per the
  M1 brief ("Excludes: bank, shop... drops (M2)"), `shopPrices`/
  `fenceValues` in items.json are empty and `maggot_king`'s `LootTable` is
  empty (gold-only reward). The shop/bank/ladder/collection-log Blazor
  components and their commands were deleted outright rather than hidden,
  per Workstream J ("hub becomes Fight + dev/debug entries").

## Design-ambiguity assumptions (flagged per CLAUDE.md — never resolved silently)

- **Defensive style's "+20% defense value"** (items doc §1) has no defined
  unit. Implemented as: while on Defensive style, the *defender's* incoming
  damage is reduced by a flat 20%, stacking additively with (and uncapped
  by) the separate 40%-capped gear Def-point reduction. This is symmetric
  with Aggressive/Accurate both being pure attacker-side modifiers, but it's
  an invented number/mechanic shape — the doc doesn't say whether this
  should stack with gear Def, replace it, or apply only when *you* attack
  vs. whenever you're hit. See `DamageModel.DefensiveStyleIncomingReduction`.
- **Boost prayer's effect magnitude** isn't specified anywhere (the UI bible
  only says "Boost prayer... drains prayer points while on"; the pre-M1
  build's Piety was +20% Atk/Str). Kept the existing +20% number, retargeted
  as +20% Power (renamed `PietyActive` → `BoostPrayerActive`,
  `TogglePiety` → `ToggleBoostPrayer`).
- **Scorch/Rend DoT magnitudes.** Items doc says "3-tick burn" (Scorch) and
  "bleed stack" (Rend) with no numbers. Reused the existing single-track DoT
  plumbing per the plan's own note ("stack' semantics deliberately
  simplified for M1"): Scorch = 3 ticks @ 3/tick, Rend = 4 ticks @ 3/tick
  with a 1.3× base-hit multiplier. Both provisional/tunable.
- **NPC (boss) armour.** The items doc's Def-point system is written for
  player gear; Maggot King's npcs.json row carries no Def stat. Boss attacks
  are scripted (not rolled), so this only matters for the player's own
  attacks landing on the boss — implemented as 0 Def (no boss-side
  mitigation), i.e. every player hit that lands deals full weapon damage.
- **P2 rotation timing.** The Boss Bible only says "rotation compresses to a
  14-tick loop" without a tick table (unlike P1's full table). Built a
  proportionally-compressed schedule (T0/T3 Bile Spit, T6 telegraph, T8/T11
  Lash-or-Volley, T12 telegraph) — internally consistent but literally
  invented within the loop-length constraint the bible gives.
- **Rot Burst start-check cadence.** The bible says "every ~40 ticks";
  implemented as checked only when the rotation loop restarts (RotationTick
  wraps to 0), not on a raw independent countdown, so it never interrupts a
  telegraphed style read mid-loop. This is a design choice the plan flagged
  as a T-list item, not the docs.
- **Boss's forecast during the style-shift telegraph.** For the compound
  "lash/grub_volley" action, the actual style isn't determined until cast
  time (depends on player position 2 ticks later). The HUD forecast for that
  telegraph shows a generic "melee or ranged" cue rather than resolving a
  style early — this is a genuine reading of the Boss Bible's own framing
  ("his choice depends on YOUR position, so spacing decides what you must
  flick") rather than an invention, but flagging since it means the forecast
  widget doesn't always show a single doctrine color for this boss's second
  telegraph.

## Where reality diverged from the plan's sketch

- **NpcTemplate went from a general-purpose class to a boss-script-first
  record.** Since Workstream J retires every NPC except `maggot_king`, there
  was no reason to keep the old generic StyleRotation/HazardProfile/
  TelegraphedMove fields "for compatibility" — they were deleted outright
  rather than left dead. A `DummyStyle` field remains purely for the
  movement/pathfinding test fixtures (a Script-less NPC used only in tests),
  not for any real content.
- **Generic (non-boss) NPC movement had to be re-added.** The first pass of
  the rewrite folded all NPC behavior into the boss rotation engine and
  dropped the old "NPC walks toward player when out of range" chase logic
  entirely, since the King is stationary. This broke the pathfinding test
  suite's implicit assumption that a plain melee/ranged dummy still chases.
  Restored as `ProcessNpcMovement`, gated by `NpcStationary` — dormant for
  M1's only real boss, live for the test fixtures and any future
  non-stationary content.
- **A latent double-fire bug in Pin Shot.** The first cut of the rotation
  engine tried to implement "delay the boss's next action by 1 tick" by
  freezing the rotation cursor in place for a tick. Caught on review: if Pin
  Shot lands on the exact tick the boss's *current* cursor position also has
  a scripted attack, freezing the cursor would let that same attack fire
  again on the following tick. Fixed by having Pin Shot skip the boss's
  *entire* turn for one tick (no lookup, no advance) instead of stalling the
  cursor — the schedule still shifts by exactly one tick, with no
  re-fire risk.
- **A latent NRE in hazard resolution for scriptless NPCs.** Discovered on
  review: `ProcessHazardResolution` unconditionally read
  `npc.ActivePhaseDef.Eruption`, which throws for a `Script == null` NPC
  (the movement-test dummies). Fixed with an early guard — hazard resolution
  (including Perfect Dodge detection) now only runs for scripted bosses,
  which is also the only case where hazards can exist in the first place.
- **Fixed 9×9 arena instead of per-duel arena data.** The plan's §4.9
  imagined `GameState.ArenaRadius` becoming per-duel data (for future
  bosses with different arena sizes). Since M1 ships exactly one boss with
  exactly one arena size, `ArenaRadius` stayed a `const` (bumped 5→4 tiles,
  i.e. 9×9). Revisit when a second arena size ships.
- **Obstacle layout emptied, not removed.** The M0 pathfinding/obstacle
  system (`GameState._obstacles`, BFS routing) is preserved as
  infrastructure — genuinely reusable for Millstone Golem's rubble walls
  later — but `ObstacleLayout` is now an empty array since Maggot King's
  arena has no obstacles per the Boss Bible.
- **`SaveEnvelope` migration needed no explicit migration code.** Because
  `SaveData`'s v2 shape is a clean break (fields removed, not renamed) and
  `System.Text.Json` silently ignores unknown JSON properties and defaults
  missing ones, a v1 save loads straight into v2's shape with an empty
  action bar/flask belt — satisfying the plan's "v1 saves migrate with an
  empty bar" requirement without a dedicated migration step.

## Verification performed (short of an actual build)

- Full manual trace of every call site touching a changed type/method
  signature (`grep`-driven sweeps for each removed API, repeated after each
  batch of edits).
- `node -e "JSON.parse(...)"` validated `items.json`/`npcs.json`/
  `invocations.json` for well-formedness after every edit.
- Re-read `GameTickService.cs` end-to-end post-edit specifically hunting for
  null-reference risk around the new `BossScript?`/`ActivePhaseDef` — this
  is what caught the two bugs listed above.
- `graphify update .` re-run after the rewrite to keep the knowledge graph
  current.

## Second pass: HUD polish (F), renderer (I), docs, one more real bug

- **HUD ergonomics fix.** The pre-M1 float layout put both the weapon arc
  *and* the prayer arc on the bottom-right, contradicting the UI bible's
  core two-thumb-claw rule (§2: left thumb = prayers, right thumb =
  weapons). Moved the prayer arc to the bottom-left (stacked above the
  flask belt), weapons stayed bottom-right. Added the doctrine-color CSS
  variables (`--doctrine-melee/ranged/magic/hazard/safe/poison/bleed`) as
  semantic aliases over the existing interim palette per the UI bible §1
  — the "reskin is a token swap" requirement.
- **Renderer (toon.js) extensions**: `setBattleHazards` now renders scorch
  tiles (permanent, gold, no urgency pulse); new `setBattleAdds` renders
  swarm placeholder blobs, click-targetable via raycast → a new
  `OnAddClick` JSInvokable → `SetTargetCommand`; a pulsing gold ring marks
  the boss's punish window (`setBattleFlags({punished})` — replaces a
  `windup: "slump"` string flag from the first pass that the JS side never
  actually consumed for anything visual, see below); `perfectDodge` is a
  new `battleEvent` case (gold glint at the player's feet, reusing the
  splat fade/rise lifecycle rather than the projectile-arc lifecycle,
  which would have hurled it across the screen).
- **A second latent bug, caught reviewing the JS side**: the add-mesh
  bookkeeping in `setBattleAdds` initially set `mesh.id = addId` (a
  string) to tag each THREE.Mesh for the render-loop's bob animation —
  but `THREE.Object3D.id` is an internal auto-assigned numeric identity
  three.js itself relies on for scene-graph bookkeeping. Overwriting it
  with a string would have silently corrupted that. Fixed by tagging via
  `mesh.userData.id` instead, which is the framework's actual "put your
  own data here" extension point.
- **Pre-existing dead flag noticed, not chased further**: `toon.js`'s
  `enemyAttack` case read `st.flags.windup?.style`, but `windup` was only
  ever set to a bare string (`"melee"`/`"ranged"`/etc.) both before and
  during the first pass of this milestone — `"someString".style` is
  `undefined` in JS, so the expression silently fell through to its `??`
  fallback every time. This was already a no-op in the pre-M1 build, not
  something this milestone introduced; left as a drive-by cleanup (removed
  the dead read) rather than a full behavior fix, since chasing what the
  *intended* windup-color behavior was is out of scope here.
- **A real, separate bug in the Web layer**: `InventoryGrid.razor` still
  called the deleted `EatItemCommand`/`DrinkPotionCommand` — missed by the
  first grep-driven cleanup sweep because that file wasn't touched by any
  of the direct signature-change edits, only by the removal of the command
  *types* it referenced. Fixed: tapping a flask in the bag is now a no-op
  (flasks bind via the Loadout Editor, not tap-to-consume); everything
  else still routes to `EquipItemCommand`.
- **`ARCHITECTURE.md` and `GAMEPLAY.md` rewritten** to match the M1 state
  (both were still describing the OSRS ladder game pre-sweep) — the
  project's own cleanup rule requires this before considering a change
  done, and both were badly stale after this milestone's scope.

## Test coverage changes

Rewritten/pruned alongside the production code: deleted suites for
fully-retired systems (`CombatCalculatorTests`, `StyleRotationTests`,
`BankSellHandlerTests`, `CollectionLogTests`, `GameTickServiceLootTests`,
`ExperienceTableTests`); rewrote `MaggotKingTests` as the choreography
suite the plan asked for (runs against the real embedded npcs.json/
items.json, not a synthetic stand-in); added `DamageModelTests` for the
new combat math; fixed the movement/swap-buffer suites' constructors and
NPC-template builders for the new signatures. **Not run** for the same
SDK-availability reason as the rest of this milestone — this is the single
biggest verification gap and the first thing to do once a build is possible.

## Still open / explicitly out of scope this milestone

Everything the plan's §13 already excludes (bank, shop, drops/loot tables,
economy pricing, other bosses, invocations, minigame, T3/T4 items, visual
polish/inked skin, portrait combat layout, HUD edit mode, presets ×5).
Within what the plan *did* scope for M1, two things are real but
deliberately shallow rather than missing:

- **2×2 boss visual scale.** `BossScript.Footprint` drives real gameplay
  (adjacency/melee-range checks already treat all 4 tiles as "the boss"),
  but the renderer still draws the King at the same single-tile scale as
  the player — no interop call sends footprint size to `toon.js` yet. The
  fight is mechanically correct; it just doesn't *look* like a 2×2 mound.
- **Per-weapon default attack style and the 5 named loadout presets**
  (UI bible §4.1) — explicitly deferred by the plan itself, noted above.

Everything else in the plan's Workstreams A–J landed this session, subject
to the verification caveat at the top of this file: **no build has actually
been run.**
