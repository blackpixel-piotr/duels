# Duels — Architecture Reference

> **Agents: read this file first.** It is the canonical map of the codebase.
> When a feature changes, update the relevant section here **before** closing the task.
> When code is removed, remove its entry here too — dead entries are misleading.

---

## Tech Stack

| Concern | Choice |
|---|---|
| Language | C# / .NET 8 |
| UI | Blazor WebAssembly — runs in any browser (iPhone Safari, Android Chrome, all desktops) |
| Architecture | Clean Architecture (Onion) — strict inward-only dependency rule |
| Tests | xUnit |
| Persistence | IndexedDB (local-first, via `ISaveStore`) — see swap path below |
| Message bus | In-process sync queue — see swap path below |

---

## Project Dependency Graph

```
Duels.Domain        (no external deps — pure game rules)
    ↑
Duels.Application   (depends on Domain only)
    ↑
Duels.Infrastructure  (depends on Application — implements interfaces)
    ↑
Duels.Web           (Blazor WASM — depends on Application + Infrastructure)

tests/Duels.Domain.Tests        → Duels.Domain
tests/Duels.Application.Tests   → Duels.Application + Duels.Domain
tests/Duels.Infrastructure.Tests → Duels.Infrastructure + Duels.Application + Duels.Domain
```

**Rule**: nothing in Domain or Application may reference Infrastructure or Web.

---

## Layer Responsibilities

### Duels.Domain (`src/Duels.Domain/`)
Pure game rules. Zero framework dependencies.

| Path | Contents |
|---|---|
| `Entities/Player.cs` | Player state: HP (flat 100), gold (starts at 0 — PROVISIONAL, see m1-findings.md addendum), prayer, special energy, boost prayer, equipped items, the 28-slot `Inventory` bag (`BagCapacity` const, UI bible §7) + unbounded `BankedItems` (M2 Workstream B) with `Deposit`/`Withdraw`, `Loadout`, `FlaskBelt`, personal-best kill time. No XP/levels (M1 retired them — see design/plans/m1-findings.md). |
| `Entities/NpcTemplate.cs` | `NpcTemplate` (record) + `NpcInstance` (per-duel runtime). Also `BossScript`/`BossPhaseDef`/`RotationStep`/`BossAttackDef`/`EruptionDef`/`RotBurstDef`/`SwarmWaveDef`/`FootprintDef` — the boss-script data model (m1-plan Workstream C) — `AddInstance` (swarm adds), and `InFlightProjectile` (a cast Ranged/Magic `BossAttackDef` in flight — sim-authoritative fractional-tile position, homing, `BossAttackDef.ProjectileSpeedTiles` default 3.0). `DummyStyle` is a test-only field for non-scripted movement fixtures. |
| `Entities/Weapon.cs` | Weapon definition carrying `DocStats` (Power/Precision/Line/Tier/Special). `AsGearPiece()` converts it to a `GearPiece`. |
| `Entities/GearPiece.cs` | Armour/gear with slot + `DocStats`. |
| `Entities/Loadout.cs` | RS3-style 4-slot action bar + 2-slot flask belt binding (`Loadout`), plus per-duel `FlaskBelt`/`FlaskSlotState` (sip charges). |
| `Entities/Quest.cs` | Quest stub: title, objectives, reward. Not wired into anything yet. |
| `Entities/InvocationDefinition.cs` | Invocation (ToA-model pre-fight modifier) schema — id, name, raid level, effect, tier, tags. Content ships in M4; `invocations.json` is an empty stub for now. |
| `ValueObjects/DocStats.cs` | Combat-math-v2 stat block (items doc §1): `Power`, `Precision`, `DefPoints`, `Line` (`GearLine`), `Tier`, `Special` (`SpecialEffect`). Replaces the OSRS `ItemModifiers`. |
| `ValueObjects/CombatStats.cs` | Attack/Strength/Defence/Hitpoints record — kept for `NpcTemplate.Stats.Hitpoints` (the only field combat-math-v2 reads); the other three are vestigial. |
| `ValueObjects/AttackStyle.cs` | Accurate / Aggressive / Defensive enum. |
| `ValueObjects/AttackType.cs` | Stab / Slash / Crush / Ranged / Magic enum. Melee weapons/attacks use `Slash` uniformly in M1 content (protection prayer already buckets Stab/Slash/Crush as one "Melee" style). |
| `ValueObjects/AttackRange.cs` | `Melee` (1) / `Distant` (7, ranged & magic) tile ranges. |
| `ValueObjects/EquipmentSlot.cs` | Weapon, Shield, Helmet, Body, Legs, Boots, Gloves, Cape, Amulet, Ring. |
| `Events/*.cs` | Domain event records: `DuelStarted`, `AttackLanded`, `AttackMissed`, `DuelWon`, `DuelLost`. Base: `DomainEvent`. |
| `Interfaces/IRandomProvider.cs` | `Next(min, max)` + `NextDouble()` — injected so tests use deterministic fakes. |
| `Interfaces/IDamageModel.cs` | `Roll(AttackerProfile, DefenderProfile)` → `DamageResult(Hit, Damage)`. Replaces `ICombatCalculator`. |
| `Services/DamageModel.cs` | Combat-math-v2 implementation (items doc §1): 80% base hit, style modifiers (Accurate +10% hit / Aggressive +20% dmg −10% hit / Defensive −10% dmg), weapon Precision as a flat hit bonus, armour Def points (0.4%/pt, 40% cap). See formula notes below. |
| `Services/TickConstants.cs` | The single authoritative tick timing: `TickDurationMs` (600) and `InputBufferWindowMs` (150, UI bible §2). Every tick-timed value in the game routes through these two constants. |

**Combat formula (v2, items doc §1)**:
1. `hitChance = clamp(80 + styleHitMod + precision×100, 0, 100) / 100` — Accurate +10, Aggressive −10, Defensive 0.
2. `damage = Power × (1 + lineDamageBonus) × styleDamageMult` — Aggressive ×1.20, Defensive ×0.90, Accurate ×1.0.
3. `damage ×= (1 − defenderMitigation)` — gear Def points (0.4%/pt, capped 40%) plus, for Defensive-style defenders, an extra flat 20% (see m1-findings.md's flagged assumption on this number).
4. Boss (NPC) attacks are **scripted, not rolled** — see `GameTickService`'s boss-rotation engine below; they always land unless dodged positionally, then reduced 75% by a matching protection prayer (unless flagged Unprayable).

---

### Duels.Application (`src/Duels.Application/`)
Use cases and the command pipeline. Depends on Domain only.

| Path | Contents |
|---|---|
| `Abstractions/ICommandDispatcher.cs` | `DispatchAsync<TCommand>` — **the queue boundary**. Swap `LocalCommandQueue` → Kafka here. |
| `Abstractions/ICommandHandler.cs` | One handler per command type. |
| `Abstractions/IGameCommand.cs` | Marker interface. Also `CommandResult(Success, Messages)`. |
| `Abstractions/IEventBus.cs` | `PublishAsync<TEvent>` + `Subscribe<TEvent>`. |
| `Abstractions/IPlayerRepository.cs` | Get/Save `Player`. |
| `Abstractions/IGameStateRepository.cs` | Get/Save `GameState`. |
| `Abstractions/INpcRepository.cs` | `GetTemplate(id)`, `GetAll()`. |
| `Abstractions/IItemRepository.cs` | Get gear/weapon by id, name lookup, `IsWeapon(id)`. |
| `Abstractions/IInvocationRepository.cs` | `Get(id)`, `GetAll()` — schema/pipeline only until M4 populates content. |
| `Abstractions/ISaveStore.cs` | `LoadAsync/SaveAsync/DeleteAsync(key)` — string-keyed JSON blob storage, game-agnostic (no SaveData knowledge). Swap point for a remote save backend (M8). |
| `Abstractions/ITickSource.cs` | Timing-only tick authority: `ElapsedMsIntoCurrentTick`, `Reset()`, `WaitForNextTickAsync(ct)`. Separates *when* a tick fires (`TickScheduler`, Infrastructure) from *what happens* during one (`GameTickService`, below). |
| `GameSession/GameState.cs` | Per-duel state: `Player`, `ActiveNpc`, `CombatLog`, `InDuel`, fixed 9×9 arena (`ArenaRadius` const), boss footprint/stationary flag, tile hazards v2 (`HazardTile`/`HazardState`: Warning→Pool→Scorch), swarm `Adds` + `TargetId` (add-vs-boss targeting), in-flight `Projectiles` (`SpawnProjectile`/`AdvanceProjectiles`/`ClearProjectiles` — Euclidean homing motion, not a tick countdown), weapon-swap input buffer, fight timer (`FightTicks`)/`KilledBy`. Persistent target lock (M1 revision): `Engaged` (drives the attack gate; only `Engage()`/`Disengage()` change it — movement never does) and `EngageApproachActive` (movement-only auto-chase-into-range convenience, retired by `OrderMove`, only re-armed by `Engage()` — this is what makes kiting work without an auto-drag-back). `DuelSummary` carries `KillTimeTicks`/`KilledBy`/`PersonalBest`/`Flawless`/`GoldGained`/`LootItemIds`. `VfxEvents` (cleared at the top of every `ProcessTick`, read once by `BattleScene.razor`) plus `AppendVfxEvent`/`ClearVfxEvents` are the VFX layer's own state (vfx-plan.md). **Combat feel pass 1** (combat-feel-plan.md): `AppendHitsplat(onEnemy, dmg, tier, style, weapon, targetEntityId)` is now the single call site every hitsplat routes through — it emits the exact pre-migration `AppendLog` message (several tests assert its text) *and* a typed `impact`/`hit_blocked` vfxEvent, plus an `attack_swing` companion (suppressed for DoT/hazard ticks and for an NPC ranged/magic hit's impact, whose swing already fired at the earlier `BossCast`/`SpawnProjectileAttack` site). `flask_sip`/`perfect_dodge`/`forced_move` are emitted at `SipFlaskHandler`/`TryPerfectDodge`/`ExecuteLunge`+`Knockback` respectively — all replacing what `BattleScene.razor` used to derive by parsing the corresponding `CombatLog` message text (see CLAUDE.md's "CombatLog is UI text only" rule). **Round 5** (combat-feel-findings.md): `targetEntityId` (optional, default `null`) lets a hit on a swarm/drone add carry the add's own id as the vfxEvent's victim instead of the hardcoded `"enemy"` — `ExecuteBasicAttackOnAdd` (`GameTickService.cs`) passes `add.Id`; every boss-attack call site is unaffected (defaults to the old behavior). |
| `Commands/*.cs` | `StartGameCommand`, `StartDuelCommand`, `AttackCommand`, `EquipItemCommand`, `UnequipItemCommand`, `WeaponShortcutCommand`, `PrayerCommand`, `SetStyleCommand`, `MoveToCommand`, `EngageCommand`, `DisengageCommand`, `FreezeEnemyCommand`, `SipFlaskCommand`, `SetTargetCommand`, `BindWeaponSlotCommand`, `BindFlaskSlotCommand`, `GrantDevLoadoutCommand`, `DepositItemCommand`, `WithdrawItemCommand` (M2 Bank — `Quantity <= 0` means "All"), `BuyItemCommand` (M2 Shop). |
| `Handlers/*.cs` | One handler per command. `GrantDevLoadoutHandler` implements the M1 dev debug menu (T1/T2 one-tap gear + bar binding). `DepositItemHandler`/`WithdrawItemHandler` move items bag↔bank (withdraw respects `Player.BagCapacity`). `BuyItemHandler` validates gold via `IItemRepository.GetShopPrice`, spends it, and adds the item to the bag or — if the bag is full — the bank (never fenced; fencing is loot-only, see `RollLoot`). |
| `Services/GameTickService.cs` | Drives the fixed 0.6s tick. `ProcessTick` runs: player movement → (dormant, non-stationary-only) NPC movement → player attack/special resolution → swarm-add movement/contact → in-flight projectile advance/impact (`AdvanceProjectiles`, against the player's just-updated tile) → **boss rotation-script engine** (style telegraph forecast, scripted attacks — Ranged/Magic spawn a homing `InFlightProjectile` via `SpawnProjectileAttack` instead of resolving immediately, melee resolves synchronously — Rot Burst inhale/resolve, punish window) → independent eruption timer → swarm spawn thresholds → hazard resolution (pool/eruption damage, Perfect Dodge) → prayer drain → special-energy regen → DoTs → victory/defeat. No boss-specific branches — `maggot_king`'s full choreography is `npcs.json` data (`NpcTemplate.Script`) consumed generically; a `Script == null` NPC (used only by movement-test fixtures) skips all boss-engine steps. |

---

### Duels.Infrastructure (`src/Duels.Infrastructure/`)
All implementations. Registered in one place — `DI/InfrastructureServiceExtensions.cs`.

| Path | Contents |
|---|---|
| `Messaging/LocalCommandQueue.cs` | `ICommandDispatcher` → resolves `ICommandHandler<T>` from DI and calls it synchronously. **Swap point for Kafka.** |
| `Messaging/InMemoryEventBus.cs` | `IEventBus` → dictionary of typed handler lists. |
| `Persistence/InMemoryGameStateRepository.cs` | `IGameStateRepository` — singleton dictionary, lives for the browser session. |
| `Persistence/InMemoryPlayerRepository.cs` | `IPlayerRepository` — same pattern. |
| `Persistence/DefinitionNpcRepository.cs` | `INpcRepository` — loads `Definitions/npcs.json` at startup (M1: the single `maggot_king` boss-script row). Cross-validates every loot-table item id against `IItemRepository`; throws loudly on a typo. |
| `Persistence/DefinitionItemRepository.cs` | `IItemRepository` — loads `Definitions/items.json` at startup. Throws on duplicate ids or a shop price referencing an unknown item. M2: `shopPrices` covers T1-T4 shop weapons/armour + both flasks (economy §4 ladder); `GetShopPrice` is a single-item lookup for `BuyItemHandler`. `GetFenceValue` is drop-table sell value only (economy §3: 15% of shop price, or the `fenceValues` override for drop-only items, or 0) — NOT shop buyback, which is a separate unimplemented session-scoped mechanic (see m2-findings.md). |
| `Persistence/DefinitionInvocationRepository.cs` | `IInvocationRepository` — loads `Definitions/invocations.json` (empty stub until M4). |
| `Persistence/IndexedDbSaveStore.cs` | `ISaveStore` → IndexedDB via `IJSRuntime` calling `wwwroot/js/persistence.js`'s `idbGet`/`idbSet`/`idbDelete`. |
| `Definitions/DefinitionLoader.cs` | Loads a JSON definitions file embedded as a resource in this assembly; malformed JSON throws with the file name. |
| `Definitions/items.json` | M1 doc-item content (items doc §1–2, §5): T1/T2 weapons for all 3 styles (`wpn_{melee,ranged,magic}_t{1,2}`), T1/T2 armour for all 3 lines × 6 slots (`arm_{warbound,stalker,occult}_{slot}_t{1,2}`), and the 2 flasks (`flask_health`, `flask_prayer`). Each weapon/gear row carries a `Doc` (`DocStats`) block. |
| `Definitions/npcs.json` | M1 boss content: the single `maggot_king` row, carrying a full `Script` (`BossScript`) — P1/P2 rotation, eruption/pool/scorch, Rot Burst, swarms, 2×2 stationary footprint. |
| `Timing/TickScheduler.cs` | `ITickSource` — drift-corrected 0.6s clock: schedules each tick from a fixed origin (`tickCount * TickDurationMs`) instead of chaining `Task.Delay(600)` calls, so per-tick overhead can't accumulate into slow drift over a long fight. |
| `Random/SystemRandomProvider.cs` | `IRandomProvider` → `Random.Shared`. |
| `DI/InfrastructureServiceExtensions.cs` | `AddDuelsInfrastructure(IServiceCollection)` — wires everything. Called once from `Program.cs`. |

**Swap paths** (zero Domain/Application changes needed):
- **Persistence**: replace `IndexedDbSaveStore` with a remote API client when backend accounts arrive (M8) — same `ISaveStore` interface.
- **Queue**: replace `LocalCommandQueue` with `KafkaCommandQueue` — same `ICommandDispatcher` interface.
- **Event bus**: replace `InMemoryEventBus` with SignalR or a message broker.

---

### Duels.Web (`src/Duels.Web/`)
Blazor WASM. Depends on Application (interfaces) + Infrastructure (DI wiring only via `Program.cs`).

| Path | Contents |
|---|---|
| `Program.cs` | Calls `AddDuelsInfrastructure()` + registers `GameService` as singleton. |
| `Services/GameService.cs` | Owns `PlayerId`. Exposes `StartNewGameAsync`, `DispatchAsync<TCommand>`, `GetStateAsync`, `PersistNowAsync`. Fires `StateChanged` event so components re-render. Saves are a versioned `SaveEnvelope` (schema **v2** — action bar + flask belt bindings, personal-best kill time; the OSRS-era xp/prestige/win-streak/endless/bank/collection-log fields are gone) wrapping `SaveData`, written via `ISaveStore`. `DispatchAsync` skips persistence for high-frequency in-duel commands — the tick loop keeps in-memory state current, and `Game.razor`'s `OnTickNotify` persists on duel end plus every 10 ticks as a safety net. |
| `Pages/NewGame.razor` | `/` route — character name input, calls `GameService.StartNewGameAsync`. |
| `Pages/Game.razor` | `/game` route — mobile shell. Hub (out of duel): `StatusStrip` + `HubMenu` (Fight Maggot King, Retry, Loadout Editor, dev T1/T2 grants) + `ActionHud` (dock layout). In duel: full-bleed `BattleScene` + `ActionHud` (float layout) + `DuelResultOverlay`. |
| `Components/Hud/StatusStrip.razor` | Always-visible slim header: name, gold, HP/prayer/spec bars, bleed/poison chips. |
| `Components/Hud/ActionHud.razor` | In duel: flask belt (2 slots, sip pips) + attack-target button (boss or targeted add) + engagement indicator (UI bible §3.3/§3, persistent target lock — reticle glyph while `State.Engaged`, sheathed glyph + tap-to-`DisengageCommand` otherwise) + 4-slot weapon action bar (sourced from `Loadout`, not raw inventory) + special-attack button + prayer row (3 protections + boost prayer) + style toggle. Out of duel: Bag/Character nav. Every button dispatches a typed command directly. |
| `Components/Hub/HubMenu.razor` | Out-of-duel home screen. Fight (Maggot King), Retry, Loadout Editor, Bank, Gold Shop (M2), and two dev-only one-tap loadout grants (T1/T2, Warbound line). Ladder/Endless/Prestige/Beg cards were removed in the M1 ladder-retirement sweep and not revived — there is no non-boss opponent roster (see m1-findings.md's M2 pre-plan addendum). |
| `Components/Loadout/LoadoutEditor.razor` | Minimal Loadout Editor (UI bible §4.1): tap a bar/belt slot, tap an owned weapon/flask to bind, tap ✕ to clear. Locked mid-fight (enforced server-side by the handlers). |
| `Components/Bank/BankSheet.razor` | M2 Workstream B (UI bible §7): two-pane bag↔bank grid (stacked/grouped by item id with counts), quantity toggle (1/5/10/All — no custom "X" yet), Deposit All. Tabs/search/favorites/placeholder-ghosts/Prepare-Loadout bridge deferred, flagged in m2-plan.md. |
| `Components/Shop/ShopSheet.razor` | M2 Workstream D (UI bible §9): Weapons/Armour/Flasks tabs, `IItemRepository.GetShopItems()`-driven grid, single confirm above 2,500g. "Why buy" tags omitted (no source content beyond Maggot King). Buyback not implemented. |
| `Components/Equipment/EquipmentStatSheet.razor`, `Components/Equipment/CompareCard.razor` | M2 Workstream C.2/C.3 (UI bible §8.1). Stat sheet reuses `GameTickService.GetLineDamageBonusPreview`/`MaxSpecialEnergy` (made `public` for this) plus a local Def-mitigation calc off `DamageModel`'s public constants — same formulas combat uses, not reimplemented. Compare card shows `DocStats` deltas (green/red) vs. the currently equipped item in the same slot; `InventoryGrid`'s tap now raises `OnSelectItem` into this card instead of equipping instantly. |
| `Components/Combat/BattleScene.razor` | Mounts the battle canvas, forwards sim state to `toon.js` (positions, vitals, hazards incl. scorch, swarm adds, in-flight projectiles, boss style-forecast badge, punish-window flag, weapon id, equipped gear ids). Position snapshots (player/enemy/adds) carry a per-entity `discontinuous` flag, true only on a tick whose `CombatLog` contains a `PlayerTeleport`/`NpcTeleport` entry (currently just Lunge) — tells the renderer to snap instead of lerp on its NPC-interpolation layer. `toon.js` currently ignores `player.discontinuous` (the player isn't on that layer — see below), so the flag is only load-bearing for the enemy/adds today; still sent for the player so no second wiring pass is needed if the player ever rejoins the layer. `GameState.Projectiles` is sent every render (`voxel.setBattleProjectiles`, id/x/z/style/speed — the attack's own `ProjectileSpeedTiles`, used by the renderer's cosmetic pursuit, not just the sim); a `BossCast` log entry now only triggers the boss's windup animation (`enemyAttack` battleEvent), not a projectile spawn — the projectile itself comes from the entity sync. Handles ground/enemy/add taps via `[JSInvokable]` callbacks. |
| `Components/Combat/DuelResultOverlay.razor` | Win/defeat screen: kill time (+ personal-best flag), what-killed-you (defeat only), flawless badge, gold, loot row, Retry/Leave. |
| `Components/Hud/CharacterSheet.razor`, `Components/Bag/BagSheet.razor` | Modal sheets wrapping `StatsPanel` and the equipment paper-doll + `InventoryGrid` respectively. |
| `Components/Stats/StatsPanel.razor` | HP/style/gold/special/prayer/boost-prayer/personal-best/bleed/poison — shown inside `CharacterSheet`. No XP/levels (M1 removed them). |
| `Components/Inventory/InventoryGrid.razor` | 28-slot OSRS-style grid + equipped items. Tap → `EquipItemCommand` (food/potion items were removed with the flask belt's introduction). |
| `Components/Hud/ToastHost.razor` | Top-center toast stack for loot/system log entries, timestamp-pruned after 4s. |
| `wwwroot/js/toon.js` | The battle renderer (three.js, owns `window.voxel`). Characters are the Universal Base glTF driven by Universal Animation Library clips. NPC movement (boss, swarm adds) is snapshot-interpolated: each keeps a `snapFrom`→`snapTo` pair and lerps between them over `TILE_MS` (~600ms) each frame (`interpolateSnapshot`/`applySnapshot`), facing its movement direction while lerping; a per-tick `discontinuous` flag (dashes/teleports/knockbacks) makes it snap instead. The player is NOT on this layer — playtest feedback found the lerp made the player visibly jump/overshoot, so the player still moves via the original constant-speed pursuit of `st.player.target` (`MOVE_SPEED.player`, same law as `voxel.js`). The simulation itself stays tick-discrete either way — all smoothing is renderer-only. `setBattleHazards` renders warning/pool/**scorch** (permanent, gold-safe) tiles; `setBattleAdds` renders swarm-add placeholder blobs (click-targetable via raycast → `OnAddClick`, hitbox stays snapped to the sim tile even though the visible mesh interpolates); `setBattleProjectiles` spawns/despawns boss Ranged/Magic projectile meshes by id from `GameState.Projectiles` (sim-authoritative for WHEN/WHETHER they hit) but deliberately does NOT snapshot-interpolate their position — bug fix (M1 revision, "projectile visual re-decoupled from sim tick position"): re-syncing to the sim's own tick-quantized homing waypoint every ~600ms produced a visible sharp turn whenever the player strafed mid-flight, since each tick's heading is a genuine direction change. Instead each projectile mesh runs its own continuous, purely cosmetic pursuit of `st.player.pos` (already-smooth) every rendered frame, at a speed matching the attack's own `ProjectileSpeedTiles` — mirrors the pre-sim-refactor cosmetic system's technique (fixed spawn point homing continuously on a live target reference, commit `a6b133b`), generalized to a variable/dynamic flight duration; arrival is still entirely the backend's call (mesh vanishes the tick `GameState` removes the id), held at a constant height (no arc); the `enemyAttack` battleEvent (fired from a `BossCast` log entry) only triggers the boss's windup animation, it doesn't spawn a projectile itself. `setBattleTelegraph` used to also fire a "committed read" preview projectile on the telegraph's rising edge — removed (bug fix: "phantom projectile at Maggot King style-switch, never deals damage" — an orphan cosmetic mesh with no sim entity behind it, playing out entirely during the telegraph's lead-in window with no relation to the real attack's own cast a few ticks later; `setBattleTelegraph` now only fires the windup pose). Sim entities are the only legal projectile source now — every mesh in `projectileMeshes` is stamped `simBacked: true` at creation (`setBattleProjectiles`, its only construction site), and the render loop asserts this every frame (`console.error` if violated) as a standing regression guard. `PROJ_TRACE` (off by default) gates a verbose spawn/despawn console trace for re-diagnosing this bug class. The player's own outgoing attack projectile is still the old fixed-duration cosmetic lerp (`st.projectiles`) — that one's legitimately out of scope, unrelated to the boss's homing attack (player attacks resolve synchronously, no travel-time sim). A pulsing gold ring shows the boss's punish window (`setBattleFlags({punished})`); `perfectDodge` is a `battleEvent` case (gold glint at the player's feet); an inked engagement reticle on the boss (UI bible §3.3/§3, persistent target lock) shows/hides off the same `holdPosition` flag `setBattleFlags` already carries — visible while target-locked, gone the instant `Disengage` breaks it. `setBattleWeapon`/`setBattleEquipment` item-id-driven, no hardcoded weapon/armor lists; `setBattleWeapon` also takes the weapon's doctrine `style` (combat feel round 3) so `setActorStance` can pick a style-appropriate idle (`spellIdle` for magic, `swordIdle` otherwise) instead of just an armed/unarmed check — `BattleScene.razor`'s `WeaponStyle()` (a live `IItemRepository` lookup, re-added for this) is the source. **M2 Workstream C.1**: `initPreview(canvasId, itemIdsCsv)`/`destroyPreview` (exported on `api`, resolved by the `window.voxel` Proxy ahead of `voxelClassic`'s same-named fallback) reuse `makeActor`/`setActorWeapon`/`setActorArmor` to render the equipment screen's character preview — the real toon rig with real composited gear meshes, auto-rotating with drag-to-spin (`PlayerPreview.razor`). Replaces the old fixed-`.vox`-model preview. `VoxelIcon.razor` (bag/bank/shop item icons) still calls into `voxel.js`/`voxelClassic` — not migrated this pass, see m2-findings.md. **VFX layer**: `st.vfx` (`createVfxSystem`, `wwwroot/js/vfx.js`) is constructed in `initBattle`, ticked via `st.vfx.update(dt)` each frame just before `effect.render`, and disposed in `destroyBattle`; `api.setVfxEvents`/`setVfxQuality` are the JS entry points `BattleScene.razor` and (future) UI call into — see that file's own header comment and `design/plans/vfx-plan.md`. **Combat feel pass 1** (combat-feel-plan.md/-findings.md): `battleEvent` is now down to the `enemyDeath`/`playerDeath` cases only (flag-driven, not CombatLog) — attack/hit/block/sip/dodge presentation moved to `handleCombatVfxEvent`, called per-event from `setVfxEvents` alongside `st.vfx.handleEvents`. `attackRoleForStyle`/`blockRoleForStyle`/`flinch`/`spawnSplat` are now module-level (promoted out of the old `battleEvent` closure) so both paths share them. Facing (both player-pursuit and enemy-interpolation blocks) is a max-angular-speed clamp (`MAX_TURN_RAD_PER_S`) instead of an exponential ease. A per-actor `presentFx` state machine (`startLunge`/`startForcedMoveSlide`/`updatePresentOffset`) adds a small additive world-space offset (+ landing squash scale) at the final `ch.group.position.set` call — never touching `actor.pos` — for the attack lunge (`attack_swing`) and forced-move arc-slide (`forced_move`); `resetBattle` clears it defensively on RETRY. The camera's `camFocus`/distance now spring toward the player-boss midpoint and ease with their separation (`st.distSpring`), gated by a `cameraMotion` clientPref (`'off'`/`'reduced'`/`'full'`) that restores exactly the old fixed-on-player behavior at `'off'`; `loadClientPrefs`/`saveClientPrefs` read/write one `duels_client_prefs` localStorage object (`cameraMotion` + `vfxQuality`, named as M6's future Settings-screen backend) — `api.getClientPrefs`/`setCameraMotion`/`setVfxQuality` are the entry points. **Round 3/4** (user-reported, `combat-feel-findings.md`): round 3 briefly made engagement beat movement for the player's facing, then round 4 reverted it — moving always faces the direction of travel (the only locomotion clip family available is forward-only, no strafe/backpedal variant exists in either vendored animation library, checked directly), facing the enemy only wins once fully stationary. A proper "face the target while running" needs either strafe/backpedal clips or a bone-masked upper-body aim layer, neither of which exist yet — flagged in backlog.md, not attempted as a half-measure. `attack_swing`'s lunge (`startLunge`) only fires for melee (a caster/archer no longer physically lurches toward the target). `hit_blocked` for ranged/magic plays no body animation at all (previously `hitA`, a hurt-flinch, which contradicted `flinch()`'s own deliberate exclusion of tier `'blocked'`) — VFX + the splat's blocked ring alone carry the read, same as melee's dedicated `block` pose reads on its own. A `PREFS` toggle button (next to `MECH`, same always-visible convention) is the only current entry point to `cameraMotion`/`vfxQuality` pending a real M6 Settings screen. **Animation quality pass** (animation-pass-plan.md/-findings.md): all fade/crossfade durations that used to be hardcoded per call site are now one `TRANSITION_S` table (`idleWalk`/`walkAttack`/`attackIdle`/`hitReact`/`death`) — `playOverlay`'s default fade and interrupt-fadeout, `flinch()`'s hit-react fade, the death overlay's fade, and the `mixer`'s `'finished'`-handler fade-out all read from it. `setActorStance`'s idle/swordIdle/spellIdle swap was the one real hard `.stop()`/`.play()` cut in the renderer; a THREE-native `crossFadeTo`/`fadeIn`/`fadeOut` can't be used on it because `updateActorAnim`'s own per-frame `setEffectiveWeight()` write on that same node (locomotion blend space, anchor 0) would cancel any pending native fade (`setEffectiveWeight` calls `stopFading()` internally) — so the swap is a manual crossfade instead: the outgoing action is kept as `node.fadeOutAction`/`fadeOutT`/`fadeOutFrom`, and `updateActorAnim`'s existing weight-write loop ramps both actions' weights in opposite directions over `TRANSITION_S.walkAttack` each frame, then stops and drops the outgoing action once the ramp completes; `resetBattle` also clears any in-flight `fadeOutAction` on duel restart. The locomotion blend space's own continuous per-frame easing (idle/walk/jog/sprint) was left untouched — already a better mechanism than a fixed crossfade for a continuous speed variable. Upper/lower body bone-mask layering (attack/cast playing independently of the run cycle) was investigated and explicitly not built this pass — every extracted clip carries authored full-body leg motion (checked directly, all 22), and the player's own attacks only ever fire while stationary (`GameTickService`'s `!playerMovedThisTick` gate) so the motivating scenario doesn't occur on the player side; see backlog.md item 49. **Round 5** (user-reported, combat-feel-findings.md): attacking a swarm/drone add used to visually attack the boss — `handleCombatVfxEvent`'s `actorFor` could only ever resolve `'player'`/`'enemy'`, so an add hit's splat/lunge/outgoing-projectile target and the player's facing (`other` was hardcoded to `st.enemy`) all defaulted to the boss even though `GameState.AppendHitsplat` was emitting the hit against the add. `actorFor` now also resolves against `st.addMeshes`, returning a lightweight position-only proxy (`{ pos, ch: { height }, isAdd: true }` — enough for splat/lunge/projectile targeting, but `flinch`/`playOverlay` are guarded off it since adds have no skeletal rig to animate); the player's facing block resolves `other` to the actively-targeted add's mesh via a new `st.playerTargetAddId` (set by `setBattlePositions`, sourced from `GameState.TargetId` via `BattleScene.razor`), taking priority over `holdPosition` since tapping an add is its own explicit action. Separately, the same round fixed "can't target a moving add": `setBattleAdds`'s invisible tap hitbox used to snap straight to the raw new sim tile the instant a snapshot arrived while the visible sphere was still mid-lerp (`TILE_MS`-interpolated) toward it — up to a full tile apart for most of that window. The hitbox's position is now written in the same per-frame block that computes the visible mesh's interpolated position, so the two are identical by construction rather than by timing luck. |
| `wwwroot/js/vfx.js` | Renderer-only particle layer (`design/plans/vfx-plan.md`), driven entirely by the semantic `vfxEvents` array `GameState` appends per tick — never touches gameplay state, never decides whether something happened. Wraps a `three.quarks` `BatchedRenderer`; event→effect mapping is data (`wwwroot/data/vfx-manifest.json`), never hardcoded. Per-effect emitters are pooled in small fixed-size round-robin groups (pool size doubles as that effect's own concurrency cap, since `ParticleSystem.restart()` kills that instance's own still-live particles) and a global 300-live-particle budget culls the globally-oldest burst first when exceeded. `vfxQuality` (`'off'\|'low'\|'full'`, now clientPref-backed — see toon.js's row) is wired end-to-end but has no UI control yet. Iteration 1 (movement dust): `entity_moved` → a small earth-tone dust puff at the entity's live rendered position (not the raw sim tile), drifting opposite the movement direction, capped at 3 concurrent per entity. Texture is a **procedural placeholder** (radial-gradient `CanvasTexture` generated at init) — the brief's Kenney CC0 particle-pack can't be fetched from this sandbox (`kenney.nl`/`itch.io` both blocked by the egress proxy's org policy); see `vfx-findings.md` and `backlog.md`. **Combat feel pass 1**: manifest rows may carry an optional `style` filter — `handleEvents` only fires a pooled effect whose row has no `style` or whose `style` matches the event's own `data.style`, so `attack_swing`/`impact`/`hit_blocked` each resolve to a *different* effect per doctrine style (`slash_arc_{melee,ranged,magic}`, `impact_burst_{melee,ranged,magic}`, `blocked_spark`/`deflect_ward`/`shield_dome`) instead of one fixed effect per event type; `landing_dust` (`forced_move`) has no style filter, same as `dust_puff`. |
| `wwwroot/data/vfx-manifest.json` | `{event, effectId, texture, count, size, lifetime, colorToken, blend}` rows — `vfx.js`'s only source of "what does this event look like." `colorToken` names a CSS custom property (read once via `getComputedStyle`), never a raw hex. |
| `wwwroot/lib/three.quarks.esm.js` | Vendored MIT particle-system lib, pinned to `0.12.1` — the newest version whose peer-dependency floor (`>=0.157.0`) our vendored `three.module.min.js` (r160/`0.160.0`) still satisfies; `0.12.2`+ requires `three>=0.182.0`. One line patched from upstream: the bare `from 'three'` import rewritten to the relative `./three.module.min.js`, matching every other vendored lib in this folder (no import map exists in `index.html`). |
| `wwwroot/data/asset-manifest.json` | item_id → renderable-asset mapping (items doc §7 shape). Human-readable mirror at repo-root `asset-map.md`, kept in sync by `AssetMapSyncTests`. Still keyed to the PoC assets (`steel_sword` + ranger set) — the M1 doc items (`wpn_*`, `arm_*`) have no 3D models yet (art pass not in scope this milestone). |
| `wwwroot/index.html` | Minimal HTML. `scrollToBottom`, `legacyLoadGame`/`legacyClearGame` (one-time pre-M0 localStorage migration only), `triggerShake`, `startMetronome`/`stopMetronome`, `spawnHitsplats` JS helpers. Loads `js/persistence.js`. Registers `service-worker.js`. |
| `wwwroot/js/persistence.js` | Game-agnostic IndexedDB key/value store (`idbGet`/`idbSet`/`idbDelete`) backing `ISaveStore`/`IndexedDbSaveStore`. |
| `wwwroot/service-worker.js` | Cache-first precache for the toon renderer's model/animation/equip assets + vendored three.js libs. Bump `CACHE_VERSION` when any file in `ASSET_URLS` changes on disk. |
| `wwwroot/css/terminal.css` | All styles — CSS variables incl. the M1 doctrine-color aliases (`--doctrine-melee/ranged/magic/hazard/safe/poison/bleed`, UI bible §1), dark terminal theme. Prayer arc is the left thumb zone, weapon arc + special is the right (UI bible §2's two-thumb-claw rule) in the `.action-hud-float` fight layout. No external CSS frameworks. |

---

## Game Content (where to add/change things)

| What | File |
|---|---|
| Change the boss's choreography | `src/Duels.Infrastructure/Definitions/npcs.json` — `maggot_king.Script` (rotation/eruption/RotBurst/swarms are all data, no code changes needed for numeric tuning) |
| Add a new weapon or armour piece | `src/Duels.Infrastructure/Definitions/items.json` — add to `weapons`/`gear` with a `Doc` block |
| Add a weapon special | `DocStats.Special.Id` in items.json, then a `case` in `GameTickService.PerformSpecialAttack`'s switch (shared dispatch, not per-weapon code) |
| Give an item a 3D model in the battle scene | `src/Duels.Web/wwwroot/data/asset-manifest.json` — add a `weapons` row (GLB, grip at origin, blade +Y) or an `armor` row + `region`; asset files go in `wwwroot/assets/models/equip/`. Mirror the row in repo-root `asset-map.md` (enforced by `AssetMapSyncTests`). |
| Add a HUD button | `src/Duels.Web/Components/Hud/ActionHud.razor` — dispatches a typed command directly |
| Change combat formula | `src/Duels.Domain/Services/DamageModel.cs` |

---

## Tests

| Suite | File | Coverage |
|---|---|---|
| Domain | `tests/Duels.Domain.Tests/DamageModelTests.cs` | Combat-math-v2: style hit/damage modifiers, Precision, Def-point mitigation + cap, line damage bonus |
| Application | `tests/Duels.Application.Tests/AttackHandlerTests.cs` | Attack queues the action, re-engages when holding position, no-duel guard |
| Application | `tests/Duels.Application.Tests/WeaponSwapBufferTests.cs` | Weapon-swap input buffer: first tap resolves immediately, overflow taps buffer and apply at the top of the next tick, the gate reopens each tick |
| Application | `tests/Duels.Application.Tests/RangeAndMovementTests.cs` | Movement/pathfinding: chase convergence, cardinal-only melee range, click-to-move hold/re-engage, straight-line pathing |
| Application | `tests/Duels.Application.Tests/MaggotKingTests.cs` | The M1 choreography suite — runs against the **real embedded** npcs.json/items.json: rotation timeline, style-telegraph forecast, eruption cadence + damage, hazard warning→pool→scorch lifecycle, Perfect Dodge, Phase 2 trigger + swarm spawn + contact bleed, Rot Burst damage/safe-tile negation/punish window |
| Application | `tests/Duels.Application.Tests/GameStateDotTests.cs` | Bleed/poison DoT lifecycle on `GameState` |
| Infrastructure | `tests/Duels.Infrastructure.Tests/Definition*RepositoryTests.cs` | Definition-file pipeline loads the real embedded JSON with expected fidelity; throws on duplicate ids or dangling references |
| Infrastructure | `tests/Duels.Infrastructure.Tests/TickSchedulerTests.cs` | Drift-corrected tick timing (real-time based, generous tolerances) |
| Infrastructure | `tests/Duels.Infrastructure.Tests/AssetMapSyncTests.cs` | asset-map.md stays in sync with asset-manifest.json |

Test pattern: inject `AlwaysHitRandom`/`FixedRandom` for deterministic combat. All stubs are inline in the test file.

**Verification caveat (M1):** this milestone was implemented in a sandboxed session with no .NET SDK available — every change was hand-traced rather than compiled. `dotnet build`/`dotnet test` must be run for real (CI or a session with a working SDK) before trusting this milestone green. See `design/plans/m1-findings.md`.

---

## Deployment

Hosted on **GitHub Pages**, deployed via GitHub Actions.

| File | Purpose |
|---|---|
| `.github/workflows/deploy.yml` | Builds with .NET 8, fixes base href to `/duels/`, copies `index.html` → `404.html` for SPA routing, deploys via `actions/deploy-pages`. |

**GitHub repository settings** (set once):
- Settings → Pages → Source → **GitHub Actions**

Live URL: `https://blackpixel-piotr.github.io/duels/`

---

## Cleanup Rule

> After any feature change, addition, or removal:
> 1. Delete all dead code — unused classes, methods, fields, using directives.
> 2. Update the relevant section(s) of this file to reflect the new state.
> 3. Run `dotnet build Duels.sln` (0 warnings) and `dotnet test` (all pass) before committing.
