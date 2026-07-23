# Duels — UI/UX Design Bible

Platform: **mobile, landscape-only**. Art target: **cel-shaded toon style** (Borderlands-2-family: thick ink outlines, hand-painted textures, halftone shading — shaders already in place on characters). The UI must look like it lives in the same inked world as the characters, not like a generic mobile overlay floating on top of it.

---

## 1. UI Design Language

**Everything is inked.** Panels are irregular hand-drawn frames with thick (3–4px at 1x) dark outlines, slightly rough edges, subtle paper/halftone texture fills. No perfect rectangles, no soft drop shadows, no glassmorphism. Buttons look stamped or riveted; pressed state = ink fill inverts + 2px inward shift.

**Type.** Two faces only:
- Display: a chunky, slightly condensed comic-stencil face for headers, damage numbers, boss names (all-caps).
- Body: a clean humanist sans for stats, tooltips, settings (legibility at small sizes beats style here).

**The color system is doctrine.** One palette carries meaning across telegraphs, prayers, weapons, and UI — learned once, read everywhere:

| Meaning | Color | Used on |
|---|---|---|
| Melee | Red-orange | Prayer icon, boss windups, weapon frames, telegraphs |
| Ranged | Green | Same |
| Magic | Blue | Same |
| Unprayable / hazard | Purple with black hatching | Ground eruptions, beams, boulders |
| Safe / reward | Gold | Safe tiles, perfect-dodge glint, loot |
| Poison | Sickly yellow-green | DoT icons, pools |
| Bleed | Deep crimson drips | DoT icons |

Every colored signal also has a **unique shape and audio cue** (colorblind-safe rule inherited from the boss bible: color + shape + sound, always all three).

**Motion.** Snappy and comic: 80–120ms transitions, smear-frame style scale pops on button presses, no easing longer than 200ms anywhere in combat-adjacent UI. Menus can breathe (200–300ms); the HUD never does.

---

## 2. Global UX Principles (Landscape Mobile)

- **Grip model:** two-thumb claw. Left thumb owns **prayers**, right thumb owns **attacks/weapons**. The center of the screen is the battlefield and is tapped by either thumb for movement/targeting.
- **Thumb zones:** all combat-critical buttons live inside the bottom-corner arcs reachable without shifting grip (~⅓ screen width from each corner). Nothing combat-critical in the top half of the screen except passive readouts.
- **Tap targets:** 56dp minimum for combat buttons. Protection prayers (the most time-critical input in the game) traded a dedicated 64dp button for a left-edge strip — see §3.2 — whose zones are ~44dp tall but bleed wider than their visible width to keep the effective touch target generous. 8dp minimum spacing elsewhere.
- **Input latency budget:** visual + haptic acknowledgment within 50ms of any tap; the 0.6s tick can absorb server/logic resolution, but the *feel* of responsiveness lives in that first 50ms.
- **Input buffering:** taps within the last 150ms of a tick queue for the next tick instead of dropping. Prayer and weapon swaps resolve on the tick they're tapped (they're instant per combat rules).
- **Attack cadence:** attacks fire on a per-weapon cooldown (2–4 ticks by archetype), not a flat once-per-tick rate; the cooldown is global and persists across weapon swaps.
- **Safe areas:** full notch/punch-hole compliance; HUD anchors to safe-area insets, battlefield renders full-bleed underneath.
- **Left-handed mode:** full HUD mirror in one setting.
- **HUD edit mode:** every HUD cluster can be dragged, scaled (75–125%), and opacity-adjusted, with per-element reset. Standard for mobile action games and non-negotiable for a game about split-second taps.

---

## 3. In-Game HUD

### 3.1 Layout map

```
┌────────────────────────────────────────────────────────────────┐
│ [HP orb]                [ BOSS NAME + HP BAR + phase pips ]  [⚙]│
│▓[Prayer orb]                  [style forecast icon]              │
│▓[debuff icons]                                                  │
│▓                                                                │
│▓                       B A T T L E F I E L D                    │
│▓                       (full-bleed, 3D, tiles)                  │
│▓                                                                │
│▓       [BOOST]                                    [weapon 1]    │
│        [style toggle]                             [weapon 2]    │
│  [flask][flask]                                    [weapon 3]    │
│                                                    [weapon 4]    │
│                                    [SPECIAL ATTACK — big]        │
└────────────────────────────────────────────────────────────────┘
```
(`▓` marks the left-edge prayer strip — see below. Exact positions are the
defaults; edit mode can rearrange. Weapons stack as a vertical arc under
the right thumb with the special attack button largest, at the natural
resting position of the right thumb.)

### 3.2 Elements in detail

**Player orbs (top-left).** OSRS-style twin orbs: HP and prayer points. Numeric value inside, radial drain. HP orb pulses and the screen edge vignettes red below 30%. Debuff icons (poison, bleed with stack count, petrify stacks) line up beneath the orbs — each icon shows remaining ticks as a radial wipe.

**Boss plate (top-center).** Boss name in display type, chunky inked HP bar, **phase pips** marked at the boss's threshold percentages (players learn to play around pips). Below it, the **style forecast icon**: when a boss telegraphs its next attack style (the standard 2-tick warning), the incoming style's icon and color flash here — a HUD echo of the in-world tell for mobile readability. Default ON; can be disabled in settings for purists, and the invocation list may include *No Forecast* (disables it for bonus raid level).

**Cast bar (top-center, under boss plate).** Appears only for channeled/fused boss actions (Transfusion, Rot Burst inhale, Gaze charge): a tick-segmented bar so players can count remaining ticks. Segmented, not smooth — the game runs on ticks and the UI should teach that.

**Protection prayer strip (left edge)** *— M1 playtest revision, replaces the original three 64dp arc buttons.* A near-transparent vertical strip hugging the screen's left edge: ~76px visible, three equal zones stacked top→bottom — **Magic / Ranged / Melee** — each tinted with its doctrine color and carrying a small icon. The touch target is wider than what's visible (~100px, bleeding past the true edge) so a thumb resting right at the bezel still lands cleanly inside a zone; outside the strip, every tap passes straight through to the battlefield beneath it. States: inactive (faint doctrine tint), active (brighter fill + a 220ms brightness flash on the tick it's toggled + a short per-style haptic pulse), insufficient prayer points (dimmed, non-interactive). Tap = toggle, resolves same tick. Only one protection active at a time — tapping a new zone switches instantly (no need to un-toggle first; this single rule is what makes flicking playable on touch). This traded the old buttons' bigger, more discoverable tap targets for edge-anchored reachability and screen space back for the battlefield — worth revisiting if playtesting shows mis-taps between zones.
**Optional "hold-to-pray" mode** (settings): prayer active only while finger is held — the flicking purist's mode, drains points only while held.

**Overhead prayer icon (in-world, above the actor's head).** Whichever protection (or boost) is active shows as a small doctrine-colored icon-in-a-ring floating above the player's head in the 3D scene itself — not just the HUD — so it reads at a glance without looking away from the fight. Same renderer is shared for bosses (mirror-match fights give the boss its own visible prayer, per §3-in-boss-designs' "boss's prayer bar" framing) once a boss actually grants one; M1's Maggot King doesn't pray, so only the player shows one today.

**Boost prayer (standard 56dp button, weapon-arc side).** Toggle, drains prayer points while on, gold glow when active. No longer grouped with the protection buttons since those moved to the edge strip — it now sits as an ordinary action-bar button alongside the style toggle.

**Weapon slots (right arc, 4 × 56dp).** The RS3-style action bar — see §4. Each slot shows the weapon's inked icon, its **attack-style color as the frame**, and a thin durability-style bar repurposed as *nothing* (reserved). Current weapon = enlarged 15% + gold underline. Tap = swap (resolves same tick, max one swap per tick; extra taps buffer). Empty slot = dashed outline, tapping opens the Loadout Editor out of combat and does nothing in combat.

**Special attack button (large, 72dp, right thumb rest position).** Shows the *current weapon's* special icon (updates on swap) over a radial special-energy fill. Full = pulsing gold rim. Tap = fire special this tick if affordable; insufficient energy = shake + dull thunk. During a Spec Clash (Grand Duelist), this button becomes the clash meter.

**Attack style toggle (compact, 3-state).** Accurate / Aggressive / Defensive as a small segmented pill near the weapon arc. Used occasionally mid-fight, constantly between fights — mid-sized target (44dp per segment) is acceptable. Selected segment fills in the doctrine gold.

**Flask belt (2 slots, 56dp each, beside the style toggle).** Each slot shows its flask's icon with **sip pips** (filled/empty dots) under it. Tap = sip this tick (consumes your action that tick — the button dims your attack inputs for that tick as feedback). Empty flask = grayed with cracked-glass overlay. Belt contents are bound in the Loadout Editor; a 3rd slot appears when unlocked. In runs, sips persist across fights — the pips are the run's resource meter.

**Engagement indicator.** When engaged, the target boss has a subtle inked reticle and your character's weapon trails faintly; when disengaged (M1 revision — "persistent target lock": only tapping the indicator itself does this, never movement), the reticle breaks apart and a small "sheathed" icon appears by the weapon arc, tappable to toggle. This state must be unmistakable at a glance — the whole disengage/re-engage mechanic depends on it.

**Tick metronome (optional, default off).** A tiny pulsing dot next to the special button that beats once per tick. For the tick-perfect crowd. Settings toggle + optional haptic tick.

### 3.3 Battlefield interaction

- **Tap a tile** → move (pathing dots preview the route for multi-tile paths, one dot per tick of travel).
- **Tap the boss** → engage/attack.
- **Tap an add** (e.g. a maggot swarm) → switches target to it and engages; basic attacks route to the add until it dies, then target reverts to the boss automatically. Same tap-to-(re)engage contract as the boss, per target. The tap target is the whole tile the add occupies, not just its (small, deliberately unshowy) model — mobile precision tapping on a tiny model was a real playtest complaint.
- **Tap elsewhere while engaged** → move there; target lock persists. Attacks fire only on stationary ticks — movement defers, never cancels. Disengage is explicit: tap the engagement indicator to sheathe; tap the boss to re-engage.
- Tap feedback: tapped tile flashes an ink ring; invalid tiles (rubble, walls) show a red X stamp.
- **Telegraphs on tiles:** hazard tiles fill with hatched purple ink as the fuse burns, one hatch band per tick; the final fuse tick flashes the tile's outline white — that white flash is the universal "Perfect Dodge now" signal across every boss. Safe tiles (Rot Burst scorch, Downdraft corners) glow gold.
- **Damage numbers:** comic pops. Player hits in white with style-colored outline; crits/specials 150% size with a starburst; DoT ticks small and muted; damage *taken* in red drifting down. Perfect Dodge triggers a gold "+15" glint at the player's feet.
- **Blocked hits:** a hit fully negated by a matching protection prayer (per the Global Combat Grammar's 100%-block rule) never shows a "0" numeral — that reads as a weak hit, not a save. It shows a slashed ring instead, colored in the doctrine that blocked it (the same color already carrying that meaning on the prayer strip, the overhead icon, and the telegraph glow), slightly smaller than a real hitsplat and without the hit-flinch reaction, since nothing actually landed.
- **In-world HP bars:** a small bar floats above both the player's and the boss's head at all times during a fight (above the overhead prayer icon, never overlapping it), fill color stepping green → amber → red as HP crosses 50%/25% — a second, always-visible read of health beyond the boss plate/player HUD box, since a mobile screen at combat zoom doesn't always keep the corner UI in the same glance as the action.

### 3.4 HUD states

- **Out of combat (hub arena, pre-pull):** weapon arc and prayers visible but dimmed 60%; a "FIGHT" banner marks the boss's aggro zone.
- **Run mode reward pick:** overlay (see §8.3) pauses the run between fights.
- **Death:** screen desaturates to inked grayscale, comic "DEFEATED" stamp, then results (§8.5).
- **Pause (solo real-time = pause allowed):** ⚙ button top-right; pausing mid-boss dims the field and shows Resume / Settings / Abandon Fight. In the minigame, Abandon = run ends.

---

## 4. Action Bar & Loadout System (the RS3-style decision)

**Decision: weapon slots are manually assigned, never auto-filled.** Picking up or buying a weapon puts it in the bank/inventory — it appears on the bar only when the player binds it. The bar is a deliberate pre-fight decision and a skill surface, exactly like an RS3 action bar. Rationale: with Mirrorhide-style fights, *which four weapons and in what order* is strategy; autofill would erase it, and muscle memory demands slots that never move on their own.

### 4.1 Loadout Editor (screen)
- Left: the 4-slot bar, large. Right: filterable list of owned weapons (filter by style, sort by stats).
- Drag weapon → slot to bind; drag slot → slot to reorder; drag off → unbind. Long-press a weapon for its stat card.
- Also binds: the **flask belt** (2–3 slots, from owned flask types) and default attack style per weapon (a weapon can remember "always swap in as Aggressive" — small but beloved QoL).
- **Presets: 5 saved bars**, named + icon. One-tap apply. A per-boss "last used here" chip appears on each boss's pre-fight screen ("Load the bar you beat Mirrorhide with last time?").
- **Minigame note:** in runs, the bar starts empty except slot 1 (starter weapon); reward picks bind automatically to the next empty slot (the *only* autofill exception, because there's no bank in a run) and can be rearranged between fights via a mini-editor on the reward screen.

### 4.2 Rules
- Bar contents are locked when a hub fight starts (no mid-fight rebinding) — the fight tests the bar you brought.
- Weapons on the bar are logically "carried": equipping stats apply only to the *wielded* weapon; the rest are swap options.
- Bank deposit-all never strips the active bar's weapons (guard rail against the classic "banked my switches" tilt).

---

## 5. Menu Architecture (full map)

```
Title Screen
└─ Main Hub (home)
   ├─ FIGHT  → Boss Roster → Boss Detail (invocations, presets) → Arena
   ├─ GAUNTLET (minigame) → Run Lobby (invocations) → Run
   ├─ BANK
   ├─ EQUIPMENT (paper-doll + preview)
   ├─ LOADOUTS (action bar editor)
   ├─ SHOP (gold) / EMPORIUM (run-currency cosmetics)
   ├─ COLLECTION LOG
   ├─ PROFILE (stats, titles, achievements) → Leaderboards
   └─ SETTINGS
```

The Main Hub is a diegetic scene — your toon character standing in a grungy arena backstage, with the menu as painted signage/props (bank vault door, weapon rack for loadouts, poster wall for the roster). Buttons are still buttons (labeled, consistent), but the frame is the world. Persistent top bar on every non-combat screen: gold count, run-currency count, player name/title, settings gear.

---

## 6. Boss Roster & Pre-Fight

### 6.1 Roster screen
- Horizontal scroll of **boss cards** — each a hand-painted poster: boss art, name, tier stripe, your killcount stamp, best time, and rare-drop silhouette (fades in as you get closer to owning it; gold-framed once obtained).
- Locked bosses show as torn "coming soon" posters with their unlock condition printed.
- Card states: New (never fought), In Progress, Cleared (base), Hard Unlocked (invocation flame icon).

### 6.2 Boss Detail / pre-fight screen
- Left: full boss art + flavor line + your stats vs this boss (kc, best time, deaths, highest raid level cleared).
- Right: **Invocation panel** (ToA-style): scrollable list of this boss's modifiers, each a toggle chip with name, one-line effect, and +raid-level value. Total **Raid Level** displayed as a big stamped number that recolors by band (bronze/silver/gold/inferno). Loot preview row updates live with the raid level (which drop tiers/rares are enabled at this level — the "rare requires 150+" gate is visible here, not discovered by disappointment).
- Bottom: loadout preset chip ("using: *Mirrorhide bar*" — tap to change), prayer point / HP top-up (auto-restored free in hub; shown for clarity), and the big **FIGHT** button.
- Locked invocations show their unlock condition ("Clear at raid level 100").

---

## 7. Bank

The carried bag is 28 slots (fixed). The bank is the unbounded store; the
bag is the constraint the bank exists to relieve.

OSRS's bank is the genre's best inventory UI; adapt it, don't reinvent it.

- **Grid** of item slots (6 rows visible, vertical scroll), items as inked icons with stack counts.
- **Tabs:** up to 9 + "All", each tab's icon = its first item. Drag item to tab header to move. First-time users get 3 pre-made tabs (Weapons / Armour / Materials) they can ignore or delete.
- **Search:** persistent search field, instant filter-as-you-type.
- **Quantity toggle:** 1 / 5 / 10 / X / All as a segmented control; X remembers last custom value. Long-press an item = radial context menu (Withdraw-X, Examine, Favorite, Move to tab).
- **Favorites row** pinned at top.
- **Placeholders:** withdrawing the last of an item leaves a ghost icon holding its slot (toggleable per item and globally). Bossing players organize banks; placeholders are why it stays organized.
- **Deposit modes:** deposit-all button (respects the action-bar guard rail from §4.2), deposit-worn (excluding bar weapons, with confirm).
- **Bank ↔ Loadout bridge:** a "Prepare Loadout" button opens the preset list; picking one withdraws/equips everything that preset needs in one tap and flags anything missing in red. This single button is the "gear up for a boss" loop compressed to one action.

---

## 8. Equipment, Runs, and Result Screens

### 8.1 Equipment screen (paper-doll)
- **Center-left: 3D toon character preview** — the shader showcase. Slow auto-rotate, drag to spin, pinch to zoom. Idle animations cycle; a button plays the equipped weapon's attack + special animation (players *will* pose for screenshots; give them a "hide UI" camera button).
- Slots arranged around the preview: helmet, body, legs, boots, gloves, cape + wielded weapon. Empty slots = dashed silhouettes.
- **Right panel — stat sheet:** attack bonus per style, defense per style, special-energy modifiers, DoT-related stats, prayer bonus if it exists. Grouped, color-coded by doctrine colors.
- **Compare flow:** tapping any owned item (from the bottom item drawer) shows its card with green/red delta arrows vs. currently equipped. Equip = one tap from the card.
- **Cosmetic override tab:** transmog layer — visual slot separate from stat slot (this is where run-earned skins live, keeping "prestige not power" visible on the character). Includes title selection shown under the player's name.

### 8.2 Gauntlet (minigame) lobby
- Big painted gate + queue button. Invocation panel identical in layout to the hub's (same component, curated run-global list) with the payout multiplier shown as the stamped number.
- Panels for: run-currency balance, Emporium shortcut, your best depth / best raid-level clear, and the flex-reward track (cape/title milestones with their invocation requirements printed).
- "How it works" card for first-timers: enter naked, build from drops, death ends the run.

### 8.3 In-run reward pick (overlay)
- After each run fight: time freezes, three hand-painted **reward cards** fan out (weapon / armour piece / prayer unlock / energy boost per the design doc). Card face: icon, name, one-line effect, style color frame. Tap to flip-confirm, second tap locks it.
- Below the cards: current run bar + equipment mini-view and a compact rearrange handle (the §4.1 mini-editor).
- A skull stamp shows fights cleared / remaining; next enemy silhouette teases the upcoming draw.

### 8.4 Victory / loot screen (hub)
- Chest bursts open comic-style; items fly to a loot row. **Rares get the full fanfare:** screen ink-flash, unique jingle, oversized card, and an (optional, off by default for streamers? no — default on) global-style toast "You received …". Kill time, raid level, and personal-best stamp shown. Buttons: Re-fight (same invocations), Change Invocations, Leave.

### 8.5 Death / run-end screen
- Hub death: "DEFEATED" stamp, what killed you (attack name + the boss bible's mechanic name — deaths should teach), fight stats, instant **Retry** (front and center; failure costs a tap, per the design doc's cheap-failure rule).
- Run end: depth reached, currency earned (with the depth-based drip math visible), build recap (the bar you assembled), and a "next time" nudge surfacing one invocation or milestone within reach. Instant Re-queue button.

---

## 9. Shops

**Gold Shop (progression gear).** Categorized grid (weapons by style, armour by slot), each item card showing stats + compare-delta vs equipped, gold price, and a "why buy" tag (e.g., "Unlocks viable Magic swaps for Mirrorhide"). Buyback tab for accidental sales. No confirm-spam: single confirm on purchases above a threshold only — **2,500g, canonical** (backlog resolution batch 1 §7).

**The Emporium (run-currency, prestige-only).** Distinct visual identity — gaudier, trophy-hall vibe. Sells: cosmetic skins, transmog sets, titles, weapon VFX trails, arena victory poses, starting-loadout variants *for the minigame*, and the flex milestones display (not purchasable — earned, but displayed here to be coveted). Every item card states "Cosmetic — no stats," reinforcing the prestige-not-power law.

---

## 10. Collection Log, Profile, Leaderboards

**Collection Log.** The bossing-audience retention screen. One page per boss (art header, drop list as silhouettes → filled icons, kc, rare-drop count), plus pages for run rewards and milestones. Completion percentage per page and global. New-unlock toast: "Collection log updated" with the log-book stamp animation.

**Profile.** Player card (name, title, transmog preview), per-boss stat table (kc, best time, deaths, highest raid level), streaks, total playtime, account milestones. Shareable card export (screenshot-formatted) — free marketing.

**Leaderboards.** Per boss: best kill time at each raid-level band. Minigame: best depth (endless variant later) and fastest completion per invocation band. Friends filter first, global second. Anti-cheat note flagged for engineering.

---

## 11. Settings & Accessibility

Grouped: **Graphics** (quality presets, fps cap 30/60/120, shader intensity, screen shake, damage number density), **Audio** (master/music/sfx/UI sliders; *audio cues are gameplay* — a dedicated "combat cues" slider kept separate from general SFX), **Controls** (HUD edit mode entry, left-handed mirror, hold-to-pray toggle, tick metronome + haptics, tap-vs-drag movement sensitivity), **Accessibility** (colorblind alt-palettes — the shape+sound doctrine already carries meaning, palettes are the third layer; telegraph opacity boost; reduced flash mode; text size; forecast widget toggle), **Account** (cloud save status, linked accounts, restore purchases placeholder, support/privacy links).

---

## 12. FTUE / Onboarding (progressive HUD disclosure)

The HUD is *earned*, not dumped:

1. **First duel:** only HP orb + battlefield. Teaches tap-to-move, tap-to-attack, disengage. One overlay line each, ink-arrow pointers.
2. **First mini-boss:** protection prayer buttons slide in when the boss first telegraphs; forecast icon introduced ("match the color").
3. **Second weapon acquired:** weapon arc appears; the Loadout Editor tutorializes binding it (the manual-slot rule is taught by doing, not text).
4. **First full special meter:** special button lights up with a one-time callout.
5. **First eruption hazard:** telegraph reading + the white-flash Perfect Dodge cue ("move on the flash for a bonus").
6. Attack styles, boost prayer, and bank taught at their first natural moments (shop visit, prayer shortage, inventory full).

Every tutorial overlay is skippable and replayable from Settings → Help. Nothing else about the HUD ever moves after it's introduced — muscle memory is sacred.

---

## 13. System-Wide Components & Open Flags

**Shared components:** toast stack (top-center, inked stamps: drops, log updates, achievement pops — max 2 visible, queue the rest), confirm dialogs (destructive actions only: item destroy, prestige-type resets, run abandon), currency header, loading transitions (ink-splat wipe, boss-art loading cards with mechanic tips from the boss bible), connection-quality dot near the ⚙ (it's a real-time game; players deserve to know when lag, not skill, killed them — deaths during disconnect get a "connection lost" tag on the death screen).

**Open flags (decide before UI freeze):**
1. **Consumables — RESOLVED:** the Flask Belt (fixed sip charges, sip costs your action for the tick, specialty flasks as boss drops, sips persist across run fights). Full spec in the design decisions doc.
2. **Monetization:** no store designed. If one ships, Emporium's cosmetic-only framing is the natural home; reserve one Main Hub sign slot. Explicitly out of scope here.
3. **Multiplayer/spectate:** all screens designed solo-first; leaderboards are the only social surface at launch.
4. **Camera:** assumed fixed-angle ¾ isometric with slight zoom during finales; if the camera design differs, HUD anchors are unaffected but telegraph readability should be re-validated on the smallest supported screen (test device: 5.5", 16:9 — if telegraphs read there, they read everywhere).
