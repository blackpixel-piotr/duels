# Duels — Item Tables & Asset Manifest

Companion to the Economy doc. All names are placeholders that bind to nothing: stats and mechanics attach to the **item ID**; name + model are presentation and can be swapped in minutes if a model can't be found.

**Model risk flags:**
- 🟢 confirmed or near-certain in the target packs (Quaternius weapon/prop packs, Modular Character Outfits – Fantasy)
- 🟡 common archetype, very likely findable in free low-poly packs
- 🔴 exotic — do not block on it; ship the fallback archetype listed

**Target packs:** Universal Base Characters + Modular Character Outfits – Fantasy (12 outfits, 62 modular parts, 3 texture variations per outfit, CC0; free tier = Ranger + Peasant, full set via the ~$20 support tier) · Quaternius weapons/props packs for held items.

---

## 1. Combat Math Baseline (tunable, but consistent everywhere below)

- Player max HP: **100**. Base hit chance: **~80% at-tier** — an accuracy roll of **Precision + style mod vs the target's per-style Evasion**; at matched tier (neutral Evasion) it lands ~80%.
- Attack styles: Accurate +10% hit · Aggressive +20% damage, −10% hit · Defensive +20% defense value, −10% damage. **Defensive's "defense value" is a flat 20% reduction of incoming damage on the defender, additive with gear Def-point reduction** (backlog resolution batch 1 §8, ratified — was flagged as an invented shape in m1-findings.md, now canonical). **NEW: total mitigation from all sources (gear Def + Defensive style, and any future source) hard-caps at 50%** — replaces the earlier unbounded-in-practice 95% safety clamp.
- Weapon **Power** = **average** damage. A landed hit rolls a **uniform 0..2×Power**, so Power is the mean and **2×Power is the max hit** (shown on item cards; a max roll gets a distinct visual). Weapon **Precision** = flat hit-chance bonus.
- Weapons have an **AttackSpeed** in ticks (dagger 2 · sword/bow/wand/staff 3 · greatsword/maul/war crossbow 4). Power = mean damage per hit; per-hit roll is uniform 0..2×Power. Tier balance anchors on DPS: **Power = tier DPS × AttackSpeed** (tier DPS: 3.33 / 4.67 / 6.33 / 8.33; rares 9.33). Attack cooldown is **global and persists across swaps**; specials **consume the next attack slot**; a flask sip **adds +1 tick** to the current cooldown.
- **Boss standard attacks (autos)** roll **60–100% of their listed band** each cast. **Mechanic/hazard damage** (eruptions, beams, boulders, dives) **and DoTs are deterministic** — dodge-checks that always land for exactly their listed value, never rolled.
- **Per-style Evasion** (melee/ranged/magic) is each boss's accuracy lever: neutral (0) leaves the ~80% at-tier baseline; a positive value on one style makes the boss "favor" being fought another way, with no mechanic changes.
- Armour **Def** points: each point reduces incoming damage of its matching style by **0.4%** (cap 40% from gear). Spread differs per armour line (§5). The 40% gear cap and Defensive style's flat 20% (above) combine under the same 50% total-mitigation hard cap.
- Special energy: max 100 base; regen ~2/tick out of danger, 1/tick in combat (tunable).
- **Boost prayer: +20% Power while active** (backlog resolution batch 1 §8, ratified — carried over from the pre-M1 Piety number, now canonical rather than an unsourced carry-over).
- **Scorch (Cinder Wand special): 3 ticks @ 3 dmg/tick. Rend (Splitter special): bleed 4 ticks @ 3 dmg/tick, plus a 1.3× base-hit multiplier on the triggering hit** (backlog resolution batch 1 §8, ratified DoT baselines).
- **Prayer points: flat 99-point pool** (backlog resolution batch 1 §9, ratified). **Prayer drain cadence: 1 drain event per 9 ticks** while a protection or boost prayer is active (2 points/event for protection, 1 for boost) — a future "Leaky Faith" invocation is the intended modifier hook for this cadence, not yet built.
- **Protection prayer** (matching style) **fully negates** boss basic-attack damage — 100% block, not a percentage reduction — unless the attack is marked Unprayable (ground hazards, arena-wide channeled blasts). Checked on the **impact tick**, not the cast tick — a ranged/magic attack travels as a homing doctrine-colored projectile (~3 tiles/tick, flight time scales with distance) so a prayer raised any time before it lands still blocks it. See boss bible's "Prayer grammar" and the Doubt invocation (75% block) for the one thing that weakens this.

---

## 2. Shop Weapons (3 styles × 4 tiers)

| Tier | Style | Name | Archetype | Power | Speed | Prec | Special (cost — effect) | Model | Price |
|---|---|---|---|---|---|---|---|---|---|
| T1 | Melee | Rustcleaver | Shortsword | 10 | 3t | +0% | **Lunge** (25 — next hit from 2 tiles, closes the gap) | 🟢 sword | 500g |
| T1 | Ranged | Poacher's Bow | Shortbow | 10 | 3t | +0% | **Snipe** (25 — +50% damage single shot) | 🟢 bow | 500g |
| T1 | Magic | Cinder Wand | Wand | 10 | 3t | +0% | **Scorch** (25 — hit + 3-tick burn DoT) | 🟢 wand | 500g |
| T2 | Melee | Splitter | Battleaxe | 19 | 4t | +2% | **Rend** (40 — heavy hit + bleed stack) | 🟢 axe | 2,500g |
| T2 | Ranged | Bolt Thrower | Crossbow | 19 | 4t | +2% | **Pin Shot** (40 — hit + target's next action delayed 1 tick) | 🟢 crossbow | 2,500g |
| T2 | Magic | Hexknot Staff | Staff | 19 | 4t | +2% | **Sap** (40 — hit + boss damage −10% for 5 ticks) | 🟢 staff | 2,500g |
| T3 | Melee | Doombringer Maul | Warhammer | 25 | 4t | +4% | **Quake** (60 — hits all adjacent tiles, heavy) | 🟢 hammer | 10,000g |
| T3 | Ranged | Longfang | Longbow | 19 | 3t | +4% | **Piercing Arrow** (60 — line shot, ignores 25% Def) | 🟢 bow | 10,000g |
| T3 | Magic | Stormbrand | Battlestaff | 19 | 3t | +4% | **Arc** (60 — hit + chains a half-damage hit next tick) | 🟢 staff | 10,000g |
| T4 | Melee | Kingsplitter | Greatsword | 33 | 4t | +6% | **Executioner** (75 — +100% damage vs targets under 30% HP) | 🟢 sword (scaled up) | 30,000g |
| T4 | Ranged | Siegepiercer | War crossbow | 33 | 4t | +6% | **Ballista Bolt** (75 — massive single hit, 1-tick self-root) | 🟢 crossbow (scaled) | 30,000g |
| T4 | Magic | Archon's Rod | Scepter | 25 | 3t | +6% | **Annihilate** (75 — huge hit, 2-tick cast, interruptible) | 🟡 scepter → fallback: staff kitbash | 30,000g |

Special descriptions are deliberately archetype-flexible (a "sweeping/heavy/line" verb, never a shape-specific one) so any model swap is cosmetic only.

---

## 3. Boss Uniques (1/20 drop, boss-flavored identity)

**Unique-quality rule (backlog resolution batch 1 §1, closes the "no stats
for uniques" gap for all 8):** a unique's quality tier = its boss's own tier
+ 1. Weapon Power = that quality tier's DPS × the weapon's AttackSpeed (§1's
tier-DPS table). Armour uniques carry that quality tier's slot Def (§5's Def
table). **Melee AttackType by archetype** (cosmetic-only, picks the swing
animation, doesn't change mitigation — protection prayer buckets all melee
as one style): dagger = Stab, sword/axe = Slash, hammer/maul = Crush.

| Boss (tier) | Name | Slot | Stats | Effect | Model |
|---|---|---|---|---|---|
| Maggot King (T1→T2) | Rotfang | Melee (dagger, Stab) | 9 Power, 2t Speed, +2% Prec | On-hit poison: 5 ticks @ 2/tick, max 3 stacks, reapplication refreshes | 🟢 dagger |
| Hive Matron (T1→T2) | Chitin Recurve | Ranged (bow) | 14 Power, 3t Speed, +2% Prec | +10% damage against moving/airborne targets | 🟢 bow, chitin tint |
| Mirrorhide (T2→T3) | Prism Wand | Magic (wand) | 19 Power, 3t Speed, +4% Prec | Special attacks cost −15% energy | 🟢 wand + crystal kitbash |
| Bloodtithe (T2→T3) | Leech Blade | Melee (sword, Slash) | 19 Power, 3t Speed, +4% Prec | 8% lifesteal on melee hits | 🟢 sword, crimson tint |
| Gale Roc (T3→T4) | Talon Dirk | Melee (dagger, Stab) | 17 Power, 2t Speed, +6% Prec | +15% damage for 3 ticks after you Perfect Dodge | 🟢 dagger |
| The Unblinking (T3→T4) | Petrified Sabatons | Boots | 7 Def | +10 Def (all styles) while you haven't moved for 2+ ticks | 🟢 outfit boots, stone tint |
| Millstone Golem (T3→T4) | Quarry Gauntlets | Gloves | 7 Def | Knockback distance against you reduced by 1 tile | 🟢 outfit gloves |
| Grand Duelist (→T4) | Contender's Cloak | Cape | 7 Def | +5 max special energy | 🟡 cape → fallback: flat-plane mesh |

Only Rotfang is ingested into `items.json` so far (Maggot King is the only
boss that exists) — the other seven now have real, doc-sourced stats ready
for their own boss's milestone, so that milestone doesn't hit this same
"no stats" gap again.

---

## 4. Boss Rares (best-in-slot, RL 150+ gated, distinct silhouette rule applies)

| Boss | Name | Slot | Stats | Effect | Model |
|---|---|---|---|---|---|
| Maggot King | Carrion Edge | Melee weapon | 28 Power, 3t Speed, +6% Prec | 20% of hits apply poison; **Fester** (50 — heavy hit that detonates all poison on the target for instant damage) | 🟢 sword + emissive green kitbash |
| Hive Matron | Hivepiercer | Ranged weapon | 28 Power, 3t Speed, +6% Prec | Every 3rd hit crits; **Sting Volley** (60 — 3 rapid shots) | 🟢 bow + stinger kitbash |
| Mirrorhide | Mirrorshard Staff | Magic weapon | 28 Power, 3t Speed, +6% Prec | First hit after swapping *to* this staff +40% damage; **Refract** (50 — hits twice) | 🟢 staff + crystal shards |
| Bloodtithe | Tithebound Cuirass | Body | 16 Def (crimson spread) | Flask sips also cleanse 1 DoT stack | 🟢 Knight-line chest, crimson emissive |
| Gale Roc | Galestriders | Boots | 10 Def | Perfect Dodge grants +25 energy instead of +15 | 🟢 outfit boots + feather kitbash |
| The Unblinking | Unblinking Visage | Helmet | 12 Def | The first stun/petrify each fight is negated | 🟢 outfit helm + eye emblem |
| Millstone Golem | Millstone Grips | Gloves | 10 Def | Immune to knockback | 🟢 outfit gloves, stone texture |
| Grand Duelist | Champion's Mantle | Cape | 10 Def | +10 max special energy; specials cost −10% | 🟡 cape mesh + VFX trail |

**Legs BiS is intentionally absent at launch** — reserved for the first hard-mode boss variant, so the update ships with a chase item built in.

**Rares (and uniques) carry no armour `Line`** (backlog resolution batch 1 §7, ratified) — they sit outside the shop-ladder's Warbound/Stalker/Occult identity/set-bonus system by design; that system is a shop-ladder mechanic, not a general one.

Rares are never sellable; uniques sell for a flat **2,000g** (backlog resolution batch 1 §6, replacing the earlier "15% of a T4 piece" formula — simpler, and doesn't need picking which T4 piece to anchor against).

---

## 5. Armour — Three Style Lines (mapped to the outfit pack)

**Design change from the Economy doc (superseding its generic ladder):** armour comes in three themed lines, one per combat style, mapped to the pack's outfits. Stats are shared per tier; each line adds a small identity bonus. Prices per piece are unchanged from the Economy ladder.

**Per-slot pricing rule (backlog resolution batch 1 §7, ratified — closes the "range, not exact prices" gap):** economy §4 gives only a per-tier price *range*; the canonical per-slot price linearly interpolates within that range by the slot's own Def weight below — Boots/Gloves/Cape (lightest) anchor the range's low end, Body (heaviest) anchors the high end, Legs/Helmet fall proportionally between. `items.json`'s `shopPrices` implements this exactly.

| Line | Identity bonus (any piece count) | Outfit mapping |
|---|---|---|
| **Warbound** (melee) | +1% melee damage per piece | Knight-family outfits 🟢 |
| **Stalker** (ranged) | +1% ranged damage per piece | Ranger-family outfits 🟢 (in the free tier) |
| **Occult** (magic) | +1% magic damage per piece | Wizard-family outfits 🟢 |

**Set bonuses (within one line):** 4 pieces → +5% damage of the line's style · 6 pieces → +10 max special energy.

**Def per piece per tier** (Def spread: 50% vs the line's own weakness profile is flat for launch — all styles equal — revisit if builds need sharpening):

| Slot | T1 | T2 | T3 | T4 |
|---|---|---|---|---|
| Body | 3 | 6 | 9 | 14 |
| Legs | 2.5 | 5 | 8 | 12 |
| Helmet | 2 | 4 | 6 | 10 |
| Boots / Gloves / Cape | 1.5 | 3 | 5 | 7 |

Full T4 six-piece ≈ 60 Def ≈ 24% damage reduction, ~29% with Defensive style — a real but not mandatory wall, per the math baseline.

**Outfit → tier assignment plan** (full pack, 12 outfits = 3 lines × 4 tiers):
- Each line claims 4 outfits of ascending visual heft; exact picks made after downloading the full pack (only Peasant, Ranger, Knight, Wizard, Noble are confirmed names — leave manifest rows open for the rest).
- The pack's **3 texture variations per outfit**: variation 1 = shop appearance; variations 2–3 = **Emporium recolor cosmetics** (instant prestige stock at zero art cost).
- **Peasant outfit** = the default naked/starter look (and the minigame's enter-naked appearance).
- **Noble outfit** = an Emporium-exclusive transmog set (pure flex, fits its look).
- **Free-tier fallback** (if shipping before the $20 unlock): Peasant recolors carry T1–T2 and Ranger recolors carry T3–T4 across all three lines, with line identity shown by UI frame color only. Functional, ugly, temporary.

**Capes** are the one slot the outfit pack likely lacks: ship the flat-plane toon mesh placeholder (per Economy §9) tinted per tier.

---

## 6. Flasks (models)

One bottle/vial mesh (🟢 expected in Quaternius fantasy prop packs; fallback: any CC0 potion mesh), liquid tinted per the UI doctrine colors:

| Flask | Liquid tint | Source |
|---|---|---|
| Health | Red | Shop, 1,000g |
| Prayer | Light blue | Shop, 1,000g |
| Rotward | Sickly green | Maggot King (shards) |
| Coagulant | Deep crimson | Bloodtithe (shards) |
| Fire Ward | Orange | Grand Duelist Pit route / future burn boss |

---

## 7. Asset Manifest Convention (`asset-map.md`, engineering-owned)

One row per item ID; art replaces rows one at a time with zero data changes.

| item_id | display_name | model_ref | tint/material | flag |
|---|---|---|---|---|
| wpn_melee_t1 | Rustcleaver | quaternius/weapons/sword_01 | tier1_iron | 🟢 |
| arm_stalker_body_t2 | Stalker Jerkin | outfits_fantasy/ranger/chest | tier2_bronze | 🟢 |
| wpn_rare_mk | Carrion Edge | quaternius/weapons/sword_03 + kitbash/maggot_emissive | boss_maggotking | 🟢 |
| … | | | | |

**Search shopping list (priority order for the model hunt):** sword ×3 sizes, axe, hammer, dagger ×2, bow ×3, crossbow ×2, wand ×2, staff ×3, scepter 🟡, bottle/vial, cape mesh 🟡. Everything else comes from the outfit pack.
