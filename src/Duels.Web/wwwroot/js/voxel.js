// Voxel engine: MagicaVoxel .vox parser + software renderer.
// Renders voxel models as chunky pixel art onto small canvases that are
// CSS-upscaled with image-rendering: pixelated. No dependencies, no WebGL.
//
// Player asset: assets/player.vox = chr_knight.vox from ephtracy's official
// MagicaVoxel sample models (https://github.com/ephtracy/voxel-model).
//
// API:
//   voxel.initPreview(canvasId, modelUrl)  — spinning drag-to-rotate preview
//   voxel.destroyPreview(canvasId)         — stop loop, detach listeners
//   voxel.renderIcon(modelUrl, size)       — Promise<dataURL> static 3/4-view
//                                            icon, cached per url+size
(function () {
    'use strict';

    const TILT = 0.3;            // isometric-ish downward tilt
    const SPIN_SPEED = 0.008;    // rad/frame idle auto-spin (~28s/rev)
    const DRAG_SPEED = 0.02;     // rad per dragged px
    const RESUME_MS = 2000;      // hold still after drag before spinning again
    const SHADES = 6;            // quantized depth-shading levels

    // ── .vox parsing ─────────────────────────────────────────────────────────

    // MagicaVoxel default palette (used when a file has no RGBA chunk):
    // 216 descending {ff,cc,99,66,33,00} B-fastest combos, then r/g/b ramps + grays.
    function defaultPalette() {
        const p = [[0, 0, 0]];
        const v = [255, 204, 153, 102, 51, 0];
        for (const r of v) for (const g of v) for (const b of v) p.push([r, g, b]);
        const ramp = [238, 221, 187, 170, 136, 119, 85, 68, 34, 17];
        for (const e of ramp) p.push([e, 0, 0]);
        for (const e of ramp) p.push([0, e, 0]);
        for (const e of ramp) p.push([0, 0, e]);
        for (const e of ramp.slice(0, 9)) p.push([e, e, e]);
        return p;
    }

    // Parses the first model in a .vox file into { voxels: [{x,y,z,ci}], palette }.
    // MagicaVoxel is Z-up; converted here to renderer Y-up.
    function parseVox(buf) {
        const dv = new DataView(buf);
        const tag = i => String.fromCharCode(dv.getUint8(i), dv.getUint8(i + 1), dv.getUint8(i + 2), dv.getUint8(i + 3));
        if (tag(0) !== 'VOX ') throw new Error('not a .vox file');

        let voxels = null;
        let palette = null;
        let pos = 8;
        while (pos + 12 <= buf.byteLength) {
            const id = tag(pos);
            const contentSize = dv.getUint32(pos + 4, true);
            const childrenSize = dv.getUint32(pos + 8, true);
            const start = pos + 12;
            if (id === 'XYZI' && voxels === null) {
                const n = dv.getUint32(start, true);
                voxels = new Array(n);
                for (let i = 0; i < n; i++) {
                    const o = start + 4 + i * 4;
                    voxels[i] = {
                        x: dv.getUint8(o),
                        y: dv.getUint8(o + 2),      // vox z → renderer y (up)
                        z: dv.getUint8(o + 1),      // vox y → renderer z (depth)
                        ci: dv.getUint8(o + 3),
                    };
                }
            } else if (id === 'RGBA') {
                // Palette convention: XYZI color index i reads RGBA entry i-1.
                palette = [[0, 0, 0]];
                for (let i = 0; i < 255; i++) {
                    const o = start + i * 4;
                    palette.push([dv.getUint8(o), dv.getUint8(o + 1), dv.getUint8(o + 2)]);
                }
            }
            // Chunk layout: [content][children]. Descend into MAIN's children;
            // skip other chunks entirely (content + children).
            pos = id === 'MAIN' ? start + contentSize : start + contentSize + childrenSize;
        }
        if (!voxels) throw new Error('no XYZI chunk');
        return { voxels, palette: palette ?? defaultPalette() };
    }

    // ── Model prep (once per load) ────────────────────────────────────────────

    // Center on x/z, ground at y=0, drop fully-enclosed interior voxels, and
    // precompute quantized depth-shade fillStyle strings per used color.
    function prepModel(parsed) {
        const vs = parsed.voxels;
        let minX = 1e9, maxX = -1e9, minY = 1e9, minZ = 1e9, maxZ = -1e9, maxY = -1e9;
        for (const v of vs) {
            if (v.x < minX) minX = v.x; if (v.x > maxX) maxX = v.x;
            if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y;
            if (v.z < minZ) minZ = v.z; if (v.z > maxZ) maxZ = v.z;
        }
        const cx = (minX + maxX) / 2, cz = (minZ + maxZ) / 2;

        const occupied = new Set(vs.map(v => v.x + '|' + v.y + '|' + v.z));
        const surface = vs.filter(v =>
            !(occupied.has((v.x + 1) + '|' + v.y + '|' + v.z) &&
              occupied.has((v.x - 1) + '|' + v.y + '|' + v.z) &&
              occupied.has(v.x + '|' + (v.y + 1) + '|' + v.z) &&
              occupied.has(v.x + '|' + (v.y - 1) + '|' + v.z) &&
              occupied.has(v.x + '|' + v.y + '|' + (v.z + 1)) &&
              occupied.has(v.x + '|' + v.y + '|' + (v.z - 1))));

        let radius = 1;
        const voxels = surface.map(v => {
            const x = v.x - cx, z = v.z - cz;
            const r = Math.sqrt(x * x + z * z);
            if (r > radius) radius = r;
            return { x, y: v.y - minY, z, ci: v.ci };
        });

        // shadeTable[ci][level] = fillStyle, level 0 (far/dark) .. SHADES-1 (near/bright)
        const shadeTable = {};
        for (const v of voxels) {
            if (shadeTable[v.ci]) continue;
            const [r, g, b] = parsed.palette[v.ci] ?? [255, 0, 255];
            const levels = new Array(SHADES);
            for (let l = 0; l < SHADES; l++) {
                const f = 0.65 + l * (0.5 / (SHADES - 1));
                levels[l] = `rgb(${Math.min(255, r * f) | 0},${Math.min(255, g * f) | 0},${Math.min(255, b * f) | 0})`;
            }
            shadeTable[v.ci] = levels;
        }

        return { voxels, height: maxY - minY + 1, radius, shadeTable, scratch: new Array(voxels.length) };
    }

    const modelCache = new Map(); // url → Promise<model>
    function loadModel(url) {
        if (!modelCache.has(url)) {
            modelCache.set(url, fetch(url)
                .then(r => { if (!r.ok) throw new Error(`${url}: HTTP ${r.status}`); return r.arrayBuffer(); })
                .then(buf => prepModel(parseVox(buf))));
        }
        return modelCache.get(url);
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    // Draw one model at voxel size S with its ground-center at (cx, baseY).
    function renderModel(ctx, model, angle, S, cx, baseY) {
        const c = Math.cos(angle), sn = Math.sin(angle);
        const vs = model.voxels, out = model.scratch;
        for (let i = 0; i < vs.length; i++) {
            const v = vs[i];
            const rx = v.x * c - v.z * sn;
            const rz = v.x * sn + v.z * c;
            out[i] = { rx, rz, y: v.y, ci: v.ci };
        }
        out.sort((a, b) => a.rz - b.rz); // far → near (painter's algorithm)

        const invSpan = 1 / (model.radius * 2 || 1);
        for (let i = 0; i < out.length; i++) {
            const v = out[i];
            let lvl = ((v.rz + model.radius) * invSpan * SHADES) | 0;
            if (lvl < 0) lvl = 0; else if (lvl >= SHADES) lvl = SHADES - 1;
            ctx.fillStyle = model.shadeTable[v.ci][lvl];
            ctx.fillRect((cx + v.rx * S) | 0, (baseY - v.y * S + v.rz * S * TILT) | 0, S + 1, S + 1);
        }
    }

    function render(ctx, W, H, model, angle) {
        ctx.clearRect(0, 0, W, H);
        // Auto-fit: horizontal must clear the rotation-swept diameter, vertical
        // the model height plus the tilt swing at top and bottom.
        const S = Math.max(1, Math.floor(Math.min(
            (W - 2) / (model.radius * 2 + 1),
            (H - 2) / (model.height + 1 + model.radius * 2 * TILT))));
        renderModel(ctx, model, angle, S, W / 2, (H + model.height * S) / 2);
    }

    // ── Interactive previews ──────────────────────────────────────────────────

    const previews = new Map(); // canvasId → state

    async function initPreview(canvasId, modelUrl) {
        destroyPreview(canvasId);
        let model;
        try { model = await loadModel(modelUrl); }
        catch (e) { console.warn('voxel: failed to load', modelUrl, e); return; }

        const canvas = document.getElementById(canvasId);
        if (!canvas) return; // component unmounted while the model was loading

        const st = {
            canvas,
            ctx: canvas.getContext('2d'),
            model,
            angle: 0.6,
            lastDrawn: NaN,
            dragging: false,
            dragX: 0,
            dragAngle: 0,
            resumeAt: 0,
            raf: 0,
        };

        st.onDown = e => {
            e.preventDefault();
            e.stopPropagation();
            st.dragging = true;
            st.dragX = e.clientX;
            st.dragAngle = st.angle;
            canvas.setPointerCapture(e.pointerId);
        };
        st.onMove = e => {
            if (st.dragging) st.angle = st.dragAngle + (e.clientX - st.dragX) * DRAG_SPEED;
        };
        st.onUp = () => {
            st.dragging = false;
            st.resumeAt = performance.now() + RESUME_MS;
        };
        canvas.addEventListener('pointerdown', st.onDown);
        canvas.addEventListener('pointermove', st.onMove);
        canvas.addEventListener('pointerup', st.onUp);
        canvas.addEventListener('pointercancel', st.onUp);

        const loop = () => {
            if (!st.dragging && performance.now() >= st.resumeAt) st.angle += SPIN_SPEED;
            if (st.angle !== st.lastDrawn) {
                render(st.ctx, canvas.width, canvas.height, st.model, st.angle);
                st.lastDrawn = st.angle;
            }
            st.raf = requestAnimationFrame(loop);
        };
        st.raf = requestAnimationFrame(loop);
        previews.set(canvasId, st);
    }

    function destroyPreview(canvasId) {
        const st = previews.get(canvasId);
        if (!st) return;
        cancelAnimationFrame(st.raf);
        st.canvas.removeEventListener('pointerdown', st.onDown);
        st.canvas.removeEventListener('pointermove', st.onMove);
        st.canvas.removeEventListener('pointerup', st.onUp);
        st.canvas.removeEventListener('pointercancel', st.onUp);
        previews.delete(canvasId);
    }

    // ── Static icons ──────────────────────────────────────────────────────────

    const iconCache = new Map(); // url|size → Promise<dataURL>

    function renderIcon(modelUrl, size) {
        const key = modelUrl + '|' + size;
        if (!iconCache.has(key)) {
            iconCache.set(key, loadModel(modelUrl).then(model => {
                const c = document.createElement('canvas');
                c.width = size; c.height = size;
                render(c.getContext('2d'), size, size, model, Math.PI / 5); // fixed 3/4 view
                return c.toDataURL();
            }));
        }
        return iconCache.get(key);
    }

    // Item ids that have a model at assets/items/<id>.vox. Add ids here as
    // assets are added — the manifest avoids a 404 fetch per unmodeled item.
    const ITEM_ASSETS = new Set([
        'abyssal_whip',
        'dragon_dagger',
    ]);

    // dataURL for an item's icon, or null when the item has no model yet
    // (callers fall back to their text rendering).
    function itemIcon(itemId, size) {
        if (!ITEM_ASSETS.has(itemId)) return Promise.resolve(null);
        return renderIcon(`assets/items/${itemId}.vox`, size).catch(e => {
            console.warn('voxel: item icon failed', itemId, e);
            return null;
        });
    }

    // ── Battle scene ──────────────────────────────────────────────────────────
    // Pokemon-style duel view: player close-up lower-left (back 3/4), enemy
    // upper-right (front 3/4). One rAF loop per canvas; actors render to
    // per-actor offscreen canvases (tint/flash applied there) then blit onto
    // the main canvas at their animated positions.

    // NPC ids with a model at assets/npcs/<id>.vox (free chr_* characters from
    // ephtracy's MagicaVoxel sample repo, mapped/recolored per opponent).
    const NPC_ASSETS = new Set([
        'goblin', 'swashbuckler', 'barbarian', 'desert_bandit', 'gladiator',
        'corsair', 'berserker', 'warlord', 'champion', 'rare_tourist', 'rare_gladiator',
    ]);

    // Single source of truth for scene geometry. x/y are fractional anchors of
    // the actor's feet on the logical canvas; zoneY is where hitsplats stack.
    // The logical canvas is the CSS box divided by PX (chunky pixels without
    // aspect distortion); scaleFrac scales off the smaller canvas dimension.
    const BATTLE_PX = 3;
    const LAYOUT = {
        player: { x: 0.30, y: 0.92, scaleFrac: 1.12, angle: Math.PI + Math.PI / 5, zoneY: 0.62 },
        enemy:  { x: 0.72, y: 0.42, scaleFrac: 0.72, angle: -Math.PI / 5,          zoneY: 0.20 },
    };

    const battles = new Map(); // canvasId → state

    function makeActor(model, base, tint) {
        return { model, base, tint: tint ?? null, anims: [], bobPhase: Math.random() * 6.28, off: document.createElement('canvas') };
    }

    // Render an actor to its offscreen canvas; returns {S, w, h, feetX, feetY}
    // where feet* locate the model's ground-center inside the offscreen.
    function renderActorOffscreen(actor, flashTint, minDim) {
        const m = actor.model;
        const S = Math.max(1, Math.floor(minDim * actor.base.scaleFrac / (m.height + 1 + m.radius * 2 * TILT)));
        const w = Math.ceil((m.radius * 2 + 1) * S) + 2;
        const h = Math.ceil((m.height + 1 + m.radius * 2 * TILT) * S) + 2;
        const off = actor.off;
        if (off.width !== w || off.height !== h) { off.width = w; off.height = h; }
        const ctx = off.getContext('2d');
        ctx.clearRect(0, 0, w, h);
        renderModel(ctx, m, actor.base.angle, S, w / 2, h - m.radius * TILT * S - S - 1);
        const tint = flashTint ?? actor.tint;
        if (tint) {
            ctx.globalCompositeOperation = 'source-atop';
            ctx.fillStyle = tint;
            ctx.fillRect(0, 0, w, h);
            ctx.globalCompositeOperation = 'source-over';
        }
        return { S, w, h, feetX: w / 2, feetY: h - m.radius * TILT * S - 1 };
    }

    // Active animation offsets for an actor at time `now`.
    function animState(actor, now, towardX, towardY) {
        const st = { dx: 0, dy: 0, flash: null, squash: 0, alpha: 1, scale: 1 };
        actor.anims = actor.anims.filter(a => a.type === 'death' || now - a.t0 < a.dur);
        for (const a of actor.anims) {
            const p = Math.min(1, (now - a.t0) / a.dur);
            switch (a.type) {
                case 'lunge': { // out-and-back toward the opponent
                    const e = Math.sin(Math.PI * p);
                    st.dx += towardX * 0.20 * e;
                    st.dy += towardY * 0.20 * e;
                    break;
                }
                case 'hit': { // white → red flash + recoil away from attacker
                    st.flash = p < 0.3 ? 'rgba(255,255,255,0.85)' : `rgba(224,32,16,${0.6 * (1 - p)})`;
                    const e = Math.sin(Math.PI * Math.min(1, p * 1.4));
                    st.dx -= towardX * 0.06 * e;
                    st.dy -= towardY * 0.06 * e;
                    break;
                }
                case 'dodge': { // sidestep, no flash (miss)
                    st.dx += Math.sin(Math.PI * p) * 8;
                    break;
                }
                case 'death': {
                    st.squash = Math.max(st.squash, p * 0.65);
                    st.alpha = Math.min(st.alpha, 1 - p * 0.75);
                    break;
                }
                case 'spawn': { // pop-in for endless wave swaps
                    st.scale = Math.min(st.scale, 0.2 + 0.8 * p);
                    st.alpha = Math.min(st.alpha, p);
                    break;
                }
            }
        }
        const dying = actor.anims.some(a => a.type === 'death');
        if (!dying) st.dy += Math.sin(now * 0.0025 + actor.bobPhase) * 1.5;
        return st;
    }

    function drawActor(ctx, W, H, actor, other, now) {
        const ax = actor.base.x * W, ay = actor.base.y * H;
        const a = animState(actor, now, (other.base.x - actor.base.x) * W, (other.base.y - actor.base.y) * H);
        const r = renderActorOffscreen(actor, a.flash, Math.min(W, H));

        // Ground shadow (fades out with the actor)
        ctx.fillStyle = `rgba(0,0,0,${0.30 * a.alpha})`;
        ctx.beginPath();
        ctx.ellipse(ax + a.dx * 0.4, ay, actor.model.radius * r.S * 0.9, actor.model.radius * r.S * 0.32, 0, 0, 6.2832);
        ctx.fill();

        const w = r.w * a.scale, h = r.h * a.scale * (1 - a.squash);
        ctx.globalAlpha = a.alpha;
        ctx.drawImage(actor.off, ax + a.dx - w / 2, ay + a.dy - h, w, h);
        ctx.globalAlpha = 1;
    }

    function drawTelegraphGlow(ctx, W, H, actor, now) {
        const minDim = Math.min(W, H);
        const cx = actor.base.x * W;
        const cy = actor.base.y * H - minDim * actor.base.scaleFrac * 0.30;
        const rad = minDim * actor.base.scaleFrac * 0.55;
        const pulse = 0.22 + 0.14 * Math.sin(now * 0.006);
        const g = ctx.createRadialGradient(cx, cy, rad * 0.15, cx, cy, rad);
        g.addColorStop(0, `rgba(204,68,255,${pulse})`);
        g.addColorStop(1, 'rgba(204,68,255,0)');
        ctx.fillStyle = g;
        ctx.fillRect(cx - rad, cy - rad, rad * 2, rad * 2);
    }

    function npcModelUrl(enemyId) {
        return NPC_ASSETS.has(enemyId) ? `assets/npcs/${enemyId}.vox` : 'assets/player.vox';
    }

    // Unmodeled enemies reuse the player model with a permanent red wash so a
    // fight never looks like a mirror match.
    function npcFallbackTint(enemyId) {
        return NPC_ASSETS.has(enemyId) ? null : 'rgba(224,48,16,0.28)';
    }

    async function initBattle(canvasId, opts) {
        destroyBattle(canvasId);

        const playerModel = await loadModel(opts.playerUrl).catch(e => {
            console.warn('voxel: battle player model failed', e);
            return null;
        });
        const enemyModel = await loadModel(npcModelUrl(opts.enemyId)).catch(() => null)
            ?? await loadModel('assets/player.vox').catch(e => {
                console.warn('voxel: battle enemy model failed', e);
                return null;
            });
        if (!playerModel || !enemyModel) return;

        const canvas = document.getElementById(canvasId);
        if (!canvas || battles.has(canvasId)) return; // unmounted or re-init raced ahead

        const st = {
            canvas,
            ctx: canvas.getContext('2d'),
            player: makeActor(playerModel, LAYOUT.player, null),
            enemy: makeActor(enemyModel, LAYOUT.enemy, npcFallbackTint(opts.enemyId)),
            enemySwapToken: 0,
            flags: { telegraph: false },
            raf: 0,
            ro: null,
        };

        // Logical resolution follows the CSS box (÷BATTLE_PX) so chunky pixels
        // upscale without aspect distortion; fractional anchors adapt free.
        const fit = () => {
            const w = Math.max(60, Math.round(canvas.clientWidth / BATTLE_PX));
            const h = Math.max(60, Math.round(canvas.clientHeight / BATTLE_PX));
            if (canvas.width !== w) canvas.width = w;
            if (canvas.height !== h) canvas.height = h;
        };
        fit();
        st.ro = new ResizeObserver(fit);
        st.ro.observe(canvas);

        // Anchor the hitsplat zones from the same LAYOUT the actors use.
        for (const [zoneId, base] of [[opts.zonePlayerId, LAYOUT.player], [opts.zoneNpcId, LAYOUT.enemy]]) {
            const zone = document.getElementById(zoneId);
            if (zone) {
                zone.style.left = (base.x * 100) + '%';
                zone.style.top = (base.zoneY * 100) + '%';
            }
        }

        const loop = () => {
            const now = performance.now();
            const W = canvas.width, H = canvas.height;
            st.ctx.clearRect(0, 0, W, H);
            if (st.flags.telegraph) drawTelegraphGlow(st.ctx, W, H, st.enemy, now);
            drawActor(st.ctx, W, H, st.enemy, st.player, now);  // far actor first
            drawActor(st.ctx, W, H, st.player, st.enemy, now);
            st.raf = requestAnimationFrame(loop);
        };
        st.raf = requestAnimationFrame(loop);
        battles.set(canvasId, st);
    }

    function destroyBattle(canvasId) {
        const st = battles.get(canvasId);
        if (!st) return;
        cancelAnimationFrame(st.raf);
        st.ro?.disconnect();
        battles.delete(canvasId);
    }

    // Swap the enemy actor in place (endless waves) with a pop-in.
    async function setBattleEnemy(canvasId, enemyId) {
        const st = battles.get(canvasId);
        if (!st) return;
        const token = ++st.enemySwapToken;
        const model = await loadModel(npcModelUrl(enemyId)).catch(() => null)
            ?? await loadModel('assets/player.vox').catch(() => null);
        const live = battles.get(canvasId);
        if (!model || !live || live.enemySwapToken !== token) return; // stale swap or destroyed
        live.enemy = makeActor(model, LAYOUT.enemy, npcFallbackTint(enemyId));
        live.enemy.anims.push({ type: 'spawn', t0: performance.now(), dur: 300 });
    }

    function setBattleFlags(canvasId, flags) {
        const st = battles.get(canvasId);
        if (st) Object.assign(st.flags, flags);
    }

    // evt = { type, tier? }; types: playerAttack | enemyAttack | playerHit |
    // enemyHit | enemyDeath | playerDeath. No-op when the battle is gone.
    function battleEvent(canvasId, evt) {
        const st = battles.get(canvasId);
        if (!st) return;
        const now = performance.now();
        switch (evt.type) {
            case 'playerAttack': st.player.anims.push({ type: 'lunge', t0: now, dur: 320 }); break;
            case 'enemyAttack':  st.enemy.anims.push({ type: 'lunge', t0: now, dur: 320 }); break;
            case 'playerHit':
                st.player.anims.push(evt.tier === 'miss'
                    ? { type: 'dodge', t0: now, dur: 260 }
                    : { type: 'hit', t0: now, dur: 260 });
                break;
            case 'enemyHit':
                st.enemy.anims.push(evt.tier === 'miss'
                    ? { type: 'dodge', t0: now, dur: 260 }
                    : { type: 'hit', t0: now, dur: 260 });
                break;
            case 'enemyDeath':
                st.flags.telegraph = false;
                st.enemy.anims = [{ type: 'death', t0: now, dur: 900 }];
                break;
            case 'playerDeath':
                st.flags.telegraph = false;
                st.player.anims = [{ type: 'death', t0: now, dur: 900 }];
                break;
        }
    }

    window.voxel = {
        initPreview, destroyPreview, renderIcon, itemIcon,
        initBattle, destroyBattle, setBattleEnemy, setBattleFlags, battleEvent,
    };
})();
