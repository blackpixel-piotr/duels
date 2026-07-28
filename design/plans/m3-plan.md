# M3 — Combat Grammar as Systems + Bosses 2–4: Technical Plan

Per the implementation brief (§"M3 — Combat Grammar as Systems + Bosses 2–4"):
> Extract/complete shared systems (working agreement #4), then Hive Matron,
> Mirrorhide, Bloodtithe per the bible. Boss roster screen + pre-fight screen
> (without invocations panel).
> **Playtest question:** does each boss feel like a different game?

Per CLAUDE.md's milestone workflow, this is the plan; implementation starts
only after it's reviewed.

---

## 1. Current-state audit (what M3 builds on)

**The "shared systems" premise doesn't fully hold today.** `GameTickService.cs`
(1,349 lines) contains `ProcessBossScript`, `ResolveBossAttack`,
`ProcessMasterScript`, `ResolveRotationStep`, `MasterAttack`,
`ProcessHazardResolution`, and `ProcessAdds` — these are Maggot King's
mechanic *implementations*, living directly in the tick service. What's
genuinely data-driven and boss-agnostic in shape is only the rotation
*schedule* (`BossScript`/`BossPhaseDef`/`RotationStep`/`EruptionDef`/
`RotBurstDef`/`SwarmWaveDef` in `NpcTemplate.cs`) — but the mechanic
*vocabulary* those records assume (one ground-hazard type, one
arena-wide-blast type, one swarm-add type) is Maggot King's specific kit,
not a general toolkit yet. Concretely:

- **Perfect Dodge** is one inline check, wired only to eruption-tile-vacate
  (`GameTickService.cs` ~L873–879) — not a general "vacate a hazard on its
  final fuse tick / sidestep a locked projectile on its landing tick" hook.
- **Punish window** is `NpcInstance.SlumpTicksLeft`/`InPunishWindow`
  (`NpcTemplate.cs` ~L236, ~L250) — built and named specifically for Rot
  Burst's slump, not a boss-agnostic "vulnerable, cannot act, +25% dmg
  taken for N ticks" state any mechanic can trigger.
- **DoT is two parallel, non-shared implementations**: `GameState.PlayerPoisoned`
  (player-side, from Maggot King's pools) and `NpcInstance.PoisonStacks`
  (boss-side, from Rotfang, added in backlog batch 1) — no shared
  stacking-DoT primitive sits under either.
- **Style rotation** comes only from a boss's own fixed-tick table today —
  nothing lets a boss's next attack style depend on anything the *player*
  did (Mirrorhide's whole identity).

**The command layer is already boss-agnostic**: `StartDuelCommand` takes a
boss-id string; only `Game.razor:157` hardcodes the literal `"maggot_king"`.
Wiring a real boss picker is cheap once new `NpcTemplate`s exist.

**Item data is in unusually good shape already.** Backlog batch 1's
unique-quality rule (items doc §3, "closes the 'no stats for uniques' gap
for all 8") already stats every boss's unique *and* every boss's rare (§4) —
Hive Matron's Chitin Recurve/Hivepiercer, Mirrorhide's Prism Wand/Mirrorshard
Staff, and Bloodtithe's Leech Blade/Tithebound Cuirass all have full
Power/Speed/Precision or Def numbers, doc-sourced, ready to ingest. M3
should not hit the surprise M2 hit with Rotfang (stats missing entirely).

**A real content gap remains, though, in the same shape as Maggot King's
original one**: items doc / economy doc §5 describe Common/Uncommon loot
*slots* generically ("sellables, materials, off-tier gear, shards") but no
document gives named Common/Uncommon rows for Hive Matron, Mirrorhide, or
Bloodtithe — only Maggot King's got authored, in backlog batch 1 §2. See
§10 Q1.

**SaveData is schema v3** (`SaveData.cs`): one `PersonalBestKillTicks` field,
implicitly Maggot-King-only, no kill count, no death count, no per-boss
breakdown. Roster/pre-fight screens (UI bible §6) need per-boss stats, so
this needs a v4 addition.

**Hub has no boss selection at all** — `HubMenu.razor:4-9` is a single
hardcoded "FIGHT THE MAGGOT KING" card.

---

## 2. Workstream A — Shared combat systems: extraction and completion

This is the brief's "extract/complete shared systems" line, and the actual
bulk of this milestone. Build these against all three new bosses' needs up
front — not just Hive Matron's — so Mirrorhide/Bloodtithe don't force a
second extraction pass later.

1. **Generic hazard/telegraph framework.** Pull the mark→fuse→resolve→
   optional-lingering-effect shape out of `EruptionDef` specifically, so
   Hive Matron's Sting Lob venom splash and Pin's charge-line can be
   authored as data rather than new bespoke `GameTickService` methods.
2. **Perfect Dodge, generalized.** Needs to fire from any hazard's final
   fuse tick and any locked-projectile's landing tick — not just eruption
   tiles — so Hive Matron's Pin and Bloodtithe's Scythe-Arc-miss aren't
   each a hand-copied check.
3. **Punish window, generalized.** `SlumpTicksLeft`/`InPunishWindow` becomes
   a boss-agnostic "vulnerable, cannot act, +25% dmg taken for N ticks"
   state any mechanic can start — needed by Hive Matron's Pin-into-wall,
   Mirrorhide's Shatter reward, and Bloodtithe's missed Scythe Arc.
4. **DoT, unified.** Bloodtithe's bleed (player-side stack, **correctly
   prayed hits apply no stack**, distinct from poison) needs a real shared
   primitive — not a third one-off field next to `PlayerPoisoned`/
   `NpcInstance.PoisonStacks`. Proposed shape: a stacking-DoT type
   parameterized by (damage/tick, max stacks, duration, refresh-on-reapply,
   prayer-negates-application y/n), with poison (both sides) and the new
   bleed as instances of it. **This is a refactor of two already-shipped,
   tested mechanisms**, not purely additive — needs its own before/after
   pass keeping `MaggotKingTests`/`RotfangPoisonTests` green.
5. **Style-driven mechanic hooks.** Mirrorhide's Echo Offense (its next
   attack style = whatever style the player last hit it with) and
   Attunement/Shatter need a "boss reacts to the player's last attack
   style" hook that doesn't exist — today a boss's style only ever comes
   from its own rotation table.
6. **Boss movement AI beyond stationary/chase.** Hive Matron's
   preferred-range-3–5 + periodic 3-tile dash-reset is a new AI mode;
   today `NpcInstance` only knows "stationary" (Maggot King) or generic
   chase (`DummyStyle`, itself dead for real content per backlog #28).
7. **Non-swarm adds.** Hive Matron's drones (3 HP, orbit at radius 2,
   body-block melee lanes) extend `AddInstance` — today swarm-only, 1 HP,
   dies-in-one-hit, walks straight at the player. Real engine work, not
   a data row.
8. **Interactive arena tiles.** Bloodtithe's Font tiles (player-triggered,
   purges bleed, 8-tick cooldown per use) are a new arena-object category —
   nothing today lets the *player* trigger a tile; only boss-authored
   hazard/scorch tiles exist.
9. **Boss facing/orientation.** Bloodtithe's 90°/tick turn-toward-player
   with a back-tile exemption + damage bonus is an entirely new concept;
   `NpcInstance` has no orientation field at all today.
10. **Interruptible channels.** Bloodtithe's Transfusion (5-tick heal
    channel, interrupted only by a special-attack hit) is a new variant of
    the channel pattern Rot Burst introduced — Rot Burst's inhale isn't
    interruptible.
11. **Reflect windows.** Mirrorhide's Reflection (3-tick channel → 6 ticks
    reflecting 50% of one attuned style's damage back) is new.
12. **Special-attack echo (Copycat).** Mirrorhide replays the player's
    last-used special with boss numbers. **Scope note**: only the six M1
    specials are actually dispatched today (backlog #13 — T3/T4 specials
    are data-only); Copycat can only ever copy a special the player could
    actually land, so it naturally stays scoped to the six wired ones.
    This does **not** require closing #13 first — flagging so it isn't
    read as a hidden dependency.
13. **Untargetable/cloak status.** Mirrorhide's 3-tick cloak (untargetable,
    repositions behind the player) is new.
14. **Knockback.** Hive Matron's Tail Stab (2-tile knockback on adjacency
    punish) — no knockback mechanism exists in the codebase today; new.

---

## 3. Workstream B — Hive Matron (Tier 1)

Boss bible §2. Arena 11×11. Movement AI (A.6). Core kit: Dart Volley
(standard ranged auto), Sting Lob (arcing, lands on cast-tile, venom splash
via A.1), Pin (line-charge, knockback A.14 + stun, Perfect Dodge-eligible
via A.2, missed-into-wall punish window via A.3). Chitin Guard: a new
periodic self-buff state (−50% incoming ranged/magic, 8 ticks). Drones (A.7)
spawn in pairs at 75/50/25% HP. Phase 2 (Frenzy): dash damage trail, Dart
Volley becomes a 3-round burst requiring *held* prayer rather than a flick
(see §10 Q2), Pin double-chains.

Anti-camping check: Tail Stab (2-tick adjacency) punishes face-tanking;
Sting Lob (lands on the player's cast-tile regardless of range) and Pin
punish max-range passivity.

Drop niche: Chitin Recurve (unique) + Hivepiercer (rare) — both fully
statted already (items doc §3/§4), ingest per M2's Workstream A pattern.
Common/Uncommon rows: blocked, see §10 Q1.

---

## 4. Workstream C — Mirrorhide (Tier 2)

Boss bible §3. Arena 9×9. Cloak (A.13). Attunement/immunity tracking is
Mirrorhide-specific new state, built on the generalized punish-window
primitive (A.3) for its Shatter reward. Echo Offense (A.5). Attack kit:
Echo Strike, Prism Sweep (frontal cone), Reflection (A.11). Phase 2:
tighter attunement window (3 hits, not 4; 10-tick immunity), Copycat (A.12),
cloak-into-pounce (Perfect Dodge-eligible).

Drop niche: Prism Wand (unique) + Mirrorshard Staff (rare) — both fully
statted. Common/Uncommon rows: blocked, see §10 Q1.

---

## 5. Workstream D — Bloodtithe (Tier 2)

Boss bible §4. Arena 9×9 + 2 Font tiles (A.8). Relentless-walk movement
(1 tile/2 ticks — simpler than Hive Matron's A.6, can land first if useful
as a warm-up for the movement-AI extraction). Bleed DoT (A.4), correctly-
prayed hits apply no stack. Tithe aura: new proximity life-drain-per-tick
effect. Facing/orientation (A.9). Attack kit: Scythe Arc (frontal 3-tile,
missed = punish window via A.3), Blood Lance (line-pierce ranged), Transfusion
(A.10). Phase 2: Crimson Pact (self-buff, HP-for-speed trade), bleed cap
raised to 8, Harvest (consumes all current bleed stacks for damage —
countered by routing to a Font).

Anti-camping check: Blood Lance punishes kiting; the Tithe aura + turn-rate
mechanic punishes face-tanking the front (the back is the *reward* for
correct positioning, not free real estate, so this is the fight's
melee-positioning teaching moment rather than a pure anti-camp tool).

Drop niche: Leech Blade (unique) + Tithebound Cuirass (rare) — both fully
statted. Common/Uncommon rows: blocked, see §10 Q1.

---

## 6. Workstream E — Item/loot ingestion

- Ingest the six already-statted unique/rare rows into `items.json`, mirroring
  M2 Workstream A's ingestion pipeline exactly (no new stat-sourcing
  questions here — batch 1 already closed that gap for all 8 bosses).
- Kill gold: already numbered in economy doc §3 (Hive Matron 500g/kill, T1;
  Mirrorhide/Bloodtithe 900g/kill, T2) — straightforward data wiring, no gap.
- Common/Uncommon loot rows: **blocked**, same shape as M2's original Maggot
  King gap. See §10 Q1 — flagged, not invented.

---

## 7. Workstream F — Boss roster screen (UI bible §6.1)

New Razor component: horizontal-scroll boss cards for all 8 bosses in the
Roster Overview table. Maggot King/Hive Matron/Mirrorhide/Bloodtithe get
real card states (New/In Progress/Cleared, driven by the new per-boss save
data). Gale Roc onward render as locked "coming soon" posters.

**Scoping note (flag, see §10 Q3)**: "Hard Unlocked" (invocation flame icon)
and the rare-drop silhouette fade-in are both raid-level/invocation-
dependent (M4) — omitted for M3, matching the brief's own "pre-fight screen
(without invocations panel)" scoping. The four unbuilt bosses' "coming
soon" posters are read as *not yet built*, not a real gameplay gate — the
design-decisions doc is explicit that there are no killcount/unlock gates
in this game — but flagging the reading rather than assuming it's obviously
correct, since the UI bible's own copy example ("unlock condition printed")
could be misread as implying a real mechanic.

Needs **SaveData v4**: a per-boss dictionary (kills, best-time-ticks,
deaths) replacing the single Maggot-King-implicit `PersonalBestKillTicks`.
Old saves migrate by mapping the existing field into a `"maggot_king"` entry
(same forward-compatible pattern v3's bank field used).

---

## 8. Workstream G — Pre-fight screen (UI bible §6.2)

Left: boss art/flavor + per-boss stats from the new save-data map (kc, best
time, deaths — **not** "highest raid level cleared," which needs RL from M4).
Right column (invocation panel) is explicitly excluded per the brief's
"without invocations panel" — out of scope entirely for M3, not stubbed.

**Scoping note (flag, see §10 Q4)**: the doc's loadout-preset chip ("using:
*Mirrorhide bar* — tap to change") assumes named per-boss presets, which is
backlog item #9 (5 named loadout presets), still open and M1-deferred.
Proposed: ship the chip showing the single active bar with no preset
switching, rather than pulling #9 forward into this milestone.

FIGHT button dispatches `StartDuelCommand(playerId, <selected boss id>)` —
cheap; the command layer is already boss-agnostic (only `Game.razor:157`'s
hardcoded literal needs to change).

---

## 9. Sequencing

1. **Workstream A**, built and validated against Hive Matron's concrete
   needs first (simplest of the three, Tier 1, matches the bible's own
   skill-curriculum order) — ship Hive Matron end-to-end, including her
   item ingestion, before starting Mirrorhide. Proves the extraction
   against one real boss before a second and third lean on it.
2. **Mirrorhide** next (introduces A.5/A.11/A.12/A.13 on top of the now-
   proven A.1–4/6/7).
3. **Bloodtithe** last (introduces A.8/A.9/A.10 — the aura/font/orientation
   trio, the most novel mechanics; benefits from the other two bosses'
   systems work already being settled).
4. **Workstreams F/G** (roster + pre-fight screens) after all three bosses
   exist — "Cleared" states and per-boss stats need real fights to
   generate against.
5. **SaveData v4** lands when F starts, not before — no reason to carry an
   unused schema field through B/C/D.

---

## 10. Design questions and tunables (flag, don't resolve silently)

1. **Common/Uncommon loot content for the 3 new bosses is undocumented.**
   Needs either a human content-authoring pass (mirroring Maggot King's
   batch-1 §2 resolution) or a ratified generic table applied uniformly
   per tier (economy doc §5's own description already reads generically:
   "sellables, materials, off-tier gear, shards"). Not decided here.
2. **Sustained/held prayer** (Hive Matron P2's 3-round Dart Volley burst,
   "prayer must be *held*, not flicked"). Does the current prayer-flick
   input model support a hold state, or does this need new input handling?
   Needs confirmation before Workstream B's Phase 2 lands.
3. **Locked-boss posters as "not yet built" vs. a real gate** — confirmed
   reading (see Workstream F), not re-litigated, but flagged since it's an
   interpretation of the UI bible's copy example rather than something the
   doc states in so many words.
4. **Loadout preset chip scoped down to single-bar** (Workstream G) rather
   than pulling backlog #9 forward — needs confirmation since the UI
   bible's literal text assumes named presets.
5. **Backlog #15** (Maggot King's Phase 1 Eruption still on an independent
   timer, flagged "audit before M5") — Workstream A.1 touches this exact
   hazard mechanism while generalizing it. Should P1's migration/audit be
   pulled into M3 opportunistically, since the code is being touched
   anyway, or stay parked for the pre-M5 pass as originally scoped? Not
   decided here.
6. **Backlog #33** (boss Evasion never exercised by a non-zero value) —
   none of Hive Matron/Mirrorhide/Bloodtithe's bible entries specify a
   favored/disfavored style (only the Unblinking, M5, explicitly states
   one, as "the counterweight to Bloodtithe"). Unless a human wants to
   assign one of the three a non-neutral Evasion now, this stays open past
   M3 too — correcting the backlog's "M3+" framing to, in practice, "M5."
7. **A.4's DoT unification is a refactor of two shipped mechanisms**
   (player poison, Rotfang's NPC poison), not purely additive — flagging
   the regression-test obligation explicitly (existing `MaggotKingTests`/
   `RotfangPoisonTests` must stay green) rather than treating it as a
   free extraction.

---

## 11. Explicitly out of scope for M3

- Invocations panel, raid level, mastery-gated unlocks, collection log (M4).
- Gale Roc, The Unblinking, Millstone Golem, Grand Duelist (M5+).
- Shop buyback, Bank UX polish, T3/T4 weapon specials (backlog #5–7, #13) —
  pre-existing deferrals, not reopened by M3 unless a new boss mechanic
  specifically needs one (Copycat doesn't — see Workstream A.12).
- 5 named loadout presets (#9) — stays deferred; see Workstream G's scoping
  note.
- `VoxelIcon.razor`/`voxel.js` item-icon migration (#12) — unrelated to
  boss content.
- Any Distribution decision (#34) — unrelated to this milestone.
