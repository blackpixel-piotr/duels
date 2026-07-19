# Duels — Invocation Lists & Raid Level System

Companion to the Boss Design Bible. All tick references use 1 tick = 0.6s.

---

## 1. How Raid Level Works

- Each invocation is a pre-fight toggle worth a fixed **Raid Level (RL)** value. Total RL scales the reward roll (see Economy doc) and gates loot tiers.
- **Bands:** Base (0) · Bronze (1–99) · Silver (100–199) · Gold (200–299) · Inferno (300+). The band recolors the stamped RL number on the pre-fight screen.
- **Rare best-in-slot drops require RL 150+** and their drop rate improves with RL (Economy doc §5).
- Per-boss maximum sits around **RL 430–470** (all universals + all boss-specifics + capstone) — Inferno is reachable only by stacking nearly everything.
- **Stacking rules:** all invocations stack unless marked mutually exclusive (✕ group). Values never change mid-fight.
- Invocations exist in the **hub** (per-boss) and the **minigame** (curated run-global list, §5). Tags: [H] hub, [R] run, [H/R] both.

**Unlock rules (mastery-gated, per design decisions):**
- **Tier A** invocations: unlocked by clearing the boss's base version.
- **Tier B**: unlocked by clearing that boss at RL 100+ (Silver).
- **Tier C / Capstone**: unlocked by clearing at RL 200+ (Gold), or by a listed sibling condition ("clear with X active").

---

## 2. Universal Invocations (available on every hub boss)

| Invocation | RL | Effect | Tier | Tags |
|---|---|---|---|---|
| Quickened | +30 | All boss telegraphs one tier faster (3→2 ticks, 2→1) | B | H/R |
| No Forecast | +10 | HUD style-forecast widget disabled | A | H/R |
| Leaky Faith | +15 | Prayer points drain 50% faster | A | H/R |
| Doubt | +25 | Protection prayers block 75% instead of 100% | B | H/R |
| Slow Surge | +20 | Special energy regenerates at half rate | A | H/R |
| Costly Edge | +10 | Special attacks cost +25% energy | A | H/R |
| No Belt ✕1 | +40 | Flask belt disabled entirely | B | H |
| Half Measures ✕1 | +15 | Flask sips 50% effective | A | H |
| Short Sips ✕1 | +10 | Every flask has one fewer sip | A | H |
| Glass | +25 | You take +25% damage | A | H/R |
| Grindstone | +15 | Boss +25% HP | A | H/R |
| Overwhelm | +20 | Boss damage +15% | A | H/R |
| Sticky Floor | +15 | Standing in any hazard pool slows you (1 tile per 2 ticks) for 3 ticks after leaving | B | H/R |
| Festering | +15 | All DoTs on you tick 50% harder | A | H/R |
| Locked Stance | +10 | Defensive attack style disabled | A | H/R |

✕1 = mutually exclusive group (belt modifiers).
Universal pool total: **+265** (taking the highest of each ✕ group).

---

## 3. Per-Boss Invocations

### Maggot King
| Invocation | RL | Effect | Tier |
|---|---|---|---|
| Broodfather | +20 | Maggot swarms spawn continuously from 100% | A |
| Softened Ground | +20 | Eruptions mark 7 tiles instead of 3/5 | A |
| Fecund | +25 | Poison pools never expire (Rot Burst safe-tiles become scarce) | B |
| Restless Rot | +30 | Style-shift telegraph reduced to 1 tick | B |
| Royal Rot | +25 | Rot Burst active from Phase 1 | B |
| **Capstone — Court of Worms** | +50 | P2 rotation from 100%; swarms respawn 5 ticks after death | C (clear with Fecund + Broodfather) |
Boss pool: **+170**

### Hive Matron
| Invocation | RL | Effect | Tier |
|---|---|---|---|
| Silk Trails | +15 | All dashes leave damage trails from Phase 1 | A |
| Venomous Court | +20 | Every hit she lands applies poison | A |
| No Rest | +20 | She dashes after every 2nd attack | A |
| Long Reach | +30 | Tail Stab triggers after 1 tick of adjacency (the weave becomes tick-perfect) | B |
| Royal Guard | +25 | 4 permanent drones (respawn 10 ticks after death) | B |
| **Capstone — Swarm Crown** | +50 | Pin always chains twice; Chitin Guard uptime doubled | C (clear with Long Reach) |
Boss pool: **+160**

### Mirrorhide
| Invocation | RL | Effect | Tier |
|---|---|---|---|
| Grudge | +15 | Attunement immunity lasts 14 ticks | A |
| Hall of Mirrors | +15 | Cloaks twice as often | A |
| Deep Mirror | +20 | Copycat also copies your boost prayer's effect | B |
| Silverquick | +30 | Shimmer warning reduced to 1 tick (Shatters become tick-perfect) | B |
| Twinned Hide | +35 | Head and body attune independently — two immunity states | B |
| **Capstone — Perfect Reflection** | +50 | Reflection returns 100% of attuned-style damage; Echo attacks +1 damage band | C (clear with Twinned Hide) |
Boss pool: **+165**

### Bloodtithe
| Invocation | RL | Effect | Tier |
|---|---|---|---|
| Old Debt | +10 | You start the fight with 2 bleed stacks | A |
| Unquenchable | +15 | His lifesteal and Tithe healing doubled | A |
| Deep Tithe | +20 | Tithe aura radius 2 | A |
| Eager Harvest | +20 | Harvest telegraph reduced to 2 ticks | B |
| Dry Fonts | +25 | One font only | B |
| Hemophilia | +25 | Each bleed stack reduces your outgoing damage 2% | B |
| **Capstone — The Full Tithe** | +50 | Crimson Pact permanent below 50% (constant 1 tile/tick) | C (clear with Dry Fonts) |
Boss pool: **+165**

### The Gale Roc
| Invocation | RL | Effect | Tier |
|---|---|---|---|
| Twin Talons | +15 | Talon Rake sweeps five lines | A |
| Stormcaller | +20 | Ambient lightning active from 100% | A |
| Long Dive | +20 | Dive Bomb impact at 4 ticks after cast (tighter juke) | B |
| Eye of the Storm | +25 | Downdraft calm-tiles reduced to 2 | B |
| No Perch | +30 | Landing punish windows halved (incl. Perfect-Dodge extension) | B |
| **Capstone — Hurricane** | +50 | Every flight event chains two patterns from 100% | C (clear with No Perch) |
Boss pool: **+160**

### The Unblinking
| Invocation | RL | Effect | Tier |
|---|---|---|---|
| Stone Garden | +15 | Gravel Crawlers move twice as fast | A |
| It Sees You | +20 | Unblinking Stare fires on a fixed timer, not randomly | A |
| Wide Iris | +25 | Gaze Beam is 2 tiles thick (shadow cones shrink) | B |
| Deep Petrify | +25 | Petrify stuns at 2 stacks | B |
| Brittle World | +30 | Pillars crumble after 1 block | B |
| **Capstone — Lidless Age** | +50 | Twin beams from 100%; finale tracking beam at 75% | C (clear with Brittle World) |
Boss pool: **+165**

### The Millstone Golem
| Invocation | RL | Effect | Tier |
|---|---|---|---|
| Twin Press | +15 | +1 boulder per Boulder Press | A |
| Deep Quarry | +20 | Collapse (ceiling debris) active from 100% | A |
| Fault Lines | +20 | Avalanche finale starts at 30% | B |
| Rockslide | +25 | Boulders alternate 1/2 tiles per tick (effective 1.5×) | B |
| Load-Bearing | +30 | Your rubble walls crumble after blocking 3 boulders | B |
| **Capstone — The Grind** | +50 | Boulders always split on wall impact; Quake Toss cooldown halved | C (clear with Load-Bearing) |
Boss pool: **+160**

### The Grand Duelist
| Invocation | RL | Effect | Tier |
|---|---|---|---|
| Southpaw | +20 | All his telegraph sides are mirrored | A |
| Title Match | +20 | Route Choice is his, not yours | A |
| Champion's Purse | +25 | His special bar fills 50% faster | B |
| Encore | +30 | You fight both route arenas (at 66% and 45%) | B |
| Perfect Record | +35 | Feints active from Phase 1 | B |
| **Capstone — Veteran's Poise** | +50 | His prayer-flick lag is 1 tick for the entire fight | C (clear with Perfect Record) |
Boss pool: **+180**

---

## 4. Design Rules for the Lists

- **Every invocation changes decisions, not just numbers** — the pure stat ones (Glass, Grindstone, Overwhelm) exist as cheap filler to fine-tune RL totals, but the identity of each boss's list is mechanical.
- **Capstones require sibling clears** so Gold/Inferno is a ladder of demonstrated mastery, never a checkbox splurge.
- **No invocation removes a boss's counterplay** — telegraphs compress, never vanish; safe tiles shrink, never disappear.
- **Free-value audit:** No Forecast is deliberately cheap because skilled players lose little — its RL reflects that. Any invocation the top 10% can take for free must be priced low.

---

## 5. Run-Global Invocation List (minigame)

Selected in the Gauntlet lobby; total RL maps to the payout multiplier (Economy doc §6) and gates flex rewards ("complete at 300+"). Boss-specific invocations never appear here.

All [H/R]-tagged universals above, plus run-exclusive:

| Invocation | RL | Effect |
|---|---|---|
| Dry Run | +40 | Flask reward-picks removed from the pool entirely |
| Thin Draws | +20 | Reward picks offer 2 cards instead of 3 |
| Long Gauntlet | +25 | +2 fights per run (more danger, but also more picks) |
| Elite Draws | +30 | Bosses use their Phase-2 behavior from 75% HP |
| Iron Start | +15 | Starter weapon is style-locked (no starter style choice) |
| Sudden Death | +35 | Health-flask picks capped at 1 for the whole run |
| No Second Wind | +20 | Perfect Dodge grants no special energy this run |

Run list maximum: **+265 (universals) + 185 (run-exclusive) = ~450**, matching hub Inferno reachability.

**Unlocks:** run invocations unlock by completion milestones (first completion → Tier A; complete at 100+ → Tier B; complete at 200+ → the rest), mirroring the hub's mastery gates.
