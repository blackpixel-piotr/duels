# Duels — Economy Design & Numbers

All numbers are launch targets, chosen for coherence with the pacing decisions (first clear in 2–4h, meaningful purchase cadence, cheap failure). Everything here is a tuning lever — §8 lists the levers explicitly.

---

## 1. Design Targets (the numbers serve these, not vice versa)

- **Cold start (backlog resolution batch 1 §4, ratified):** new players start with **600g and one free T1 weapon** of their chosen style (a "choose your style" FTUE beat at character creation) — resolves the earlier flagged gap where 0 starting gold + no non-boss income left a fresh player unable to afford gear without already having gear.
- First shop purchase within **15 minutes** of first play — trivially met: the 600g start makes the first purchase possible at minute 0, before the first fight.
- A meaningful gear upgrade every **45–90 min** in early game, stretching to **3–5 h** per upgrade late game.
- First Maggot King clear (base) reachable in **2–4 h** total play.
- Failure costs **zero currency and zero items** — death is only time.
- Hub = the only gold economy. **Runs pay no gold** — run rewards are Laurels (prestige currency) only. This keeps the minigame permanently out-of-the-box and the hub as the sole gear engine.
- Farming a boss at your gear tier should out-earn farming anything below it (no "safe-spot the old boss" meta).

---

## 2. Currencies

| Currency | Source | Spent on | Notes |
|---|---|---|---|
| **Gold** | Boss kills, item drops (sell value), the 600g cold-start grant | Gold Shop gear | The vertical-progression currency |
| **Laurels** | Minigame runs (completion-weighted) | Emporium cosmetics/prestige | Never buys stats |
| **Drops** | Boss loot tables | — (equipped or sold) | Uniques and rares are untradeable-flagged for now (no player trading at launch) |

---

## 3. Gold Income

There is no duel income; Tier-1 bosses carry the gold on-ramp.

**Boss kills — base gold by tier**, multiplied by raid level:

> **Kill gold = base × (1 + RL/100)**

| Boss tier | Base gold/kill | Target kill+reset time (at tier) | Base gold/hour |
|---|---|---|---|
| Tier 1 (Maggot King, Hive Matron) | 500g | ~4 min | ~7,500 g/h |
| Tier 2 (Mirrorhide, Bloodtithe) | 900g | ~4.5 min | ~12,000 g/h |
| Tier 3 (Gale Roc, Unblinking, Millstone) | 1,500g | ~5 min | ~18,000 g/h |
| Grand Duelist | 2,500g | ~7 min | ~21,400 g/h |

At RL 150 those rates roughly ×2.5 — invocations are always the best gold in the game, which is exactly the incentive the design wants.

**Item sell values:** common drops sell for 15% of their shop-equivalent price (single canonical rate); a floor income that makes no drop feel like nothing.

---

## 4. Gold Shop Price Ladder

Weapons per style (melee/ranged/magic) and armour per slot at each tier. Prices assume the income table above.

| Tier | Weapon | Armour piece (×6 slots) | Full set (weapon + 6 armour, actual priced total) | Earned at tier below in… |
|---|---|---|---|---|
| T1 | 500g | 200–350g | 2,000g | ~10 min (weapon is free — cold-start grant covers most of it) |
| T2 | 2,500g | 900–1,400g | 8,900g | ~1h20 total from character creation |
| T3 | 10,000g | 3,500–5,500g | 35,000g | ~3 h of Tier-2 bossing |
| T4 (shop ceiling) | 30,000g | 10,000–15,000g | 100,500g | ~5.5 h of Tier-3 bossing |

**Above T4, gear comes only from drops** — rares are the endgame chase, the shop is the floor.
Flasks: baseline Health & Prayer flasks cost 1,000g each (one-time unlock); specialty flasks are drops only.
**Buyback:** full price refund within the session, 80% after.

**Cadence check (recomputed, backlog resolution batch 1 §6 — was last computed against the pre-ratification 300g Tier-1 rate and never refreshed against the 500g rate, then the 600g/free-weapon cold-start grant changed the starting point too):**
First purchase: **minute 0** (the 600g start covers 1–2 T1 armour pieces before the first fight — exceeds the ≤15 min target). T1 full kit ≈ **~10 min** (weapon free; 900g short of the 1,500g armour total after the 600g start, covered by 2 Maggot King kills at 500g/~4 min each) — notably faster than the old ~20–30 min target; worth a playtest sanity check on whether that's too fast, not just faster. T2 kit ≈ **~1h20** total from character creation (continuing at the Tier-1 rate, 7,500g/h) — fits the "45–90 min meaningful upgrade" target (§1) squarely. T3 kit ≈ **~3h** of Tier-2 bossing (12,000g/h). T4 kit ≈ **~5.5h** of Tier-3 bossing (18,000g/h) — after which the economy hands progression to drop tables and invocation-scaled rares. Full-set totals in the table above are the actual per-slot prices summed (`items.json`), not the pre-implementation ballpark this row used to cite — they run ~25–33% higher than the old rough estimates (e.g. T4 ~100.5k vs. the old "~75,000g"), a discrepancy this recompute surfaced and corrected rather than papering over.

---

## 5. Drop Tables & Rates

Per boss, per kill, one roll on:

| Slot | Chance | Contents |
|---|---|---|
| Gold bonus | always | The §3 kill gold |
| Common | 65% | Sellables, flask-sip tokens (cosmetic-adjacent QoL), materials (reserved for future crafting) |
| Uncommon | 25% | Off-tier gear pieces (bridges shop tiers), specialty-flask *shards* (5 shards = the flask) |
| Unique | 1/20 | Boss-themed gear with a mechanical identity (the boss's drop niche from the Boss Bible) |
| **Rare (BiS)** | see below | The boss's best-in-slot chase item |

**Rare rate scales with raid level** (mobile-tuned — deliberately far kinder than OSRS's 1/512s, because sessions are short):

> Locked below RL 150. At RL 150: **1/50**. Improves linearly to **1/25 at RL 300+**.

Expected chase length at RL 150 ≈ 50 kills ≈ **4–5 hours** of at-tier bossing per rare; ~2.5 h at Inferno. Eight bosses × one rare each + uniques ≈ a 40–60 hour full collection-log horizon at launch, before hard-mode variants extend it.

**Bad-luck protection:** hidden +2% relative rate per dry kill past 1.5× expected, resets on hit. Never displayed, never marketed — it exists to protect the bottom decile, not to be optimized.

---

## 6. Laurels (run currency) & Emporium Pricing

**Earning** (per run, × payout multiplier = 1 + run-RL/100):

| Outcome | Laurels (base) |
|---|---|
| Full completion | 100 |
| Death at finale boss | 12 |
| Death at fight 4–5 | 6 |
| Death at fight 2–3 | 3 |
| Death at fight 1 | 1 |

Completion at run-RL 200 = 300 Laurels. The drip follows the resolved failure rule: deep losses inch the bar, clearing is always ~8× better than the best failure.

**Emporium price ladder:**

| Item class | Laurels | ≈ base completions |
|---|---|---|
| Titles, small badges | 200–400 | 2–4 |
| Victory poses, VFX trails | 800 | 8 |
| Weapon skins | 1,500 | 15 |
| Full transmog sets | 3,500 | 35 |
| Minigame starting-loadout variants | 1,000 | 10 |

At ~12–15 min per run, a full transmog set ≈ **7–9 hours** of minigame play at base RL, roughly half that for invocation runners. Flex milestones (capes/titles for 300+ clears) are never purchasable.

---

## 7. Faucets & Sinks Audit

**Faucets:** boss gold (RL-scaled), item sell values, the one-time 600g cold-start grant.
**Sinks:** shop ladder (finite — exhausted ~hour 15–18), flask unlocks, buyback friction.
**Known gap:** once T4 is bought, gold has no sink and inflates. Acceptable at launch (drops carry endgame), but flag for the first content update: candidate sinks are gold-purchased cosmetic dyes, invocation-run "stakes" (optional gold ante for bonus loot rolls), or crafting using the reserved materials. Decide before inflation is visible, not after.
**Laurels** have no gap: the Emporium catalogue grows with every cosmetic batch.

---

## 8. Tuning Levers (change these, not the structure)

1. Kill gold bases (§3) — the master income knob.
2. RL multiplier slope (1 + RL/100) — how much invocations pay.
3. Shop tier prices (§4) — upgrade cadence.
4. Rare gate (150) and rate band (1/50→1/25) — chase length.
5. Laurel completion base (100) and drip table — minigame grind length.
6. Bad-luck protection threshold (1.5×) — bottom-decile experience.

---

## 9. Interim Asset Mapping (Quaternius free packs)

> Superseded in detail by **duels-items.md** (full item tables, style-lined armour mapped to the Modular Character Outfits – Fantasy pack, and the manifest). The tint doctrine below remains the authoritative rarity-reading system.

Until bespoke art exists, gear "design" = **silhouette from a Quaternius asset + toon-shader material tint per tier**. Quaternius's low-poly style takes the cel shader well; consistency comes from the tint doctrine, not the models.

**Tier tint doctrine** (applies to every item, and doubles as instant rarity-reading in the bank/loot UI):

| Tier | Material treatment |
|---|---|
| T1 | Flat iron/leather greys-browns, no accents |
| T2 | Bronze/brass trim tint |
| T3 | Steel blue + single emissive accent line |
| T4 | Gold trim + soft emissive glow |
| Uniques | Boss palette (Maggot King unique = sickly greens, Bloodtithe = crimson, etc.) |
| Rares (BiS) | Boss palette + animated emissive + unique VFX trail |

**Mapping rules:**
- One base model per weapon archetype per style (sword/axe family = melee, bow/crossbow = ranged, staff/wand = magic); tiers are re-tints, **rares get a different silhouette** — the chase item must be recognizable across the arena.
- Armour slots map to Quaternius modular character pieces; where a slot has no matching part (e.g. capes), use a flat-toon plane mesh placeholder tinted per tier.
- Specialty flasks: one bottle model, liquid-color per flask type (matching the UI doctrine colors).
- Keep a manifest file (`asset-map.md`, engineering-owned) listing item-ID → model → tint, so bespoke art can replace entries one row at a time later with zero data changes.
- License note: Quaternius assets are CC0/free — safe for commercial release, but verify the license text shipped with each specific pack at integration time.
