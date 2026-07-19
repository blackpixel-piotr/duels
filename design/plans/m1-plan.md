# M1 — Vertical Slice: Technical Plan

Status: **draft — awaiting review**. Implementation starts only after this plan is approved (working agreement #1). Findings will be recorded in `m1-findings.md` as implementation proceeds, per the milestone workflow.

Scope (implementation brief, M1): new HUD per UI bible §3 · RS3-style action bar + minimal Loadout Editor (§4) · flask belt system · **Maggot King rebuilt to full Boss Bible choreography** · death→instant retry · basic victory/loot screen · dev loadout presets (T1/T2) via debug menu.
**Excludes:** bank, shop, other bosses, invocations, minigame, drops (M2).

Playtest question: **is fighting Maggot King on a phone *fun* — flicking, dodging, sipping under pressure?** If no, stop and tune before building anything else.

Carried-in resolutions from M0: **Q1** — the game switches to the items-doc combat math (Power/Precision, 100 HP, 80% base hit), landing in M1 with this rebuild; OSRS formulas are not preserved. **Q2** — player moves 2 tiles/tick (unchanged).

---

## 1. Current-state audit (what M1 builds on)

| Area | Today | Gap vs M1 target |
|---|---|---|
| Combat math | OSRS formulas in `Domain/Services/CombatCalculator.cs`; XP/levels drive stats; HP = Hitpoints level; spec +10 per attack | Doc math: 100 HP, 80% base hit, weapon Power/Precision, armour Def points (0.4%/pt, 40% cap), style mods ±10/20%, spec regen 2/tick out of danger / 1 in combat |
| Boss framework | `NpcTemplate` + hard-coded per-boss branches in `GameTickService` (warlord/berserker/champion by id); single `HazardProfile` (random-cooldown eruptions + pools); single 1v1 target; 11×11 arena, both combatants 1×1 and mobile | Data-driven rotation script (fixed tick loop), positional style choice, pools→**scorch** lifecycle, channeled cast (Rot Burst), punish window, **adds (swarms) + target switching**, Perfect Dodge, 2×2 stationary boss, 9×9 arena |
| Maggot King | `maggot_king` npcs.json row: style rotation every 3 attacks, generic hazard waves, phase-2 frenzy | Full §1 choreography: 20-tick P1 loop / 14-tick P2 loop, eruption timer 16→12 ticks, swarms at 50/25%, Rot Burst + scorch inversion, 5-tick punish slump |
| Consumables | Farmable food/potions in inventory (`EatItemHandler`/`DrinkPotionHandler`, tick delays), hard-coded belt buttons in `ActionHud` | Flask belt: 2 bound slots, fixed sips (baseline 3), sip consumes the tick's action, free refill per attempt |
| HUD | Portrait single-column shell (`Game.razor`, max-width 520px): StatusStrip top, EventLog middle, ActionHud bottom rows | UI bible §3 landscape: full-bleed battlefield, prayer arc left / weapon arc right, orbs top-left, boss plate + pips + forecast top-center, cast bar, flask belt, engagement indicator, doctrine-color tokens |
| Weapon slots | `WeaponShortcutCommand` per owned weapon; M0 one-swap-per-tick buffer | 4-slot manually-bound action bar + minimal Loadout Editor; bar locked mid-fight |
| Result screens | `DuelResultOverlay` (win/lose, gold/xp/loot, retry button) | §8.5 death: "what killed you" + instant Retry; §8.4 victory basics (kill time; fanfare polish deferred) |
| Persistence | M0 `ISaveStore`/IndexedDB, `SaveEnvelope` v1 | v2: action bar, flask belt bindings |
| Renderer | `toon.js` hazard tiles (warn/pool), 1v1 actors, M0 asset manifest | Telegraph hatch-fill + **white-flash final tick**, scorch/safe-gold tiles, swarm actors, 2×2 boss scale, punish/slump visual |
| Dev loadout | `StartTestFightHandler` force-gives PoC ranger set | Debug menu granting T1/T2 doc presets with bound bars + flasks |

---

## 2. Workstream A — Combat math v2 (the doc model)

Items doc §1 + boss bible grammar become the game's combat model, replacing OSRS math **globally** (single model; see D1 on what this does to the legacy ladder).

- **Player baseline:** `MaxHp = 100` flat. Base hit chance 80%. Attack styles: Accurate +10% hit · Aggressive +20% damage, −10% hit · Defensive +20% defence value, −10% damage.
- **Weapons:** `Power` = damage per hit (flat, no roll-to-max ramp — damage variance comes from hit/miss and mechanics, not a 0..max roll), `Precision` = flat hit-chance bonus. **Attack tempo (D2 resolved):** weapons and enemies keep unique per-item/per-enemy tick delays (1, 2, 3, 4…) — the existing `AttackSpeed`/cooldown machinery carries over into the new math unchanged; "1 attack per tick" in the grammar is *not* the model. The doc's weapon tables carry no speed column, so per-archetype speeds are proposed as tunables (see B).
- **Armour:** Def points; each point reduces incoming damage of its matching style by 0.4%, capped at 40% from gear. Line identity bonus (+1% line-style damage per piece), set bonuses (4pc → +5% style damage, 6pc → +10 max special energy).
- **Special energy:** max 100 (+gear bonuses); regen 2/tick out of danger, 1/tick in combat (replaces +10-per-attack).
- **Protection prayers:** keep 75% reduction and tick-start flick semantics unchanged (the bible's flicking grammar is the point of this boss).
- **Implementation:** new `Domain/Services/DamageModel` (+ snapshot types carrying Power/Precision/Def/style) replaces `CombatCalculator` at the call sites in `GameTickService`. Old OSRS calculator and `ItemModifiers`-based aggregation are deleted once nothing references them (cleanup rule). NPC attacks express their damage directly as **band values from the boss definition** (see C), not attack/strength rolls.
- **XP/levels:** under doc math, Attack/Strength/Defence/Hitpoints levels have **no combat effect**. They keep accruing and displaying (nothing removed in M1), but are vestigial — their fate is D3.

## 3. Workstream B — Doc-item ingestion + dev loadouts

Data commits through the M0 pipeline, plus the six T1/T2 special attacks as real effects.

- **items.json gains a `docStats` block** per the M0 schema plan: `power`, `precision`, `defPoints`, `line`, `tier`, `special {id, cost, params}`. Rows added *verbatim from the items doc*: `wpn_melee_t1/t2`, `wpn_ranged_t1/t2`, `wpn_magic_t1/t2` (Rustcleaver, Poacher's Bow, Cinder Wand, Splitter, Bolt Thrower, Hexknot Staff) and the T1/T2 armour of all three lines (Warbound/Stalker/Occult × body/legs/helmet/boots/gloves/cape, Def per §5's table), plus `flask_health`, `flask_prayer`.
- **Specials implemented (6):** Lunge (25 — next hit connects from 2 tiles, closes the gap), Snipe (25 — +50% damage single shot), Scorch (25 — hit + 3-tick burn DoT), Rend (40 — heavy hit + bleed stack), Pin Shot (40 — boss's next action delayed 1 tick), Sap (40 — boss damage −10% for 5 ticks). Burn/bleed reuse the existing DoT plumbing; delay pushes the rotation cursor; Sap is a timed damage-debuff on the boss. Effects live in a small `SpecialEffectId` dispatch in the tick service — shared machinery, not per-weapon code.
- **Ranged/magic weapon ranges:** bows/crossbows/wands/staves need real `range` values — the doc doesn't state tile ranges. Proposed: melee 1 / ranged 7 / magic 7 (current `AttackRange.ForStyle` values), flagged as tunable (T-list).
- **Weapon attack speeds (per D2):** the doc tables have no speed column, so archetype-based tick delays are proposed as data, tunable: T1 shortsword/shortbow/wand = 3 · T2 battleaxe/crossbow/staff = 4 (heavier archetype, higher Power-per-hit justifies the extra tick). Only T1/T2 ship in M1.
- **Legacy consumables deleted (D5 resolved):** shark/karambwan/anglerfish/super-combat-potion/antidote item rows are removed from items.json along with `EatItemCommand`/`DrinkPotionCommand`, their handlers, and the old HUD belt buttons; ladder NPC loot-table entries referencing them are stripped (those NPCs just drop their gold/uniques). The flask belt (E) is the only in-duel consumption.
- **Dev debug menu (Web, dev-only panel):** two one-tap grants — **T1 preset** (3 T1 weapons on the bar, full T1 armour of the matching line for the playtester's pick — default Warbound, Health + Prayer flasks on the belt) and **T2 preset** (same at T2). Implemented as a `GrantDevLoadoutCommand`; no shop, no bank, no drops.

## 4. Workstream C — Shared boss systems (working agreement #4)

Everything the Maggot King needs, built as reusable systems keyed from boss definition data — never code in a boss class. New state lives in `GameState`/`NpcInstance`; behavior in focused Application services the tick service calls in a fixed order.

1. **Rotation script engine.** A boss definition carries `rotation: [{tick, action}]` with a loop length per phase. Actions reference named attacks (`bile_spit`, `lash_or_volley`, `style_telegraph`, `idle`). The engine advances a tick cursor, resolves the current action, and exposes "what's coming in N ticks" for the forecast widget. Positional attacks (`lash_or_volley`) pick melee-if-adjacent / ranged-if-not at resolution time.
2. **Attack/damage banding.** Boss attacks are defined as `{style, band}` with band → damage from the grammar (relative to player 100 HP): Light 5–10, Medium 15–20, Heavy 30–40, Severe 50–60. Concrete per-attack numbers are boss-definition data within the band (T-list; starting values proposed in §5).
3. **Telegraph framework.** Unifies style-shift telegraphs (2 ticks, colored glow + forecast icon) and tile telegraphs (fuse ticks, hatch fill per tick, **final tick = white flash** — the universal Perfect Dodge cue). Exposes telegraph state in the render snapshot so HUD + battlefield both read it.
4. **Hazard tiles v2.** Extends M0's warn→pool lifecycle with the third state: **pool expires → scorch** (permanent for the fight, walkable, no damage). Tile kinds carried in the snapshot: `warning(fuse, flashNow)`, `pool`, `scorch`, `safe-gold` (scorch during Rot Burst).
5. **Channeled casts.** `{castTicks, resolution}` — boss becomes "inhaling" for N ticks (cast bar UI, tick-segmented), then resolves (Rot Burst: arena-wide Severe, unprayable, **negated on scorch tiles**).
6. **Punish windows.** Timed boss state: `+25% damage taken, cannot act` for N ticks, set by mechanics (post-Rot-Burst slump 5 ticks). Visible in snapshot (slump pose + HUD cue).
7. **Adds (swarms) + targeting.** New lightweight `AddInstance { id, tile, hp, contactEffect }` list on `GameState`. Movement: 1 tile/tick toward player; contact applies effect (bleed stack) . Targeting: tap an add → `TargetId` switches; player attacks resolve against the current target (adds die in 2 hits — HP 2, player hits always ≥1 damage vs adds). Boss remains default target; killing an add reverts targeting to the boss.
8. **Perfect Dodge.** If the player's tile at tick start was a final-fuse tile and their tile at hazard resolution is not in the erupting set → +15 special energy + gold glint event. Never required to survive; always rewarded.
9. **Boss footprint + arena.** `NpcTemplate` gains `footprint` (1×1 default, 2×2 for the King) and `arena {radius, bossAnchor, mobile}` — the King sits on his 2×2 mound at center-north of a **9×9** arena and pivots instead of walking. Adjacency/melee-range checks and pathfinding treat all footprint tiles as the boss. `GameState.ArenaRadius` becomes per-duel data.
10. **Kill/fight stats.** Fight timer (ticks→kill time) and `KilledBy` (last damaging attack's display name + mechanic name) recorded for the result screens.

## 5. Workstream D — Maggot King choreography (data)

The King's npcs.json row is rewritten as the first full boss definition consuming §4's systems — numbers straight from the bible, damage values chosen inside their bands (all T-list):

- **P1 (100–50%), 20-tick loop:** T0/T4 Bile Spit (magic, Medium=18) · T8 style telegraph (2t) · T10/T14 Lash (melee, adjacent) / Grub Volley (ranged) (Medium=18) · T16 style telegraph · T18–19 idle.
- **Eruption timer (independent):** every 16 ticks, 3 random tiles + player tile, 3-tick fuse, Heavy=35 unprayable; pools 20 ticks (Light=6/tick + poison stack) → expire to scorch.
- **P2 (<50%):** loop compresses to 14 ticks; eruptions every 12 ticks, 5 tiles, pools 30 ticks. Swarms ×2 at 50% and 25% (corner spawns, 1 tile/tick, contact = bleed stack, 2 HP). **Rot Burst** every ~40 ticks: 4-tick inhale → arena-wide Severe=55 ignoring prayer, safe on scorch → 5-tick slump punish window.
- **Boss HP:** not specified anywhere in the docs — required tuning input. Kill-time target ~3–4 min at T1 gear (approved, D4). The originally proposed 1,400 assumed 1 attack/tick; with D2's resolved tempo (T1 speed-3 weapons: Power 10 @ 80% hit ≈ 2.7 dmg/tick, ~40% downtime → ≈ 1.6/tick sustained) the same target gives **450 HP** as the starting value — expressly provisional until the playtest (T-list).
- **Choreography tests** in the MaggotKingTests style: rotation timeline (attack styles land on the scripted ticks), eruption cadence, pool→scorch conversion, Rot Burst safe-tile negation + punish window, swarm spawn thresholds and contact bleed, Perfect Dodge energy grant, P2 compression.

## 6. Workstream E — Flask belt system

Per the decisions doc (resolved) and UI bible §3.2:

- **`FlaskBelt` on `GameState`** (persisted bindings on the save): 2 slots bound in the Loadout Editor; each slot = flask id + sips remaining. Baseline 3 sips per fight; **hub refills are free and full on every duel start**.
- **Sip semantics:** `SipFlaskCommand` — consumes the player's action for that tick (sets the same cooldown an attack would; UI dims attack inputs for that tick), restores its resource. Health restores 40 HP, Prayer restores 40 points — unspecified in docs, T-list.
- **Legacy food/potions (D5 resolved — delete):** the flask belt fully replaces in-duel consumption; handlers, commands, HUD buttons, and the item rows themselves are removed (details in B). Boss damage budgets assume ~3 Health sips (decisions doc tuning note).
- Rotward/Coagulant/Fire Ward are M2+ drops — schema supports them (flask rows are items), content excluded.

## 7. Workstream F — HUD rebuild (UI bible §3, functional pass)

Landscape-only combat layout replacing the portrait shell for duels. Architecture requirement: **all styling through design tokens** — a `tokens.css` with the doctrine palette (melee red-orange, ranged green, magic blue, hazard purple+hatch, safe/reward gold, poison yellow-green, bleed crimson) + semantic classes; interim look neutral but doctrine colors correct.

- **Layout:** full-bleed battlefield; safe-area-inset anchored corners. Left thumb arc: 3 protection prayers (64dp) + boost prayer (56dp). Right thumb arc: 4 weapon slots (56dp, style-colored frames, active = enlarged + gold underline) + special attack button (72dp, radial energy fill) at thumb rest. Style toggle (3×44dp segments) + flask belt (2×56dp with sip pips) beside it. 8dp minimum spacing.
- **Top:** HP + prayer orbs (numeric, radial drain, red vignette <30% HP) with debuff icons beneath (poison/bleed with tick-wipe); boss plate (name, HP bar, **phase pip at 50%**) + **style forecast widget** (2-tick telegraph echo, doctrine color + shape, default ON); cast bar (tick-segmented, Rot Burst inhale) under the plate.
- **Battlefield interaction:** tap tile = move (path dots), tap boss/add = engage/target, tap elsewhere = disengage-move; invalid tile = red X stamp. Telegraph tiles: purple hatch fill per fuse tick, white-flash outline on final tick; pools poison-green; scorch neutral dark; scorch glows gold during Rot Burst.
- **States:** engagement indicator (reticle on target, "sheathed" icon when disengaged); out-of-combat dim (60%); button states (inactive/active/insufficient — grayed+hatched); sip dims attack inputs for the tick; 50ms tap acknowledgment (visual immediately on pointerdown, before dispatch).
- **Kept from today:** tick metronome (becomes the §3.2 optional pulsing dot, default off), hitsplats/toasts. **EventLog** is demoted to a collapsed drawer (debug value; not in §3's layout) — D6.
- **Deferred (per brief):** inked frames/halftone/comic fonts, HUD edit mode, left-handed mirror, hold-to-pray, settings screen; portrait gets a "rotate your device" overlay rather than a responsive portrait combat layout.
- Out-of-duel screens (hub menu, bag, stats) keep the current portrait shell untouched this milestone.

## 8. Workstream G — Action bar + minimal Loadout Editor (§4)

- **Model:** `Loadout { WeaponSlots: string?[4], FlaskSlots: string?[2], DefaultStylePerWeapon }` on the player, persisted (SaveEnvelope **schemaVersion 2**; v1 saves migrate with an empty bar). Manual binding only — acquiring a weapon never auto-fills a slot.
- **Minimal editor screen (out of combat):** 4 large slots + owned-weapon list (filter by style); tap-to-bind/tap-to-clear (drag optional, tap is the guaranteed path), slot reorder, flask belt binding from owned flask types, per-weapon default attack style. **Deferred from §4.1:** named presets ×5, per-boss "last used" chips (roster screen is M3), long-press stat cards.
- **Rules enforced:** bar locked when the fight starts; wielded weapon's stats only; in-duel weapon buttons now source from the bar (empty slot = dashed outline, dead in combat, opens editor out of combat). The M0 swap buffer applies unchanged.

## 9. Workstream H — Death→retry + victory screens

Extend `DuelSummary` with `KillTimeTicks`, `KilledBy`; refit `DuelResultOverlay`:
- **Death (§8.5 basics):** DEFEATED stamp, *what killed you* (attack + mechanic name from the boss definition — deaths teach), fight stats, **Retry front-and-center** (restarts the same duel instantly — flasks refill, arena resets; one tap from corpse to pull).
- **Victory (§8.4 basics):** kill time + personal best (per-boss best stored on the save), gold/xp rows as today; Re-fight / Leave. Chest-burst fanfare, rare-drop ceremony deferred (no drops until M2).

## 10. Workstream I — Renderer extensions (toon.js)

All via the existing one-batched-snapshot path — no new per-property interop (reserved decision R2 upheld): tile kinds (warn-hatch levels, white flash, pool, scorch, safe-gold), add actors (small placeholder blobs at tile positions, HP pips), 2×2 boss (scaled model on the mound, pivot-only facing), inhale/slump poses (reuse existing clips: cast → inhale, hit-big → slump), punish-window tint, Perfect Dodge gold glint at player's feet, damage numbers per §3.3 (player white w/ style outline, taken red drifting down, DoT small/muted).

---

## 10b. Workstream J — Ladder retirement sweep (D1)

The decisions doc already retires these; D1 authorizes doing it now. Removed in one sweep alongside A/B:
- **NPCs:** every npcs.json row except `maggot_king` (goblin → champion, rare_tourist, rare_gladiator) and the `Ladder` progression array + unlock flow in `GameTickService`.
- **Items:** all OSRS-style weapon/gear rows (whips, godswords, rune/dragon/bandos sets…) — items.json's weapons/gear become the doc tables only. PoC `steel_sword` + ranger set stay temporarily as the only modeled assets (asset-map rows unchanged) until doc items get models.
- **Ladder-bound systems:** wagers + win streaks, endless mode (`StartEndlessCommand`/waves/`BuildEndlessNpc`), beg, global prestige (per-boss graduation replaces it in M4), `ItemUnlockService`'s once-only ladder drops.
- **Ladder-era UI:** shop modal, bank modal, NPC ladder modal, collection log modal, and their hub cards — M2 rebuilds bank/shop from the design docs; hub becomes Fight (Maggot King) + dev/debug entries.
- Save fields for retired systems (win streak, endless best, unlocked opponents…) are dropped from SaveData in the v2 schema bump (G); v1 saves migrate by discarding them.

## 11. Sequencing (each step merges to `claude/text-duel-game-3t4vkf` when green)

0. Flip CLAUDE.md to "Current milestone: M1" on plan approval.
1. **B** — doc items + specials + debug grants (playable with old HUD/math via legacy stats).
2. **A + J** — combat math v2 switch + ladder retirement sweep (one step: the sweep is what makes a single-model codebase possible without dual math).
3. **E** — flask belt core (commands + refill; temporary buttons on old HUD).
4. **C** — shared boss systems, each with tests.
5. **D** — Maggot King choreography + choreography test suite + I-parts needed to see it.
6. **F** — HUD rebuild (tokens first, then layout, then widgets).
7. **G** — action bar + editor + save v2.
8. **H** — result screens.
9. Cleanup rule sweep + ARCHITECTURE.md + graphify update + `m1-findings.md` finalized.

Verification: `dotnet build` 0 warnings + full test suite per step (CI or SDK-capable session — see M0 findings caveat), choreography tests for D, and the phone playtest against the M1 question with both dev presets.

---

## 12. Design questions (flagged, not resolved) and tunables

**Question statuses after first review round:**
- **D1 — RESOLVED: retire the ladder now.** The old ladder NPCs and OSRS-style items are removed entirely in M1 rather than left untuned under the new math (see Workstream J). Until M2, the game's content is the Maggot King + dev presets — accepted trade-off for a clean single-model codebase.
- **D2 — RESOLVED (corrected).** "1 attack per tick" is wrong: weapons and enemies keep unique tick delays (1, 2, 3, 4…). The existing attack-speed/cooldown machinery carries into the new math; per-archetype speed values for the doc weapons are proposed in B as tunables.
- **D3 — XP/levels fate.** They become combat-inert in M1 (still displayed/accrued). Remove entirely later, or keep as cosmetic progression? Not an M1 blocker; flagging so it isn't resolved silently.
- **D4 — RESOLVED.** Kill-time target 3–4 min at T1 approved. Starting HP recomputed to 450 after the D2 tempo correction (§5); playtest adjusts.
- **D5 — RESOLVED.** Food and potions are deleted outright — item rows, commands, handlers, HUD buttons, and their ladder loot-table entries. Flasks are the only consumable.
- **D6 — RESOLVED (no objection).** Combat log stays as a collapsed drawer.
- **D7 — RESOLVED (adjusted).** Prayer drain must be higher than today's 1/tick: **2 points/tick** while a protection is on (99-point pool ≈ 30s of unflicked prayer; flicking is the intended economy), provisional/tunable. Full restore each duel start; Prayer flask restores 40.

**Tunables (data values marked provisional in the definitions, adjusted at playtest):** per-attack damages within bands (Bile 18, Lash/Volley 18, Eruption 35, Pool 6/tick, Rot Burst 55) · boss HP 450 · weapon speeds (T1 = 3 ticks, T2 = 4) · prayer drain 2/tick · swarm HP 2 / bleed stack = existing bleed (4 dmg over 2 ticks) — "stack" semantics deliberately simplified for M1 · flask restore amounts (40/40) · sips 3 · ranged/magic weapon range 7 · Rot Burst cadence ~40 ticks · spec regen danger definition ("in combat" = duel active).

## 13. Explicitly out of scope

Bank, shop, drops/loot tables, economy pricing, other bosses, invocations panel, minigame, boss roster/pre-fight screens, presets ×5 + per-boss chips, HUD edit mode/left-handed/hold-to-pray/settings, FTUE overlays, collection log, T3/T4 items, boss uniques/rares, visual polish pass (inked skin), portrait combat layout.
