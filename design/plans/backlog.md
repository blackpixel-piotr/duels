# Duels — Backlog

Cross-milestone list of everything flagged as missing, deferred, blocked,
or needing human confirmation — pulled from `m0-findings.md`, `m1-findings.md`
(including its M2 pre-plan addendum), `m2-findings.md`, and the `m0`/`m1`/`m2`
plan docs' own "explicitly out of scope" / open-question sections.

**Purpose**: a findings file records what happened *during* a milestone and
then the milestone closes — nothing forces a reader to go dig through six
findings files to find out what's still owed. This file is that index. Per
CLAUDE.md, every future milestone's findings must add its own gaps here
instead of letting them go stale in a closed milestone's file.

**How to use this**: each entry names its origin, why it's not done, and
what unblocks it. When an item is picked up, move it to "Resolved" with the
commit/PR or milestone that closed it — don't delete the history. When a new
milestone's findings surface something missing, add it here in the same
pass, not later.

---

## A. Content gaps — blocked on design-doc content, not engineering

*(All resolved — see Resolved section. Numbers below retired, not reused.)*

## B. Deferred features — spec'd, just not built

5. **Shop buyback** (economy §4: 100% refund same session, 80% after). No
   session-scoped purchase-history mechanism exists; `IItemRepository`
   deliberately doesn't carry one (fence value ≠ buyback, see finding #16).
   *(M2, `m2-plan.md` Workstream D.3)*
6. **Bank UX polish** (UI bible §7): tabs, search-as-you-type, favorites
   row, placeholder ghost icons, the "Prepare Loadout" bank↔loadout bridge
   button. Core loop (deposit/withdraw/quantity toggle) shipped; these were
   explicitly deferred past it. *(M2, `m2-plan.md` Workstream B.4)*
35. **Hive Matron's Pin "stunned 2 ticks against the wall" is approximated
    with `DelayPlayerAttack`** (delays the player's next attack) rather
    than a real movement-lock — no player-movement-freeze mechanism exists
    in the engine, and building one for a single boss's one attack was
    judged the wrong trade against M3's actual scope. Revisit if a future
    boss needs a real stun (a genuine movement-lock, not an attack-delay
    stand-in). *(M3, `m3-findings.md`)*
36. **Hive Matron's drones station-keep at their orbit radius rather than
    truly body-blocking melee approach lanes.** The Boss Bible's "body-
    blocking melee approach lanes" implies real lane-collision the
    player's pathing must route around; what shipped is real-HP adds
    (3 hits) near the boss that must be dealt with, not collision
    geometry. *(M3, `m3-findings.md`)*
7. **Shop "why buy" tags** (UI bible §9: e.g. "Unlocks viable Magic swaps
   for Mirrorhide") — omitted; no per-item tag content exists beyond
   Maggot King, and the doc gives no tag table. *(M2, `m2-plan.md` D.1)*
8. **Per-weapon default attack style binding** (UI bible §4.1). The domain
   model already supports it (`Loadout.SetDefaultStyle` exists) but no UI
   calls it — the Loadout Editor only does tap-to-bind/tap-to-clear.
   *(M1, deferred from the plan's own §8)*
9. **5 named loadout presets + per-boss "last used" chips** (UI bible
   §4.1). Loadout Editor is intentionally minimal, single active bar only.
   *(M1, deferred from the plan's own §8)*
10. **Full item/stat card UI** (UI bible: "max hit shown on item cards").
    M1 shipped only a lightweight "Power N · Max hit 2N" text readout on
    the Loadout Editor and inventory hover titles instead of a real card
    surface — flagged at the time as a design decision to revisit.
    **Likely superseded**: M2's `EquipmentStatSheet`/`CompareCard` cover
    a good chunk of this now; worth confirming closed rather than assuming.
    *(M1, `m1-findings.md` seventeenth pass)*
11. **Master-script DSL.** The 28-tick P2 layout for Maggot King lives in
    code (a `switch` on `RotationTick`), not data — a fully data-driven
    mechanic-action rotation-token DSL was deferred as speculative with
    only one master-script boss in the game. Revisit once a second
    master-script boss makes the pattern worth generalizing. *(M1,
    `m1-findings.md` nineteenth pass)*
12. **`VoxelIcon.razor` (bag/bank/shop item-grid icons) still renders via
    `voxel.js`.** M2's Three.js rebuild covered the equipment *preview*
    only (`PlayerPreview.razor`) — CLAUDE.md's "Three.js only, no
    secondary renderers" invariant is not yet fully true in the codebase.
    *(M2, `m2-plan.md` Workstream C.1)*

## C. Data exists, engine doesn't dispatch it (unwired mechanics)

13. **T3/T4 weapon specials + all four bosses' rares/uniques are
    data-only.** `quake`, `piercing_arrow`, `arc`, `executioner`,
    `ballista_bolt`, `annihilate`, `fester`, `sting_volley`, `refract` all
    exist as real `SpecialEffect` rows (id, cost, description) but
    `GameTickService.PerformSpecialAttack`'s dispatch switch only has
    cases for M1's six specials — pressing any of these logs `"Unknown
    special"` and no-ops. Same for the passives: Carrion Edge's 20%-poison,
    Chitin Recurve's +10%-vs-moving, Hivepiercer's every-3rd-crit, Prism
    Wand's -15%-special-cost, Mirrorshard Staff's swap-bonus, Leech Blade's
    8%-lifesteal, Tithebound Cuirass's flask-cleanses-DoT are all
    unwired — real per-item passive-hook work, one boss's worth (Rotfang)
    is the only exception. *(M2/M3, items ingested in each milestone's own
    Workstream A/E)*
15. **Maggot King's P1 Eruption still runs on an independent timer**,
    violating the "Master-script rule" doctrine added for P2 (Global
    Combat Grammar: "every phase runs on one master tick script... never
    produced by timer drift"). Explicitly flagged as a pre-M5 audit target
    alongside Gale Roc's ambient lightning and the Unblinking's gravel
    crawlers — none of those bosses have code yet, so this is the one
    live offender. *(M1, `m1-findings.md` eighteenth pass)*

## D. Design questions needing human confirmation (not doc-blocked, just unanswered)

*(Items #16–24 all resolved — see Resolved section. Those numbers retired,
not reused.)*

34. **Distribution: browser/PWA vs. store wrapper (e.g. Capacitor) — never
    actually decided.** Flagged in the implementation brief itself
    ("affects input/fullscreen/perf testing targets. Decide by M1's
    playtest") but M1 shipped without deciding it and it was never carried
    forward as an open item until now. This isn't cosmetic: it determines
    how fullscreen and portrait/landscape orientation are even supposed to
    work. A store-wrapped app gets OS-level landscape lock (no phone-held-
    portrait case exists at all) and real fullscreen for free — no CSS
    trick or in-page button needed for either. A pure browser/PWA target
    can't get true fullscreen on iOS at all (Safari/Firefox-iOS, both
    WebKit, don't implement the Fullscreen API for arbitrary elements —
    only `<video>` can), and needs the CSS rotate-prompt workaround
    (`RotateOverlay`, see the landscape-mandate Resolved entry above) to
    handle portrait. **Update**: the rotate-prompt approach was abandoned
    (see the landscape-mandate Resolved entry's third follow-up) — non-
    combat screens are now genuinely orientation-responsive, no forced
    layout in either state, so portrait is no longer blocked at all in the
    browser build. The in-browser fullscreen button remains a provisional,
    dev-time-only affordance (real fullscreen still only matters for a
    non-wrapped browser/PWA target) until this decision is made.
    *(Brief, flagged since M0/M1, surfaced concretely this session)*
37. **Common/Uncommon loot content is undocumented for every boss but
    Maggot King** — now affects Hive Matron, Mirrorhide, and Bloodtithe
    too (each shipped reusing Maggot King's exact generic-materials shape,
    shifted to their own tier's gear bridge, PROVISIONAL). Needs either a
    real per-boss content-authoring pass or a human ruling that the
    generic reuse *is* the intended permanent design, not a placeholder.
    *(M3, `m3-plan.md` design question 1 / `m3-findings.md`)*
38. **Bloodtithe's bleed can kill a fresh 100-HP player in ~7 seconds**
    (verified in browser testing, "Slain by: Bleed," no direct hit was the
    killing blow) — the combination of his rotation landing 2–3 hits
    inside a very short opening window (each both damaging and
    stacking/refreshing a 10-tick bleed) plus the Tithe aura's separate
    continuous drain outpaces what a player can realistically counter
    (reach a Font) before dying on a first, ungeared attempt. Every number
    involved is PROVISIONAL (no boss bible value for bleed's per-stack
    rate); needs a human tuning pass — candidates are lowering the
    per-stack rate, widening his early rotation's spacing, or ruling this
    lethality intentional (his fantasy is "inevitable" pressure) and
    leaning on FTUE guidance instead. *(M3, `m3-findings.md`)*
39. **DoT unification (player poison / NPC poison / player bleed) was
    flagged as a possible task in `m3-plan.md` Workstream A.4 and
    deliberately not attempted** — the three tracks turned out to have
    different enough shapes that a real unification looked like a risky
    refactor of two already-shipped mechanisms for uncertain benefit.
    Bloodtithe's bleed shipped as a fourth, parallel, Rotfang-poison-shaped
    track instead (`GameState.PlayerBleedStacks`). A future milestone that
    touches DoTs again should treat "four parallel shapes" as the current
    state, not assume this unification already happened. *(M3,
    `m3-findings.md`)*

## E. Technical debt / dev tooling

25. **Freeze/camera/movement debug panels are unreachable in the live
    app.** Gated behind `GameState.TestScene`, which nothing in the
    reachable app ever sets `true` (found M1 sixth pass, still true as of
    the eighteenth pass). The newer "MECH ▾" mechanic-toggle panel
    deliberately did *not* reuse `TestScene` (it's overloaded — also
    switches the battle background) and is instead always-visible in every
    fight, acceptable only because every M1/M2 fight is currently a dev
    fight (reached via the dev-loadout cards). **Needs a real dev gate
    once non-dev fights ship** (i.e., once the Shop/Bank loop is the real
    onboarding path, not a debug button). Still open post-batch-1: the
    cold-start resolution (#16, now resolved) explicitly kept the dev-loadout
    buttons and only added a `// PROVISIONAL: needs dev gate (backlog #25)`
    comment — this item is what that comment points at. *(M1)*
28. **`NpcTemplate.DummyStyle` and its non-scripted movement path are now
    provably dead for real content.** Built for "the pathfinding/movement
    test fixtures and any future non-boss mob" — the boss-only ruling
    (backlog item, see Section D discussion in `m1-findings.md`'s M2
    pre-plan addendum) means there will never be a non-boss mob. Still
    load-bearing for test fixtures; safe to leave, but the "future non-boss
    mob" half of its own doc comment is now known-stale. Now cross-referenced
    from `AttackRange.Distant`'s own `// PROVISIONAL: dead path (#28)`
    comment, added in batch 1 §9. *(M2, discovered during pre-plan)*

## F. Known cosmetic/renderer gaps

29. **Maggot King's 2×2 footprint isn't visually reflected.**
    `BossScript.Footprint` drives real gameplay (adjacency/melee-range
    checks already treat all 4 tiles as "the boss") but the renderer still
    draws him at the same single-tile scale as the player — no interop
    call sends footprint size to `toon.js`. Mechanically correct, visually
    wrong. *(M1)*
30. **Telegraph audio cue never implemented.** UI bible calls for
    shape + color + audio on every telegraph; shape (outline/rim glow +
    HUD icon) and color shipped, audio didn't — no audio-hook
    infrastructure exists in the renderer yet. *(M1)*
31. **Phase 1's second Bile Spit cast (T4) has no telegraph of its own** —
    matches the boss bible's literal rotation table (which only marks
    style *changes*, and T0→T4 isn't one), but flagged in case "every
    attack should have some tell" turns out to be the right read once
    played with the visuals working. *(M1)*
32. **Item-grid icons rendered blank during M2 browser verification** for
    the new T3/T4 weapon/armour ids (Bag/Bank/Shop screens). Observed, not
    investigated — predates this session, unrelated to anything M2 changed
    (icon path is `voxel.js`/`VoxelIcon.razor`, item #12's territory), but
    worth a follow-up look. *(M2, observed during verification)*
33. **Boss Evasion is wired end-to-end but never exercised by a real
    non-zero value.** Maggot King's Evasion is all zeros (M1's only
    boss); the "favors ranged/melee" lever is covered by a unit test but
    not by any real fight yet. Will get real coverage from the first
    boss with non-neutral Evasion (M3+). *(M1)*
40. **VFX texture asset (`assets/vfx/`) uses a procedural placeholder,
    not the brief's Kenney particle pack.** `kenney.nl` and `itch.io` both
    return 403 from this session's egress proxy (org policy — confirmed
    via direct request, not a transient failure), and this session's
    GitHub access is scoped to `blackpixel-piotr/duels` only, so a
    third-party mirror wasn't substituted either. Movement dust
    (iteration 1) instead uses a runtime-generated radial-gradient
    `CanvasTexture` (`vfx.js`'s `makeProceduralDustTexture`) — visually a
    soft round puff, functionally fine, just not the sourced CC0 asset.
    Swap `resolveTexture` for a real `TextureLoader().load(...)` of a
    vendored PNG once a session with broader network access can fetch the
    Kenney pack; update `vfx-manifest.json`'s `texture` field and
    `asset-map.md`'s VFX Textures section flag at the same time.
    *(VFX layer iteration 1, `design/plans/vfx-plan.md`/`vfx-findings.md`)*
41. **VFX global particle budget (`PARTICLE_BUDGET = 300` in `vfx.js`) is
    marked `// PROVISIONAL`** — it's the brief's own stated default, not a
    number from a `/design/*.md` file, and hasn't been tuned against a
    real device. Iteration 1 (movement dust, max 3 concurrent × ~5
    particles per entity) never gets close to it, so the auto-cull-oldest
    path is untested by real load — revisit once a second VFX effect ships
    and this can actually be exercised. *(VFX layer iteration 1)*
42. **Screen shake's `LogEntryKind.BossSpecial` trigger was left
    unmigrated** in Combat feel pass 1's CombatLog-as-UI-text-only sweep
    (CLAUDE.md). It reads a typed enum, not `.Message` text, so it isn't
    the string-parsing anti-pattern that pass killed everywhere else, but
    it's still a CombatLog-derived renderer decision. Migrating it for
    real means deciding which of ~30 `BossSpecial` log lines *per boss*
    (phase transitions like "Phase 2 begins" vs. minor telegraph flavor
    text like "mandibles glow amber") actually deserve a shake — a content
    judgment across 4 bosses' scripts, not a mechanical refactor, and out
    of scope for a pass about attack/defend/forced-move presentation.
    *(Combat feel pass 1, `combat-feel-plan.md`/`-findings.md`)*
43. **Facing turn-rate (`MAX_TURN_RAD_PER_S` in `toon.js`) and the camera
    spring's separation-to-zoom band are both PROVISIONAL** — the brief's
    own stated "~90°/100ms" figure and a hand-picked 4-tile-neutral/±15%
    band respectively, neither sourced from a design doc, neither tuned
    against a real device. *(Combat feel pass 1)*
44. **New VFX effects (slash_arc/impact_burst/blocked_spark/deflect_ward/
    shield_dome/landing_dust) all inherit the same slight-upward-drift
    `ForceOverLife` iteration 1's dust puff uses**, whether or not it suits
    the effect (a "shield dome" floating upward is a minor visual
    mismatch). No per-effect behavior override exists yet in `vfx.js` —
    worth adding once there's a second data point beyond "everything
    drifts a little." *(Combat feel pass 1)*
45. **No real Settings UI exists to read/write `clientPrefs`** (`cameraMotion`/
    `vfxQuality`, one `duels_client_prefs` localStorage object) — a dev-only
    `PREFS` toggle panel in `BattleScene.razor` (same "always visible, not
    TestScene-gated" convention as `MECH`) was added after the fact once a
    user couldn't find any way to reach `cameraMotion` at all, but it's
    styled/labeled as a debug panel, not the real player-facing settings
    screen M6 owns. Built as that future screen's backend per explicit
    instruction — `api.getClientPrefs`/`setCameraMotion`/`setVfxQuality`
    are the entry points a real screen would call. *(Combat feel pass 1)*
46. **`Spell_Simple_Enter`/`Spell_Simple_Exit` (the wind-up/return halves
    of the magic-cast clip set) are not chained around `Spell_Simple_Shoot`.**
    Only `Spell_Simple_Idle_Loop` (the held stance) was extracted and
    wired this round — a real Enter/Exit chain would add cosmetic latency
    ahead of the player's attack, which resolves synchronously with no
    travel-time sim, and risks reading as sluggish rather than better.
    Judgment call, not a technical blocker — worth revisiting after
    playtesting the idle-stance fix alone; if casting still feels abrupt,
    `tools/extract_anims.mjs`'s KEEP list already has the clip names
    documented. *(Combat feel pass 1, round-3 follow-up)*
47. **No ranged (bow) or magic (staff/wand) weapon model exists in any
    vendored asset pack** — checked directly (every file in
    `resources/assets/weapons assets/FBX/`): swords, axes, daggers,
    hammers, a spear, a scythe, bows, shields, no staff/wand. This is the
    same root cause as `asset-map.md`'s existing M1 placeholder note
    (`wpn_*` all render as `sword.glb`); re-confirmed while investigating
    the magic-cast animation report, not a new finding, just re-surfaced
    with the exact file inventory checked. *(Combat feel pass 1, round-3
    follow-up)*

---

## Resolved

*(Move an item here, with a pointer to what closed it, instead of deleting
it — this is the record that it was tracked and picked up, not just that
it disappeared.)*

**#14 No boss-side poison DoT track exists** — closed for its
Rotfang-shaped half back in M2 batch 1 (`NpcInstance.PoisonStacks`,
built for Rotfang's on-hit poison) but the item's own text ("blocks...
Carrion Edge's 20% of hits apply poison / Fester's detonates all poison")
was never updated to say so and stayed listed as fully open. Corrected
now: the mechanism this item asked for exists and works (confirmed by
`RotfangPoisonTests`); Carrion Edge's own passive/Fester's detonation
using that mechanism is a separate, still-open gap, folded into #13
(data-only passives) rather than tracked twice.

**Backlog batch 1** (`design/plans/m2-backlog-resolutions.md`, "A+D
resolutions, constants ratification, landscape mandate") closed the
following 15 items. See `design/plans/m2-findings.md`'s batch-1 addendum
for implementation detail.

- **#1 Rotfang has no combat stats** — closed by batch 1 §1. Unique-quality
  rule (boss tier + 1; weapon Power = tier DPS × AttackSpeed; armour uses
  tier's slot Def) ratified in items doc §3, all 8 boss uniques' stats
  tabulated, Rotfang ingested into `items.json` with on-hit poison (5
  ticks @ 2/tick, max 3 stacks, refresh-on-reapply) wired end-to-end.
- **#2 Maggot King Common/Uncommon loot rows don't exist** — closed by
  batch 1 §2. Rows added to items doc, ingested into `npcs.json` via the
  new weighted-group loot engine (`LootEntry.GroupId`/`Weight`).
- **#3 Economy §3's "regular duels" language points at nothing** — closed
  by batch 1 §6. Editorial sweep removed the stale wording; Tier-1 bosses
  stated as the on-ramp.
- **#4 Rotward/specialty-flask shard mechanism undesigned** — closed by
  batch 1 §3. Shard-stacking → auto-combine-at-5 → post-unlock 100g
  conversion mechanism designed and implemented (`GameTickService`,
  `SipFlaskHandler`); Rotward flask (3 sips, cleanse + 15-tick poison
  immunity) shipped.
- **#16 Cold-start gap (0 starting gold)** — closed by batch 1 §4. Ruling:
  600g starting gold + free T1 weapon of chosen style (new NewGame.razor
  style-pick FTUE beat). Dev-loadout buttons kept, flagged
  `// PROVISIONAL: needs dev gate` pointing at #25 (still open).
- **#17 XP/levels: gone for good or revisit?** — closed by batch 1 §5.
  Ruling: gone for good; progression = gear/invocations/collection log
  only. Recorded in `duels-design-decisions.md` under Retired Mechanics.
- **#18 Fence/sell-value split's unique formula unwired** — closed by
  batch 1 §6. Ruling: uniques sell flat 2,000g (replaces the "15% of a T4
  piece" formula); `fenceValues` override populated for Rotfang.
- **#19 Armour price interpolation formula unconfirmed** — closed by
  batch 1 §7. Ratified as canonical; rule stated in items doc §5.
- **#20 T3/T4 melee weapon AttackType inferred** — closed by batch 1 §1.
  Archetype rule ratified and stated: dagger=Stab, sword/axe=Slash,
  hammer/maul=Crush.
- **#21 Carrion Edge's Line=None unconfirmed** — closed by batch 1 §7.
  Ratified: rares carry `Line=None` by design, outside line
  identity/set-bonus system.
- **#22 Purchase-confirm threshold (2,500g) unconfirmed** — closed by
  batch 1 §7. Ratified as canonical.
- **#23 Economy §4 cadence-check row not recomputed** — closed by batch 1
  §6. Recomputed against 500g Tier-1 kills + 600g start + free T1 weapon;
  updated timings printed in `duels-economy.md` §4.
- **#24 M1 provisional-assumption sweep (4 sub-items)** — closed by
  batch 1 §8. Defensive style (flat 20% incoming reduction, additive with
  gear Def) ratified, **plus new rule**: total mitigation from all sources
  hard-caps at 50% (`DamageModel.TotalMitigationCap`). Boost prayer (+20%
  Power) ratified. Scorch (3 ticks @ 3/tick) / Rend (4 ticks @ 3/tick +
  1.3× base hit) ratified as canonical DoT baselines. Boss Def=0 ratified
  as doctrine and added to the boss bible's Global Combat Grammar.
- **#26 `duels-economy_1.md` duplicate file** — closed by batch 1 §9.
  Confirmed byte-identical-in-intent duplicate, deleted.
- **#27 Unsourced constants sweep (PrayerPoints, PrayerDrainCadenceTicks,
  AttackRange.Distant)** — closed by batch 1 §9. `Player.PrayerPoints = 99`
  and `GameState.PrayerDrainCadenceTicks = 9` ratified as canonical
  (documented in items doc §1); `AttackRange.Distant = 8` kept and marked
  `// PROVISIONAL: dead path (#28)` since #28 itself stays open.

**Landscape-everywhere mandate** (batch 1 §10, a new ruling rather than a
backlog item) — Hub menu (single column → 2-col grid), Bag (single column →
paperdoll+stats left / inventory right split), and Loadout Editor (widened
480px → 640px) refactored to landscape-first layouts. CLAUDE.md gained the
"every screen is landscape-first" rule verbatim. See `m2-findings.md`
batch-1 addendum for which screens structurally resisted full conversion
(Loadout Editor's Action-Bar/Flask-Belt split stayed a widened single
column rather than a true left/right split — flagged as a real follow-up,
not risked unverified).
  - **Superseded same session, then reverted back**: batch 1 originally
    shipped this via a `<RotateOverlay />` that blocked all non-combat
    screens in portrait with a "rotate your device" prompt. First revised
    per explicit user instruction to auto-rotate everywhere instead (no
    blocking prompt), using the same CSS fixed+`transform:rotate(90deg)`
    trick `.battle-fs` uses for combat. Real-device playtesting then
    surfaced a structural bug this approach can't cleanly solve: rotating
    an ancestor swaps which DOM axis reads as "visual vertical" for
    everything inside it, so any *flowing/stacking, potentially-scrollable*
    content (the New Game form, Bag's grid, Bank's lists, Shop, Loadout —
    all of it, since none of these screens are absolutely-positioned like
    combat's HUD) gets its overflow pushed onto the wrong screen axis and
    the native scroll gesture stops matching what the user expects — "badly
    overflowing, can't even scroll up." `.battle-fs` never hit this because
    it has no scrolling content at all.
    **Reverted to `<RotateOverlay />`** (recreated, same file/behavior as
    originally shipped) for all non-combat screens; combat's own
    `.battle-fs` auto-rotate is untouched throughout both changes.
    This also lines up with the still-undecided Distribution question
    below: a store-wrapped app gets OS-level landscape lock and real
    fullscreen for free, making the in-browser rotate-prompt only a
    dev/playtest-time affordance, not a permanent UX compromise in the
    shipped product. CLAUDE.md's rule text reverted to describe the prompt.
  - **Fully reverted a third time — the whole §10 mandate is undone.**
    Even the rotate-prompt turned out to be the wrong call for actual
    playtesting: forcing a tester to physically rotate their phone just to
    reach the Hub/Bag/Bank/Shop/Loadout, on the promise of a landscape
    layout that only partially delivered on the UI bible's spec anyway
    (see the "structurally resisted full conversion" note above), made
    those screens worse to use, not better. Per explicit instruction,
    restored the exact pre-batch-1 behavior: **no forced orientation on
    any non-combat screen at all** — `RotateOverlay` deleted outright (not
    just its auto-rotate variant), Hub menu back to a single-column flex
    list, Bag back to one scrolling column (no left/right split), Loadout
    Editor back to its original 480px width. A phone held vertically gets
    a vertical menu; held horizontally gets a horizontal one — genuinely
    responsive, not a forced presentation either way. Combat is the sole
    screen that's ever forced into a specific orientation, via
    `.battle-fs`'s pre-existing auto-rotate (never touched by any of this).
    Re-verified via Playwright: no `.rotate-overlay` in the DOM at all in
    either orientation, New Game/Hub/Bag render as natural single-column
    layouts in a portrait viewport with no overflow, landscape viewport
    renders identically to before batch 1 ever touched these screens,
    combat still auto-rotates. CLAUDE.md's rule rewritten a third time to
    describe this as the actual, final state — do not re-attempt a forced-
    landscape mandate for non-combat screens without being asked again.
