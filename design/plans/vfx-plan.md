# VFX layer — plan

Not a milestone (M1-M3 are the Progression Spine / Combat Grammar track);
this is a renderer-only cross-cutting feature. Filed under `design/plans/`
per the milestone workflow anyway, since it needs the same plan-first /
findings-after discipline.

## Why, and why now

Combat currently has zero particle feedback — hits are hitsplats + shake +
color flashes, movement is silent. The locked architecture invariant (C#
sim ticks, Three.js is a dumb renderer) already gives us the right seam:
the sim can describe *what happened* semantically without knowing anything
about *how it looks*. This plan adds that seam (`vfxEvents`) and ships one
real effect through it (movement dust) to prove the pipe end-to-end before
any future effect (impacts, telegraphs, perfect dodge, projectile trails)
rides the same rail.

## Architecture

1. **`GameState.VfxEvents`** — a `List<VfxEvent>` cleared at the top of
   every `GameTickService.ProcessTick`, populated during the tick, read
   once by `BattleScene.razor` before the next tick clears it.
   `VfxEvent(string Type, string EntityId, IReadOnlyDictionary<string,
   double>? Data)` — semantic type + which entity + a small numeric payload
   (e.g. movement direction). No effect ids, no colors, no particle counts
   anywhere in `Duels.Application`/`Duels.Domain` — the sim only ever calls
   `state.AppendVfxEvent("entity_moved", "player", new() { ["dx"] = ...,
   ["dz"] = ... })`.

   **Why per-tick-and-cleared, not an ever-growing log**: `CombatLog`
   (the existing pattern) is append-only forever specifically so
   `BattleScene`'s `_lastCount` cursor never misses a hitsplat/log line
   even if a render gets skipped — those drive real UI (toasts, damage
   numbers). VFX has the opposite requirement: doctrine says "the VFX
   system can be disabled with zero gameplay impact," so it's fine, even
   correct, for a coalesced Blazor render to silently drop a tick's worth
   of dust events. Reusing the cursor pattern here would mean maintaining
   an unbounded (or FIFO-trimmed-with-a-fragile-cursor) list for something
   explicitly allowed to be lossy. Clear-and-refill mirrors how
   `Hazards`/`Adds`/`Projectiles` are already sent wholesale every tick.

   **Where it plugs into `GameTickService`**: `ProcessTick` already
   computes `preTickPlayerTile` (line ~108) and `playerMovedThisTick =
   state.PlayerTile != preTickPlayerTile` (line ~129) for the persistent-
   target-lock check, and that line runs strictly after
   `ProcessPlayerMovement`/`ProcessNpcMovement` and strictly before every
   teleport-causing path (`ExecuteLunge`, Bloodtithe/Mirrorhide knockback —
   both later in the same tick, both tagged `LogEntryKind.PlayerTeleport`
   already for the renderer's snap-vs-lerp decision). So
   `playerMovedThisTick` is already exactly "the player took an ordinary
   step this tick, not a teleport" — no new detection logic needed, just
   `AppendVfxEvent` gated on that existing bool.

2. **Renderer-side `VfxSystem`** — new file `wwwroot/js/vfx.js` (ES
   module), not woven into `toon.js`'s ~1900 lines. Owns a
   `three.quarks` `BatchedRenderer` added to the battle scene, the
   manifest, and per-entity emitter pools. `toon.js` wires it in three
   places: `initBattle` (construct, add to scene), the render `loop`
   (`vfx.update(dt, now)` right before `effect.render(...)`), and a new
   `api.setVfxEvents(canvasId, events)` (forwards to
   `vfx.handleEvents(events, st, now)`) + `destroyBattle` (dispose).
   `BattleScene.razor` calls `voxel.setVfxEvents` once per tick with
   `s.VfxEvents`, **skipped entirely when the array is empty** (matches
   "empty array = omitted" — also means a standing-still player adds zero
   interop calls, not an empty-array call every 600ms).

3. **`vfx-manifest.json`** (`wwwroot/data/vfx-manifest.json`, loaded once
   by `vfx.js`, mirroring how `toon.js` already loads
   `asset-manifest.json`): rows of `{ event, effectId, texture, count,
   size, lifetime, colorToken, blend }`. One row for iteration 1:
   ```json
   { "event": "entity_moved", "effectId": "dust_puff", "texture": "assets/vfx/dust_puff.png",
     "count": 5, "size": 0.35, "lifetime": 0.4, "colorToken": "--border", "blend": "additive" }
   ```
   `colorToken` names a CSS custom property; `vfx.js` reads it once via
   `getComputedStyle(document.documentElement)` at init (same trick would
   work for any future doctrine-colored effect) rather than ever holding a
   raw hex in JS.

4. **Dependency: three.quarks.** Verified against our vendored three.js.

   `three.module.min.js` (`wwwroot/lib/`) is r160 (`REVISION = "160"`,
   i.e. npm `0.160.0`). three.quarks' peer-dependency floor has moved
   twice: `>=0.153.0` through `0.10.x`, `>=0.157.0` through `0.11.x`–
   `0.12.1`, then a hard jump to `>=0.182.0` at `0.12.2`+ (confirmed via
   `registry.npmjs.org/three.quarks`'s per-version `peerDependencies`).
   `0.160.0` satisfies the `0.12.1` floor (`>=0.157.0`) but not `0.12.2`'s
   (`>=0.182.0`). **Pinning `three.quarks@0.12.1`** — newest version
   actually compatible with our three.js, not latest overall (`0.17.1`,
   which requires three `>=0.182.0`).

   Packaging quirk found while inspecting the tarball: `dist/
   three.quarks.esm.min.js` is mislabeled — it's the UMD bundle (checks
   `typeof exports`/`typeof define`, falls back to a `THREE.QUARKS`
   global), not real ESM. The genuine ESM build is the unminified `dist/
   three.quarks.esm.js`, which does `import { ... } from 'three'` (a bare
   specifier). Every other vendored lib in this repo (`GLTFLoader.js`,
   `OutlineEffect.js`, `SkeletonUtils.js`) instead imports three via a
   relative path (`from './three.module.min.js'`) — no import map exists
   anywhere in `index.html`. Matching that convention (one `sed` rewrite
   of the single `from 'three'` line when vendoring) is simpler and more
   consistent than introducing the project's first import map for one
   file.

5. **Assets.** Task brief calls for Kenney particle-pack (CC0) textures
   under `assets/vfx/`. **Blocked**: `kenney.nl` and `itch.io` both return
   403 from the sandbox's egress proxy (org policy, confirmed via direct
   `curl` — not something to route around per the proxy's own README).
   No GitHub mirror was substituted either — this session's GitHub access
   is scoped to `blackpixel-piotr/duels` only, so blindly guessing a
   third-party mirror URL both violates that scope's spirit and can't be
   verified as genuinely CC0/Kenney. **Substitute**: a tiny procedurally-
   generated soft-circle sprite (radial gradient drawn to an offscreen
   `<canvas>` once at `vfx.js` init, uploaded as a `THREE.CanvasTexture` —
   no PNG file at all yet). Flagged as provisional in code (`// PROVISIONAL:
   Kenney particle pack unreachable from this sandbox (kenney.nl/itch.io
   blocked by egress policy) — procedural placeholder texture until a
   session with broader network access can vendor the real pack`) and
   logged in `backlog.md`. `asset-map.md` gets a new "VFX Textures"
   section noting the placeholder (doesn't touch the existing weapon/armor
   rows `AssetMapSyncTests` checks, so the sync test is unaffected).

6. **Performance rails** (`vfx.js`):
   - Global budget: `const PARTICLE_BUDGET = 300;` (module constant,
     `// PROVISIONAL: no doc source, matches the brief's stated default`).
     A running live-particle counter across all emitters; a new burst that
     would exceed the budget culls the *oldest* live particles first
     (across all effects, not just the incoming one) rather than refusing
     the new spawn — a fresh effect is always the one most relevant to
     what just happened on screen.
   - Emitter pooling: one `ParticleEmitter` per `effectId` (not per
     event), reused for every burst of that effect — `.emit(count)` at a
     repositioned origin, never a `new ParticleEmitter(...)` inside the
     per-event handler.
   - `vfxQuality` stub: `'off' | 'low' | 'full'` (module-level, default
     `'full'`), `setVfxQuality(canvasId, q)` exported on `api` for later
     UI wiring. `'off'` short-circuits `handleEvents` entirely (master
     toggle — doctrine requirement: zero gameplay impact when disabled).
     `'low'` halves `count` per burst. Not wired to any UI control this
     pass (brief says "wire the setting, UI later").

7. **Postprocessing/bloom**: explicitly out of scope per the brief. Dust
   uses additive blending only for a cheap glow-adjacent look.

## Iteration 1: movement dust

- **Trigger**: `entity_moved` event, player only. Emitted once per tile
  entered (see §1 above — `playerMovedThisTick` already fires exactly
  once per tick the player's tile actually changed by walking).
- **Suppression**: teleports (Lunge, Bloodtithe/Mirrorhide knockback) never
  set `playerMovedThisTick`, so they're already excluded — no separate
  discontinuous-flag check needed on the C# side. (Dashes getting their
  own effect later, per the brief, is a `vfx-manifest.json` addition, not
  a suppression change here.)
- **Placement**: `vfx.js` reads the entity's *live rendered* position at
  event-handling time — for the player that's `st.player.pos.wx/wz`
  (the continuous constant-speed-pursuit position `toon.js`'s render loop
  already maintains every frame, TILE-scaled), not the raw sim tile the
  event rode in on. This is what "positioned via the interpolation layer,
  not on tile-snap" means in practice: by the time the event is handled,
  the player is already mid-stride toward that tile, so the puff spawns
  wherever they visually are, not where they're about to be.
- **Drift**: emitter velocity direction is `(-dx, -dz)` (event's own
  `data.dx/dz`, tile-space deltas are already axis-aligned to world space
  at this TILE scale, same as every other position write in `toon.js` —
  no rotation needed).
- **Lifetime/color**: 0.4s, `--border` (`#4a3d2e`, "sandstone" — an
  existing neutral/earthy token, not a new doctrine color). Flagging this
  choice: the doctrine paragraph below says VFX "always use doctrine color
  tokens," but the 7 existing doctrine tokens
  (`--doctrine-{melee,ranged,magic,hazard,safe,poison,bleed}`) are all
  combat-meaning colors — dust carries no gameplay information, so binding
  it to one would be a lie (a hazard-purple dust puff would read as
  meaningful). Reading `--border` instead satisfies the *token, never raw
  hex* half of that rule; the *doctrine* half is scoped to effects that
  actually encode a style/state. Noted here rather than resolved silently
  per CLAUDE.md.
- **Emission rate / cap**: exactly one burst per `entity_moved` event;
  `vfx.js` tracks live dust-puff count per `entityId` and skips spawning
  (not queues) once 3 are concurrently alive for that entity — cheap,
  matches "capped at 3 concurrent per entity."

## Doctrine paragraph (added to `CLAUDE.md`'s renderer section verbatim,
see that file's diff)

> VFX are renderer-only, driven by semantic vfxEvents in the snapshot, and
> mapped to effects via vfx-manifest.json. VFX never encode gameplay
> information on their own, always use doctrine color tokens, and are
> always subordinate to telegraph readability — no effect may obscure a
> telegraph, projectile, or overhead prayer icon. The VFX system can be
> disabled with zero gameplay impact.

## Explicitly out of scope this pass

- Any event beyond `entity_moved` (projectile_spawned, impact,
  perfect_dodge, flask_sip, boss_telegraph are named in the brief as
  *future* semantic events — the manifest/dispatch plumbing is generic
  enough to add them later as new manifest rows + new `AppendVfxEvent`
  call sites, zero `vfx.js` core changes expected).
- NPC/boss movement dust (brief says player-only for iteration 1).
- Dash/teleport dust variant.
- vfxQuality UI control (stub only).
- Bloom/postprocessing.
- Real Kenney texture (network-blocked — see §5).

## Verification plan

`dotnet build`/`dotnet test` (SDK now installed in this session — M1's
"no SDK available" caveat doesn't apply here), then the project's `verify`
skill (Playwright against the battle canvas) to confirm: dust puffs
actually render while walking, no console errors from the three.quarks
import or the manifest fetch, and the existing HUD/telegraph/hitsplat
visuals are unaffected. Findings go to `design/plans/vfx-findings.md`.
