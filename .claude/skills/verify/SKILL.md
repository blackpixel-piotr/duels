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
- The toon renderer (`js/toon.js`, three.js vendored in `wwwroot/lib/`) IS the battle renderer — it owns `window.voxel` (a Proxy over its battle API). The classic voxel painter (`js/voxel.js` → `window.voxelClassic`) is retired from battle duty but stays loaded for item icons (`voxel.itemIcon`), the character preview (`initPreview`/`previewWhipAttack`), and the anim editor — those calls forward through the Proxy. No STYLE toggle, no `duels_renderer` flag.
- Battle camera: FOV 15, dolly 18.3 wu/zoom (telephoto ~2x framing); fog near/far track the dolly per frame. Characters re-ground against the animated idle via foot bones at makeActor time (bind-pose grounding sinks toes through the floor).
- Toon characters load `wwwroot/assets/models/superhero.gltf` (+.bin). Base-color textures (`T_Superhero_Male_Dark`, `T_Hair_1_BaseColor`, `T_Eye_Brown`) ship beside it and feed `toonMat`'s map slot; normal/roughness maps are intentionally absent — a URL modifier feeds 1px placeholders (the toon ramp ignores them, saves ~12MB). The enemy is told apart by a rust `tint` multiplied over the body texture (faces stay untinted). Cloning MUST go through `SkeletonUtils.clone` (plain `.clone()` leaves SkinnedMeshes bound to the original skeleton → mesh collapses to a speck). On any load error it falls back to authored clips on the rig, then to the procedural box character.
- Animations come from the Quaternius Universal Animation Library (`assets/models/anims1.glb`/`anims2.glb`, same universal skeleton → clips bind by bone name). They're extracted from `resources/assets/Universal Animation Library*[Standard]/Unreal-Godot/*.glb` by `tools/extract_anims.mjs` (needs `npm i @gltf-transform/core @gltf-transform/functions`; gotcha: disposing an Animation does NOT dispose its channels/samplers — the tool handles this, else the output keeps ~90% of the buffer). Locomotion is a 1D blend space over ground speed (idle/walk/jog/sprint, cadence-matched timeScale); attacks/hits/eat/death are LoopOnce overlays picked per weapon/style/tier in `battleEvent`.
- Toon debug: `window.voxelToon._battles.get('battle-canvas')` exposes per-battle state (actors, actions, bones) for Playwright probes. Headless chromium runs ~12fps (ReadPixels GPU stalls), so short anims (attack = 0.34s) are hard to catch by `waitForTimeout` — instead set `action.time = t; action.paused = true` to freeze a pose for screenshots.
- Toon PoC parity gaps (expected, not bugs): no whip rope, no HP erosion, no eat anims, no overhead prayer icons, no weapon models, simplified projectiles.

## Targeting / engagement
- `GameState.ActiveNpc` IS the target for the whole duel (1v1 — nothing else to point at); clicking the enemy never changes WHO, only whether the player is `!HoldPosition` (engaging it). `OrderMove` sets `HoldPosition=true` and it stays set after arrival — no auto-chase, no auto-attack — until `Engage()` (called by `EngageCommand`/enemy click, and now also by `AttackHandler`/`WeaponShortcutHandler` so pressing ATTACK or a weapon slot re-engages instead of queuing a swing that never fires while held).
- The ATTACK-TARGET button (`ActionHud.razor`, `.attack-target-btn`) sits above the weapon row, dispatches the same `AttackCommand` as the fists-only ATTACK quickslot. Only rendered under `Layout="float"` in practice — `dock` only renders outside a duel where the weapon/target rows never show.
- Playwright gotcha: the battle camera is telephoto (FOV 15, dolly 18.3) and locked on the player, so the enemy is usually off-screen at spawn. To click it reliably: `setCameraDebug('battle-canvas', {zoom: 0.3, pitch: 0.85})` to zoom out, then project its world position through the live three.js camera rather than eyeballing pixels: `const p = st.enemy.ch.group.position.clone(); p.y = st.enemy.ch.height * 0.5; p.project(st.camera);` → NDC to fraction `{fx:(p.x+1)/2, fy:(1-p.y)/2}`. Click while the enemy is still frozen (stationary) so its projected position doesn't go stale between computing the fraction and the click landing.

## Known noise
- `GET /Duels.Web.styles.css` 404s in dev — pre-existing, ignore.
- Google Fonts fetch fails under the proxy — ignore.
- Enemy HP shows `0/max` after ANY duel end (ActiveNpc nulled), not only on kills.
