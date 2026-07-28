// ── VFX layer (vfx-plan.md) ──────────────────────────────────────────────
// Renderer-only particle system, driven entirely by the semantic vfxEvents
// array the C# sim appends to GameState per tick (see BattleScene.razor's
// voxel.setVfxEvents call and GameState.VfxEvents). This module never
// touches gameplay state and never decides WHETHER something happened —
// only how it looks. toon.js wires it in at three points: initBattle
// (construct), the render loop (update(dt)), and destroyBattle (dispose).
//
// Event -> effect mapping lives in data/vfx-manifest.json, never hardcoded
// here, mirroring how asset-manifest.json drives equipment rendering.
import * as THREE from '../lib/three.module.min.js';
import {
    BatchedRenderer, ParticleSystem, ConstantValue, IntervalValue, ConstantColor,
    ColorOverLife, SizeOverLife, ForceOverLife, Gradient, PiecewiseBezier, Bezier,
    PointEmitter,
} from '../lib/three.quarks.esm.js';

// Global live-particle budget (renderer-only cosmetic rail, vfx-plan.md §6).
// PROVISIONAL: no design-doc source for this number — the brief's own
// stated default (300), not tuned against a real device yet.
const PARTICLE_BUDGET = 300;

// Per-effect pool size doubles as its own "concurrent" cap: three.quarks'
// ParticleSystem.restart() kills that system's own still-alive particles,
// so N pooled systems round-robined is exactly "N concurrent bursts of
// this effect," not a separate counter to maintain.
const DUST_POOL_SIZE = 3; // iteration 1: "capped at 3 concurrent per entity"

async function loadVfxManifest() {
    try {
        const res = await fetch('data/vfx-manifest.json');
        if (!res.ok) throw new Error(`vfx-manifest.json responded ${res.status}`);
        return (await res.json()).rows ?? [];
    } catch (e) {
        console.error('vfx.js: failed to load vfx-manifest.json — VFX layer disabled.', e);
        return [];
    }
}

// A soft round dust puff, generated at runtime — no texture asset is
// vendored yet. PROVISIONAL: the brief calls for Kenney particle-pack (CC0)
// textures under assets/vfx/; kenney.nl and itch.io both 403 from this
// sandbox's egress proxy (org policy, not a transient failure — see
// vfx-plan.md §5). Swap this for a real THREE.TextureLoader().load(...) of
// a vendored PNG once a session with broader network access can fetch one;
// nothing else in this file needs to change (manifest row already carries
// a `texture` field, currently the sentinel "procedural:radial_soft").
function makeProceduralDustTexture() {
    const size = 64;
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = size;
    const ctx = canvas.getContext('2d');
    const g = ctx.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
    g.addColorStop(0, 'rgba(255,255,255,0.9)');
    g.addColorStop(0.5, 'rgba(255,255,255,0.35)');
    g.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, size, size);
    const tex = new THREE.CanvasTexture(canvas);
    tex.needsUpdate = true;
    return tex;
}

function resolveTexture(ref) {
    if (ref === 'procedural:radial_soft') return makeProceduralDustTexture();
    // Real vendored texture path (future rows) — mirrors asset-manifest's
    // convention of paths being relative to wwwroot.
    return new THREE.TextureLoader().load(ref);
}

// colorToken names a CSS custom property (design-token doctrine palette
// per CLAUDE.md) — read once here rather than ever holding a raw hex in
// this file. Falls back to a neutral grey if the token is somehow missing.
function readColorToken(token) {
    const hex = getComputedStyle(document.documentElement).getPropertyValue(token).trim() || '#7a6a52';
    return new THREE.Color(hex);
}

// One pooled burst-emitter for a manifest row. worldSpace:true so particles
// keep drifting from their spawn point in world space once emitted, rather
// than following the (reused, repositioned) emitter object afterward.
function buildBurstSystem(row, color) {
    const texture = resolveTexture(row.texture);
    const system = new ParticleSystem({
        duration: 1,
        looping: false,
        startLife: new IntervalValue(row.lifetime * 0.85, row.lifetime),
        startSpeed: new IntervalValue(0.15, 0.5),
        startSize: new IntervalValue(row.size * 0.75, row.size * 1.25),
        startColor: new ConstantColor(new THREE.Vector4(1, 1, 1, 1)),
        worldSpace: true,
        maxParticle: row.count * 2, // headroom: previous burst's tail can still be fading
        emissionOverTime: new ConstantValue(0),
        emissionBursts: [{ time: 0, count: new ConstantValue(row.count), cycle: 1, interval: 0, probability: 1 }],
        shape: new PointEmitter(),
        material: new THREE.MeshBasicMaterial({
            map: texture,
            blending: row.blend === 'additive' ? THREE.AdditiveBlending : THREE.NormalBlending,
            transparent: true,
            depthWrite: false,
        }),
    });
    // Fade out over the full lifetime; color stays constant (no doctrine
    // meaning to encode — see vfx-plan.md's note on --border here).
    system.addBehavior(new ColorOverLife(new Gradient(
        [[new THREE.Vector3(color.r, color.g, color.b), 0], [new THREE.Vector3(color.r, color.g, color.b), 1]],
        [[0.55, 0], [0, 1]],
    )));
    // Billows outward slightly as it fades, cheap "puff" read without a
    // second draw call.
    system.addBehavior(new SizeOverLife(new PiecewiseBezier([[new Bezier(1, 1.3, 1.6, 1.6), 0]])));
    // Mutated per-burst (see spawnBurst) to bias drift opposite whatever
    // direction triggered this event — kept as its own behavior instance so
    // its x/z generators can be swapped without rebuilding the system.
    const drift = new ForceOverLife(new ConstantValue(0), new ConstantValue(0.05), new ConstantValue(0));
    system.addBehavior(drift);
    return { system, drift };
}

// A tiny fixed pool per effectId, round-robined — see DUST_POOL_SIZE's
// comment for why pool size == concurrency cap. `all` is every slot ever
// created (across every effect), used for the global particle budget below.
function makePool(renderer, group, row, all) {
    const color = readColorToken(row.colorToken);
    const slots = [];
    for (let i = 0; i < DUST_POOL_SIZE; i++) {
        const { system, drift } = buildBurstSystem(row, color);
        renderer.addSystem(system);
        group.add(system.emitter);
        const slot = { system, drift, lastUsedAt: 0 };
        slots.push(slot);
        all.push(slot);
    }
    return { row, slots, cursor: 0 };
}

function totalLiveParticles(all) {
    let n = 0;
    for (const slot of all) n += slot.system.particleNum ?? 0;
    return n;
}

// Auto-cull oldest (across every effect, not just the incoming one) when a
// new burst would exceed the global budget — a fresh effect is always more
// relevant to what's on screen right now than one already fading out.
function makeRoomFor(all, incomingCount) {
    let guard = all.length + 1; // avoid an infinite loop if particleNum bookkeeping is ever off
    while (totalLiveParticles(all) + incomingCount > PARTICLE_BUDGET && guard-- > 0) {
        let oldest = null;
        for (const slot of all) {
            if (slot.system.particleNum > 0 && (oldest === null || slot.lastUsedAt < oldest.lastUsedAt))
                oldest = slot;
        }
        if (!oldest) break;
        oldest.system.particleNum = 0; // hard-cull: this slot's burst is done being relevant
    }
}

export function createVfxSystem(scene) {
    const renderer = new BatchedRenderer();
    const group = new THREE.Group();
    group.add(renderer);
    scene.add(group);

    const allSlots = []; // every pooled slot, across every effect — for the global budget
    const poolsByEvent = new Map(); // event type -> [{ row, slots, cursor }]
    let quality = 'full'; // 'off' | 'low' | 'full' — vfx-plan.md §6, UI wiring deferred

    const ready = loadVfxManifest().then(rows => {
        for (const row of rows) {
            const pool = makePool(renderer, group, row, allSlots);
            const list = poolsByEvent.get(row.event) ?? [];
            list.push(pool);
            poolsByEvent.set(row.event, list);
        }
    });

    function spawnBurst(pool, worldX, worldZ, dx, dz) {
        const slot = pool.slots[pool.cursor];
        pool.cursor = (pool.cursor + 1) % pool.slots.length;
        const count = quality === 'low' ? Math.max(1, Math.round(pool.row.count / 2)) : pool.row.count;
        makeRoomFor(allSlots, count);
        // Drift opposite the movement direction — tile deltas are already
        // axis-aligned to world space at this TILE scale (same assumption
        // every other position write in toon.js already makes).
        const driftScale = 0.9;
        slot.drift.x = new ConstantValue(-dx * driftScale);
        slot.drift.z = new ConstantValue(-dz * driftScale);
        slot.system.emitter.position.set(worldX, 0.05, worldZ);
        // three.quarks only auto-refreshes matrixWorld once, ever, per
        // ParticleSystem (its own `firstTimeUpdate` flag) — every
        // subsequent reposition of a reused (pooled) emitter needs this
        // forced update, or the burst that fires on the next render-loop
        // tick reads last frame's (stale, pre-reposition) matrixWorld and
        // spawns at the wrong world position. Found by direct
        // Playwright/scene-graph inspection while verifying this feature —
        // omitting this call produced correct particleNum telemetry (the
        // burst genuinely fires) but nothing visible on screen.
        slot.system.emitter.updateWorldMatrix(true, false);
        slot.system.restart();
        slot.lastUsedAt = performance.now();
    }

    return {
        // positions: { [entityId]: { wx, wz } } — the entity's LIVE rendered
        // position this frame (toon.js's own interpolation/pursuit layer),
        // not the raw sim tile the event rode in on. See vfx-plan.md's
        // "positioned via the interpolation layer, not on tile-snap."
        handleEvents(events, positions, now) {
            if (quality === 'off' || !events?.length) return;
            const pools = poolsByEvent; // captured post-`ready` below via closures is fine — Map is live
            for (const ev of events) {
                const list = pools.get(ev.type);
                const pos = positions?.[ev.entityId];
                if (!list || !pos) continue;
                const dx = ev.data?.dx ?? 0, dz = ev.data?.dz ?? 0;
                for (const pool of list) {
                    // combat-feel pass 1: several event types (attack_swing,
                    // impact, hit_blocked) carry a doctrine style and want a
                    // DIFFERENT effect per style (a slash trail's color, or
                    // which of blocked_spark/shield_dome/deflect_ward plays)
                    // rather than one fixed effect for the whole event type
                    // — multiple manifest rows share the same `event` with
                    // different `style` filters; a row with no style always
                    // fires (iteration 1's dust_puff, entity_moved has no
                    // style at all).
                    if (pool.row.style && pool.row.style !== ev.data?.style) continue;
                    spawnBurst(pool, pos.wx, pos.wz, dx, dz);
                }
            }
        },
        update(dt) {
            if (quality === 'off') return;
            renderer.update(dt);
        },
        setQuality(q) {
            if (q === 'off' || q === 'low' || q === 'full') quality = q;
        },
        dispose() {
            for (const slot of allSlots) {
                renderer.deleteSystem(slot.system);
                slot.system.dispose();
            }
            scene.remove(group);
        },
        _ready: ready, // test/debug hook only
        _debugLiveParticles: () => totalLiveParticles(allSlots), // Playwright probe hook, mirrors api._battles
    };
}
