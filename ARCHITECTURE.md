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
| Persistence | In-memory (browser session) — see swap path below |
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
| `ValueObjects/CombatStats.cs` | Attack/Strength/Defence/Hitpoints record. |
| `ValueObjects/ItemModifiers.cs` | All OSRS-style bonuses (stab/slash/crush atk+def, str bonus, prayer). `.Add()` for aggregation. `.AttackBonusFor(type)` / `.DefenceBonusFor(type)` helpers. |
| `ValueObjects/AttackStyle.cs` | Accurate / Aggressive / Defensive enum. |
| `ValueObjects/AttackType.cs` | Stab / Slash / Crush enum. |
| `ValueObjects/EquipmentSlot.cs` | Weapon, Shield, Helmet, Body, Legs, Boots, Gloves, Cape, Amulet, Ring. |
| `Events/*.cs` | Domain event records: `DuelStarted`, `AttackLanded`, `AttackMissed`, `DuelWon`, `DuelLost`, `ItemUnlocked`, `LevelUp`. Base: `DomainEvent`. |
| `Interfaces/IRandomProvider.cs` | `Next(min, max)` + `NextDouble()` — injected so tests use deterministic fakes. |
| `Interfaces/ICombatCalculator.cs` | `Roll(attacker, defender)` → `CombatRollResult(Hit, Damage)`. Input: `CombatantSnapshot`. |
| `Services/CombatCalculator.cs` | OSRS-faithful implementation of `ICombatCalculator`. See formula notes below. |

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
| `GameSession/GameState.cs` | Per-session state: `Player`, `ActiveNpc`, `CombatLog`, `InDuel`. `AppendLog(msg, kind)` with 500-entry cap. |
| `Commands/*.cs` | `StartGameCommand`, `StartDuelCommand`, `AttackCommand`, `EquipItemCommand`, `UnequipItemCommand`, `InspectCommand`, `ListNpcsCommand`, `HelpCommand`. |
| `Handlers/*.cs` | One handler per command. `AttackHandler` runs one full combat tick (player → NPC → NPC → player), emits events, calls `ItemUnlockService` on kill, awards XP + logs level-ups. |
| `Parsing/CommandParser.cs` | Maps raw text (`!duel goblin`, `!whip`, `!attack aggressive`) to typed commands. Returns `ParseResult(Success, Command, Error)`. **Both terminal input and quickslot bar go through this.** |
| `Services/ItemUnlockService.cs` | Rolls loot table on NPC death using `IRandomProvider`. Skips items already owned. |

---

### Duels.Infrastructure (`src/Duels.Infrastructure/`)
All implementations. Registered in one place — `DI/InfrastructureServiceExtensions.cs`.

| Path | Contents |
|---|---|
| `Messaging/LocalCommandQueue.cs` | `ICommandDispatcher` → resolves `ICommandHandler<T>` from DI and calls it synchronously. **Swap point for Kafka.** |
| `Messaging/InMemoryEventBus.cs` | `IEventBus` → dictionary of typed handler lists. |
| `Persistence/InMemoryGameStateRepository.cs` | `IGameStateRepository` — singleton dictionary, lives for the browser session. |
| `Persistence/InMemoryPlayerRepository.cs` | `IPlayerRepository` — same pattern. |
| `Persistence/InMemoryNpcRepository.cs` | `INpcRepository` — all NPC templates hard-coded here. Add new NPCs here. |
| `Persistence/InMemoryItemRepository.cs` | `IItemRepository` — all weapons and gear hard-coded here. Add new items here. |
| `Random/SystemRandomProvider.cs` | `IRandomProvider` → `Random.Shared`. |
| `DI/InfrastructureServiceExtensions.cs` | `AddDuelsInfrastructure(IServiceCollection)` — wires everything. Called once from `Program.cs`. |

**Swap paths** (zero Domain/Application changes needed):
- **Persistence**: replace `InMemoryGameStateRepository` with `LocalStorageGameStateRepository` or a remote API client.
- **Queue**: replace `LocalCommandQueue` with `KafkaCommandQueue` — same `ICommandDispatcher` interface.
- **Event bus**: replace `InMemoryEventBus` with SignalR or a message broker.

---

### Duels.Web (`src/Duels.Web/`)
Blazor WASM. Depends on Application (interfaces) + Infrastructure (DI wiring only via `Program.cs`).

| Path | Contents |
|---|---|
| `Program.cs` | Calls `AddDuelsInfrastructure()` + registers `GameService` as singleton. |
| `Services/GameService.cs` | Owns `PlayerId`. Exposes `StartNewGameAsync`, `ExecuteCommandAsync`, `GetStateAsync`. Fires `StateChanged` event so components re-render. |
| `Pages/NewGame.razor` | `/` route — character name input, calls `GameService.StartNewGameAsync`. |
| `Pages/Game.razor` | `/game` route — three-column layout. Subscribes to `GameService.StateChanged`. |
| `Components/Terminal/TerminalComponent.razor` | Scrolling combat log. Auto-scrolls via `scrollToBottom` JS interop. |
| `Components/Terminal/TerminalInput.razor` | Text field + Send button. Fires `OnSubmit(string)` callback. Calls `focusElement` JS interop after submit. |
| `Components/QuickSlots/QuickSlotBar.razor` | Row of preset command buttons. Each fires `OnCommand(string)` — same pipeline as typed text. Edit slot definitions here to add/change quickslots. |
| `Components/Stats/StatsPanel.razor` | Left panel: HP bar, special bar, combat levels, gold. |
| `Components/Inventory/InventoryGrid.razor` | Right panel: 28-slot OSRS-style grid + equipped items. Click → `!equip <id>`. |
| `Components/Combat/NpcCard.razor` | NPC name, level, HP bar. Shown only during a duel. |
| `wwwroot/index.html` | Minimal HTML. Includes `scrollToBottom` + `focusElement` JS helpers. |
| `wwwroot/css/terminal.css` | All styles — CSS variables, dark terminal theme. No external CSS frameworks. |

---

## Game Content (where to add/change things)

| What | File |
|---|---|
| Add a new NPC | `src/Duels.Infrastructure/Persistence/InMemoryNpcRepository.cs` — add entry to `BuildNpcs()` |
| Add a new weapon | `src/Duels.Infrastructure/Persistence/InMemoryItemRepository.cs` — add to `BuildWeapons()` |
| Add new gear | Same file — add to `BuildGear()` |
| Add a command alias | `src/Duels.Application/Parsing/CommandParser.cs` — add to the `switch` expression |
| Add a quickslot | `src/Duels.Web/Components/QuickSlots/QuickSlotBar.razor` — add to `_slots` array |
| Change combat formula | `src/Duels.Domain/Services/CombatCalculator.cs` |
| Change XP curve | `src/Duels.Domain/Entities/Player.cs` — `XpForLevel` method |

---

## Tests

| Suite | File | Coverage |
|---|---|---|
| Domain | `tests/Duels.Domain.Tests/CombatCalculatorTests.cs` | Combat rolls (hit/miss/damage), XP/level formula, `ItemModifiers.Add`, player HP floor |
| Application | `tests/Duels.Application.Tests/AttackHandlerTests.cs` | Attack emits events, NPC kill emits `DuelWon`, no-duel guard |

Test pattern: inject `FixedRandom`/`AlwaysHitRandom` for deterministic combat. All stubs are inline in the test file.

---

## Deployment

Hosted on **Cloudflare Pages** (free tier, supports private repos).

| File | Purpose |
|---|---|
| `build.sh` | Installs .NET 8 via `dotnet-install.sh`, then runs `dotnet publish`. Used as the Cloudflare build command. |
| `src/Duels.Web/wwwroot/_redirects` | SPA routing — `/* /index.html 200` so `/game` works on direct load/refresh. |

**Cloudflare dashboard settings** (set once):
- Build command: `bash build.sh`
- Build output directory: `release/wwwroot`

Live URL: `https://duels.pages.dev`

---

## Cleanup Rule

> After any feature change, addition, or removal:
> 1. Delete all dead code — unused classes, methods, fields, using directives.
> 2. Update the relevant section(s) of this file to reflect the new state.
> 3. Run `dotnet build Duels.sln` (0 warnings) and `dotnet test` (all pass) before committing.
