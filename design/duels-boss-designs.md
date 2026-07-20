# Duels — Boss Design Bible (8 Bosses)

All timings use the game tick: **1 tick = 0.6s**.

---

## Global Combat Grammar

These rules apply to every boss so players can transfer learning between fights.

**Player assumptions**
- 1 attack per tick (base weapon speed), 1 tile moved per tick.
- Prayer swaps and weapon swaps resolve instantly on input; max one of each per tick.
- Special attacks consume special energy; energy regens slowly over time.

**Telegraph tiers**
- Standard fuse: 3 ticks (1.8s) — ground markers fill, then erupt on the following tick.
- Fast fuse: 2 ticks (1.2s).
- Tick-perfect: 1 tick (0.6s) — reserved for high-invocation modes and endgame phases.
- Every telegraph has a shape + color + audio cue (colorblind-safe by design).

**Perfect Dodge (universal reward)**
Vacating a hazard tile on its **final fuse tick**, or sidestepping a locked projectile on its landing tick, grants **+15 special energy** and a visual glint. Never required to survive — always rewarded. This is the tick-perfect skill ceiling in every fight.

**Punish Windows (universal)**
Big attacks that miss or end in a landing/recovery leave the boss vulnerable: **+25% damage taken, cannot act** for the listed duration. Aggression after a good dodge is always the optimal line.

**Damage bands** (relative to an appropriately geared player's HP)
- Light ≈ 5–10% · Medium ≈ 15–20% · Heavy ≈ 30–40% · Severe ≈ 50–60% (checks, never one-shots) · Lethal = reserved for fully ignored mechanics.

**Damage rolls**
Player damage: an accuracy roll (Precision + style mod vs the target's per-style Evasion, ~80% at-tier) gates each hit; on a hit, damage is a uniform 0..2×Power — Power is the mean, 2×Power the max hit (its own distinct splat). Boss standard attacks (autos) roll 60–100% of their listed band each cast, so a "Medium" auto reads as a band, not a fixed number. Mechanic and hazard damage (eruptions, beams, boulders, dives) and DoTs are deterministic — dodge-checks that always land for exactly their listed value, never rolled. Each boss carries a per-style Evasion (melee/ranged/magic): neutral by default, raised on one style to make the boss "favor" being fought another way — the accuracy tuning lever, no mechanic changes required.

**Prayer grammar**
A protection prayer matching the incoming attack's style **fully negates that hit (100% block)** — full negation, not mitigation, is what makes flicking the correct-color prayer feel decisive rather than just a damage slider. This is the baseline every boss's attacks assume unless explicitly marked **Unprayable** (ground hazards, channeled arena-wide blasts, and similar mechanics that ignore prayer outright and must be dodged positionally instead). The *Doubt* invocation (see Invocations doc) is the only thing that weakens this to a 75% block — it's a curse precisely because it breaks the base rule.

Protection prayers are evaluated on the impact tick, never the cast tick. Ranged/magic boss attacks travel as doctrine-colored projectiles with 2 ticks of flight; the projectile is the primary flick cue. The projectile homes in on the player's current tile every frame, not the tile they stood on at cast — these are never positionally dodgeable, only prayer-blockable, and the visual shouldn't imply otherwise by flying toward a tile the player has since left. Tier-1 bosses telegraph style changes 3 ticks ahead; 2 is standard; 1 is invocation-tier. A doctrine color is a promise: red-orange, green, and blue only ever appear on a telegraph when that specific style is what's actually coming. A telegraph whose resolved style genuinely isn't known yet (e.g. a position-dependent choice like Lash-if-adjacent/Grub-Volley-if-not) glows a neutral amber instead — never a doctrine color standing in for "maybe," since a player who's learned the doctrine palette elsewhere will read it as a commitment the boss isn't making.

**Independent-timer stagger**
A boss's own hazard/channel timers (Eruption, Rot Burst, and their like) run independently of its rotation script by design — that's what layers hazard pressure on top of prayer pressure instead of the two taking turns. But an independent timer is still free to land on the *exact same tick* as a style telegraph, a channel warning, or an attack/channel impact purely by arithmetic coincidence, forcing two separate reactions (a prayer flick and a tile relocation) into one single reaction window with no stagger between them. When that would happen, nudge the independent timer's event by exactly one tick rather than letting it pile on — re-check the following tick rather than assuming one nudge is always enough. This is a floor, not a ceiling: the fight is still allowed (expected, even) to demand hazard awareness and prayer discipline in the same *stretch* of ticks — it just shouldn't compress both into the same *single* tick with zero gap.

**Anti-camping rule**
Every boss has at least one tool that punishes max-range passivity and one that punishes brainless face-tanking. No corner is ever free.

---

## Roster Overview

| # | Boss | Skill axis | Mobility | Arena | Phases | Tier |
|---|------|-----------|----------|-------|--------|------|
| 1 | Maggot King | Prayer flicking + ground hazards | Semi-stationary | 9×9 | 2 | 1 |
| 2 | Hive Matron | Spacing, weave-melee rhythm | Very fast | 11×11 | 2 | 1 |
| 3 | Mirrorhide | Weapon switching | Medium stalker | 9×9 | 2 | 2 |
| 4 | Bloodtithe | Sustain, DoT management, back-positioning | Slow, relentless | 9×9 + fonts | 2 | 2 |
| 5 | The Gale Roc | Delayed aerial dodges, reflexes | Ground + flight | 11×11 open | 3 flights | 3 |
| 6 | The Unblinking | Line-of-sight, cover management | Fully stationary | 11×11 + 4 pillars | 2 + finale | 3 |
| 7 | The Millstone Golem | Lane dodging, battlefield shaping | Slow, huge (3×3) | 13×9 lanes | 2 + finale | 3 |
| 8 | The Grand Duelist | Everything — the mirror match | Player-like | 9×9 + 2 side arenas | 3 + route choice | Final |

Minigame pool: bosses 1–7 (Gale Roc and Millstone use slightly shrunk arenas in runs). The Grand Duelist is hub-only — the graduation fight.

---

## 1. Maggot King *(existing boss, formalized)*

**Fantasy:** bloated carrion royalty on a rotting mound. Barely moves; the floor is his weapon.
**Teaches:** prayer flicking under ground-hazard pressure. The tutorial for the game's two core survival verbs.

**Arena:** 9×9. King occupies a 2×2 mound at center-north; can pivot but not walk.

### Phase 1 (100–50%)
Attack rotation on a fixed 20-tick loop. Maggot King is Tier 1, so its style
telegraphs lead by **3 ticks** (Global Combat Grammar's Tier-1 baseline) —
Bile Spit and Grub Volley also travel as their own doctrine-colored
projectiles per that same section, each with 2 ticks of flight and their
own impact-tick prayer check, independent of whether a style-shift
telegraph happens to precede them:

| Tick | Action | Details |
|---|---|---|
| T0 | **Bile Spit** (magic) | Single target, Medium. Pray Magic. |
| T4 | **Bile Spit** (magic) | Same. |
| T7 | *Style shift telegraph* | Mandibles glow amber (ambiguous — his choice depends on your position) → 3 ticks warning (lands T10). |
| T10 | **Lash** (melee if adjacent) / **Grub Volley** (ranged if not) | Medium. Pray accordingly — his choice depends on YOUR position, so spacing decides what you must flick. |
| T14 | Lash / Grub Volley | Same. |
| T17 | *Style shift telegraph* | Glow blue → 3 ticks warning (lands T0 of the next loop, idle ticks included). |
| T18–19 | idle | Free damage window. Loop restarts. |

**Eruption** (independent timer, every 16 ticks, staggered off any same-tick rotation-script event per the Global Combat Grammar's "Independent-timer stagger"): marks 3 random tiles **plus the player's current tile**. Standard 3-tick fuse → erupt for Heavy (unprayable). Erupted tiles leave **poison pools** for 20 ticks (Light/tick standing in them, applies poison stack).

### Phase 2 (<50%)
- Rotation compresses to a 14-tick loop; style telegraphs tighten to the 2-tick standard (down from Phase 1's 3-tick Tier-1 baseline) — an intentional escalation, not a bug.
- Eruption every 12 ticks, now 5 tiles; pools last 30 ticks. The floor fills up — routing matters.
- **Maggot swarms:** at 50% and 25%, two swarms spawn at arena corners, crawl 1 tile/tick toward the player. Contact = bleed stack. Each dies to 2 hits — a target-switch decision under prayer pressure.
- **Rot Burst** (signature, every ~40 ticks): King inhales for 4 ticks (whole body swells) → arena-wide Severe blast that ignores prayer. **Safe tiles are the scorched tiles left by past eruptions** (pools expire into scorch). The hazard system inverts: the floor that punished you becomes your shelter, and good players track scorch locations all phase.

**Punish window:** after Rot Burst, the King slumps for 5 ticks (+25% damage).
**Perfect Dodge hooks:** last-tick eruption dodges are the natural special-energy engine of this fight.

**Invocation hooks:** *Fecund* (pools never expire — Rot Burst safe tiles become precious), *Restless Rot* (style telegraph → 1 tick), *Broodfather* (swarms spawn continuously).

**Drop niche:** entry best-in-slot melee weapon (rare), poison-resist armor pieces.

---

## 2. The Hive Matron

**Fantasy:** a wasp queen the size of a horse — glassy wings, needle legs, never still.
**Teaches:** spacing and the **weave**: melee is possible but must be danced, not held.

**Arena:** 11×11, open. She skitters/hovers constantly.

### Core movement AI
- Prefers range 3–5 from the player. After every 3rd attack she **dashes 3 tiles** to reset spacing.
- **Adjacency punish:** if the player stands adjacent for **2 consecutive ticks**, she instantly answers with **Tail Stab** — Heavy melee + 2-tile knockback + poison. Melee is therefore a rhythm: step in → hit (1 tick) → step out → repeat. The "weave" is this boss's entire melee game and it feels fantastic once learned.
- **Chitin Guard:** every ~25 ticks she raises wing casings for 8 ticks — ranged/magic damage reduced 50%. The fight breathes: distance players are periodically pulled into weave range, melee players get their best windows.

### Phase 1 (100–40%) attack kit
| Attack | Cast | Effect |
|---|---|---|
| **Dart Volley** (ranged) | 1-tick cast | Medium, pray Range. Her bread and butter. |
| **Sting Lob** | Arcs 2 ticks in the air, lands on your tile-at-cast | Move 1 tile to dodge. Leaves a 1-tile venom splash for 10 ticks. |
| **Pin** (signature) | Marks a line through your tile, charges along it 2 ticks later | Hit = Heavy + pinned/stunned 2 ticks against the wall. Sidestep perpendicular. Perfect Dodge eligible. If she hits a wall having missed, she's stuck: **4-tick punish window.** |

**Drones:** at 75% / 50% / 25%, two drones spawn and orbit her at radius 2, body-blocking melee approach lanes. 3 hits each; or bait them out of formation with movement.

### Phase 2 (<40%) — Frenzy
- Dashes leave a **damage trail** along their path for 4 ticks (Light/tick) — the arena becomes temporarily laced.
- Dart Volley becomes a **3-round burst** across 3 consecutive ticks: prayer must be **held**, not flicked — the anti-flick counterpoint that stops one habit solving every boss.
- Pin now chains twice (second charge 3 ticks after the first, re-aimed).

**Invocation hooks:** *Royal Guard* (4 permanent drones), *Venomous Court* (all her hits apply poison), *No Rest* (dash after every 2nd attack).

**Drop niche:** best-in-slot ranged weapon (rare), lightweight armor with +movement-related stats if those exist.

---

## 3. Mirrorhide

**Fantasy:** a panther-like beast with prismatic scales that drink and echo whatever hurts it.
**Teaches:** weapon switching as an active language — and that your choices write the boss's script.

**Arena:** 9×9. Stalks at medium pace; occasionally **cloaks** for 3 ticks (untargetable, repositions behind you).

### Core mechanic — Attunement
- After being hit by the **same attack style 4 times in a row**, its scales shimmer that style's color for 2 ticks (warning), then it becomes **immune to that style for 8 ticks**.
- **Shatter:** hitting it with a *different* style during the 2-tick shimmer cancels the attunement and opens a **+25% damage window for 4 ticks**. Prepared swappers are paid, not just tolerated.

### Core mechanic — Echo Offense
Its attacks always use **the style you last hit it with**. Hit it with melee → its next attack is melee. Your weapon swaps therefore choose your own prayer requirements. Advanced play: swap one tick *before* its attack windup so it commits to the style you're already praying against. You are scripting the fight; the boss is your mirror.

### Attack kit
| Attack | Notes |
|---|---|
| **Echo Strike** | Medium, in your last-used style. 2-tick windup showing the style. |
| **Prism Sweep** | Frontal 3-tile cone, Heavy, always melee-typed. 2-tick windup — step behind it. |
| **Reflection** (signature) | 3-tick channel → for 6 ticks reflects 50% of its currently attuned style's damage back at you. Swap styles or hold fire. |

### Phase 2 (<50%)
- Attunement triggers after **3** same-style hits; immunity lasts 10 ticks. The swap cadence tightens.
- **Copycat:** it stores the last special attack *you* used and throws it back with boss numbers (2-tick telegraph shows the stolen weapon's silhouette). Spamming your best special now arms your enemy — bait it by using a throwaway special before Copycat comes off cooldown.
- Cloak now ends with a pounce at your tile (1-tick warning, Perfect Dodge eligible).

**Punish window:** a Shattered attunement is the fight's main damage window; the whole fight loops around manufacturing them.

**Invocation hooks:** *Twinned Hide* (head and body attune independently — two immunity states to track), *Deep Mirror* (Copycat also copies your boost prayer), *Silverquick* (shimmer warning → 1 tick).

**Drop niche:** best-in-slot magic weapon (rare); swap-speed or special-energy utility items.

---

## 4. Bloodtithe

**Fantasy:** a gaunt vampiric tyrant dragging a scythe. Slow. Inevitable. The fight is a tax audit on your resources.
**Teaches:** DoT math, sustain discipline, positional melee (his back is the only free real estate).

**Arena:** 9×9 with two **Font tiles** in opposite corners — standing on one purges all your bleed stacks; each font then goes dark for 8 ticks.

### Core pressure
- Walks toward you at 1 tile per 2 ticks. Never stops, never lunges. Dread, not burst.
- **Bleed:** every hit he lands applies a bleed stack (Light per tick for 10 ticks, stacking to 5). **Correctly prayed hits apply no stack** — flick quality converts directly into sustain.
- **Tithe aura:** ending a tick within radius 1 of his front/sides drains 1% of your HP to him as healing. His **back tile is exempt and takes +30% damage** — but he turns to face you at 90° per tick, so staying behind him is an orbit minigame against his turn rate.

### Attack kit
| Attack | Notes |
|---|---|
| **Scythe Arc** (melee) | Hits the 3 frontal tiles, Heavy. 2-tick windup — the cue to slip behind. Missing it gives a 3-tick punish window. |
| **Blood Lance** (ranged) | Pierces a full line, Medium + bleed stack. Pray Range. Used when you kite far. |
| **Transfusion** (signature) | 5-tick channel healing him 2% max HP per tick — **interrupted only by hitting him with a special attack.** Special energy gains a strategic job beyond damage: you must budget an interrupt at all times. |

### Phase 2 (<50%)
- **Crimson Pact:** periodically sacrifices 10% of his current HP for 10 ticks of 1-tile/tick speed. The dread accelerates; the orbit gets harder.
- Bleed cap rises to 8 stacks.
- **Harvest** (signature): 3-tick telegraph, then consumes all your bleed stacks — Medium damage **per stack consumed**. The counter is font routing: purge before it lands. High-level play is holding a font in reserve like a cooldown.

**Invocation hooks:** *Dry Fonts* (one font only), *Deep Tithe* (aura radius 2), *Hemophilia* (bleeds also reduce your outgoing damage by 2% per stack).

**Drop niche:** best-in-slot body/legs (rare), lifesteal or bleed-themed weapons.

---

## 5. The Gale Roc

**Fantasy:** a storm-bird vast enough to blot the arena, half the fight spent as a shadow on the ground.
**Teaches:** delayed dodges — committing to movement on a timer that isn't now. The RS3-style "the attack lands X seconds after you saw it" fight.

**Arena:** 11×11, fully open. Corners are mechanically meaningful.

### Ground game (deliberately simple — the sky is the star)
Fixed, learnable 12-tick rotation: **Wing Buffet** (melee cone + 1-tile pushback) → **Feather Volley** (ranged) → **Wind Shear** (magic), each with the standard 2-tick style telegraph. This is the recovery/damage portion between flights.

### Flight events — at 80%, 55%, 30% HP
It takes off (untargetable), its shadow circles, and its cry pitch announces one of three patterns:

**A. Dive Bomb** *(high pitch)*
Marks your tile. The mark **follows you for 2 ticks, then locks.** Impact comes **5 ticks after cast** (3.0s). Moving early does nothing — you must juke *after* the lock. Severe damage on hit. **Perfect Dodge (moving on the final tick) extends the landing punish window from 4 to 6 ticks.** This is the fight's signature skill check.

**B. Talon Rake** *(three short cries)*
Three full-length line sweeps cross the arena with 2-tick gaps between them, forcing rhythmic lane hops. Heavy per sweep. Lanes are announced by shadow lines 2 ticks ahead.

**C. Downdraft** *(long low cry)*
Every tile except the **four corner calm-tiles** becomes wind-buffeted in 4 ticks. Fail to reach a corner: Severe + flung to a random tile. A pure positional sprint — where you were standing when the cry sounded decides if it's trivial or desperate.

After every pattern she **lands stunned: 4-tick punish window** (+25% damage).

### Phase 3 behavior (<30%)
- Flight patterns chain **two in a row** before she lands (e.g. Dive Bomb into Downdraft — juke, then sprint).
- Ambient **lightning**: random single tiles strike with a 1-tick flash. Light damage — not lethal, but constant reflex static layered under everything else.
- Ground rotation compresses to 9 ticks.

**Invocation hooks:** *Eye of the Storm* (calm-tiles reduced to 2), *Stormcaller* (lightning active from 100%), *No Perch* (landing punish windows halved).

**Drop niche:** best-in-slot boots/cape (rare), the game's premier "movement fight" trophy.

---

## 6. The Unblinking

**Fantasy:** a colossal stone idol fused into the north wall — a single lidless eye. It cannot move. It doesn't need to.
**Teaches:** line-of-sight as a survival resource, cover management, map awareness at all times.

**Arena:** 11×11 with **4 pillars** (at symmetric mid-points). The idol occupies the entire north edge; its plinth is reachable for melee.

### Core mechanic — Gaze Beam
- Eye charges 4 ticks (building glow; the flaring side telegraphs sweep direction), then a beam **sweeps the arena over 6 ticks**, clockwise or counter.
- Standing in the beam: Heavy per tick + a **Petrify** stack. 3 stacks = turned to stone, stunned 5 ticks (usually fatal inside a sweep).
- **Pillars block the beam.** Safety is the shadow-cone directly behind a pillar *relative to the eye* — the safe tiles move as the beam sweeps, so you shuffle around the pillar with it.
- **Pillars are consumable:** each blocked sweep chips a pillar; after **3 blocks it crumbles** into rubble (blocks movement, not beam). Four pillars, finite blocks — cover is a budget for the whole fight, and burning it early is how runs die at 20%.

### Between beams
- **Tear Bolts** (magic): standard pressure, pray Magic. Keeps prayer honest while you position.
- **Gravel Crawlers:** slow adds spawn at arena edges and creep toward you; contact = explosion (Heavy). But lure one into the beam and its corpse **petrifies into a 1-tile mini-pillar lasting 10 ticks** — a consumable cover you manufacture from the boss's own mechanic. Managing crawlers as ammunition is the fight's expert layer.
- **Unblinking Stare** (random, rare): screen-edge vignette locks onto you — break line of sight within 3 ticks or take Severe. An LoS pop-quiz that can arrive at any moment, anywhere.

**Melee note:** the plinth is attackable, but adjacency during a sweep is exposed unless you've dropped a crawler-corpse nearby. Melee here is high-risk scheduling; ranged/magic is the natural read. Bosses are allowed to have favored styles — this is the ranged/magic counterweight to Bloodtithe.

### Phase 2 (<50%)
- **Twin beams** sweep from both directions simultaneously — safety is the *intersection* of two moving shadows.
- Pillars crumble after 2 blocks.

### Finale (<15%) — Lidless
A single continuous beam **tracks you at 1 tile/tick** while all other mechanics stop. Kite it around remaining cover while burning the boss — a pure movement-DPS weave to the kill. With good cover budgeting you have pillars left; without, you're weaving on rubble and prayer.

**Invocation hooks:** *Brittle World* (pillars crumble after 1 block), *Wide Iris* (beam is 2 tiles thick), *It Sees You* (Stare fires on a timer, not randomly).

**Drop niche:** best-in-slot helmet (rare); the "Petrified" cosmetic line.

---

## 7. The Millstone Golem

**Fantasy:** a quarry colossus, 3×3 tiles of grinding stone. Slow as a landslide — and the arena itself is the weapon.
**Teaches:** lane dodging under a metronome, and **battlefield shaping**: the player builds the maze.

**Arena:** 13×9 — wide, lane-oriented.

### Core mechanic — Boulder Press
Every ~10 ticks he slams, launching 2–3 boulders down marked lanes (marks appear 2 ticks before entry). Boulders roll **1 tile/tick** across the full arena; hit = Heavy + knocked into an adjacent lane (often into the next boulder — the classic double-hit that dodging fundamentals prevent). This metronome runs the entire fight underneath everything else. **Perfect Dodges on boulders (last-tick sidesteps) are the fight's special-energy engine.**

### Core mechanic — Rubble Walls (the signature idea)
His slam attacks raise **rubble walls** on the impacted row. Walls:
- **block boulders** — a wall is permanent boulder cover for every lane behind it;
- **block your ranged/magic line-of-sight** — cover costs you angles.

Since his slams land where *you* are, **you choose where walls rise by choosing where to bait slams.** Expert players architect a kill-zone by mid-fight: boulder-shadowed, with one clean firing lane. Casual players end up in a random maze of their own panic. Same mechanic, wildly different outcomes — that's the depth budget of this fight.

### Attack kit
| Attack | Notes |
|---|---|
| **Millstone Spin** | If you're adjacent: 3-tick windup, then the entire adjacent ring takes Heavy + bleed. Melee must step out and back in — his version of the weave. His rear quarter is briefly safest since he turns 1 step per 2 ticks. |
| **Quake Toss** (anti-camp) | If you sit >6 tiles away for 8+ ticks, he lobs a boulder in an arc at your tile (2-tick air time, dodgeable). No free sniping corner exists. |
| **Slam** | The wall-builder above; also deals Heavy in a 3×3 at impact (3-tick fuse). |

### Phase 2 (<50%) — Collapse
- Ceiling debris: random 2×2 zones marked, fall after a 3-tick fuse, become **permanent rubble** — the arena shrinks and self-mazes on top of your architecture.
- Boulders now **split on wall impact** into two smaller diagonal rollers (Medium). Your cover starts leaking — walls stop being absolute.

### Finale (<20%) — Avalanche
Continuous boulder waves pour from one side, lane gaps shifting each wave, while he stands exposed at the far end. All other attacks stop. Weave the waves, burn him down. The player who built walls mid-fight has islands of calm inside the avalanche; the player who didn't dances every wave raw.

**Invocation hooks:** *Rockslide* (boulders 1.5 tiles/tick — rounds to alternating 1/2 movement), *Load-Bearing* (your walls also crumble after blocking 3 boulders), *Deep Quarry* (Collapse active from 100%).

**Drop niche:** best-in-slot gloves/shield-slot (rare), heavy armor line.

---

## 8. The Grand Duelist *(final boss — hub only)*

**Fantasy:** the arena's undefeated champion. Not a monster — a *player*. Sword, shortbow, and staff on his back; a prayer icon over his head; a special bar you can see. The game's title fight.
**Teaches:** everything at once. The mirror match.

**Arena:** the grand ring, 9×9, crowd roaring. Two gated side-arenas (see Route Choice).

### Core mind-game — he plays by your rules
- **He has the three styles** and swaps weapons visibly (1-tick swap animation = your prayer telegraph).
- **He prays** — the icon above his head shows his active protection. To damage him you must attack the style he *isn't* praying against: **your weapon swapping is now offense**, the graduation of what Mirrorhide taught.
- P1: he re-flicks to match your style with a **2-tick lag** — alternating two weapons on a 2-tick cycle out-paces him for consistent damage.
- His boost prayer flares periodically (+damage); his hits during it upgrade one band (Medium→Heavy).

### Core mechanic — his specials are YOUR specials
His special bar fills in view. When full, he uses **the actual special attack of his current weapon** — drawn from the game's real player arsenal. Players recognize every one, and every counter they've learned applies. (Content reuse: every weapon special you ship is also final-boss content.)

**Spec Clash (signature):** if you hit him with your own special while his bar is full and casting, the specials **clash** — a 3-tick struggle animation, resolved in favor of whoever holds more remaining special energy; the loser takes both hits. Hoarding energy near his full bar becomes a visible poker game.

### Phase structure

**Phase 1 (100–66%)** — the honest duel. Style/prayer chess, spec clashes, standard telegraphs. Rotation is semi-scripted so players learn his tells.

**Route Choice (at 66%)** — he leaps to the gates; two doors open; walking through one commits the fight (per-kill replay value, chosen not rolled):

- **The Armory** — an open arena ringed with weapon racks. He periodically dashes to racks to grab counter-weapons; **you can race him to them** — rack weapons are temporary, powerful, with unique one-use specials. A denial/spacing fight: control the racks, control the phase. (Spacing skills — Hive Matron's lesson.)
- **The Pit** — fire trenches ignite in shifting patterns; **both of you** take hazard damage. He dodges well, but **while dodging his prayer-flick lag doubles** — every hazard wave is a damage window on him. Attrition and chaos. (Hazard skills — Maggot King and Millstone's lessons.)

**Phase 3 (33–0%)** — back to the main ring, everything unlocked:
- **Vengeance Pact:** he casts vengeance (the game's own mechanic) — the next big hit he takes is partially reflected. Bait it with a weak poke or eat the reflect; the icon is visible.
- **Feints:** some windups begin as one style and morph on the final tick (audio cue 1 tick before the morph distinguishes them) — the anti-autopilot check on your flicking.
- His flick lag drops to **1 tick** — only swap-baiting and off-tempo attacks land damage.

**Last Stand (<10%):** he drinks a boost, gains speed — but **his prayer bar is now visible and draining**, and every flick you force costs him more. Drain it to zero and he stands defenseless for the kill window. The win condition becomes literal: **out-discipline him.** Thematically, the whole game in one bar.

**Invocation hooks:** *Southpaw* (his telegraph sides mirror), *Champion's Purse* (his special bar fills 50% faster), *Title Match* (Route Choice is his, not yours), *Perfect Record* (feints throughout the entire fight).

**Drop niche:** best-in-slot cape (rare), Duelist weapon skin set, the hub's ultimate title.

---

## Design Cross-Checks

**Every boss is unique on at least two axes:**
mobility (stationary Unblinking ↔ frantic Matron), what the floor means (weapon at Maggot King, cover-budget at Unblinking, player-built at Millstone), what prayer means (flick at Maggot King, hold at Matron P2, *offense* at Grand Duelist), and where damage windows come from (dodge-rewards at Gale Roc, manufactured shatters at Mirrorhide, resource-drain at Bloodtithe).

**Skill curriculum** (intended hub order): Maggot King → Hive Matron → Mirrorhide → Bloodtithe → Gale Roc → Unblinking → Millstone Golem → Grand Duelist. Each boss's core verb is a prerequisite skill somewhere in the Duelist fight.

**Minigame integration:** bosses 1–7 are in the run pool at their base (no-invocation) tuning, with finales trimmed (e.g. Avalanche shortened) to keep run pace at 10–15 min. The Grand Duelist stays hub-exclusive so the title fight keeps its weight.

**Tick-perfect ceiling everywhere:** Perfect Dodge (+15 special energy) is legal in all eight fights; each boss additionally has one bespoke tick-perfect payoff (extended Roc stun, Mirrorhide shatter, Duelist swap-baiting, last-tick eruption/boulder dodges).

**Art-budget honesty:** Unblinking (static model + beam VFX), Millstone (one golem + boulder/rubble props), and Bloodtithe (humanoid rig) are cheap. Matron and Gale Roc are the animation-expensive ones. The Grand Duelist reuses the player rig and player weapon models — the final boss is nearly free.
