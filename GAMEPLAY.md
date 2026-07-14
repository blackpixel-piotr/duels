# Duels — Gameplay Summary

Text-based browser RPG (Blazor WASM), OSRS Duel Arena simulator. Mobile-first.

---

## Core Loop

1. Start a new game, enter your name
2. Type commands or tap quickslot buttons to fight NPCs
3. Win → earn gold, unlock the next NPC on the ladder
4. Spend gold in the shop on better weapons
5. Climb the ladder until you beat the Duel Champion

---

## Player

- Fixed stats: 99 Attack / 99 Strength / 99 Defence / 99 HP
- Starts with **10,000 gold** (testing value — lower for release)
- Special energy starts at 100%, recharges +10% per attack, consumed by specials
- No armour — weapon choice is everything

---

## Combat

Pure OSRS formulas. Each round:

1. **You attack** — hit chance from your weapon bonuses vs NPC defence; damage is random 0 → max hit
2. **NPC retaliates** — same formula reversed
3. Repeats until one side dies; you respawn at full HP on defeat (no perma-death)

**Max hit formula** (99 Str, Accurate style):
```
effective_str = 99 + 3 (style) + 8 = 110
max_hit = floor(0.5 + 110 × (strength_bonus + 64) / 640)
```

Examples: bare fist ≈ 11, rune scimitar (+66) ≈ 22, AGS (+132) ≈ 34 base / **42 with spec**

---

## Weapons

| Weapon | Str bonus | Max hit | Price | Special |
|---|---|---|---|---|
| Rune Scimitar | +66 | ~22 | 200g | — |
| Dragon Scimitar | +67 | ~22 | 600g | — |
| Dragon Dagger | +40 | ~18 | 800g | 2 hits, +15% accuracy, 25% energy |
| Abyssal Whip | +82 | ~25 | 2,500g | 25% energy |
| Armadyl Sword | +85 | ~26 | 5,000g | — |
| Dragon Claws | +56 | ~21 | 15,000g | 4 hits at ½ each, 50% energy |
| Bandos Godsword | +132 | ~34 | 30,000g | 2 hits, 50% energy |
| Zamorak Godsword | +132 | ~34 | 40,000g | 2 hits, 2nd always lands, 50% energy |
| Saradomin Godsword | +132 | ~34 | 50,000g | 1 hit, heals 50% of damage dealt, 50% energy |
| Armadyl Godsword | +132 | ~42 | 65,000g | 1 hit ×1.25 dmg, +37.5% accuracy, 50% energy |
| Scythe of Vitur | +75 | ~27 | 150,000g | 3 hits, 100% energy |

Typing a weapon shorthand (`ags`, `dds`, `whip`, `claws`, `scythe`, `bgs`, `zgs`, `sgs`) auto-equips it and fires its special if you're in a duel.

---

## NPC Ladder

Unlocked in order by winning each fight.

| # | Name | HP | Str modifier | Gold reward |
|---|---|---|---|---|
| 1 | Swashbuckler Pete | 50 | +30 | 30g |
| 2 | Iron Barbarian | 65 | +50 | 100g |
| 3 | Desert Bandit | 70 | +40 | 300g |
| 4 | Arena Gladiator | 80 | +82 | 1,000g |
| 5 | Pirate Corsair | 85 | +56 | 3,500g |
| 6 | Frenzied Berserker | 90 | +100 | 12,000g |
| 7 | Battle Warlord | 95 | +115 | 40,000g |
| 8 | Duel Champion | 99 | +132 | 120,000g |

All NPCs have 99/99/99 stats. Difficulty scales through higher weapon modifiers (better hit chance and higher max hit).

---

## Boss: The Maggot King

A standalone modern-mechanics boss (not on the ladder) — available from the start via `duel maggot_king`.

- **Style rotation**: cycles Crush → Ranged → Magic every 3 attacks; switch protection prayers with the rotation
- **Maggot burrow**: periodically marks tiles around you (flashing warning). After 2 ticks they **erupt for 22** — protection prayers do NOT help; move off the tiles
- **Poison pools**: erupted tiles become pools for 8 ticks; standing in one costs 4/tick and eruptions poison you
- **Phase 2** at 50% HP: rotation quickens, waves get bigger and more frequent
- **Loot**: heavy gold, and a 1-in-10 **Maggot Crown** (best-in-slot prayer helmet)

The fight is the full modern-OSRS checklist: prayer dance, positioning, and attack uptime all at once.

---

## UI

- **Desktop**: 3-column layout — Stats | Combat log + input | Inventory
- **Mobile**: tabs switch between Stats / Combat / Items
- **QuickSlot bar**: dynamic weapon buttons (inventory + equipped) + fixed slots: Special, Shop, Stats, Inspect NPC, NPCs list
- **Retry button** appears after each duel to rematch the same opponent
- Terminal-style combat log, colour-coded by event type (hit / miss / system / loot)

---

## Commands

All commands work with or without a leading `!`.

| Command | Description |
|---|---|
| `duel <npc>` | Start a duel (e.g. `duel swashbuckler`) |
| `attack` | Basic attack |
| `spec` | Fire equipped weapon's special attack |
| `ags` / `dds` / `whip` / `claws` / `scythe` / `bgs` / `zgs` / `sgs` | Equip weapon + fire special |
| `shop` | Browse the weapon shop |
| `buy <item>` | Buy an item (e.g. `buy dragon_dagger`) |
| `equip <item>` | Equip an item from inventory |
| `stats` | View your stats |
| `inspect npc` | Inspect the current opponent |
| `npcs` | List unlocked opponents |
| `help` | Show command list |

---

## Tech Stack

- **Frontend**: Blazor WebAssembly (.NET 8)
- **Architecture**: Domain → Application → Infrastructure layers, command/handler pattern
- **Combat**: OSRS-accurate formulas (`CombatCalculator.cs`)
- **State**: In-memory (single session, no persistence)
