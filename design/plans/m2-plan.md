# M2 — Progression Spine: Technical Plan

Per the implementation brief: "Bank (UI bible §7), equipment paper-doll +
Three.js preview (§8.1), gold shop, item tables ingested from the items doc,
drop tables + kill gold (economy §3–5), regular duels as gold on-ramp."

**Playtest question:** does the first hour's purchase cadence match the
economy targets (first purchase ≤15 min, T1 kit ≈ min 20–30, T2 kit ≈ hour 2)?

---

## 1. Current-state audit (what M2 builds on)

M1 shipped combat-math-v2 and a definition-file pipeline, but deliberately
left every M2-adjacent surface empty or absent, per its own brief ("Excludes:
bank, shop, other bosses, invocations, minigame") and its findings doc's
flagged scope reduction: *"Item/NPC economy content is data-complete but
shop/bank-free... shopPrices/fenceValues in items.json are empty and
maggot_king's LootTable is empty (gold-only reward). The shop/bank/ladder/
collection-log Blazor components... were deleted outright."*

Concretely, as of `claude/text-duel-game-3t4vkf`:

- **`items.json`** (`src/Duels.Infrastructure/Definitions/items.json`) has
  only T1/T2 shop weapons (3 styles × 2 tiers) and T1/T2 armour (3 lines × 6
  slots × 2 tiers). No T3/T4. No boss uniques/rares. `shopPrices` and
  `fenceValues` are both `{}`. Consumables list only `flask_health`/
  `flask_prayer` — no specialty flasks (expected, since those are boss drops
  and no boss drops anything yet).
- **`npcs.json`** has a single entry, `maggot_king`, with an empty
  `LootTable` and a flat `GoldReward: 500`. The economy doc's Tier-1 base is
  **300g**, not 500 — the 500 was an M1 placeholder never reconciled against
  the (later-written?) economy numbers. Flagging as a correction, not a
  silent carry-forward.
- **`Player`** (`src/Duels.Domain/Entities/Player.cs`) has `Gold`,
  `Equipped`, a single flat `Inventory` list, `AddGold`/`SpendGold`,
  `Equip`/`Unequip`/`AddToInventory`/`RemoveFromInventory`. No bank storage
  of any kind exists — everything lives in the one inventory list.
- **`GameTickService.RollLoot`/`HandleVictory`** (lines ~1052–1117) already
  drive a full loot loop: gold reward → `RollLoot` iterates `LootEntry`
  (`ItemId`, `DropChance`, `MinQty/MaxQty`, `OnceOnly`) → adds to inventory,
  or **auto-fences via `IItemRepository.GetFenceValue`** if the 28-slot bag
  (`player.Inventory.Count >= 28`, hardcoded) is full. This mechanism is
  ready to consume a real drop table the moment one exists — no engine work
  needed for basic drops.
- **`IItemRepository.GetFenceValue`** currently returns *shop price ÷ 2* for
  shop items, or a flat 100g fallback otherwise. The economy doc's §5 says
  drop-table "common" sells at **10–20%** of shop-equivalent, while §4 says
  shop **buyback** (selling back an item you just bought, same session) is
  **100% within session, 80% after**. The current single number conflates
  both concepts under one 50% constant that matches neither. This needs
  splitting — see Workstream D.
- **Equipment UI already exists**, further along than the brief implies: a
  `BagSheet.razor` (`src/Duels.Web/Components/Bag/`) shows a paper-doll
  (`EquipmentSlotButton` × `EquipmentSlot`) with a `PlayerPreview` centered
  between two columns, plus an `InventoryGrid` below. This is roughly UI
  bible §8.1's skeleton already. What's missing against the doc spec: the
  **stat sheet** (attack/defense bonus per style, DoT stats, prayer bonus),
  the **compare flow** (green/red delta vs equipped), and the **cosmetic
  override tab**. See Workstream C.
- **`PlayerPreview.razor`** renders via `voxel.js` (`voxel.initPreview`), a
  **hand-rolled MagicaVoxel `.vox` software rasterizer with no WebGL** — not
  Three.js. It shows a fixed `player_sakuna.vox` model; it does not reflect
  equipped gear at all. The actual battle renderer (`BattleScene.razor` →
  `toon.js`) *is* Three.js (`three.module.min.js`, `GLTFLoader`,
  `OutlineEffect` for the cel shader) and already has a GLTF-loading /
  asset-manifest pipeline for equip models. The brief explicitly asks for a
  **"Three.js preview"** for §8.1 — see Workstream C's flagged decision.
- **No shop, no bank, no buyback UI exist** — `HubMenu.razor` only has
  Fight / Retry / Loadout Editor / two dev-loadout buttons / Anim Editor.
- **`GameTickService` has no attack behavior for `Script == null` NPCs**
  (early-returns out of every rotation/hazard/master-script/DoT method at
  lines 477, 781, 827, 894 — a `Script`-less NPC currently only *moves* via
  `DummyStyle`, `GameTickService.cs:302`). **This is not a gap to fill for
  M2**: per explicit direction during this plan's review, there is no
  separate non-boss NPC category, now or ever — every fightable thing in
  the game is a boss NPC (`NpcTemplate` + `BossScript`), including whatever
  "regular duels" turns out to mean. See the resolved note under
  Workstream F below (F is folded into E).
- **A pre-M1 regular-duel roster existed and was deleted** in the M1 ladder
  sweep (commit `e604884`, `NpcLadderModal.razor` + old `npcs.json` rows:
  `goblin`, `swashbuckler`, `barbarian`, `desert_bandit`, `gladiator`,
  `corsair`, `berserker`, …), each with OSRS-model stats and a `MaxWager`
  field for a wagering/ladder system the design-decisions doc doesn't carry
  forward. Noted for history only — **not being resurrected in any form**,
  not even as reskinned boss-shaped content; see Workstream F.
- **`SaveData`** (`src/Duels.Web/Models/SaveData.cs`) is schema v2. It has no
  field for bank contents. `System.Text.Json` ignores unknown properties on
  load and defaults missing ones, so a v3 field is additive/backward
  compatible the same way v2 was (per the record's own doc comment) — no
  migration code needed, only a version bump for clarity.
- **RL/invocations don't exist yet** (M4). The economy formula `Kill gold =
  base × (1 + RL/100)` reduces to flat `base` for all of M2. The Rare loot
  slot is explicitly "Locked below RL 150" — M2's drop tables have no Rare
  row to add yet; only Common/Uncommon/Unique per kill.

---

## 2. Workstream A — Item table completion (items doc §2–§6)

Extend `items.json` (and the domain `ItemsDefinitionFile`/`DefinitionItemRepository`
where the schema needs new fields) to carry everything the items doc
specifies for **content that exists today** — i.e. the shop ladder (available
regardless of boss roster) and Maggot King's own unique/rare (Maggot King is
the only boss that exists). Boss uniques/rares for Hive Matron, Mirrorhide,
Bloodtithe, Gale Roc, The Unblinking, Millstone Golem, and Grand Duelist
reference bosses that don't exist until M3/M5/M6 — **do not ingest those
rows yet**; there is no drop source for them and no way to test them. Add
them alongside each boss's own milestone instead.

- **A.1 — Shop weapons T3/T4** (items doc §2, 6 new rows): Doombringer Maul,
  Longfang, Stormbrand, Kingsplitter, Siegepiercer, Archon's Rod. Doc
  fields per row: Power/Speed/Precision/Special exactly as tabulated.
- **A.2 — Armour T3/T4**, all three lines × 6 slots (items doc §5's Def
  table): 36 new rows, mirroring the existing T1/T2 row shape. Set-bonus
  math (4-piece +5% line damage, 6-piece +10 max special energy) is
  *already implemented* in `GameTickService.ComputeLineDamageBonus`/
  `MaxSpecialEnergy` (lines 1013–1039) — purely data-driven once T3/T4 rows
  exist, no engine change.
- **A.3 — Maggot King's unique (Rotfang) and rare (Carrion Edge)** (items
  doc §3–§4). Rotfang is a melee dagger whose effect is "hits apply poison
  (stacking with fight mechanics)" — this needs a `SpecialEffect`-adjacent
  passive-on-hit hook; check whether the existing poison/DoT plumbing
  (Scorch/Rend's single-track DoT, per m1-findings) can carry a third
  poison track, or whether Rotfang's poison reuses the *existing* poison
  system Maggot King's own eruption pools already apply to the player
  (`GameState`'s hazard tiles apply poison — confirm exact mechanism before
  implementing so this isn't a second, parallel poison model). Carrion Edge
  (rare) is RL 150+ gated — since RL doesn't exist until M4, **ingest the
  item row now** (items doc §4 numbers) but leave it undroppable: no Rare
  loot-table slot exists yet (Workstream E), so it simply has no way to
  drop in M2. This avoids re-touching items.json in M4 just to add a row
  that was always fully specified.
- **A.4 — Specialty flask stub.** Rotward (Maggot King's counter-flask,
  items doc §6) is data-completable now (liquid tint, "Maggot King shards"
  source) but its *shard* drop mechanic depends on the Uncommon loot slot
  (Workstream E) and the flask belt's shard-to-flask conversion, which
  doesn't exist in the domain model at all yet (`FlaskBelt`/`Loadout` only
  know fixed sip charges, no shard currency). **Flagging as likely
  out-of-scope for M2 itself** — implementing shard accumulation is a small
  but real new subsystem, not just a data row. Recommend deferring Rotward
  to whichever milestone needs it as a mechanical counter (Maggot King is
  already the only boss it counters, and M2 doesn't add new bosses), unless
  reviewer wants shard tracking pulled forward now.
- **A.5 — `shopPrices` + `fenceValues`.** Populate `shopPrices` for every
  T1–T4 weapon/armour row (items doc §4's price ladder) plus the two
  baseline flasks (1,000g each, one-time unlock — confirm "one-time unlock"
  means a boolean owned-flag rather than a per-use consumable purchase;
  current `Player`/`Loadout` model has no such flag, needs one). Uniques/
  rares are "never sellable" (rares) or "sell for 15% of a T4 piece"
  (uniques) per items doc §4 — this is a *fence* value, not a shop price;
  keep them out of `shopPrices` entirely and add a `fenceValues` override
  row for Rotfang instead (T4 armour piece price × 0.15 — pick the
  appropriate T4 anchor since Rotfang isn't itself an armour piece; flag if
  the "which T4 piece" reference is ambiguous).
- **A.6 — Split fence vs. buyback** (see audit). `GetFenceValue` should
  become the drop-table "common sell" number (10–20% of shop-equivalent,
  items doc §5) for items that *have* a shop price, and keep the flat
  fallback for drop-only items (uniques, per A.5). Buyback (100% same
  session / 80% after) is a **separate, session-scoped mechanic** that
  belongs to the shop's purchase history, not to `IItemRepository` at all
  — see Workstream D.3.

---

## 3. Workstream B — Bank (UI bible §7)

**New domain concept: `Player.BankedItems`**, a second item list distinct
from the 28-slot `Inventory` ("bag"). Bank capacity is effectively unbounded
(OSRS-style) — no cap logic needed to match `RollLoot`'s bag-full fence
fallback.

- **B.1 — Domain**: `Player.AddToBank(itemId)` / `RemoveFromBank(itemId)` /
  `BankedItems` (mirrors existing `AddToInventory`/`RemoveFromInventory`
  shape). `Deposit(itemId)` moves bag→bank, `Withdraw(itemId)` moves
  bank→bag (bag has no cap enforced today beyond the loot-time check in
  `RollLoot` — confirm whether manual withdraw should also respect the
  28-slot cap, since the doc doesn't say the bag is uncapped elsewhere; the
  28 appears to be an incidental M1 constant, not a documented number —
  **flagging**: no design doc specifies bag size; 28 is an unattributed M1
  choice worth confirming or replacing with a sourced number).
- **B.2 — Commands/handlers**: `DepositItemCommand`/`WithdrawItemCommand`
  (+ quantity variants), following the existing `EquipItemCommand`/
  `UnequipItemHandler` pattern exactly.
- **B.3 — Persistence**: `SaveData` v3 adds `List<string> BankedItems`.
  Backward-compatible default (empty list) per the v1→v2 migration note
  already in the file.
- **B.4 — UI, functional core only** (per the brief's "Visual Design Scope":
  mandatory = gameplay function, deferred = skin/polish). Ship:
  - Grid of slots showing banked items + stack counts, scrollable.
  - Quantity toggle (1/5/10/X/All) on withdraw.
  - Deposit-all button; deposit-worn is **not** mandatory for M2 (worn gear
    deposit needs an "exclude bar weapons + confirm" guard the brief
    doesn't otherwise require yet — defer with the rest of §7's polish
    list).
  - **Explicitly deferred** (flagging, not silently cutting, per the M1
    Loadout Editor precedent): tabs, search-as-you-type, favorites row,
    placeholder ghost icons, "Prepare Loadout" bank↔loadout bridge button.
    These are real UX value-adds but each is its own small feature; none
    blocks the playtest question (purchase cadence), and cramming all of
    §7 in risks the same "interim, ugly-but-readable" mandate the brief
    already sanctions. Recommend a follow-up pass once the core loop
    (loot→bank→sell/shop→equip) is validated on a phone.
- **B.5 — Hub wiring**: new `HubMenu` entry opening a `BankSheet.razor`
  (new component, sibling to `BagSheet.razor`).

---

## 4. Workstream C — Equipment paper-doll + preview (UI bible §8.1)

- **C.1 — Renderer decision (flagging for review, not deciding silently).**
  The brief's milestone line says *"equipment paper-doll + **Three.js**
  preview"*. The existing `PlayerPreview.razor` uses `voxel.js`, a parallel
  non-Three.js renderer with no gear-compositing capability at all (fixed
  `.vox` model). Meanwhile `toon.js` (Three.js) already has a GLTF loader
  and asset-manifest pipeline for equip models, used every duel to render
  the player's actual worn gear in battle. Two paths:
  1. **Recommended**: build the §8.1 preview on `toon.js`'s existing
     Three.js/GLTF pipeline (an `initPreview`-style export analogous to
     voxel.js's API, but instantiated from the same equip-model loader
     already used in combat) — matches the brief's literal wording, reuses
     one asset pipeline instead of maintaining two, and the "locked
     architecture invariant" already treats Three.js as the sanctioned
     renderer.
  2. Extend `voxel.js` to composite equipped-gear voxel pieces onto the
     preview model instead. Keeps a second rendering technology alive
     for no clear reason (voxel.js has no combat use besides bag icons)
     and doesn't match "Three.js preview" as written.
  This is a real architectural fork, not a detail — flagging for explicit
  sign-off before starting C.2–C.4 rather than picking silently. Recommend
  (1).
- **C.2 — Stat sheet** (§8.1 "right panel"): attack bonus per style,
  defense per style, special-energy modifiers, DoT-related stats, prayer
  bonus if it exists — computed from the player's current `Equipped` set
  via the same `DocStats`/`ComputeLineDamageBonus`/`MaxSpecialEnergy` logic
  `GameTickService` already uses for combat, exposed read-only to the UI
  layer (need a query-side method, not a duplicate calculation — reuse,
  don't reimplement, per working-agreement #4's spirit).
- **C.3 — Compare flow**: tapping an owned item (bag or bank) shows its
  card with green/red delta arrows vs. currently equipped in the same slot;
  equip is one tap from the card. Delta = `DocStats` diff, same source data
  as C.2.
- **C.4 — Cosmetic override tab**: transmog layer is explicitly a
  run-earned/Emporium reward system (economy doc §6, UI bible §9) that
  doesn't exist until M6/M7 content exists to populate it. **Deferring
  entirely** — ship the tab only when there's something to put in it; an
  empty tab is dead UI.

---

## 5. Workstream D — Gold Shop (UI bible §9, economy §4)

- **D.1 — Shop screen**: categorized grid (weapons by style, armour by
  slot) sourced from `IItemRepository.GetShopItems()` (already exists and
  already orders by price — just needs real data via Workstream A). Each
  card: stats, compare-delta vs. equipped (reuse C.2/C.3's diff logic),
  gold price. The doc's "why buy" tag (e.g. "Unlocks viable Magic swaps for
  Mirrorhide") is **boss-aware flavor text with no source data** — no boss
  exists to reference yet besides Maggot King, and the doc gives no
  per-item tag table. Flagging: either omit the tag for M2 (cleanest — it's
  presentation, not a mechanic) or write Maggot-King-only tags as an
  explicit, reviewable content pass. Recommend omitting.
- **D.2 — Purchase flow**: `BuyItemCommand`/handler — validate gold,
  `SpendGold`, `AddToInventory` (or bank, if bag is full — reuse the fence
  logic's bag-capacity check, but deposit to bank instead of auto-selling
  on a *purchase*, since fencing a freshly bought item would be a strange
  UX). No confirm-spam: single confirm above a threshold only (doc doesn't
  give a number — flagging as a tunable to pick, not invent silently;
  suggest anchoring it to a tier boundary, e.g. confirm above T2 price,
  for review).
- **D.3 — Buyback tab**: session-scoped purchase history (what was bought
  this session, at what price) so a sale within the same session refunds
  100%, dropping to the drop-table fence rate (Workstream A.6) after. This
  is new state — doesn't fit cleanly in `Player` (which persists across
  sessions) or `IItemRepository` (stateless definitions); most likely lives
  on `GameState` or a small new session-scoped value alongside it. Flagging
  for design during implementation rather than pre-committing a shape here.

---

## 6. Workstream E — Drop tables + kill gold (economy §3, §5)

- **E.1 — Kill gold**: set `maggot_king.GoldReward` to the economy doc's
  Tier-1 base, **300g** (correcting the current 500g placeholder — flagging
  the correction explicitly per CLAUDE.md, this isn't a silent number
  change). RL-multiplier math is a no-op until M4 (`1 + 0/100 = 1`); no
  code needed now, `GoldReward` stays a flat field until Workstream-M4 adds
  the RL scalar.
- **E.2 — Maggot King's drop table**: economy §5 defines the *slot
  mechanism* (Gold always / Common 65% / Uncommon 25% / Unique 1-in-20 /
  Rare locked-below-150) but **no design doc anywhere enumerates concrete
  item rows** for Maggot King's Common or Uncommon slots — only the generic
  category description ("sellables... materials reserved for future
  crafting" / "off-tier gear pieces... specialty-flask shards") and the
  boss-designs doc's one-line "drop niche" (which only describes the Rare).
  **This is a genuine content gap, not an implementation detail — flagging
  for explicit resolution before Workstream E is built**, rather than
  inventing placeholder item names/rates myself. The mechanism itself
  (LootEntry rolls, already-working RollLoot pipeline) needs no new engine
  code regardless of what fills it; only the content rows are blocked.
  Sketch of what needs deciding: what "sellables" are (gold-equivalent
  filler items, or literally just more gold at the Common roll?); which
  off-tier gear pieces count as "bridging shop tiers" for a Tier-1 boss
  (T1→T2 pieces, presumably, but from which line — random, or themed to
  Maggot King?).
- **E.3 — Unique drop**: Rotfang at 1/20 (`DropChance: 0.05`), not
  `OnceOnly` (uniques are sellable dupes per items doc §4's 15% fence
  value, so repeat drops should be alloweded, not blocked).
- **E.4 — Rare slot**: no-op for M2 (locked below RL 150; leave out of the
  loot table entirely until M4 adds RL and the rate band).
- **E.5 — Bad-luck protection**: explicitly tied to the Rare roll only
  ("hidden +2% relative rate per dry kill past 1.5× expected... resets on
  hit") — no-op for M2 since there's no Rare roll yet. Confirmed out of
  scope, not deferred-and-forgotten.

---

## 7. Workstream F — "Regular duels as gold on-ramp" (economy §3) — RESOLVED, folded into E

**Resolved during this plan's review**: there is no separate non-boss NPC
roster. Every fightable opponent is, and will only ever be, a boss NPC
(`NpcTemplate` + `BossScript`) drawn from the 8-boss roster. This corrects
this plan's original Workstream F, which read the brief's "regular duels as
gold on-ramp" line and the economy doc's §3 "Regular duels (warm-up
content): 50g→150g per win" / design-decisions doc's "Regular duels serve
as warm-up and gear source, not the core content" as implying a *separate*
category of lightweight mob opponents (reusing the retired pre-M1 ladder's
shape) — a plausible reading of those docs taken alone, but the wrong one.

Consequences:

- **No opponent roster, no roster stats, no new `npcs.json` mook rows, no
  duel-opponent picker UI.** Delete this entirely from scope — there was
  never anything here beyond a roster draft, which is now moot.
- **No non-scripted attack engine needed either.** The `Script == null`
  early-return behavior in `GameTickService` (audit, §1) is fine as-is;
  nothing in M2 exercises it, and nothing should be built to fix it.
- **What "gold on-ramp" actually means for M2 is now an open question, not
  a closed one** — flagging squarely rather than guessing: with only
  Maggot King fightable in M2 (M3 adds the rest of the roster), does the
  economy doc's §3 "regular duels" paragraph:
  1. describe content that simply doesn't exist yet and won't until there's
     a roster of *bosses* spanning a 50g–150g-per-win difficulty band below
     Tier 1 (i.e., is it actually talking about easy/early bosses, not a
     separate mob category, and the wording is just imprecise)?
  2. describe a feature that's been cut from the redesign and the economy
     doc's §3 section is stale, superseded by design-decisions.md's boss-
     only structure and simply never trimmed?
  3. describe something else entirely that hasn't come up in this review?
  Either way, **M2's actual on-ramp is Maggot King's own kill-gold rate**
  (economy §3's Tier-1 table: ~300g/kill, ~4,500g/h at-tier, per Workstream
  E.1) — there is nothing else to farm until M3. This plan makes no further
  assumption about what "regular duels" means; resolving it is a design
  decision for whoever owns the economy doc, not an implementation detail
  for Claude Code to infer.
- This resolution, and the corrected reading of the brief's "regular duels
  as gold on-ramp" line, belongs in the findings record — see the
  "Findings addendum" the user requested, written out at the end of this
  planning pass.

---

## 8. Workstream G — Save schema + hub wiring

- **G.1 — `SaveData` v3**: add `List<string> BankedItems = []` (per B.3).
  Bump `GameService.CurrentSchemaVersion` to 3 — no migration logic needed
  (missing field defaults per the existing v1→v2 pattern).
- **G.2 — Hub menu**: add Bank and Shop entries alongside the existing
  Fight/Retry/Loadout cards; extend the Fight card into an opponent list
  (F.3). Equipment/paper-doll is already reachable via the existing Bag
  entry point (if one exists in the current nav — confirm `BagSheet`'s
  trigger point in `Game.razor`/wherever it's opened from, since it wasn't
  in the `HubMenu.razor` read above; it may be opened from the in-duel HUD
  instead of the hub, which would be consistent with a "check your gear
  mid-loadout-prep" flow but worth confirming during implementation).

---

## 9. Sequencing

Each step merges to `claude/text-duel-game-3t4vkf` when green, per
CLAUDE.md's "after every completed step, not just when asked" instruction.

1. **A** (item table completion) — pure data + minor repository/schema
   extensions, unblocks everything else, no UI dependency.
2. **E.1** (kill-gold correction) — one-line data fix, ships immediately,
   independent of everything else.
3. **B** (Bank) — domain + persistence + minimal UI. Needed before Shop/
   drop-table testing generates more items than the 28-slot bag can hold.
4. **D** (Shop) — depends on A's priced items and B's overflow destination.
5. **C** (Equipment paper-doll + preview) — depends on A (real item stats
   to show) and benefits from B/D existing (things to compare against);
   **C.1's renderer decision blocks C.2–C.4** and should be confirmed with
   the reviewer before this step starts, not discovered mid-implementation.
6. ~~F (regular duels)~~ — cut; resolved as "no separate roster," folded
   into E. Nothing to sequence.
7. **E.2–E.5** (Maggot King's real drop table) — blocked on a content-review
   step (no concrete Common/Uncommon rows exist in any doc); sequenced last
   because the mechanism (RollLoot) already works and doesn't block
   anything else.
8. **G** (save schema bump + hub wiring) — threads through every prior
   step; land incrementally as each workstream's UI entry point is ready
   rather than as one final integration step.

---

## 10. Design questions (flagged, not resolved) and tunables

1. **C.1 — Preview renderer**: Three.js (reuse `toon.js`'s GLTF pipeline,
   recommended) vs. extending `voxel.js`. Blocks Workstream C.
2. **A.3 — Rotfang's poison mechanic**: does it reuse Maggot King's
   existing hazard-pool poison application, or does it need its own
   independent poison-stack track? Needs an answer before implementing, not
   during.
3. **A.4 — Specialty flask shards**: is shard-accumulation (a new currency
   the domain model doesn't have at all) in scope for M2, or deferred with
   Rotward itself? Leaning defer.
4. **A.5 — Flask "one-time unlock"**: does this need a boolean
   owned-flag on `Player`/`Loadout`, replacing the "buy every time" shop
   model for just these two items? The doc's wording ("one-time unlock")
   implies yes but the mechanism isn't specified.
5. **B.1 — Bag cap of 28**: unattributed M1 constant, not sourced from any
   design doc. Keep as-is, or is there an actual intended number?
6. **D.1 — "Why buy" tags**: omit for M2 (recommended, it's boss-aware
   flavor text with no source content beyond Maggot King) or write a
   Maggot-King-only version?
7. **D.2 — Confirm-purchase threshold**: doc says "single confirm on
   purchases above a threshold only" with no number. Proposed: confirm
   above T2 price (2,500g+). Needs sign-off.
8. **E.2 — Maggot King's Common/Uncommon loot rows**: no concrete item list
   exists in any design doc. Blocks Workstream E until resolved — see
   Workstream E.2 for the specific open sub-questions.
9. ~~F.1 — Regular-duel roster~~ **RESOLVED**: no separate roster exists or
   will exist; see Workstream F.
10. ~~F.2 — Non-scripted attack engine~~ **RESOLVED (moot)**: nothing in M2
    needs it.
11. **What does economy §3's "regular duels" paragraph actually refer to?**
    Newly opened by F's resolution (not the same question as #9/#10 above)
    — with no non-boss NPCs, M2's gold on-ramp is just Maggot King's kill
    rate. Whether the doc's §3 language describes a future easy-boss band,
    stale pre-redesign wording, or something else is unresolved — see
    Workstream F's three numbered readings. Does not block any M2
    workstream (all of M2's income already resolves to boss kill-gold
    either way); flagging for whoever next edits the economy doc.
12. **A.6 — Fence vs. buyback split**: confirmed as a real modeling gap
    (see audit) — needs the split described in A.6/D.3, not a design
    ambiguity so much as an implementation task, but flagging since it
    changes `IItemRepository`'s contract.

---

## 11. Explicitly out of scope (per the brief and M2's own boundaries)

- Other bosses (Hive Matron, Mirrorhide, Bloodtithe, …) and their unique/
  rare items — M3+.
- Invocations, raid level, rare-drop gating, bad-luck protection's actual
  effect — M4.
- Collection log, profile, leaderboards — M8/never-explicitly-scheduled.
- Cosmetic/transmog override tab (§8.1) — no content exists to populate it
  until the minigame/Emporium ship (M6/M7).
- Wager system, win streaks — retired, not returning.
- Separate non-boss/mook NPC roster ("regular duel opponents") — resolved
  during this plan's review as never existing; all fightable content is
  boss NPCs. See Workstream F.
- Bank tabs/search/favorites/placeholders/Prepare-Loadout bridge — real
  UX value, deliberately deferred past the core loop (see B.4).
- Shop buyback's exact session-tracking shape — flagged (D.3) for
  implementation-time design, not pre-specified here.
