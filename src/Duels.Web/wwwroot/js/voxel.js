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
        player: { x: 0.26, y: 0.92, scaleFrac: 0.85, angle: Math.PI + Math.PI / 5, zoneY: 0.70 },
        enemy:  { x: 0.75, y: 0.40, scaleFrac: 0.55, angle: -Math.PI / 5,          zoneY: 0.24 },
    };

    // Attack-style accent colors (match .style-badge-* in terminal.css).
    const STYLE_COLORS = { melee: '#ff5555', ranged: '#7ddc7d', magic: '#6ec6ff' };

    const battles = new Map(); // canvasId → state

    // Actors own a copy of the voxel list ordered core-first, so HP-driven
    // erosion chips voxels off the outside while the silhouette survives.
    // view is what actually renders: the first visCount entries of ordered.
    function makeActor(model, base, tint) {
        let cy = 0;
        for (const v of model.voxels) cy += v.y;
        cy /= model.voxels.length || 1;
        const ordered = model.voxels
            .map(v => ({ v, k: Math.hypot(v.x, (v.y - cy) * 0.7, v.z) + Math.random() * model.radius * 0.5 }))
            .sort((a, b) => a.k - b.k)
            .map(o => o.v);
        return {
            model, base, tint: tint ?? null, anims: [],
            bobPhase: Math.random() * 6.28, off: document.createElement('canvas'),
            ordered, visCount: ordered.length, lastRemoved: [],
            view: { ...model, voxels: ordered.slice(), scratch: new Array(ordered.length) },
            crumbled: false,
        };
    }

    // Erode or regrow an actor to `count` visible voxels. Voxels removed by an
    // erosion step are kept in lastRemoved so the hit event can turn exactly
    // those into debris.
    function setActorVisible(actor, count) {
        const N = actor.ordered.length;
        count = Math.max(8, Math.min(N, count | 0));
        if (count === actor.visCount) return;
        if (count < actor.visCount) actor.lastRemoved = actor.ordered.slice(count, actor.visCount);
        actor.visCount = count;
        actor.view.voxels = actor.ordered.slice(0, count);
        actor.view.scratch = new Array(count);
    }

    // Canvas position of a model-space voxel at the actor's base pose.
    function voxelCanvasPos(actor, v, W, H) {
        const S = actorScale(actor, Math.min(W, H));
        const c = Math.cos(actor.base.angle), sn = Math.sin(actor.base.angle);
        const rx = v.x * c - v.z * sn, rz = v.x * sn + v.z * c;
        return { x: actor.base.x * W + rx * S, y: actor.base.y * H - v.y * S + rz * S * TILT, S };
    }

    function torso(base, W, H) {
        const minDim = Math.min(W, H);
        return { x: base.x * W, y: base.y * H - minDim * base.scaleFrac * 0.5 };
    }

    // Voxel size for an actor at the current canvas resolution.
    function actorScale(actor, minDim) {
        const m = actor.model;
        return Math.max(1, Math.floor(minDim * actor.base.scaleFrac / (m.height + 1 + m.radius * 2 * TILT)));
    }

    // Render an actor to its offscreen canvas; returns {S, w, h, feetX, feetY}
    // where feet* locate the model's ground-center inside the offscreen.
    // Renders the eroded view, not the full model.
    function renderActorOffscreen(actor, flashTint, minDim, angle) {
        const m = actor.model;
        const S = actorScale(actor, minDim);
        const w = Math.ceil((m.radius * 2 + 1) * S) + 2;
        const h = Math.ceil((m.height + 1 + m.radius * 2 * TILT) * S) + 2;
        const off = actor.off;
        if (off.width !== w || off.height !== h) { off.width = w; off.height = h; }
        const ctx = off.getContext('2d');
        ctx.clearRect(0, 0, w, h);
        renderModel(ctx, actor.view, angle ?? actor.base.angle, S, w / 2, h - m.radius * TILT * S - S - 1);
        const tint = flashTint ?? actor.tint;
        if (tint) {
            ctx.globalCompositeOperation = 'source-atop';
            ctx.fillStyle = tint;
            ctx.fillRect(0, 0, w, h);
            ctx.globalCompositeOperation = 'source-over';
        }
        return { S, w, h, feetX: w / 2, feetY: h - m.radius * TILT * S - 1 };
    }

    // Active animation offsets for an actor at time `now` (scene time).
    // Anims may carry a future t0 (projectile travel); they render nothing
    // until their time comes.
    function animState(actor, now, towardX, towardY, windup) {
        const st = { dx: 0, dy: 0, flash: null, alpha: 1, scale: 1 };
        const len = Math.hypot(towardX, towardY) || 1;
        actor.anims = actor.anims.filter(a => now - a.t0 < a.dur);
        for (const a of actor.anims) {
            const p = Math.min(1, (now - a.t0) / a.dur);
            if (p < 0) continue;
            switch (a.type) {
                case 'attack': { // anticipation → strike → recovery, in place
                    let e;
                    if (p < 0.4) e = -(p / 0.4) * 0.6;                        // pull back
                    else if (p < 0.55) e = -0.6 + ((p - 0.4) / 0.15) * 1.6;   // snap forward
                    else { e = 1 - (p - 0.55) / 0.45; e *= e; }               // ease back
                    st.dx += (towardX / len) * 5 * e;
                    st.dy += (towardY / len) * 5 * e - Math.max(0, e) * 2;
                    break;
                }
                case 'hit': { // white → red flash + damage-scaled knockback
                    st.flash = p < 0.3 ? 'rgba(255,255,255,0.85)' : `rgba(224,32,16,${0.6 * (1 - p)})`;
                    const e = Math.sin(Math.PI * Math.min(1, p * 1.4));
                    const amp = a.amp ?? 4;
                    st.dx -= (towardX / len) * amp * e;
                    st.dy -= (towardY / len) * amp * e;
                    break;
                }
                case 'dodge': { // sidestep, no flash (miss)
                    st.dx += Math.sin(Math.PI * p) * 8;
                    break;
                }
                case 'spawn': { // pop-in for endless wave swaps / duel resets
                    st.scale = Math.min(st.scale, 0.2 + 0.8 * p);
                    st.alpha = Math.min(st.alpha, p);
                    break;
                }
            }
        }
        st.dy += Math.sin(now * 0.0025 + actor.bobPhase) * 1.5;
        if (windup) { // lean back/up before the attack lands
            st.dx -= (towardX / len) * 2;
            st.dy -= 2 + Math.sin(now * 0.02) * 1.5;
            st.scale *= 1.04;
        }
        return st;
    }

    // Draws an actor; returns its animState so followers (the weapon) can
    // track its motion. Crumbled actors are gone — their voxels are particles.
    function drawActor(ctx, W, H, actor, other, now, windup) {
        if (actor.crumbled) return null;
        const ax = actor.base.x * W, ay = actor.base.y * H;
        const a = animState(actor, now, (other.base.x - actor.base.x) * W, (other.base.y - actor.base.y) * H, windup);
        const wobble = Math.sin(now * 0.0004 + actor.bobPhase) * 0.05; // ambient life
        const r = renderActorOffscreen(actor, a.flash, Math.min(W, H), actor.base.angle + wobble);

        // Ground shadow (fades out with the actor)
        ctx.fillStyle = `rgba(0,0,0,${0.30 * a.alpha})`;
        ctx.beginPath();
        ctx.ellipse(ax + a.dx * 0.4, ay, actor.model.radius * r.S * 0.9, actor.model.radius * r.S * 0.32, 0, 0, 6.2832);
        ctx.fill();

        const w = r.w * a.scale, h = r.h * a.scale;
        ctx.globalAlpha = a.alpha;
        ctx.drawImage(actor.off, ax + a.dx - w / 2, ay + a.dy - h, w, h);
        ctx.globalAlpha = 1;
        return a;
    }

    // Equipped weapon rendered at the player's hand, swung on attack with a
    // motion trail. Only weapons with a .vox model appear (ITEM_ASSETS).
    function drawWeapon(st, W, H, playerA, now) {
        const wp = st.weapon;
        if (!wp || st.player.crumbled || !playerA) return;
        const pl = st.player, m = pl.model;
        const pS = actorScale(pl, Math.min(W, H));
        const wS = Math.max(1, Math.floor(m.height * pS * 0.5 / (wp.model.height + 1)));
        if (wp.S !== wS) { // (re)render the weapon offscreen at this scale
            wp.S = wS;
            const w = Math.ceil((wp.model.radius * 2 + 1) * wS) + 2;
            const h = Math.ceil((wp.model.height + 1 + wp.model.radius * 2 * TILT) * wS) + 2;
            wp.off.width = w; wp.off.height = h;
            renderModel(wp.off.getContext('2d'), wp.model, Math.PI / 5, wS,
                w / 2, h - wp.model.radius * TILT * wS - wS - 1);
        }
        const dirX = Math.sign(st.enemy.base.x - pl.base.x) || 1;
        const hx = pl.base.x * W + dirX * m.radius * pS * 0.8 + playerA.dx;
        const hy = pl.base.y * H - m.height * pS * 0.52 + playerA.dy;

        let ang = 0.5, trail = false; // idle rest angle
        const atk = pl.anims.find(a => a.type === 'attack');
        if (atk) {
            const p = Math.min(1, Math.max(0, (now - atk.t0) / atk.dur));
            if (p < 0.4) ang = 0.5 - (p / 0.4) * 1.3;                              // raise
            else if (p < 0.55) { ang = -0.8 + ((p - 0.4) / 0.15) * 2.0; trail = true; } // sweep
            else ang = 1.2 - ((p - 0.55) / 0.45) * 0.7;                            // settle
        }
        const blit = (a2, alpha) => {
            st.ctx.save();
            st.ctx.globalAlpha = alpha * playerA.alpha;
            st.ctx.translate(hx, hy);
            st.ctx.rotate(a2 * dirX);
            st.ctx.drawImage(wp.off, -wp.off.width / 2, -wp.off.height * 0.9);
            st.ctx.restore();
        };
        if (trail) { blit(ang - 0.6, 0.18); blit(ang - 0.3, 0.35); }
        blit(ang, 1);
    }

    // Debris burst at (a subset of) the given voxels' positions.
    function spawnDebris(st, actor, other, count, t0, sourceVoxels) {
        const W = st.canvas.width, H = st.canvas.height;
        const src = (sourceVoxels && sourceVoxels.length)
            ? sourceVoxels
            : actor.ordered.slice(Math.max(0, actor.visCount - 30), actor.visCount);
        if (!src.length) return;
        const dirX = Math.sign(actor.base.x - other.base.x) || 1; // away from attacker
        const floor = actor.base.y * H + 2;
        for (let i = 0; i < count && st.particles.length < 150; i++) {
            const v = src[(Math.random() * src.length) | 0];
            const p = voxelCanvasPos(actor, v, W, H);
            st.particles.push({
                x: p.x, y: p.y,
                vx: dirX * (0.4 + Math.random() * 1.4) + (Math.random() - 0.5) * 0.8,
                vy: -1.2 - Math.random() * 1.6,
                size: Math.max(1, p.S - (Math.random() < 0.5 ? 1 : 0)),
                color: (actor.model.shadeTable[v.ci] ?? ['#888', '#888', '#888', '#888'])[3],
                t0, life: 700 + Math.random() * 500, floor,
            });
        }
    }

    // Death: the whole remaining model bursts into physical voxel particles
    // that scatter, bounce on the platform, and fade.
    function crumbleActor(st, actor, other) {
        const W = st.canvas.width, H = st.canvas.height;
        const vs = actor.view.voxels;
        const step = vs.length > 400 ? 2 : 1;
        const dirX = Math.sign(actor.base.x - other.base.x) || 1;
        const cx = actor.base.x * W;
        const t0 = st.time;
        for (let i = 0; i < vs.length; i += step) {
            const v = vs[i];
            const p = voxelCanvasPos(actor, v, W, H);
            st.particles.push({
                x: p.x, y: p.y,
                vx: dirX * Math.random() * 1.0 + (p.x - cx) * 0.03,
                vy: -0.4 - Math.random() * 2.2,
                size: p.S,
                color: (actor.model.shadeTable[v.ci] ?? ['#888', '#888', '#888', '#888'])[3],
                t0, life: 800 + Math.random() * 900,
                floor: actor.base.y * H + 2 + Math.random() * 3,
            });
        }
        actor.crumbled = true;
        actor.anims = [];
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

    // Elliptical battle pad under an actor's feet (Pokemon-style depth cue).
    function drawPlatform(ctx, W, H, actor) {
        const S = actorScale(actor, Math.min(W, H));
        const rx = actor.model.radius * S * 1.5, ry = rx * 0.38;
        const x = actor.base.x * W, y = actor.base.y * H + 1;
        ctx.fillStyle = 'rgba(16,26,20,0.55)';
        ctx.beginPath(); ctx.ellipse(x, y, rx, ry, 0, 0, 6.2832); ctx.fill();
        ctx.strokeStyle = 'rgba(140,220,170,0.18)';
        ctx.lineWidth = 1;
        ctx.beginPath(); ctx.ellipse(x, y, rx, ry, 0, 0, 6.2832); ctx.stroke();
    }

    // Pulsing starburst above the enemy while their attack winds up; color
    // encodes the incoming style so prayer switches can be timed off it.
    function drawWindupGlint(ctx, W, H, actor, now, color) {
        const minDim = Math.min(W, H);
        const x = actor.base.x * W;
        const y = actor.base.y * H - minDim * actor.base.scaleFrac * 1.02;
        const pulse = 0.5 + 0.5 * Math.sin(now * 0.02);
        const r = minDim * 0.05 * (0.7 + 0.5 * pulse);
        ctx.save();
        ctx.globalAlpha = 0.5 + 0.5 * pulse;
        ctx.strokeStyle = color;
        ctx.lineWidth = 1.5;
        for (let i = 0; i < 4; i++) {
            const ang = i * Math.PI / 4 + now * 0.003;
            ctx.beginPath();
            ctx.moveTo(x - Math.cos(ang) * r, y - Math.sin(ang) * r);
            ctx.lineTo(x + Math.cos(ang) * r, y + Math.sin(ang) * r);
            ctx.stroke();
        }
        ctx.restore();
    }

    // Impact slash over the defender: sweeping arcs + radial sparks, fading out.
    function drawSlash(ctx, x, y, r, p, color) {
        ctx.save();
        ctx.globalAlpha = 1 - p;
        ctx.strokeStyle = color;
        ctx.lineWidth = 2;
        const sweep = p * 1.4;
        for (let i = 0; i < 3; i++) {
            ctx.beginPath();
            ctx.arc(x, y, r * (0.45 + i * 0.25), -2.2 + sweep, -0.9 + sweep);
            ctx.stroke();
        }
        ctx.lineWidth = 1;
        for (let i = 0; i < 5; i++) {
            const ang = i * 1.2566 + 0.6;
            const r0 = r * (0.25 + 0.55 * p), r1 = r * (0.5 + 0.8 * p);
            ctx.beginPath();
            ctx.moveTo(x + Math.cos(ang) * r0, y + Math.sin(ang) * r0);
            ctx.lineTo(x + Math.cos(ang) * r1, y + Math.sin(ang) * r1);
            ctx.stroke();
        }
        ctx.restore();
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
            weaponToken: 0,
            weapon: null,
            flags: { telegraph: false, windup: null },
            lastWindupStyle: null,
            effects: [],
            particles: [],
            projectiles: [],
            // Scene time: freezes during hit-stop, crawls during slow-mo.
            time: 0,
            lastReal: performance.now(),
            freezeUntil: 0,       // real-time ms
            slowUntil: 0,         // real-time ms
            pendingFreezes: [],   // { at: sceneTime, dur: realMs }
            cam: null,            // { amt, x, y, t0, dur } punch-zoom
            shake: null,          // { mag, t0, dur }
            delayEnemyVis: 0,     // projectile travel offsets, consumed by the
            delayPlayerVis: 0,    // matching hit event
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
            // Scene clock: dt freezes for hit-stop, crawls for killing-blow
            // slow-mo; everything below runs on st.time, so all motion —
            // anims, bob, debris, projectiles — obeys the same clock.
            const real = performance.now();
            let dt = Math.min(50, real - st.lastReal);
            st.lastReal = real;
            if (real < st.freezeUntil) dt = 0;
            else if (real < st.slowUntil) dt *= 0.3;
            st.time += dt;
            const now = st.time;
            for (let i = st.pendingFreezes.length - 1; i >= 0; i--) {
                if (st.pendingFreezes[i].at <= now) {
                    st.freezeUntil = real + st.pendingFreezes[i].dur;
                    st.pendingFreezes.splice(i, 1);
                }
            }

            const W = canvas.width, H = canvas.height;
            const ctx = st.ctx;
            const minDim = Math.min(W, H);
            ctx.clearRect(0, 0, W, H);
            ctx.save();

            // Camera: ambient breathe, punch-zoom anchored on the victim, shake
            const breathe = 1 + 0.010 * (0.5 + 0.5 * Math.sin(now * 0.0005));
            ctx.translate(W / 2, H / 2); ctx.scale(breathe, breathe); ctx.translate(-W / 2, -H / 2);
            if (st.cam) {
                const p = (now - st.cam.t0) / st.cam.dur;
                if (p >= 1) st.cam = null;
                else if (p >= 0) {
                    const z = 1 + st.cam.amt * (1 - p) * (1 - p);
                    ctx.translate(st.cam.x, st.cam.y); ctx.scale(z, z); ctx.translate(-st.cam.x, -st.cam.y);
                }
            }
            if (st.shake) {
                const p = (now - st.shake.t0) / st.shake.dur;
                if (p >= 1) st.shake = null;
                else if (p >= 0)
                    ctx.translate((Math.random() * 2 - 1) * st.shake.mag, (Math.random() * 2 - 1) * st.shake.mag);
            }

            drawPlatform(ctx, W, H, st.enemy);
            drawPlatform(ctx, W, H, st.player);
            if (st.flags.telegraph) drawTelegraphGlow(ctx, W, H, st.enemy, now);
            drawActor(ctx, W, H, st.enemy, st.player, now, st.flags.windup);  // far actor first
            const pa = drawActor(ctx, W, H, st.player, st.enemy, now, null);
            drawWeapon(st, W, H, pa, now);
            if (st.flags.windup && !st.enemy.crumbled)
                drawWindupGlint(ctx, W, H, st.enemy, now, STYLE_COLORS[st.flags.windup] ?? '#ffffff');

            // Impact slashes (style-color cue; debris carries the weight now)
            st.effects = st.effects.filter(e => now - e.t0 < e.dur);
            for (const e of st.effects) {
                if (now < e.t0) continue;
                const base = e.on === 'player' ? LAYOUT.player : LAYOUT.enemy;
                drawSlash(ctx, base.x * W, base.y * H - minDim * base.scaleFrac * 0.45,
                    minDim * base.scaleFrac * 0.28, (now - e.t0) / e.dur, e.color);
            }

            // Projectiles (arrow line / swirling motes)
            st.projectiles = st.projectiles.filter(pr => now - pr.t0 < pr.dur);
            for (const pr of st.projectiles) {
                const p = (now - pr.t0) / pr.dur;
                if (p < 0) continue;
                const x = pr.x0 + (pr.x1 - pr.x0) * p;
                const y = pr.y0 + (pr.y1 - pr.y0) * p - Math.sin(Math.PI * p) * (pr.style === 'ranged' ? 7 : 3);
                ctx.fillStyle = STYLE_COLORS[pr.style] ?? '#fff';
                if (pr.style === 'magic') {
                    for (let i = 0; i < 4; i++) {
                        const a = now * 0.02 + i * 1.57;
                        ctx.fillRect((x + Math.cos(a) * 3) | 0, (y + Math.sin(a) * 2) | 0, 2, 2);
                    }
                } else {
                    const dx = pr.x1 - pr.x0, dy = pr.y1 - pr.y0, L = Math.hypot(dx, dy) || 1;
                    for (let i = 0; i < 3; i++)
                        ctx.fillRect((x - dx / L * i * 2) | 0, (y - dy / L * i * 2) | 0, 2, 2);
                }
            }

            // Debris: gravity, platform bounce, fade over the last 30% of life
            st.particles = st.particles.filter(pt => now - pt.t0 < pt.life);
            for (const pt of st.particles) {
                if (now < pt.t0) continue;
                const k = dt / 16.7;
                pt.vy += 0.28 * k;
                pt.x += pt.vx * k;
                pt.y += pt.vy * k;
                if (pt.y > pt.floor) { pt.y = pt.floor; pt.vy *= -0.35; pt.vx *= 0.7; }
                const lp = (now - pt.t0) / pt.life;
                ctx.globalAlpha = lp > 0.7 ? (1 - lp) / 0.3 : 1;
                ctx.fillStyle = pt.color;
                ctx.fillRect(pt.x | 0, pt.y | 0, pt.size, pt.size);
            }
            ctx.globalAlpha = 1;

            ctx.restore();
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
        live.enemy.anims.push({ type: 'spawn', t0: live.time, dur: 300 });
    }

    function setBattleFlags(canvasId, flags) {
        const st = battles.get(canvasId);
        if (!st) return;
        Object.assign(st.flags, flags);
        // Style rotation can advance the moment the NPC attacks, so the impact
        // color comes from the style that was showing during the wind-up.
        if (st.flags.windup) st.lastWindupStyle = st.flags.windup;
    }

    // HP fractions (0..1) drive voxel erosion/regrow; called every game tick.
    function setBattleVitals(canvasId, vitals) {
        const st = battles.get(canvasId);
        if (!st) return;
        for (const [actor, frac] of [[st.player, vitals.player], [st.enemy, vitals.enemy]]) {
            if (actor.crumbled || typeof frac !== 'number') continue;
            const f = Math.max(0, Math.min(1, frac));
            setActorVisible(actor, Math.round(actor.ordered.length * (0.45 + 0.55 * f)));
        }
    }

    // Equip/unequip the weapon shown in the player's hand.
    async function setBattleWeapon(canvasId, weaponId) {
        const st = battles.get(canvasId);
        if (!st) return;
        const token = ++st.weaponToken;
        if (!weaponId || !ITEM_ASSETS.has(weaponId)) { st.weapon = null; return; }
        const model = await loadModel(`assets/items/${weaponId}.vox`).catch(() => null);
        const live = battles.get(canvasId);
        if (!live || live.weaponToken !== token) return; // stale or destroyed
        live.weapon = model ? { model, off: document.createElement('canvas'), S: 0 } : null;
    }

    // Restore both actors for a rematch on the same mounted scene (RETRY).
    function resetBattle(canvasId) {
        const st = battles.get(canvasId);
        if (!st) return;
        for (const actor of [st.player, st.enemy]) {
            actor.crumbled = false;
            actor.anims = [{ type: 'spawn', t0: st.time, dur: 250 }];
            actor.lastRemoved = [];
            setActorVisible(actor, actor.ordered.length);
        }
        st.effects = []; st.particles = []; st.projectiles = [];
        st.pendingFreezes = []; st.cam = null; st.shake = null;
        st.freezeUntil = 0; st.slowUntil = 0;
        st.delayEnemyVis = 0; st.delayPlayerVis = 0;
    }

    // evt = { type, tier?, dmg?, style? }; types: playerAttack | enemyAttack |
    // playerHit | enemyHit | enemyDeath | playerDeath. Attack events fire
    // projectiles for ranged/magic and delay the matching hit's visuals by the
    // travel time. No-op when the battle is gone.
    function battleEvent(canvasId, evt) {
        const st = battles.get(canvasId);
        if (!st) return;
        const now = st.time;
        const dmg = evt.dmg | 0;
        const landed = evt.tier !== 'miss' && evt.tier !== 'poison';
        const big = evt.tier === 'heavy' || evt.tier === 'spec' || evt.tier === 'boss';
        const W = st.canvas.width, H = st.canvas.height;

        const attack = (attacker, defender, style) => {
            attacker.anims.push({ type: 'attack', t0: now, dur: 340 });
            if (style === 'ranged' || style === 'magic') {
                const a = torso(attacker.base, W, H), b = torso(defender.base, W, H);
                st.projectiles.push({ x0: a.x, y0: a.y, x1: b.x, y1: b.y, t0: now + 60, dur: 170, style });
                return 230; // defender visuals wait for the projectile
            }
            return 0;
        };
        const impact = (defender, attacker, delay, color) => {
            const t0 = now + delay;
            if (evt.tier === 'miss') { defender.anims.push({ type: 'dodge', t0, dur: 260 }); return; }
            defender.anims.push({ type: 'hit', t0, dur: 260, amp: Math.min(7, 2 + dmg * 0.35) });
            if (!landed) return; // poison DoT: flash only
            st.effects.push({ on: defender === st.player ? 'player' : 'enemy', t0, dur: 280, color });
            spawnDebris(st, defender, attacker, Math.min(4 + dmg, 14), t0, defender.lastRemoved);
            defender.lastRemoved = [];
            st.pendingFreezes.push({ at: t0, dur: big ? 95 : 60 });
            st.shake = { mag: Math.min(3, 1 + dmg * 0.15), t0, dur: 90 };
            if (big) {
                const c = torso(defender.base, W, H);
                st.cam = { amt: 0.10, x: c.x, y: c.y, t0, dur: 300 };
            }
        };

        switch (evt.type) {
            case 'playerAttack': st.delayEnemyVis = attack(st.player, st.enemy, evt.style); break;
            case 'enemyAttack':  st.delayPlayerVis = attack(st.enemy, st.player, st.lastWindupStyle); break;
            case 'enemyHit': {
                const d = st.delayEnemyVis; st.delayEnemyVis = 0;
                impact(st.enemy, st.player, d, '#ffd76a');
                break;
            }
            case 'playerHit': {
                const d = st.delayPlayerVis; st.delayPlayerVis = 0;
                impact(st.player, st.enemy, d, STYLE_COLORS[st.lastWindupStyle] ?? '#ffffff');
                break;
            }
            case 'enemyDeath':
            case 'playerDeath': {
                st.flags.telegraph = false;
                st.flags.windup = null;
                const dying = evt.type === 'enemyDeath' ? st.enemy : st.player;
                const other = evt.type === 'enemyDeath' ? st.player : st.enemy;
                const c = torso(dying.base, W, H);
                crumbleActor(st, dying, other);
                st.slowUntil = performance.now() + 600;
                st.cam = { amt: 0.15, x: c.x, y: c.y, t0: now, dur: 900 };
                break;
            }
        }
    }

    window.voxel = {
        initPreview, destroyPreview, renderIcon, itemIcon,
        initBattle, destroyBattle, setBattleEnemy, setBattleFlags, battleEvent,
        setBattleVitals, setBattleWeapon, resetBattle,
    };
})();
