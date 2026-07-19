# Duels — Gameplay Summary

Real-time 1v1 boss-fighting game (Blazor WASM), landscape mobile-first.
Design authority lives in `/design/*.md` — this file is a quick player-facing
summary, not the spec.

---

## Core Loop (M1 vertical slice)

1. Start a new game, enter your name.
2. Grab a dev loadout (T1 or T2 — dev/debug shortcut; the shop/economy ship
   in M2) or bind your own bar in the Loadout Editor.
3. Fight **The Maggot King** — the game's only boss so far.
4. Win → gold, kill-time stat, personal best. Lose → instant Retry.

---

## Player

- Flat **100 HP**, no character levels (M1 retired the old OSRS-style
  XP/level system — combat is gear-driven, not grind-driven).
- Base hit chance **80%**, modified by attack style and weapon Precision.
- Special energy starts full, regenerates 1%/tick while dueling.
- Prayer points (99, drain 2/tick while a protection prayer is active).

---

## Combat (math v2 — items doc §1)

- **Attack styles**: Accurate (+10% hit) · Aggressive (+20% damage, −10%
  hit) · Defensive (−10% damage, +20% incoming-damage reduction).
- **Weapons** carry flat **Power** (damage per hit) and **Precision** (flat
  hit-chance bonus) — no roll-to-max-hit ramp; variance comes from hit/miss
  and boss mechanics, not a damage dice roll.
- **Armour** carries **Def points**: each point reduces incoming damage by
  0.4%, capped at 40% from gear.
- **Protection prayers** (Melee/Range/Magic) reduce a matching hit by 75% —
  flick them at tick boundaries to eat 0 damage from telegraphed attacks.
- **Boss attacks are scripted, not rolled** — they always land unless
  dodged positionally (see the Maggot King's mechanics below).

---

## Gear (T1/T2 only in M1)

Three style lines — **Warbound** (melee), **Stalker** (ranged), **Occult**
(magic) — each with a T1 and T2 weapon plus 6-slot armour (Helmet/Body/Legs/
Boots/Gloves/Cape). Each weapon has a unique special attack:

| Weapon | Style | Tier | Special |
|---|---|---|---|
| Rustcleaver | Melee | T1 | Lunge — next hit connects from 2 tiles |
| Poacher's Bow | Ranged | T1 | Snipe — +50% damage |
| Cinder Wand | Magic | T1 | Scorch — hit + 3-tick burn |
| Splitter | Melee | T2 | Rend — heavy hit + bleed |
| Bolt Thrower | Ranged | T2 | Pin Shot — hit + delays the boss 1 tick |
| Hexknot Staff | Magic | T2 | Sap — hit + boss damage −10% for 5 ticks |

Armour lines add +1% same-style damage per piece worn (+5% more at 4 pieces;
+10 max special energy at a full 6-piece set).

**Flask belt**: 2 bound slots, Health (+40 HP) and Prayer (+40 points), 3
sips each per fight, free full refill every duel start. Sipping consumes
that tick's action.

---

## Boss: The Maggot King

The only boss in M1 — a bloated, stationary 2×2 mound at the north edge of a
9×9 arena.

- **Phase 1 (100–50% HP)**, 20-tick loop: Bile Spit (magic) → Bile Spit →
  style telegraph → Lash (melee, if you're adjacent) / Grub Volley (ranged,
  if not) → repeat → style telegraph → idle. Watch the forecast icon and
  flick your prayer 2 ticks ahead.
- **Ground eruptions** (independent timer): tiles telegraph for 3 ticks,
  then erupt — unprayable, dodge only. Erupted tiles leave a poison pool
  that eventually dries into permanent, safe **scorch** ground.
- **Perfect Dodge**: vacate a tile on its final warning tick and you're
  never touched — +15 special energy, always a reward, never required.
- **Phase 2 (<50% HP)**: rotation compresses to 14 ticks, eruptions come
  faster and wider. Maggot swarms spawn at 50% and 25% HP — tap one to
  target it, 2 hits kills it, contact bleeds you. **Rot Burst**: the King
  inhales for 4 ticks, then unleashes an unprayable arena-wide blast —
  unless you're standing on scorch. He slumps afterward: +25% damage taken,
  can't act — the fight's biggest damage window.

---

## UI

- **Landscape fight HUD**: HP/prayer orbs top-left, boss plate + style
  forecast top-center, protection prayers + boost prayer on the left thumb,
  weapon action bar + special button on the right thumb, flask belt +
  style toggle nearby.
- **Action bar**: 4 weapon slots, manually bound in the Loadout Editor —
  picking up a weapon never auto-fills a slot. Locked once a fight starts.
- Tap a tile to move (disengages), tap the boss or a swarm add to
  attack/target it.

---

## Tech Stack

- **Frontend**: Blazor WebAssembly (.NET 8), three.js battle renderer.
- **Architecture**: Domain → Application → Infrastructure → Web layers,
  command/handler pattern. See `ARCHITECTURE.md` for the full map.
- **Combat**: items-doc math v2 (`DamageModel.cs`), boss choreography as
  data (`npcs.json`) consumed by a generic rotation-script engine
  (`GameTickService.cs`) — no per-boss code.
- **State**: IndexedDB local-first persistence (`ISaveStore`).
