# Duels — Core Loop Redesign: Decisions

## Vision
The boss fight is the product. Target audience: players who enjoy bossing (OSRS-style). No grind-gates to entry — you can jump into any fight at any moment (anti-Graardor: no killcount requirements). Retention comes from the fights themselves — mastery curve, drop tables, and harder versions to graduate into — not from a progression track layered on top.

## Structure: Two Modes, One Boss Roster

### 1. Boss Hub (main mode — progression & mastery)
- The linear ladder is **retired**. Replaced by a roster of bosses, each jumpable at any moment.
- Each boss has its own drop table including a rare best-in-slot item.
- There are no non-boss opponents: every fight is a boss. Tier-1 bosses are
  the on-ramp and early gold source.
- **Gear progression happens through bossing**: gear needed for boss N drops from boss N-1.
- Player should reach a real mechanics boss within **10–15 minutes** of first play.
- **Per-boss prestige/graduation** (CoX model): clear a boss's base version to unlock its hard mode / invocation scaling. Completion is the credential, not grinding.
- **Invocations (ToA model)**: per-boss chosen modifiers that raise a "raid level," scaling loot quality and gold. Difficulty is purchased by the player, not imposed on a schedule.
- Rare best-in-slot drops gated behind minimum raid levels — chasing the rare forces engagement with modifiers.
- Modifier unlocks tied to demonstrated mastery: clear at level X to unlock the next tier; clear with a modifier active to unlock its nastier sibling.

### 2. Roguelite Minigame (skill expression & bragging rights)
- **Out of the box** (Corrupted Gauntlet model): everyone enters with nothing; hub gear stays at the door. Instantly accessible, permanently balanced, completions are pure skill checks.
- Run shape: **4–6 escalating fights + finale boss, ~10–15 min per full clear**. Instant restart on death, low sunk-cost sting.
- Build assembly through dueling: each fight won offers a pick of 2–3 rewards (weapon, armour piece, prayer unlock, special energy boost). No two runs build the same loadout.
- Bosses drawn randomly from the same roster as the hub — content reuse, no separate boss budget.
- Death ends the run; runs are short enough that death means "next run," not recovery grind.

## Difficulty Layering in the Minigame
**You choose how hard; the game chooses what shape.**
- **Chosen difficulty**: invocations selected pre-queue. Multiplies run-currency payout and gates flex rewards ("complete at 300+"). This is the normal-vs-Corrupted Gauntlet split within one minigame.
- **Random variance**: enemy draws, reward offers, build paths. Variance drives replayability but never determines reward tier — payout tier comes only from chosen invocation level.
- Modifiers are **tagged hub-only / run-only / both** from the start. Minigame uses a curated run-global subset (special energy regen, prayer drain, flick windows); boss-specific modifiers stay in the hub.

## Rewards: Prestige, Never Power (in the minigame)
- **Hub drops = permanent gear** (vertical progression lives here exclusively).
- **Minigame rewards = prestige**: run currency (weighted toward completions, not attempts) spent on cosmetics, titles, weapon skins, unlockable starting-loadout variants for the minigame itself.
- Flex rewards for high-invocation completions (Gauntlet cape / Zuk helm equivalents) — proof, not stats.
- Optional link: run completions can unlock modifiers or boss variants that appear in the hub, so the modes feed each other.

## Onboarding & Pacing
- First real mechanics boss within 10–15 min ("Maggot King Lite" — single mechanic).
- Early failure must be cheap: quick boss retries; no 30-min gold setbacks.
- First full clear should feel slightly early — end on the promise of depth (modifier reveal), not exhausted patience.
- Mobile sessions assumed at ~20–40 min; every sitting should end with a felt achievement or a decision ("next time I'll try X").

## Content Priority
- Boss count is the bottleneck — everything hangs off the roster.
- **3–4 mechanically distinct bosses with modifier scaling > 8 shallow bosses + meta-systems.** Dev effort goes to bosses, not structure.
- Maggot King is the template: attack-style rotation forcing prayer flicks, telegraphed hazard tiles, lingering poison pools, escalating second phase.

## Consumables: The Flask Belt (resolved)
Pots and food exist as **flasks with fixed sip charges**, not farmable inventory items — no supply economy, no banking pressure.
- **Belt:** 2 slots (3rd unlockable as a milestone), assigned in the Loadout Editor. Bringing the right flasks is a loadout decision.
- **Sips:** each flask has its own charges per fight (baseline 3). **Sipping consumes your action for that tick** — heal or attack, never both.
- **Baseline flasks:** Health (restores HP), Prayer (restores prayer points).
- **Specialty flasks unlock as boss drops** and counter *other* bosses (the gear chain applies to flasks): e.g. Maggot King → Rotward (poison cleanse + immunity window); Bloodtithe → Coagulant (bleed purge, for Millstone's pressure); fire ward lives with the Grand Duelist's Pit route or a future burn boss.
- **Soft gating only:** flask-gated bosses hit brutally hard without the counter-flask but remain enterable — no hard locks (protects the no-killcount identity). **"No Belt"** is an invocation (+raid level), and flaskless clears are a collection-log flex.
- **Hub refills:** free and full every attempt (cheap-failure rule).
- **Runs:** all flask types are always in the reward-pick pool regardless of hub unlocks (you must be able to roll counters for any boss draw). **Sips persist across run fights** — refills come only from reward picks, making flasks a run-long resource strategy.
- **Tuning note:** all boss damage budgets assume ~3 Health sips available; Bloodtithe's attrition math and Transfusion (special-only interrupt, unchanged) tune around this.

## Retired / Reincarnated Mechanics
- **XP/levels**: gone for good (backlog resolution batch 1 §5, ratified). No combat XP or levels, ever — progression is gear, invocations, and the collection log. Any future account-level system is cosmetic-only and post-launch, out of scope for now.
- **Linear ladder**: retired.
- **Win streaks**: retired as-is (was ladder-bound); high-invocation streaks can serve a similar role if wanted.
- **Endless survival mode**: candidate to fold in as a minigame variant or unlockable route — TBD.
- **Beg for gold**: reincarnated as a comeback/failure-drip layer if needed — small meta-currency on failed runs, cosmetics/QoL only, never power.
- **Prestige reset (global)**: replaced by per-boss graduation.
- **Vengeance**: available as a modifier ("enemies have vengeance").

## Resolved Decisions (formerly open questions)

### Boss roster: 8 bosses (expanded — full designs in duels-boss-designs.md)
1. **Maggot King** — prayer flicking + ground hazards (Tier 1)
2. **Hive Matron** — spacing / weave-melee rhythm (Tier 1)
3. **Mirrorhide** — weapon switching (Tier 2)
4. **Bloodtithe** — sustain, DoT management, back-positioning (Tier 2)
5. **The Gale Roc** — delayed aerial dodges (Tier 3)
6. **The Unblinking** — stationary; line-of-sight & cover budgeting (Tier 3)
7. **The Millstone Golem** — lane dodging, player-shaped battlefield (Tier 3)
8. **The Grand Duelist** — final boss; the mirror match; hub-only
Every boss has a distinct core verb, full tick choreography (0.6s ticks), phase structure, invocation hooks, and a drop niche. Bosses 1–7 feed the minigame pool.

### Failure drip: small, depth-based
- Dying at the finale grants ~10–15% of a completion's run currency, scaling down toward near-zero for early deaths.
- Farming deep losses must never out-earn clearing.
- Flex rewards (capes/titles for high-invocation clears) are completion-only, no exceptions.

### Run→hub link: boss variants only
- Completing a run featuring boss X at high invocation unlocks a **variant of boss X** in the hub (remixed attack pattern/hazards, own small drop table; same model — cheap content).
- Modifiers are earned within the hub itself; each mode's progression stays legible.

### Survival mode: cut from launch
- Competes with the minigame for the endless-challenge slot; deferred.
- Post-launch: returns as an **endless variant of the minigame** — same build-assembly loop, no finale, depth leaderboard.
