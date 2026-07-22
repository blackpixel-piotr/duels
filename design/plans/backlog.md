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

Nothing here can be implemented without inventing numbers/names, which
CLAUDE.md forbids. Each needs a human to add the missing content to the
relevant `/design/*.md` doc before any agent can act on it.

1. **Rotfang (Maggot King's unique) has no combat stats anywhere.** Items
   doc §3 (Boss Uniques) gives every unique a Slot + Effect + Model, but
   *no* Power/Speed/Precision — only §4 (Boss Rares) tabulates stats. Can't
   ingest Rotfang as a real weapon without inventing its base numbers.
   *(M2)*
2. **Maggot King's Common/Uncommon loot table rows don't exist.** Economy
   §5 defines the roll *mechanism* (Common 65% / Uncommon 25%) but no
   design doc lists actual item rows for either slot — only a generic
   category description ("sellables... materials", "off-tier gear pieces...
   flask shards") and the boss-designs doc's one-line "drop niche" (which
   only covers the Rare). The drop pipeline (`RollLoot`) already works;
   only content is missing. *(M2, `m2-plan.md` Workstream E.2)*
3. **What does economy §3's "Regular duels" paragraph refer to, now that
   there's no non-boss NPC roster?** Ratified during M2 pre-plan: every
   fightable thing is a boss, now and always. That leaves the economy doc's
   original "regular duels: 50g→150g per win, warm-up content" language
   pointing at nothing. Doesn't block anything (M2's income already
   resolves to boss kill-gold either way) but the doc itself needs an
   editorial pass — either describes a future easy-boss band, or the
   sentence is stale and should be cut. *(M2)*
4. **Rotward (Maggot King's counter-flask) and the specialty-flask shard
   currency.** Items doc §6 names the flask (liquid tint, "Maggot King
   shards" source) but shard accumulation is a real subsystem the domain
   model doesn't have at all (`FlaskBelt`/`Loadout` only know fixed sip
   charges). Content exists in outline; the mechanism needs designing, not
   just data. *(M2, deferred per `m2-plan.md` A.4)*

## B. Deferred features — spec'd, just not built

5. **Shop buyback** (economy §4: 100% refund same session, 80% after). No
   session-scoped purchase-history mechanism exists; `IItemRepository`
   deliberately doesn't carry one (fence value ≠ buyback, see finding #16).
   *(M2, `m2-plan.md` Workstream D.3)*
6. **Bank UX polish** (UI bible §7): tabs, search-as-you-type, favorites
   row, placeholder ghost icons, the "Prepare Loadout" bank↔loadout bridge
   button. Core loop (deposit/withdraw/quantity toggle) shipped; these were
   explicitly deferred past it. *(M2, `m2-plan.md` Workstream B.4)*
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

13. **T3/T4 weapon specials + Carrion Edge's Fester are data-only.**
    `quake`, `piercing_arrow`, `arc`, `executioner`, `ballista_bolt`,
    `annihilate`, `fester` all exist as real `SpecialEffect` rows (id,
    cost, description) but `GameTickService.PerformSpecialAttack`'s
    dispatch switch only has cases for M1's six specials — pressing any of
    these seven logs `"Unknown special"` and no-ops. Quake is a real AoE,
    Annihilate an interruptible 2-tick cast bar — six distinct mechanics,
    not a small patch. *(M2, items ingested in Workstream A)*
14. **No boss-side poison DoT track exists.** `GameState.PlayerPoisoned`
    only ever poisons the *player* (applied by Maggot King's eruption
    pools) — `NpcInstance` has no equivalent field. Blocks Rotfang's
    "hits apply poison" and Carrion Edge's "20% of hits apply poison" /
    Fester's "detonates all poison on the target" from ever doing anything
    even once their stats exist. Real new engine work. *(M2, discovered
    while investigating A.3)*
15. **Maggot King's P1 Eruption still runs on an independent timer**,
    violating the "Master-script rule" doctrine added for P2 (Global
    Combat Grammar: "every phase runs on one master tick script... never
    produced by timer drift"). Explicitly flagged as a pre-M5 audit target
    alongside Gale Roc's ambient lightning and the Unblinking's gravel
    crawlers — none of those bosses have code yet, so this is the one
    live offender. *(M1, `m1-findings.md` eighteenth pass)*

## D. Design questions needing human confirmation (not doc-blocked, just unanswered)

16. **`Player.Gold` starts at 0 with no separate regular-duel income —
    real cold-start gap.** A first-time player's only income (Maggot
    King, 500g/kill) requires gear to realistically survive long enough to
    land that kill; that gear is only obtainable *from* the gold the kill
    provides. The dev-loadout hub buttons are the only working bootstrap
    today, and they're not gated behind any build flag. This became
    concrete (not hypothetical) once the Shop shipped and there was
    somewhere to actually spend a starting seed. **Needs a product
    decision**, not an invented number. *(M2)*
17. **XP/levels: gone for good, or cosmetic progression later?** M1
    removed Attack/Strength/Defence/Hitpoints XP entirely rather than
    keeping it vestigial, trading away the plan's original "keep it
    combat-inert" scope. Never explicitly re-confirmed since. *(M1
    plan D3, still open)*
18. **Fence/sell-value split (`m2-plan.md` A.6) is implemented as a
    mechanism** (15% of shop price, or an explicit override, or 0) but the
    override table (`fenceValues`) is still empty — no unique/rare content
    exists yet to populate it with real numbers (items doc §4: uniques
    sell for 15% of a T4 piece; rares never sellable — the 0 default
    already gives the rare behavior, but the unique formula isn't wired to
    an actual T4 anchor price yet since Rotfang can't ship, see #1).
19. **Armour price interpolation formula is an assumption, not a doc
    number.** Items doc §4 gives only a per-tier price *range* for armour
    (e.g. "200–350g"); the actual per-slot prices in `items.json` were
    derived by linearly interpolating within that range by the slot's Def
    weight (heaviest = priciest). Endpoints match the doc; the shape in
    between doesn't. Source-commented as PROVISIONAL in `items.json`
    itself. *(M2)*
20. **Weapon `AttackType` for the new T3/T4 melee weapons is inferred, not
    stated.** Doombringer Maul (Warhammer archetype) → `Crush`, Kingsplitter
    (Greatsword) → `Slash`, following the T1/T2 pattern — the doc's weapon
    table has no explicit Stab/Slash/Crush column. Low-stakes (protection
    prayer buckets all melee as one style) but could pick the wrong swing
    animation in `toon.js`. *(M2)*
21. **Carrion Edge's `Line` set to `None`** on the reasoning that rares sit
    outside the shop-ladder armour-line identity/set-bonus system — not
    stated either way in the doc. *(M2)*
22. **Purchase-confirm threshold (2,500g)** implemented per the plan's own
    proposal but never independently re-confirmed by a human before
    landing. *(M2, `ShopSheet.razor`'s `ConfirmThresholdGold`)*
23. **Economy §4's shop-ladder cadence-check row** ("~20 min" to a T1 kit)
    was not recomputed against the new 500g/~7,500g-per-hour Tier-1 rate —
    only the stale "of duels+first kills" wording was removed. The
    playtest question (does purchase cadence match the economy targets)
    should be checked against this before being called answered either
    way. *(M2)*
24. **Design-ambiguity assumptions from M1, still standing as
    provisional** (implemented, playable, but genuinely invented numbers
    per CLAUDE.md's own flagging rule — worth a real design pass rather
    than staying permanent by default):
    - Defensive style's "+20% defense value" implemented as a flat 20%
      incoming-damage reduction, stacking additively with (and uncapped
      by) the 40%-capped gear Def-point reduction — the doc doesn't say
      whether this is the right shape.
    - Boost prayer's magnitude (+20% Power) — carried over from the
      pre-M1 Piety number, not sourced from any current doc.
    - Scorch/Rend DoT magnitudes (3 ticks @ 3/tick; 4 ticks @ 3/tick +
      1.3× base-hit) — doc only says "3-tick burn" / "bleed stack" with no
      numbers.
    - NPC (boss) armour = 0 Def — items doc's Def-point system is written
      for player gear only; every player hit on a boss currently lands at
      full weapon damage with zero boss-side mitigation.

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
    onboarding path, not a debug button). *(M1)*
26. **`duels-economy.md` and `duels-economy_1.md` are byte-identical
    duplicate files.** Both tracked in git, kept in sync manually during
    the M2 pre-plan edits rather than silently diverging. Recommend
    deleting `duels-economy_1.md` once confirmed nothing depends on having
    two copies. *(M2)*
27. **Unsourced constants found in a targeted sweep, reported but not
    changed** (per instruction — report only): `Player.PrayerPoints = 99`
    (no doc gives a prayer-pool size), `AttackRange.Distant = 8` (used
    only by the now-provably-unused `DummyStyle` non-boss movement path —
    see #28), `GameState.PrayerDrainCadenceTicks = 9` (no doc gives prayer
    drain a tick cadence). Not urgent, but each is exactly the kind of
    invented number CLAUDE.md's provisional-constant rule now requires
    flagging in code (`// PROVISIONAL: <reason>`) — none of these three
    carry that comment yet. *(M2 pre-plan sweep)*
28. **`NpcTemplate.DummyStyle` and its non-scripted movement path are now
    provably dead for real content.** Built for "the pathfinding/movement
    test fixtures and any future non-boss mob" — the boss-only ruling
    (backlog item, see Section D discussion in `m1-findings.md`'s M2
    pre-plan addendum) means there will never be a non-boss mob. Still
    load-bearing for test fixtures; safe to leave, but the "future non-boss
    mob" half of its own doc comment is now known-stale. *(M2, discovered
    during pre-plan)*

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

---

## Resolved

*(Move an item here, with a pointer to what closed it, instead of deleting
it — this is the record that it was tracked and picked up, not just that
it disappeared.)*

- None yet.
