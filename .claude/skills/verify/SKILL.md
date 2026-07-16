---
name: verify
description: Build, launch, and drive the Duels Blazor WASM app to verify changes end-to-end (Playwright against the battle canvas + Blazor UI).
---

# Verifying Duels changes

## Build / test
- .NET 8 SDK required. In sandboxed environments the Microsoft download hosts may be proxy-blocked — `apt-get install -y dotnet-sdk-8.0` works.
- `dotnet test` — unit tests (Domain + Application, incl. GameTickService fights driven via reflection on `ProcessTick`).
- `node --check src/Duels.Web/wwwroot/js/voxel.js` — renderer syntax.
- After model/rig changes: `python3 tools/gen_models.py` (regenerates `wwwroot/assets/**` and MERGES into `rigs.json` — `characters.player_sakuna` is owned by `tools/import_sakuna.py` and must survive a regen; if it's ever missing, the player silently falls back to the wrong rig). Beware: piping its stdout through `head` can SIGPIPE-kill it before rigs.json is written — run it unpiped.

## Launch
```bash
dotnet run --project src/Duels.Web --urls http://localhost:5190   # background; ~15s to first 200
```

## Drive (Playwright)
- `npm i playwright` in a scratch dir; launch with `executablePath: '/opt/pw-browsers/chromium'` (preinstalled build doesn't match latest playwright's pinned version).
- Viewport ~900×500 (battle scene is landscape full-bleed).
- Flow: goto `/` → fill `#charname` → click `button.continue-btn` → click `text=DUEL ARENA` → `.ladder-card` filtered by NPC name → `.ladder-fight-btn` → wait `#battle-canvas`.
- Game ticks are 600ms; time observations in tick multiples.
- Useful selectors: player HP `.battle-box-player .battle-box-val`, enemy HP `.battle-box-enemy .battle-box-val`, style badge `.battle-box-enemy .style-badge`, boss warnings `.telegraph-bubble`, toasts contain log lines (match text like `ERUPTS` / `poisons you`).
- Ground click = walk order: `page.mouse.click()` on `#battle-canvas` coordinates (screen→tile mapping is voxel.js `screenToWorld`).
- A fresh character is level 1 (~60 HP) and dies to bosses quickly — design probes to finish within ~20 ticks or expect the death/result overlay.

## Renderers
- Two battle renderers behind the `window.voxel` dispatcher (`js/renderer-switch.js`): classic voxel (`js/voxel.js` → `window.voxelClassic`) and the toon/Borderlands PoC (`js/toon.js` → `window.voxelToon`, three.js vendored in `wwwroot/lib/`). Chosen at battle init from `localStorage['duels_renderer']` ('voxel' default | 'toon') — flip via the hub STYLE card or `page.addInitScript(() => localStorage.setItem('duels_renderer','toon'))`.
- Toon characters load `wwwroot/assets/models/superhero.gltf` (+.bin; texture PNGs intentionally absent — a URL modifier feeds 1px placeholders and every material is replaced with `toonMat`). Cloning MUST go through `SkeletonUtils.clone` (plain `.clone()` leaves SkinnedMeshes bound to the original skeleton → mesh collapses to a speck). The rig has no baked animations; clips are authored in code against its UE-style bones (`thigh_l/r`, `upperarm_l/r`, `spine_02`, …) composed onto the captured rest pose; death tips a `fallPivot` wrapper group. On any load error it falls back to the procedural box character.
- Toon debug: `window.voxelToon._battles.get('battle-canvas')` exposes per-battle state (actors, actions, bones) for Playwright probes. Headless chromium runs ~12fps (ReadPixels GPU stalls), so short anims (attack = 0.34s) are hard to catch by `waitForTimeout` — instead set `action.time = t; action.paused = true` to freeze a pose for screenshots.
- Toon PoC parity gaps (expected, not bugs): no whip rope, no HP erosion, no eat anims, no overhead prayer icons, no weapon models, simplified projectiles.

## Known noise
- `GET /Duels.Web.styles.css` 404s in dev — pre-existing, ignore.
- Google Fonts fetch fails under the proxy — ignore.
- Enemy HP shows `0/max` after ANY duel end (ActiveNpc nulled), not only on kills.
