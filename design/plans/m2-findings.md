# M2 — Progression Spine: Implementation Findings

Companion to `m2-plan.md`. Written during implementation — what actually
happened, where reality diverged from the plan's assumptions, and every
place a number/mechanic wasn't in a design doc and had to be flagged rather
than invented.

---

## Verification

- `dotnet build Duels.sln`: 0 errors, 0 warnings (checked after every
  workstream, not just once at the end).
- `dotnet test Duels.sln`: **101/101 passing** (11 Domain, 42 Application,
  43 Infrastructure — up from M1's 63; new coverage: `PlayerBankTests`,
  `BankAndShopHandlerTests`, the extended `DefinitionItemRepositoryTests`
  for the 15% fence rate and the T3/T4/rare item ingestion).
- `node --check` on `voxel.js` and `node --input-type=module --check` on
  `toon.js`: both syntactically clean.
- **Browser verification (Playwright, per the `verify` skill)**: launched
  the actual app (`dotnet run --project src/Duels.Web`), drove it through
  Chromium — new character → dev T1 loadout → Bank → Gold Shop → Bag
  (paperdoll/preview/stat sheet) → tap-to-compare. Zero `pageerror`
  exceptions across two full sessions; the only console noise was the two
  entries the `verify` skill's own doc already documents as pre-existing
  and benign (`Duels.Web.styles.css` 404, Google Fonts fetch blocked by the
  proxy). Screenshots confirm: Bank and Shop screens render and are
  navigable; the equipment screen's Three.js preview renders the **actual
  toon character wearing real composited gear meshes** (visible tan boots,
  green leg armor, waist/head wrap — not a placeholder, not the old fixed
  voxel model); the stat sheet shows real computed numbers (Melee +11% off
  a partial Warbound set, Def 12pts/4%, Max Special 110%); the compare card
  opens with real item data and Equip/Cancel buttons. This is the one
  workstream (C.1) that genuinely needed eyes-on verification rather than
  just unit tests, and it's real — not just wired up, actually rendering.

---

## What shipped vs. the plan

**Fully implemented, tested, and browser-verified**: Workstream A (item
table completion, with the exceptions below), G.1 (SaveData v3), B (Bank —
domain, commands, UI, hub wiring), D (Shop — purchase flow, UI, hub
wiring; buyback excepted, see below), C (stat sheet, compare flow, and the
Three.js preview rebuild — C.1's fork is not just decided, it's built).

**Still blocked / deliberately deferred** — same set the plan predicted,
plus two the plan didn't anticipate:

1. **E.2 (Maggot King's Common/Uncommon loot rows)** — still blocked, per
   the plan. No content list exists in any design doc; `LootTable` stays
   empty (gold-only reward, unchanged from M1).
2. **A.4 (Rotward specialty flask / shard system)** — deferred, per the
   plan's own recommendation. No shard currency exists in the domain model.
3. **NEW — Rotfang couldn't be ingested at all**, not just its poison
   mechanic. The plan's A.3 only flagged the *poison-on-hit mechanism* as
   needing a decision; implementing it surfaced a deeper gap: **items doc
   §3 (Boss Uniques) has no Power/Speed/Precision columns for any unique**
   — only §4 (Boss Rares) tabulates combat stats. Rotfang's row is Slot +
   Effect + Model only. Ingesting it as a real weapon would mean inventing
   its base numbers outright, which CLAUDE.md forbids regardless of how
   small the gap looks. **Rotfang is not in `items.json`.** Carrion Edge
   (the *rare*, §4) has full stats and *is* ingested — see below.
4. **NEW — no boss-side poison DoT track exists.** Investigated while
   deciding Rotfang's mechanism (per the plan's own open question):
   `GameState.PlayerPoisoned` only ever poisons the *player* (applied by
   Maggot King's own eruption pools); `NpcInstance` has no equivalent field
   at all. Rotfang's "hits apply poison" and Carrion Edge's "20% of hits
   apply poison" / Fester's "detonates all poison on the target" would all
   need a new NPC-side poison-stack mechanism — real engine work, not a
   data row. Relevant for whichever milestone eventually implements either
   item's real effect.
5. **NEW — T3/T4 weapon specials and Carrion Edge's Fester are data-only,
   not wired up.** The plan's A.1 said "ingest... Doc fields... Special
   exactly as tabulated" and I read that as the data shape, which is what
   got built: `quake`/`piercing_arrow`/`arc`/`executioner`/`ballista_bolt`/
   `annihilate`/`fester` all exist as real `SpecialEffect` rows with real
   costs and descriptions. But `GameTickService.PerformSpecialAttack`'s
   dispatch switch (the actual mechanic implementation — AoE hit for
   Quake, an execute-threshold check for Executioner, a self-root for
   Ballista Bolt, an interruptible 2-tick cast for Annihilate, etc.) still
   only has cases for the six M1 specials. Pressing the special-attack
   button with a T3/T4 weapon (or the undroppable Carrion Edge) hits the
   `default: "Unknown special '{id}'"` no-op path — logged, not a crash,
   but not the item's real identity either. Implementing six new special
   mechanics (one of them a genuine AoE, one an interruptible cast bar) is
   its own real unit of work; flagging rather than quietly shipping it as
   "done" when only the data half is.
6. **Shop buyback (D.3)** — not implemented, per the plan. No
   purchase-history/session-tracking mechanism exists yet; `IItemRepository`
   deliberately doesn't carry one (see the fence-value doc comment).
7. **Bank's deferred UX** (tabs, search, favorites, placeholder ghosts,
   Prepare-Loadout bridge) and **Shop's "why buy" tags** — omitted exactly
   as the plan proposed, not silently.
8. **VoxelIcon.razor (bag/bank/shop item icons) still calls into
   `voxel.js`.** C.1's Three.js rebuild covers the equipment *preview*
   (`PlayerPreview.razor`) only — the plan flagged this exact split in
   advance ("VoxelIcon's bag-icon usage needs its own replacement... check
   before deleting voxel.js wholesale"). `voxel.js` is still loaded and
   still the only renderer for item-grid icons; CLAUDE.md's "Three.js
   only, no secondary renderers" invariant is **not yet fully true** in the
   codebase, only true for the one surface (equipment preview) this
   milestone's brief specifically named. Flagging this gap explicitly
   rather than letting the invariant read as already-satisfied.
   Separately: item icons in the Bag/Bank/Shop grids rendered **blank**
   during browser verification for the M2 weapon/armour ids — this
   predates this session (the icon path is unrelated to anything changed
   here) and wasn't investigated further; flagging as an observed, not a
   newly introduced, gap.

---

## Interpretive choices flagged (not invented silently, but not doc-given either)

- **Armour price interpolation** (A.5): items doc §4 gives only a
  per-tier price *range* for armour (e.g. "200–350g"), not per-slot
  numbers. Used linear interpolation within each tier's documented
  [low, high] band, keyed to the slot's own Def-point weight from items
  doc §5 (Boots/Gloves/Cape lightest → low end; Body heaviest → high end).
  Endpoints match the doc exactly; the interpolation shape is an
  assumption. Source-commented in `items.json` itself (`// PROVISIONAL`).
- **Weapon `AttackType` for the new T3/T4 melee weapons**: the items doc's
  weapon table gives an "Archetype" column (Warhammer, Greatsword, …) but
  no explicit Stab/Slash/Crush. Inferred Doombringer Maul (Warhammer) as
  `Crush` and Kingsplitter (Greatsword) as `Slash`, following the
  established T1/T2 pattern (shortsword/battleaxe → Slash). Low-stakes —
  protection prayer buckets all melee as one style regardless (per
  `AttackRange.cs`'s own comment) — but flagging since it could pick the
  wrong swing animation in `toon.js`'s per-style clip selection.
- **Carrion Edge's `Line`**: set to `None` (not `Warbound`) on the
  reasoning that rares sit outside the shop-ladder armour-line identity/
  set-bonus system (items doc §5 frames line bonuses as a shop-ladder
  mechanic). Not stated either way in the doc.
- **Flask "one-time unlock" (A.5, now resolved rather than just
  flagged)**: turns out to need no new mechanism at all. Confirmed by
  reading `FlaskSlotState`/`BindFlaskSlotHandler`: sips are duel-scoped
  and never remove the flask from inventory, and binding already checks
  `player.HasItem`. So buying a flask once *is* already a permanent
  unlock under the existing model — the Shop UI just marks it "(owned)"
  and lets a re-buy through (harmless, just wastes gold) rather than
  blocking it outright.
- **Purchase confirm threshold**: implemented at the plan's proposed
  2,500g (T2 price boundary) — proposed, not independently re-confirmed
  by a human before landing. Easy to change (`ShopSheet.razor`'s
  `ConfirmThresholdGold` constant) if wrong.

---

## Small refactors made along the way (behavior-preserving)

- `GameTickService.ComputeLineDamageBonus`/`MaxSpecialEnergy` made
  `public` (were `private`) so the new equipment stat sheet (C.2) can
  reuse the exact combat formulas instead of re-implementing them —
  `ComputeLineDamageBonus` now delegates to a new public
  `GetLineDamageBonusPreview(player, GearLine)` that does the same
  per-piece counting, just parameterized on a line instead of requiring a
  real equipped weapon. Pure extraction; combat behavior unchanged (all
  101 tests, including the pre-existing ones exercising this exact math,
  still pass).
- `IItemRepository` gained `GetShopPrice(itemId)` (single-item lookup) so
  `BuyItemHandler` doesn't have to scan `GetShopItems()`'s full list.
- `Player.BagCapacity` (28) is now a named public const, sourced-commented
  to UI bible §7, and both `GameTickService.RollLoot` and
  `InventoryGrid.razor` reference it instead of repeating the literal.

---

## The starting-gold / cold-start gap (carried forward from the pre-plan pass, now sharper)

Already flagged in `m1-findings.md`'s addendum: `Player.Gold` starting
value is now `0` (was an unattributed 10,000). Implementing the Shop this
session makes the consequence concrete: with **no separate regular-duel
opponents** (ratified) and **0 starting gold**, a first-time player's only
income source (Maggot King, 500g/kill) requires gear to realistically
survive long enough to land that first kill — gear only obtainable *from*
that same gold. The dev-loadout buttons (visible in the hub for anyone,
not gated behind a build flag) are the only working bootstrap today, same
as they've been since M1's own playtest. This is a real, unresolved
product question — not something this session should resolve by inventing
a starting weapon or gold seed — flagging again because it's no longer
hypothetical: the Shop that would let a player spend a starting seed now
actually exists.

---

## Not touched (explicitly out of scope for this pass, per `m2-plan.md` §11)

Other bosses and their items, invocations/raid level, collection log/
profile/leaderboards, the cosmetic/transmog tab, wagers/win-streaks.

---

## Batch 1 addendum: `m2-backlog-resolutions.md` implementation findings

This addendum covers implementing the user-authorized backlog batch 1 (10
sections: unique stats, Maggot King loot rows, shard mechanism, cold start,
XP ruling, economy edits, M1/M2 constant ratification, constants sweep
follow-up, landscape-everywhere mandate). The plan is
`m2-backlog-resolutions.md` itself (pre-written, pre-authorized verbatim by
the user); this is what happened turning it into code.

- **Loot table model needed a real extension, not just data entry.** The
  existing `LootEntry` was flat independent-roll only. Batch 1 §2's
  "Common 65% / Uncommon 25%, weighted members within each" required a
  genuine two-stage model: `LootEntry` gained `GroupId`/`Weight`; `RollLoot`
  now rolls the group's own chance once, then does a weighted pick among
  its members — kept 100% backward compatible for ungrouped entries
  (verified by the pre-existing suite staying green plus 4 new
  `RollLootTests`). This is real engine work the resolution doc's one-line
  loot-table content undersold.
- **The "gold" pseudo-item convention is new and load-bearing.** Maggot
  King's Common group's "Gold Cache" row grants currency directly rather
  than an inventory item — `RollLoot`/`ResolveLootHit` special-case the
  literal item id `"gold"` before touching the bag. Documented inline on
  `LootEntry` since nothing in the loot pipeline previously needed to
  distinguish "this row is not a real item."
- **Shard auto-combine needed its own state, not reuse of an existing
  currency/stacking system.** `ShardToFlask` mapping + `ShardsRequiredForFlask
  = 5` + `SurplusShardGold = 100` constants live in `GameTickService`;
  the "already unlocked → shards become gold" branch and the "still
  collecting → auto-combine at 5" branch share the bag-full-fencing path
  `RollLoot` already had for normal items, extended rather than
  restructured.
- **Rotward's "poison immunity" needed a new `GameState` field.** No
  existing status-immunity framework existed to hook into, so this shipped
  as the narrowest fix: `PoisonImmuneTicksLeft` + `GrantPoisonImmunity`/
  `TickPoisonImmunity`, gated at the single call site
  (`ProcessHazardResolution`) where environmental poison actually applies.
  Not generalized to other status effects — nothing else needs it yet.
- **Boss-side poison DoT (Rotfang's on-hit effect) is a new symmetric
  system, not reuse of player poison.** `NpcInstance` gained its own
  `PoisonStacks`/`PoisonDurationTicksLeft`/`TickPoison()` — mirrors the
  player-side shape but is a separate field set since `NpcInstance` and
  `Player` don't share a status-effect base type. Ordering matters: the
  tick's damage must be read (`PoisonDamagePerTick`) *before* calling
  `TickPoison()`, since that call zeroes `PoisonStacks` as a side effect on
  the poison's final tick — got this right first try, confirmed by 4
  `RotfangPoisonTests`.
- **Cold start's 600g default broke 3 existing tests** that assumed a
  `0`-gold player (`RollLootTests`' gold-pseudo-item assertion, and two
  `BankAndShopHandlerTests` cases relying on exact post-spend gold /
  insufficient-funds behavior). Fixed by updating the expected value in one
  and adding a `ZeroGold(player)` helper called at the top of the other two
  — not a sign of a design problem, just tests that had baked in the old
  default.
- **Style-picker FTUE required a `StartGameCommand` signature change**
  (`ChosenStyle` param threaded through `StartGameHandler`, which now
  grants + equips + binds the T1 weapon for that style to loadout slot 0)
  rather than a purely cosmetic front-end change — the free weapon has to
  actually exist in the new player's bag/loadout, not just be implied by
  the copy.
  - Hit one real Razor syntax trap along the way: `@onclick="() =>
    _chosenStyle = \"Melee\""` doesn't compile (backslash-escaping a
    string literal inside a double-quoted Razor attribute delimiter is
    invalid) — fixed by switching to single-quoted attribute delimiters
    calling a named `SelectStyle(string)` method instead of an inline
    lambda with an embedded string literal.
- **Landscape-everywhere mandate: which screens needed real refactoring
  vs. just widening.**
  - **Hub menu**: real refactor, `flex-direction:column` → CSS Grid
    2-column (`repeat(2,1fr)`), with the Fight/Group/Anim-editor cards
    spanning both columns via `grid-column:1/-1`.
    - **Bag**: real refactor, single vertical column → `bag-left-col`
    (paperdoll + stat sheet + compare card) as a sibling of the inventory
    grid, `.bag-body` switched from column-flex to row-flex, panel widened
    to `min(760px,94vw)`.
  - **Loadout Editor**: widened only (480px → 640px `max-width`), *not* a
    true left/right split. The resolution doc's own framing ("left/right
    panel splits ... never a single vertical column") isn't fully met here
    — Action Bar (4 slots) and Flask Belt (2 slots) still stack vertically
    within the widened panel rather than sitting side-by-side. Flagged as
    a real follow-up in `backlog.md` rather than risked unverified in this
    pass, since a genuine split would need new markup/CSS structure beyond
    what a width bump covers.
  - **Shop, Bank, Character sheet**: already reasonably landscape-shaped
    (grid-based item lists / short vertical forms that fit inside the
    500px-tall viewport this app targets) — no structural change needed,
    confirmed by browser verification rather than assumed.
- **`RotateOverlay` is pure CSS** (`@media (orientation: portrait)`,
  `position:fixed; inset:0; z-index:1000`) — no JS/interop, matching the
  architecture invariant that Three.js/JS never makes gameplay or layout
  decisions. It's mounted once per non-combat branch of `Game.razor` (and
  once in `NewGame.razor`), explicitly never inside `.battle-fs`, so
  combat's pre-existing CSS auto-rotate trick is untouched.
- **Verification caveat — Playwright and the overlay's own correctness
  interact in a way worth recording.** Real automated `page.click()`/
  `page.fill()` calls respect actual browser hit-testing, so in a portrait
  viewport they get intercepted by the overlay exactly like a real
  finger-tap would — that's the feature working, not a test failure, but
  it meant the first three verification scripts (`probe3`–`probe7`)
  legitimately couldn't drive the app past the New Game screen in portrait
  and produced misleading "element not found" timeouts. Confirmed this by
  dispatching a JS-level synthetic click/input directly on the underlying
  elements (bypassing hit-testing on purpose, simulating "what's
  underneath still works") — reached the hub and combat screens fine with
  no console errors, and combat's `.battle-fs` auto-rotate rendered
  correctly. Landscape-viewport screenshots (Hub grid, Shop, Bag split,
  Loadout Editor) were captured with real (non-bypassed) clicks and match
  the intended layouts. Not exercised: an actual mobile device or a
  Playwright device emulation profile with real touch events — viewport
  resize was used as the orientation signal, consistent with how the CSS
  `@media (orientation: ...)` query itself works, but device-level touch
  behavior wasn't separately verified.
- **Full test suite**: 115 tests across `Duels.Domain.Tests` (21),
  `Duels.Application.Tests` (51), `Duels.Infrastructure.Tests` (43) — all
  passing after batch 1, including the 3 fixed by the cold-start default
  and 9 new tests added for this batch (4 loot-group, 4 Rotfang poison, 1
  style-start theory set). `dotnet build`: 0 warnings, 0 errors.

### Same-session follow-up: RotateOverlay replaced with auto-rotate

Immediately after batch 1 landed, explicit user instruction: remove the
blocking "rotate your device" prompt entirely — portrait should auto-rotate
everything to landscape, matching what combat already did, not ask the
player to physically turn their phone.

- `RotateOverlay.razor` deleted; its two mount points (`Game.razor`,
  `NewGame.razor`) removed.
- `Game.razor`'s non-combat branch (hub + all 5 modal sheets) is now
  wrapped in a `<div class="hub-shell">`. The CSS trick mirrors
  `.battle-fs` exactly: `display: contents` outside portrait (so it's
  invisible to `.game-shell`'s flex layout — zero behavior change in
  landscape/desktop, confirmed by re-running the landscape Playwright pass
  and diffing against the pre-follow-up screenshots), then
  `position:fixed; inset:auto; top:0; left:100vw; width:100vh;
  height:100vw; transform-origin:top left; transform:rotate(90deg)` inside
  `@media (orientation: portrait)`.
- `.new-game-screen` got the identical portrait-only override (it didn't
  need a `display:contents` wrapper since it's already the page's sole
  root element, not nested in a shared flex parent).
- Verification caveat carries over from the original batch-1 pass: real
  Playwright `page.click()`/`page.fill()` in a portrait viewport still hit
  whatever element occupies that screen coordinate at the time of the
  click, so mid-transition-state clicks can behave unpredictably during
  the CSS transform if not waited for; this wasn't hit in manual
  re-verification but wasn't stress-tested either. Confirmed via
  screenshot: portrait viewport (400×800) after reaching the hub shows
  the hub grid rendered sideways, filling the rotated 800×400 logical box,
  fully interactive (no fixed-overlay interception) — the intended
  behavior. Combat's own `.battle-fs` rotation is untouched (separate,
  mutually-exclusive sibling branch; `.hub-shell` and `.battle-fs` never
  render at the same time).
- `backlog.md`'s batch-1 Resolved entry for the landscape mandate was
  amended in place with a "superseded same session" note rather than
  rewritten, so the original RotateOverlay decision stays visible as a
  record of what actually shipped first. CLAUDE.md's landscape rule text
  updated to describe auto-rotate instead of a rotate prompt.

### Second same-session follow-up: fullscreen button, then reverting auto-rotate back to RotateOverlay

- **Fullscreen button added** to the New Game screen: a small `⛶`
  icon-button top-right, calling a new `window.toggleFullscreen()` JS
  interop function (standard + webkit-prefixed Fullscreen API, try/catch
  fallback matching the existing `hapticPulse` pattern). Verified working
  in Chromium (`document.fullscreenElement` becomes truthy on click).
  **Caveat surfaced immediately by the user on a real device**: iOS
  Safari and Firefox-for-iOS don't implement the Fullscreen API for
  arbitrary DOM elements at all (only `<video>` can go fullscreen on iOS,
  via `webkitEnterFullscreen`) — this is an Apple/WebKit platform
  limitation, not something fixable in JS. The button silently no-ops
  there (try/catch swallows the failure) rather than erroring, but it
  doesn't do anything useful on iOS either. Left as-is: correct/working
  on Chromium-family browsers, harmless no-op on iOS.
- **Auto-rotate reverted back to RotateOverlay.** The prior follow-up's
  CSS-transform auto-rotate (`.hub-shell` + `.new-game-screen` fixed +
  `rotate(90deg)`) looked correct in Playwright (content fit the test
  viewport with no overflow) but the user hit real breakage on an actual
  device: "every ingame menu and even the start screen is overflowing
  badly, sometimes it's not even possible to scroll up." Root cause,
  worked out analytically and confirmed against the CSS: rotating an
  ancestor 90° doesn't change how its descendants compute layout (layout
  happens pre-transform, in "local" coordinates) — it only repaints the
  whole subtree rotated. A `rotate(90deg)` maps local point `(x,y)` to
  screen `(-y,x)` relative to the transform origin, so **local +X maps to
  screen +Y (down) and local +Y maps to screen -X (left)**. Combat's HUD
  never hit this because every element in `.battle-fs` is
  absolutely-positioned with small corner offsets — those look fine near
  either corner regardless of which local axis is "screen-down" after
  rotation. But `.new-game-screen`/`.hub-shell`'s children are normal
  flowing/stacking content (a form, panel lists, an inventory grid): the
  default flex `row` main-axis (local X) is the one that maps correctly to
  "visual vertical," but each screen's actual content stacks along local
  Y (their own internal flex-columns) — which maps to *screen-horizontal*
  after rotation. Content taller than the rotated box's cross-axis
  bleeds sideways off-screen, and the `overflow-y:auto` that would
  normally fix it scrolls the *local* Y axis, which is horizontal on
  screen post-rotation — so a vertical swipe doesn't scroll it. This
  isn't a one-line fix: it affects every scrollable descendant panel
  bundled inside the rotated subtree (Bag's inventory grid, Bank's panes,
  Shop's list, Loadout's action bar), not just the outer screen, since the
  whole subtree rotates as one paint layer. Fixing it properly would mean
  reworking each panel's internal stacking axis to swap with orientation
  — high risk of missing a panel, not attempted.
  - Reverted: `RotateOverlay.razor` recreated (deleted, then recreated
    identical), remounted in `Game.razor`'s non-combat branch and
    `NewGame.razor`; `.hub-shell` wrapper and both portrait `rotate(90deg)`
    overrides removed from `terminal.css`; `.rotate-overlay` CSS restored.
    Combat's own `.battle-fs` auto-rotate was never touched by either
    follow-up — it doesn't have this problem since it never scrolls.
  - Re-verified via Playwright: portrait shows the rotate overlay again
    (blocking, as originally shipped); landscape hub/loadout/bag screens
    render identically to the pre-regression screenshots, no overflow.
- **Distribution decision surfaced as a real, overdue gap** (added to
  `backlog.md` as item #34): the implementation brief flagged
  "browser/PWA vs. store wrapper (e.g. Capacitor) — decide by M1's
  playtest" and it was never actually decided or carried forward as an
  open backlog item. This session's back-and-forth (CSS auto-rotate vs.
  rotate-prompt, fullscreen button vs. iOS's total lack of support for it)
  is exactly the kind of decision that question was meant to prevent
  re-litigating ad hoc: a Capacitor-wrapped app gets OS-level landscape
  lock and real fullscreen for free, making both the in-browser fullscreen
  button and the rotate-prompt dev/playtest-time-only affordances rather
  than permanent product UX. Recommended (not yet decided by a human):
  Capacitor wrapper for the App Store target, per the user's stated intent
  to ship there.
- **Full test suite re-verified**: 115/115 passing, `dotnet build`: 0
  warnings, 0 errors, after both the fullscreen-button addition and the
  auto-rotate revert.

### Third follow-up: the whole landscape-everywhere mandate reverted

Explicit user instruction: remove the forced-horizontal UI for menus
entirely and restore the exact pre-batch-1 behavior — "if phone vert menu
vert, if horizontal menu's horizontal." Even the rotate-prompt (the second
follow-up's fallback) was rejected: blocking a playtester from reaching the
Hub/Bag/Bank/Shop/Loadout until they physically rotate their phone made
those screens *worse* to test, not better, especially since the landscape
refactor itself only partially delivered on the UI bible's actual specs
(Loadout Editor never got the doc's literal "left: bar, right: list" split
— see the original batch-1 addendum's flag on this).

- Reverted, file by file, against the pre-batch-1 commit (`9796341`, the
  parent of batch 1's `2a48ab6`) so the restored CSS/markup matches exactly
  what shipped before any of this session's landscape work, rather than an
  approximation:
  - `.hub-menu`: `display:grid;grid-template-columns:repeat(2,1fr)` → back
    to `display:flex;flex-direction:column`; removed the
    `.hub-card-fight/-group/-anim-editor { grid-column:1/-1 }` span rule.
  - `.bag-panel`: `width:min(760px,94vw);max-height:92dvh` → back to the
    shared `.bag-panel, .char-panel { width:min(480px,96vw) }` rule;
    `.bag-left-col` deleted; `BagSheet.razor`'s left-column wrapper div
    removed, paperdoll/stat-sheet/compare-card/inventory-grid restored as
    flat siblings in `.bag-body`; `.bag-body` reverted from
    `flex-direction:row` back to the shared `.bag-body, .char-body { ...
    flex-direction:column }` rule.
  - `.loadout-panel`: `max-width:640px;width:min(640px,94vw)` → back to
    `max-width:480px`.
  - `RotateOverlay.razor` deleted outright this time (not recreated) —
    both mount points (`Game.razor`'s non-combat branch, `NewGame.razor`)
    removed along with the now-unused `@using Duels.Web.Components.Hud` in
    `NewGame.razor`. The `.rotate-overlay`/`.rotate-overlay-icon`/
    `.rotate-overlay-text` CSS block and its keyframes deleted from
    `terminal.css`.
  - Kept (not part of the landscape mandate, unrelated features from the
    same batch-1 commit or later): the cold-start style-picker FTUE and
    its `.style-pick-*` CSS, the fullscreen button and its `.fullscreen-
    btn` CSS, `flask_rotward` in `LoadoutEditor.razor`'s `KnownFlasks`, the
    dev-gate `PROVISIONAL` comment in `HubMenu.razor`.
  - `.battle-fs` and its portrait auto-rotate media query: untouched by
    any of the three follow-ups in this whole saga. Combat has never had
    this problem (no scrolling content) and was never the thing being
    reverted.
- Re-verified via Playwright: `.rotate-overlay` selector matches zero
  elements in the DOM in either orientation (not just hidden — genuinely
  gone). Portrait (400×800) New Game, Hub, and Bag all render as natural
  single-column layouts with no overflow and no rotation. Landscape
  (900×500) Hub renders as the original single-column list, identical in
  structure to the pre-batch-1 layout. Combat (`.hub-card-fight` →
  `.battle-fs`) still auto-rotates to fill a portrait viewport sideways,
  confirming the revert didn't touch it. Zero console/page errors across
  all passes.
- `backlog.md`'s landscape-mandate Resolved entry gained a third,
  final amendment documenting this (the entry now records all three states
  batch-1's landscape work passed through: RotateOverlay → CSS auto-rotate
  → RotateOverlay again → fully reverted). Item #34 (Distribution) updated
  to note portrait is no longer blocked at all in the browser build now,
  only the fullscreen button remains a provisional affordance.
  CLAUDE.md's orientation rule rewritten a third time to state the final,
  actual behavior plainly and tell future work not to re-attempt either
  prior approach without being asked.
- Full suite re-verified again after this revert: 115/115 tests passing,
  `dotnet build`: 0 warnings, 0 errors.
