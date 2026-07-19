# M0 — Foundations: Technical Plan

Status: **implemented** (all four workstreams). See "Implementation Notes" at the end for what landed, two scope findings surfaced during implementation, and an environment caveat on verification. Q1 and Q2 below were resolved by the human reviewer before implementation started.

Scope (implementation brief, M0): formal tick scheduler (single authoritative 0.6s tick, input buffering per UI bible §2) · definition-file pipeline for items/bosses/invocations · persistence service replacing the cache · asset manifest loader (Quaternius paths per items doc §7). **No visible feature.**

Playtest question: **does the existing combat still run identically on the new tick core?**

---

## 1. Current-state audit (what M0 builds on)

| Area | Today | File |
|---|---|---|
| Tick loop | `GameTickService` — `Task.Delay(600)` loop started/stopped by the game page; the same class also contains all per-tick combat logic (movement, attacks, hazards, DoTs, loot, victory/defeat) | `src/Duels.Application/Services/GameTickService.cs` |
| Input → tick | Buttons dispatch typed commands synchronously (`LocalCommandQueue`); action inputs set `GameState.QueuedAction`, consumed on the next tick where cooldown/range allow; prayer & weapon-swap handlers mutate state immediately (flicking works off the `TickStartProtection` snapshot) | `Handlers/*.cs`, `GameSession/GameState.cs:26,266` |
| Off-tick mutation | `KickMoveAsync` advances player movement on the click itself, outside the tick, as a latency feel-fix | `GameTickService.cs:77` |
| Persistence | `localStorage['duels_save']` via three JS interop functions; `GameService.PersistAsync` serializes the whole `SaveData` after **every** command | `Services/GameService.cs`, `wwwroot/index.html:26–28`, `Models/SaveData.cs` |
| Content | All NPCs and items hard-coded in C# builders | `Infrastructure/Persistence/InMemoryNpcRepository.cs`, `InMemoryItemRepository.cs` |
| Asset mapping | Hard-coded `WEAPON_ASSETS` / `ARMOR_ASSETS` dictionaries in the renderer; no `asset-map.md` anywhere in the repo | `wwwroot/js/toon.js` |
| Tests | Deterministic combat via injected `FixedRandom`; tick logic covered by `RangeAndMovementTests`, `MaggotKingTests`, `GameTickServiceLootTests`, `AttackHandlerTests` | `tests/Duels.Application.Tests/` |

---

## 2. Workstream A — Formal tick scheduler

Goal: separate *scheduling* from *game logic*, make the 0.6s tick the single authoritative clock, and formalize the input-buffering contract from UI bible §2 — while the per-tick combat logic itself stays byte-for-byte identical.

### Design

- **`TickConstants` (Domain).** One named home for `TickDurationMs = 600` and `InputBufferWindowMs = 150`. All current literal `600`s route through it.
- **`ITickSource` (Application abstraction) + `TickScheduler` (Infrastructure).** The scheduler owns timing only: it raises `TickElapsed(long tickNumber)` on a drift-corrected schedule (next tick computed from a monotonic clock, not naive `Task.Delay` accumulation, so background-tab throttling doesn't skew cadence) and exposes `TimeIntoTick` for the input buffer. Tests get a `ManualTickSource` that advances by explicit calls — the existing test suites currently invoke `ProcessTick` indirectly; they move to the manual source unchanged in intent.
- **`GameTickService` becomes the tick *consumer*.** Its `Loop` is replaced by a subscription to `ITickSource`; `ProcessTick` and everything below it is untouched in this milestone (boss-specific branches inside it are M3's extraction problem, explicitly not M0's).
- **`TickInputBuffer` (Application).** Formalizes UI bible §2 input classes:
  - **Instant class** (resolve on the tick they're tapped, i.e. mutate state immediately as today): protection-prayer toggle, boost prayer, weapon swap, attack-style toggle. Constraint added: **max one weapon swap per tick; extra taps buffer to the next tick** (§3.2). Prayer flicking semantics are already correct via `TickStartProtection` and must not change.
  - **Action class** (one per tick — attack, spec, sip, move order): dispatched commands stamp the current `TimeIntoTick`. A tap landing **within the final 150ms of a tick** is queued for the next tick instead of racing tick resolution; earlier taps behave exactly as today (`QueuedAction`). Buffer depth 1 per class — latest input wins (open question Q4).
  - The buffer sits between the command handlers and `GameState` mutation for the constrained cases only; handlers keep their current shape.
- **`KickMoveAsync` stays** (it deliberately trades tick purity for touch feel and only advances movement the tick would have advanced anyway) but gets folded behind the scheduler's clock so it can't double-run within one tick.
- 50ms visual/haptic acknowledgment (§2) is HUD work — **M1, out of scope**; M0 only guarantees the dispatch path stays synchronous and cheap.
- Tick authority / interop boundary (reserved decision): M0 adds **no** new JS interop in the combat loop; the existing one-snapshot-per-frame `BattleScene` path is unchanged.

### Verification (the playtest question, made mechanical)

- **Golden-run test:** script a full fight (fixed seed via `FixedRandom`, scripted inputs per tick) against the current code, capture the combat log + end state; the same script on the new tick core must produce an identical transcript. This test is written *first*, against the old core, and survives the refactor.
- Existing test suites pass unchanged.
- Manual: play a ladder duel and the Maggot King test fight on a phone; combat should be indistinguishable.

## 3. Workstream B — Definition-file pipeline (items / bosses / invocations)

Goal: content becomes data (working agreement #3). Definition files mirror the doc tables 1:1, keyed by `item_id` / `boss_id`; repositories load them instead of hard-coding.

### Design

- **Format & location:** JSON files embedded as resources in `Duels.Infrastructure/Definitions/` (`items.json`, `npcs.json`, `invocations.json`), loaded once at startup. Embedded resources (vs `wwwroot` fetch) keep the repositories synchronous — no async refactor ripples through handlers — and load identically under test. (Flagged as a technical choice; see Q6.)
- **Schemas mirror the doc tables:**
  - *Items:* `item_id`, `display_name`, `slot`, `tier`, `style/line`, doc-stat block (`power`, `precision`, `def`, identity/set bonus keys), `special {name, cost, effect_id, params}`, `price`, plus a *legacy-stat block* (`ItemModifiers`, attack speed, range) for the current OSRS-model content. Which block an item carries is explicit; the two models coexist until the combat-math switch (Q1).
  - *NPCs/bosses:* everything `NpcTemplate` holds today (stats, modifiers, style, loot table, gold, attack speed, telegraphed move, hazard profile) — a 1:1 serialization of the existing builders.
  - *Invocations:* schema + loader only, content stub (invocations are M4; the brief only asks that the pipeline exist).
- **Repositories:** `DefinitionItemRepository` / `DefinitionNpcRepository` (Infrastructure) implement the existing `IItemRepository` / `INpcRepository` unchanged; DI swap in `InfrastructureServiceExtensions`. The `InMemory*` builders are deleted after migration (cleanup rule).
- **Migration:** current hard-coded content is exported 1:1 into the JSON files (a one-off — verified by a test asserting the loaded set equals the old builders' output, then the builders are removed). This proves the pipeline on real content while keeping combat identical.
- **Validation, loud at startup:** duplicate ids, unknown fields, loot-table references to missing item ids, negative prices/stats → throw with a message naming the file and row. Bad data must never load silently.
- **Doc-row ingestion readiness:** the item schema accepts the items-doc rows (`wpn_melee_t1` …) verbatim, so M1's dev-loadout ingestion is a data commit, not a code change.

## 4. Workstream C — Persistence service (replaces the cache)

**Reserved decision — needs explicit confirmation before this workstream starts:** the brief recommends local-first **IndexedDB** via a small persistence service, backend accounts deferred to M8. This plan assumes that recommendation is confirmed as-is.

### Design

- **`ISaveStore` (Application abstraction):** `LoadAsync(key)` / `SaveAsync(key, json)` / `DeleteAsync(key)` — string-keyed JSON blobs, nothing game-aware.
- **`IndexedDbSaveStore` (Infrastructure/Web):** thin JS module (`wwwroot/js/persistence.js`, one object store, promise-based get/put/delete) + C# wrapper via `IJSRuntime`. Replaces the three `index.html` localStorage helpers.
- **Versioned envelope:** `{ schemaVersion, payload }` wrapping the existing `SaveData` shape (`schemaVersion: 1`). Future migrations key off it; the current ad-hoc XP-sentinel migration in `RestoreSaveAsync` becomes migration step 0.
- **One-time migration:** on first load, if IndexedDB is empty and `localStorage['duels_save']` exists → import it, then remove the localStorage entry. No save is lost.
- **Save cadence:** stop serializing after literally every command. Persist on: duel end, out-of-duel state-changing commands (buy/sell/equip/bank/prestige), and a low-frequency safety interval while in a duel. `GameService` keeps the orchestration role; only its storage calls change.
- Failure behavior stays as today (persistence errors never break gameplay), but errors are logged to console rather than swallowed blind.

## 5. Workstream D — Asset manifest loader

Goal: the renderer's item→model mapping becomes data with the items-doc §7 shape, and `asset-map.md` exists and stays current.

### Design

- **`wwwroot/data/asset-manifest.json`** — one entry per `item_id`: `{ item_id, display_name, model_ref, tint, flag }`, exactly the §7 columns. `model_ref` uses pack-relative paths (`quaternius/weapons/…`, `outfits_fantasy/…`) resolved against `wwwroot/assets/models/equip/`.
- **`asset-map.md`** (repo root, per brief) — the human-readable table view of the same rows, created now with the *current* PoC content (steel_sword + ranger set + existing weapon models) and updated with every content addition. The JSON is what the code loads; the md is the doc of record for art handoff. Sync is enforced by a test that regenerates the md table from the JSON and diffs it.
- **Loader:** `toon.js` fetches the manifest once at renderer init and builds `WEAPON_ASSETS` / `ARMOR_ASSETS` (and `ARMOR_REGION`, which gains a `region` column in the manifest for armor rows) from it; the hard-coded dictionaries are deleted. The service worker precache list gains the manifest file. No behavior change — same models, same sockets.
- Rows for not-yet-downloaded doc items are **not** invented; the manifest holds only assets that exist on disk (🟢 rows land in M1+ as models arrive).

---

## 6. Sequencing

1. **B first** (definition pipeline + migration) — self-contained, unblocks everything, and its equality test hardens the ground for A.
2. **A** (tick core) — golden-run test written against the old core *before* the refactor.
3. **C** (persistence) — after its reserved-decision confirmation; independent of A/B.
4. **D** (asset manifest) — independent; can run parallel to C.
5. Cleanup rule per CLAUDE.md/ARCHITECTURE.md: delete dead code (old builders, localStorage helpers), update `ARCHITECTURE.md` sections, `dotnet build` 0 warnings, `dotnet test` green, `graphify update .` after each workstream.

Definition of done: all four workstreams merged, golden-run transcript identical, a phone playthrough of the existing content is indistinguishable from pre-M0.

---

## 7. Reserved decisions & design questions (need answers, not assumptions)

**Reserved (human calls from the brief):**
- **R1 — Persistence:** confirm local-first IndexedDB replacing localStorage now (workstream C blocks on this).
- **R2 — Tick authority / interop boundary:** confirm the recommendation as stated. M0 already conforms (C# ticks, batched snapshot to Three.js); no work needed beyond not regressing it.

**Design questions (flagged per working agreement #2 — current behavior is preserved either way in M0, but the schema/plan should record the intent):**
- **Q1 — RESOLVED.** OSRS formulas are not sacred — the game can diverge to its own unique combat math. The doc's Power/Precision/Def-% model (§1) is the target; the live OSRS `CombatCalculator` is not a constraint M0 needs to preserve forever. The switch itself still lands in M1 with the Maggot King rebuild (M0's golden-run test only needs the *tick core* refactor to be behavior-preserving, not the combat formula to stay OSRS); M0's item schema carries both stat blocks so the doc-math switch is a content/logic change in M1, not a schema migration.
- **Q2 — RESOLVED.** 2 tiles/tick for the player is correct and stays as the live, authoritative behavior. The boss bible's "1 tile moved per tick" grammar line describes NPCs/bosses, not the player; no code or design-doc change needed for M0.
- **Q3 — Special energy regen.** Doc: ~2/tick out of danger, 1/tick in combat. Live: +10 per player attack. Same treatment as Q1 (M1 switch)? Confirm.
- **Q4 — Buffer depth.** "Extra taps buffer" (§3.2, weapon swaps): assumed depth 1, latest-wins, for all buffered input. Confirm.
- **Q5 — Legacy ladder content.** The linear ladder is retired by the decisions doc but is the only playable content until M1. Assumed: it migrates into definition files as-is and dies later with its milestone, not in M0. Confirm.
- **Q6 — Definition file location.** Embedded resources (recommended, synchronous) vs `wwwroot` fetch (editable without rebuild). Confirm the recommendation.

## 8. Explicitly out of scope

New HUD, action bar, flask belt, Maggot King rebuild, telegraph framework/Perfect Dodge/punish windows as shared systems (M1/M3), any doc-item ingestion beyond schema readiness, invocation content, combat-math switch, boss-branch extraction from `GameTickService`, any visible feature.

---

## 9. Implementation Notes (post-implementation)

All four workstreams landed. Notes on what shipped and two places the plan's assumptions met reality:

**Workstream B (definitions).** `items.json`/`npcs.json` are 1:1 transcriptions of the old `InMemoryItemRepository`/`InMemoryNpcRepository` builders (verified field-by-field during transcription, plus fidelity-spot-check tests); the old builders are deleted. `invocations.json` ships as the empty-array stub the plan called for. Q6 resolved as recommended: embedded resources, not `wwwroot` fetches — repositories stay synchronous.

**Workstream A (tick core) — scope finding on input buffering.** The formal drift-corrected `TickScheduler`/`ITickSource` landed as planned, with `GameTickService.Loop`'s body otherwise untouched (same `ProcessTick`, same sequencing). For input buffering, one rule from UI bible §2/§3.2 turned out to need real machinery and one didn't:
- **Weapon swaps** ("max one swap per tick; extra taps buffer") were a genuine gap — multiple same-tick taps used to all resolve instantly, last-wins. Implemented: `GameState.TryClaimWeaponSwapSlot()`/`PendingWeaponSwapId`, `WeaponShortcutHandler` defers an overflow tap, `GameTickService.ProcessTick` applies it at the top of the next tick. Q4 (buffer depth) resolved as assumed: depth 1, latest tap wins.
- **The general 150ms end-of-tick action buffer** (attack/spec/sip/move) is, on inspection, moot in this codebase: Blazor WASM runs UI event handlers and the tick loop on the same single logical thread with cooperative `async`/`await` yielding, not real parallelism — a tap either lands before `ProcessTick` runs (used this tick) or after (next tick), and `QueuedAction` already never drops a tap regardless of timing. There is no race here to protect against, so no additional buffering code was added for that class; `KickMoveAsync` (the click-to-move latency fix) got a small guard against double-advancing a move order right at a tick boundary, which is the one place timing-adjacent risk actually existed.

**Workstream C (persistence).** IndexedDB (`persistence.js` + `IndexedDbSaveStore`) replaces localStorage, behind a new `ISaveStore` and a versioned `SaveEnvelope`, with a one-time migration off the old `duels_save` localStorage key. Save-cadence reduction landed differently than first sketched: `GameTickService` (Application layer) can't construct `SaveData` (a Web-layer type) itself, so instead of a duel-end callback threaded through the tick service, `GameService.DispatchAsync` skips persistence for the high-frequency in-duel commands, and `Game.razor`'s existing `OnTickNotify` (which already detects the dueling→not-dueling transition for other reasons) now also persists on duel end plus every 10 ticks as an in-duel safety net. Net effect matches the plan's intent (duel end + periodic safety interval + immediate for state-changing commands) without a new cross-layer event path.

**Workstream D (asset manifest).** `asset-manifest.json` + `asset-map.md` cover the current PoC assets (`steel_sword`, 6-piece ranger set) exactly as they existed in `toon.js`'s old hard-coded dicts; `AssetMapSyncTests` enforces the two files staying in step. Service worker `CACHE_VERSION` bumped since `ASSET_URLS` gained the manifest file.

**Verification caveat.** This session's sandboxed environment has no local .NET SDK, and the egress proxy returns a policy denial (403) for `builds.dotnet.microsoft.com` — installing one wasn't possible here (not an environment we should route around per the proxy's own guidance). Every change was re-read and manually traced for correctness (types, call sites, DI wiring, existing test fixtures updated for the new `GameTickService`/`GameService` constructor parameters), but **`dotnet build Duels.sln` and `dotnet test` have not actually been run.** Per the cleanup rule, that must happen — via CI or a session with a working SDK — before this is considered done.
