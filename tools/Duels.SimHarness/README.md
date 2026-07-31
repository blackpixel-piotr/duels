# Duels.SimHarness — the headless dev arena

Drive the **real** combat engine (`GameTickService`) against the **real**
embedded boss/item content, with no Blazor, no Three.js, no browser, and no
grinding through the game to reach a boss. A whole fight resolves in
milliseconds and prints a tick-by-tick trace.

Use it to: audit a boss's script, reproduce a "the fight feels wrong" report,
script and regression-check a perfect fight, or sanity-check a mechanic change
before touching the UI.

## Run

```bash
dotnet run -c Release --project tools/Duels.SimHarness -- <bossId> <brain> [--ticks N] [--seed S]
```

- `bossId` — `maggot_king` | `hive_matron` | `mirrorhide` | `bloodtithe`
  (locked/unbuilt bosses error out, by design).
- `brain` — the player strategy:
  - `idle` — stands still, does nothing. Watch a boss's raw script.
  - `naive` — prays Range, never dodges. The un-mastered baseline.
  - `melee` — a diagnostic weave probe (see backlog #40).
  - `perfect` — the scripted clean Hive Matron kill (ranged spacing).
  - `auto` — the **live** autopilot behind the in-game "Playtest Fight" button
    (the `AutoPlayBrain` in `Duels.Application/AutoPlay`). Boss-agnostic; use it
    to preview how a boss will play out under autopilot before watching it live.
- `--ticks N` — max ticks before giving up (default 400).
- `--seed S` — deterministic seeded RNG (accuracy/damage spread). Omit for the
  default always-hit RNG, which makes a scripted run read as pure skill (boss
  hits land at full value, correctly-prayed hits take zero).

Run in **Release** so `GameState`'s `#if DEBUG` `[PROJ]` diagnostics don't
interleave with the trace.

### Examples

```bash
# Raw Hive Matron script vs. a dummy:
dotnet run -c Release --project tools/Duels.SimHarness -- hive_matron idle --ticks 40
# The scripted perfect fight:
dotnet run -c Release --project tools/Duels.SimHarness -- hive_matron perfect
# Is melee actually viable? (spoiler: no — backlog #40)
dotnet run -c Release --project tools/Duels.SimHarness -- hive_matron melee
```

## Add a strategy

Implement `IPlayerBrain` (one `Decide(SimContext ctx)` method). The seam lives
in `Duels.Application/AutoPlay/` — the **same** code that powers the in-game
"Playtest Fight" autopilot, so a brain you write here can drive the live game
too. `SimContext` exposes only what a real player perceives (positions, HP,
in-flight projectile style, telegraphed marks, hazard tiles) and the same
commands the Blazor UI dispatches (`Pray`, `MoveTo`, `HoldAndFight`,
`QueueSpecial`, `SwapWeapon`). Register harness-only brains in `Program.cs`'s
switch; see `Brains.cs` for examples.

## Files

- `SimWorld.cs` — builds one deterministic player-vs-boss instance.
- `IPlayerBrain.cs` — the strategy seam + `SimContext` (perception + commands).
- `Brains.cs` — the shipped strategies.
- `TraceRecorder.cs` — per-tick snapshot table + combat-log deltas + verdict.
- `RandomProviders.cs` — always-hit / seeded RNG.
- `Program.cs` — CLI.

Not wired into CI yet; a natural next step is a "every boss stays winnable by
its perfect brain" smoke test (backlog #41).
