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
| `Entities/Player.cs` | Player state: XP, levels, HP, gold, equipped items, inventory. XP → level via OSRS formula (`LevelForXp`, `XpForLevel`). |
| `Entities/NpcTemplate.cs` | NPC definition + `NpcInstance` (per-duel runtime HP). Also `LootEntry`. |
| `Entities/Weapon.cs` | Weapon definition + `SpecialAttack` record. `AsGearPiece()` converts it to a `GearPiece`. |
| `Entities/GearPiece.cs` | Armour/gear with slot + `ItemModifiers`. |
| `Entities/Quest.cs` | Quest stub: title, objectives, reward. |
| `Entities/InvocationDefinition.cs` | Invocation (ToA-model pre-fight modifier) schema — id, name, raid level, effect, tier, tags. Content ships in M4; `invocations.json` is an empty stub for now. |
| `ValueObjects/CombatStats.cs` | Attack/Strength/Defence/Hitpoints record. |
| `ValueObjects/ItemModifiers.cs` | All OSRS-style bonuses (stab/slash/crush atk+def, str bonus, prayer). `.Add()` for aggregation. `.AttackBonusFor(type)` / `.DefenceBonusFor(type)` helpers. |
| `ValueObjects/AttackStyle.cs` | Accurate / Aggressive / Defensive enum. |
| `ValueObjects/AttackType.cs` | Stab / Slash / Crush enum. |
| `ValueObjects/EquipmentSlot.cs` | Weapon, Shield, Helmet, Body, Legs, Boots, Gloves, Cape, Amulet, Ring. |
| `Events/*.cs` | Domain event records: `DuelStarted`, `AttackLanded`, `AttackMissed`, `DuelWon`, `DuelLost`, `ItemUnlocked`, `LevelUp`. Base: `DomainEvent`. |
| `Interfaces/IRandomProvider.cs` | `Next(min, max)` + `NextDouble()` — injected so tests use deterministic fakes. |
| `Interfaces/ICombatCalculator.cs` | `Roll(attacker, defender)` → `CombatRollResult(Hit, Damage)`. Input: `CombatantSnapshot`. |
| `Services/CombatCalculator.cs` | OSRS-faithful implementation of `ICombatCalculator`. See formula notes below. |
| `Services/TickConstants.cs` | The single authoritative tick timing: `TickDurationMs` (600) and `InputBufferWindowMs` (150, UI bible §2). Every tick-timed value in the game routes through these two constants. |

**Combat formula (OSRS)**:
1. `maxAttackRoll = (attackLevel + styleBonus + 8) × (equipAttackBonus + 64)`
2. `maxDefenceRoll = (defenceLevel + styleBonus + 8) × (equipDefenceBonus + 64)`
3. `maxHit = floor(0.5 + (strengthLevel + styleBonus + 8) × (strengthBonus + 64) / 640)`
4. Accuracy: if `attackRoll > defRoll` → `1 − (defRoll+2)/(2×(attackRoll+1))`; else → `attackRoll/(2×(defRoll+1))`
5. Roll `0..maxHit` if hit, else 0.

Style bonuses: Accurate → +3 attack; Aggressive → +3 strength; Defensive → +3 defence.

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
| `GameSession/GameState.cs` | Per-session state: `Player`, `ActiveNpc`, `CombatLog`, `InDuel`. `AppendLog(msg, kind)` with 500-entry cap. Also owns the weapon-swap input buffer (`WeaponSwapClaimedThisTick`/`PendingWeaponSwapId`) — UI bible §3.2's "max one swap per tick; extra taps buffer". |
| `Commands/*.cs` | `StartGameCommand`, `StartDuelCommand`, `AttackCommand`, `EquipItemCommand`, `UnequipItemCommand`, `InspectCommand`, `ListNpcsCommand`, `HelpCommand`. |
| `Handlers/*.cs` | One handler per command. `AttackHandler` runs one full combat tick (player → NPC → NPC → player), emits events, calls `ItemUnlockService` on kill, awards XP + logs level-ups. |
| `Parsing/CommandParser.cs` | Maps raw text (`!duel goblin`, `!whip`, `!attack aggressive`) to typed commands. Kept for potential future text-input reintroduction, but unused by the current UI — every button dispatches a typed command directly via `GameService.DispatchAsync<T>()`, no parsing step. Not registered in DI. |
| `Services/ItemUnlockService.cs` | Rolls loot table on NPC death using `IRandomProvider`. Skips items already owned. |
| `Services/GameTickService.cs` | Formal tick scheduler consumer (M0): its `Loop` awaits `ITickSource.WaitForNextTickAsync` (drift-corrected, see `TickScheduler` below) instead of a raw `Task.Delay(600)` chain; `ProcessTick` itself (combat, movement, hazards, DoTs, loot, victory/defeat) is unchanged. Also applies a buffered weapon swap at the top of each tick (see `GameState` above). |

---

### Duels.Infrastructure (`src/Duels.Infrastructure/`)
All implementations. Registered in one place — `DI/InfrastructureServiceExtensions.cs`.

| Path | Contents |
|---|---|
| `Messaging/LocalCommandQueue.cs` | `ICommandDispatcher` → resolves `ICommandHandler<T>` from DI and calls it synchronously. **Swap point for Kafka.** |
| `Messaging/InMemoryEventBus.cs` | `IEventBus` → dictionary of typed handler lists. |
| `Persistence/InMemoryGameStateRepository.cs` | `IGameStateRepository` — singleton dictionary, lives for the browser session. |
| `Persistence/InMemoryPlayerRepository.cs` | `IPlayerRepository` — same pattern. |
| `Persistence/DefinitionNpcRepository.cs` | `INpcRepository` — loads `Definitions/npcs.json` at startup. Cross-validates every loot-table item id against `IItemRepository`; throws loudly on a typo. |
| `Persistence/DefinitionItemRepository.cs` | `IItemRepository` — loads `Definitions/items.json` at startup. Throws on duplicate ids or a shop price referencing an unknown item. |
| `Persistence/DefinitionInvocationRepository.cs` | `IInvocationRepository` — loads `Definitions/invocations.json` (empty stub until M4). |
| `Persistence/IndexedDbSaveStore.cs` | `ISaveStore` → IndexedDB via `IJSRuntime` calling `wwwroot/js/persistence.js`'s `idbGet`/`idbSet`/`idbDelete`. Replaces the old direct localStorage calls. |
| `Definitions/DefinitionLoader.cs` | Loads a JSON definitions file embedded as a resource in this assembly; malformed JSON throws with the file name. |
| `Definitions/items.json`, `npcs.json`, `invocations.json` | Content as data (working agreement #3) — mirror the design docs' tables 1:1, keyed by item_id/npc_id/invocation_id. Embedded resources (`Definitions\*.json` in the csproj), not `wwwroot` fetches, so the repositories stay synchronous. |
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
| `Services/GameService.cs` | Owns `PlayerId`. Exposes `StartNewGameAsync`, `DispatchAsync<TCommand>`, `GetStateAsync`, `PersistNowAsync`. Fires `StateChanged` event so components re-render. Saves are a versioned `SaveEnvelope` (`Models/SaveEnvelope.cs`) wrapping `SaveData`, written via `ISaveStore`; a one-time migration imports a pre-M0 localStorage save on first load. `DispatchAsync` skips persistence for high-frequency in-duel commands (attack/prayer/weapon-swap/style/move/engage/eat/drink) — the tick loop keeps in-memory state current, and `Game.razor`'s `OnTickNotify` persists on duel end plus every 10 ticks as a safety net. |
| `Pages/NewGame.razor` | `/` route — character name input, calls `GameService.StartNewGameAsync`. |
| `Pages/Game.razor` | `/game` route — single-column mobile shell (`.game-shell`, max-width 520px on every screen size). Subscribes to `GameService.StateChanged` + `GameTickService.RegisterNotify`. |
| `Components/Hud/StatusStrip.razor` | Always-visible slim header: name, Atk/Str/Def levels, gold, HP/prayer/spec bars, status chips. |
| `Components/Hud/ActionHud.razor` | Fixed bottom action bar. In duel: tick metronome (`#tick-bar`) + consumable belt + weapon grid (cooldown fill) + prayer row. Out of duel: ARENA/SHOP/BAG/CHAR/LOG nav. Every button dispatches a typed command directly — no text parsing. |
| `Components/Hub/HubMenu.razor` | Out-of-duel home screen — big cards for Arena/Endless/Shop plus conditional Retry/Prestige/Beg cards. |
| `Components/Combat/CombatStage.razor` | Enemy-first duel view — NPC name/style badge/HP bar + telegraph banner, compact player row with `#zone-player`/`#zone-npc` hitsplat anchors. |
| `Components/Terminal/EventLog.razor` | Narrative combat log, fills all remaining space above the action HUD. Auto-scrolls via `scrollToBottom` JS interop; also drives hitsplat spawn + shake JS calls off the same log diff. |
| `Components/Hud/ToastHost.razor` | Top-center toast stack for level-up/loot log entries, timestamp-pruned after 3s. |
| `Components/Hud/CharacterSheet.razor`, `Components/Bag/BagSheet.razor` | Modal sheets wrapping `StatsPanel` and `EquipmentPanel`+`InventoryGrid` respectively. |
| `Components/Stats/StatsPanel.razor` | Full stat detail (levels, xp bars, gold, prayer/streak/veng state) — shown inside `CharacterSheet`. |
| `Components/Inventory/InventoryGrid.razor` | 28-slot OSRS-style grid + equipped items. Tap routes by item type: food → `EatItemCommand`, potions → `DrinkPotionCommand`, else → `EquipItemCommand`. |
| `wwwroot/js/toon.js` | The battle renderer (three.js, owns `window.voxel`). Characters are the Universal Base glTF driven by Universal Animation Library clips. Equipment: `setBattleWeapon` sockets modeled weapons (`WEAPON_ASSETS`) into a hand_r socket + swaps in the sword-idle stance; `setBattleEquipment` skins modular armor pieces (`ARMOR_ASSETS`, Quaternius outfit parts sharing the same rig) onto the live skeleton and masks the base body's skin under each covered region (`ARMOR_REGION`/`maskBodySkin` — the pieces are body-segment replacements, not overlays). `WEAPON_ASSETS`/`ARMOR_ASSETS`/`ARMOR_REGION` are built at load from `data/asset-manifest.json` (M0 asset manifest loader), not hard-coded. Assets live in `wwwroot/assets/models/equip/`. |
| `wwwroot/data/asset-manifest.json` | item_id → renderable-asset mapping (items doc §7 shape) that `toon.js` fetches at startup. Human-readable mirror at repo-root `asset-map.md`, kept in sync by `AssetMapSyncTests`. |
| `Components/Combat/BattleScene.razor` | Mounts the battle canvas, forwards sim state to the renderer (positions, vitals, hazards, flags, weapon id, and the sorted non-weapon equipped item ids via `setBattleEquipment`). |
| `wwwroot/index.html` | Minimal HTML. `scrollToBottom`, `legacyLoadGame`/`legacyClearGame` (one-time pre-M0 localStorage migration only — see `GameService`), `triggerShake`, `startMetronome`/`stopMetronome` (tick-cycle sweep + flash, a prayer-flick timing aid), `spawnHitsplats` JS helpers. Loads `js/persistence.js`. Registers `service-worker.js`. |
| `wwwroot/js/persistence.js` | Game-agnostic IndexedDB key/value store (`idbGet`/`idbSet`/`idbDelete`) backing `ISaveStore`/`IndexedDbSaveStore`. |
| `wwwroot/service-worker.js` | Cache-first precache for the toon renderer's model/animation/equip assets + vendored three.js libs (~8MB, static, never change between sessions) — skips the network entirely on repeat visits. Bump `CACHE_VERSION` when any file in `ASSET_URLS` changes on disk. Note: this only saves network time, not the glTF parse/decode CPU cost — `GLTFLoader` re-parses from scratch every page load regardless of caching, since the parsed result isn't itself persisted. |
| `wwwroot/css/terminal.css` | All styles — CSS variables, dark terminal theme, CRT scanline overlay + glow utilities. No external CSS frameworks. |

---

## Game Content (where to add/change things)

| What | File |
|---|---|
| Add a new NPC | `src/Duels.Infrastructure/Definitions/npcs.json` — add an entry (loot table item ids must already exist in items.json or the repository throws at startup) |
| Add a new weapon | `src/Duels.Infrastructure/Definitions/items.json` — add to `weapons` |
| Add new gear | Same file — add to `gear`; add a `shopPrices`/`fenceValues` row if it's purchasable/fenceable |
| Give an item a 3D model in the battle scene | `src/Duels.Web/wwwroot/data/asset-manifest.json` — add a `weapons` row (GLB, grip at origin, blade +Y) or an `armor` row + `region` (modular outfit glTF on the universal rig); asset files go in `wwwroot/assets/models/equip/`. Mirror the row in repo-root `asset-map.md` (enforced by `AssetMapSyncTests`). PoC: `steel_sword` + the 6-piece ranger set (equipped by the TEST FIGHT loadout). |
| Add a HUD button | `src/Duels.Web/Components/Hud/ActionHud.razor` — dispatches a typed command directly, no parser involved |
| Change combat formula | `src/Duels.Domain/Services/CombatCalculator.cs` |
| Change XP curve | `src/Duels.Domain/Services/ExperienceTable.cs` |

---

## Tests

| Suite | File | Coverage |
|---|---|---|
| Domain | `tests/Duels.Domain.Tests/CombatCalculatorTests.cs` | Combat rolls (hit/miss/damage), XP/level formula, `ItemModifiers.Add`, player HP floor |
| Application | `tests/Duels.Application.Tests/AttackHandlerTests.cs` | Attack emits events, NPC kill emits `DuelWon`, no-duel guard |
| Application | `tests/Duels.Application.Tests/WeaponSwapBufferTests.cs` | Weapon-swap input buffer: first tap resolves immediately, overflow taps buffer and apply at the top of the next tick, the gate reopens each tick |
| Infrastructure | `tests/Duels.Infrastructure.Tests/Definition*RepositoryTests.cs` | Definition-file pipeline loads the real embedded JSON with expected fidelity; throws on duplicate ids or dangling references |
| Infrastructure | `tests/Duels.Infrastructure.Tests/TickSchedulerTests.cs` | Drift-corrected tick timing (real-time based, generous tolerances) |
| Infrastructure | `tests/Duels.Infrastructure.Tests/AssetMapSyncTests.cs` | asset-map.md stays in sync with asset-manifest.json |

Test pattern: inject `FixedRandom`/`AlwaysHitRandom` for deterministic combat. All stubs are inline in the test file.

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
