---
name: verify
description: Build, launch, and drive the Duels Blazor WASM app to verify changes end-to-end (Playwright against the battle canvas + Blazor UI).
---

# Verifying Duels changes

## Build / test
- .NET 8 SDK required. In sandboxed environments the Microsoft download hosts may be proxy-blocked — `apt-get install -y dotnet-sdk-8.0` works.
- `dotnet test` — unit tests (Domain + Application, incl. GameTickService fights driven via reflection on `ProcessTick`).
- `node --check src/Duels.Web/wwwroot/js/voxel.js` — renderer syntax.
- After model/rig changes: `python3 tools/gen_models.py` (regenerates `wwwroot/assets/**` + `rigs.json`). Beware: piping its stdout through `head` can SIGPIPE-kill it before rigs.json is written — run it unpiped.

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

## Known noise
- `GET /Duels.Web.styles.css` 404s in dev — pre-existing, ignore.
- Google Fonts fetch fails under the proxy — ignore.
- Enemy HP shows `0/max` after ANY duel end (ActiveNpc nulled), not only on kills.
