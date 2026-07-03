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

    function render(ctx, W, H, model, angle) {
        ctx.clearRect(0, 0, W, H);
        // Auto-fit: horizontal must clear the rotation-swept diameter, vertical
        // the model height plus the tilt swing at top and bottom.
        const S = Math.max(1, Math.floor(Math.min(
            (W - 2) / (model.radius * 2 + 1),
            (H - 2) / (model.height + 1 + model.radius * 2 * TILT))));
        const cx = W / 2;
        const baseY = (H + model.height * S) / 2;

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

    // ── Static icons (item/enemy assets — pipeline ready, no consumers yet) ──

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

    window.voxel = { initPreview, destroyPreview, renderIcon };
})();
