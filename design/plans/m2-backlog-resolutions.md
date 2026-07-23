## Backlog resolution batch: Sections A + D, housekeeping, landscape mandate
All /design edits below are authorized verbatim. Move each closed item to
backlog.md → Resolved with a pointer to this batch.

### 1. Unique weapon/armour stats (closes #1, prevents recurrence for all 8)
Rule (add to items doc §3): unique quality = boss tier + 1; weapon Power =
that tier's DPS × AttackSpeed; armour uniques use that tier's slot Def.
Add stat columns to §3 with these values, ingest Rotfang into items.json now:
- Rotfang: dagger, 2t, Power 9, +2% Prec. On-hit poison: 5 ticks @ 2/tick,
  max 3 stacks, reapplication refreshes.
- Chitin Recurve: bow 3t, Power 14, +2% · Prism Wand: wand 3t, Power 19, +4%
- Leech Blade: sword 3t, Power 19, +4% · Talon Dirk: dagger 2t, Power 17, +6%
- Petrified Sabatons / Quarry Gauntlets / Contender's Cloak: Def 7 each.
Melee AttackType by archetype (closes #20, add column): dagger=Stab,
sword=Slash, axe=Slash, hammer/maul=Crush. Cosmetic-only for now.

### 2. Maggot King loot table rows (closes #2) — add to items doc, ingest
Common (65%): 40% Gold Cache (40–80g direct gold) · 35% Chitin Fragment
(material, sells 25g) · 25% Rot Gland (material, sells 35g).
Uncommon (25%): 30% random T2 weapon · 30% random T2 armour piece (line
rolled equally) · 40% Rotward Shard.

### 3. Specialty-flask shards (closes #4) — mechanism, add to decisions doc
Shards are stackable bank items. Collecting 5 auto-combines into the flask
unlock (one-time, per flask type). Post-unlock, further shards convert to
100g each on pickup. Rotward flask: 3 sips; sip = cleanse all poison +
poison immunity 15 ticks. Unlocked flasks appear in the Loadout Editor.

### 4. Cold start (closes #16) — product decision
New players get: one free T1 weapon of their chosen style (a "choose your
style" FTUE beat at first launch) + 600g starting gold. Record in economy
§1 and decisions doc. Dev-loadout grant buttons remain but add
// PROVISIONAL: needs dev gate (backlog #25).

### 5. XP/levels (closes #17) — ruling: gone for good
No combat XP or levels, ever. Progression = gear, invocations, collection
log. Any future account-level is cosmetic-only, post-launch, out of scope.
Record in decisions doc under Retired Mechanics.

### 6. Economy edits (closes #3, #18, #23)
- Editorial sweep of economy §3: remove any remaining regular-duel
  language; Tier-1 bosses are the stated on-ramp.
- Uniques sell for a flat 2,000g (replaces the "15% of a T4 piece"
  formula); populate the fenceValues override for Rotfang. Rares stay 0.
- Recompute economy §4's cadence-check row against 500g Tier-1 kills +
  600g start + free T1 weapon; update the printed timings. Report the
  computed first-hour cadence so the M2 playtest question can be judged.

### 7. Ratify M2 provisional shapes (closes #19, #21, #22)
- Armour per-slot prices: Def-weight linear interpolation within the doc's
  tier ranges is now canonical; state the rule in items §5.
- Rares carry Line=None — outside line identity/set bonuses by design.
- Purchase-confirm threshold 2,500g: canonical.

### 8. Ratify M1 provisional numbers (closes #24) — record all in items §1
- Defensive style = flat 20% incoming-damage reduction, additive with gear
  Def; NEW: total mitigation from all sources hard-caps at 50%.
- Boost prayer = +20% Power: canonical.
- Scorch: 3 ticks @ 3/tick. Rend: bleed 4 ticks @ 3/tick + 1.3× base hit:
  canonical DoT baselines.
- Boss Def = 0 is doctrine: bosses mitigate via Evasion and HP, never Def
  points. Add to boss bible Global Combat Grammar: "Bosses have no Def;
  their defensive identity is per-style Evasion."

### 9. Constants sweep follow-up (closes #27, #26)
- Player.PrayerPoints = 99: canonical (document in items §1).
- PrayerDrainCadenceTicks = 9: canonical baseline (1 point per 9 ticks
  while any prayer is active; Leaky Faith invocation modifies this).
- AttackRange.Distant = 8: keep, mark // PROVISIONAL: dead path (#28).
- Delete duels-economy_1.md (byte-identical duplicate).

### 10. Landscape-everywhere mandate (new ruling — stops accruing debt)
The entire app is landscape-only, not just combat.
- Audit every non-combat screen (Bag, Bank, Shop, Loadout Editor,
  Equipment, dev hub) for portrait/vertical-stack layouts; refactor to
  landscape-first compositions per UI bible §5–§9 (left/right panel
  splits, horizontal scrolling grids — never a single vertical column
  assuming a tall viewport).
- Portrait orientation shows a rotate-device overlay; no screen may have
  a portrait layout variant.
- CLAUDE.md, add rule: "Every screen is landscape-first. No
  vertical-stack page layouts assuming a tall viewport; portrait shows a
  rotate prompt. Building a portrait-shaped screen is a defect."
- Report which screens required refactoring and any that structurally
  resist landscape (flag, don't improvise).

Update /agent + changelog. Log as "Backlog batch 1: A+D resolutions,
constants ratification, landscape mandate."
