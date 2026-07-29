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

// Dust is worldSpace:true (see buildBurstSystem's comment) so a pooled
// emitter's later reposition never warps an earlier, still-alive burst —
// but that means a puff is otherwise perfectly stationary once spawned,
// which playtest feedback called out ("should follow the player a bit").
// This nudges every currently-alive dust particle by a fraction of the
// player's own per-frame movement each frame (see update()'s dragDust
// call) — a partial drag, not a full attach, so it still visibly settles
// behind the player rather than snapping along 1:1.
const DUST_FOLLOW_STRENGTH = 0.35;

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
        // 4x, not 2x: leaves headroom for a live countMult/sizeMult debug
        // tweak (setDustDebug) to actually raise the burst count above the
        // manifest baseline — maxParticle is fixed at construction time, so
        // a live count boost silently truncates without this margin.
        maxParticle: row.count * 4,
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
    // meaning to encode — see vfx-plan.md's note on --border here). Alpha
    // defaults to an instant pop to alphaPeak then a linear fade (every
    // combat-effect row's existing, unchanged look); a row can opt into a
    // quick fade-IN first (fadeInFrac > 0) — dust_puff does, since an
    // instant-opaque circle read as a flat smudge rather than a puff
    // (playtest report: "feels cheap").
    const alphaPeak = row.alphaPeak ?? 0.55;
    const fadeInFrac = row.fadeInFrac ?? 0;
    const alphaKeyframes = fadeInFrac > 0
        ? [[0, 0], [alphaPeak, fadeInFrac], [0, 1]]
        : [[alphaPeak, 0], [0, 1]];
    system.addBehavior(new ColorOverLife(new Gradient(
        [[new THREE.Vector3(color.r, color.g, color.b), 0], [new THREE.Vector3(color.r, color.g, color.b), 1]],
        alphaKeyframes,
    )));
    // Billows outward slightly as it fades, cheap "puff" read without a
    // second draw call.
    system.addBehavior(new SizeOverLife(new PiecewiseBezier([[new Bezier(1, 1.3, 1.6, 1.6), 0]])));
    // Mutated per-burst (see spawnBurst) to bias drift opposite whatever
    // direction triggered this event — kept as its own behavior instance so
    // its x/z generators can be swapped without rebuilding the system. Y is
    // a constant per-row upward lift (row.liftForce, default 0.05 — every
    // existing combat-effect row's unchanged value); dust_puff raises it,
    // since PointEmitter's fully-spherical initial velocity sends roughly
    // half of every burst's particles downward with only 0.05 of upward
    // counter-force to fight it, which visibly sank dust particles a few
    // centimeters below the ground plane by mid-life (found by direct
    // particle-position inspection, not just eyeballing) — read as clipping
    // through the floor, part of the "feels cheap" report.
    const drift = new ForceOverLife(new ConstantValue(0), new ConstantValue(row.liftForce ?? 0.05), new ConstantValue(0));
    system.addBehavior(drift);
    return { system, drift };
}

// A tiny fixed pool per effectId, round-robined — see DUST_POOL_SIZE's
// comment for why pool size == concurrency cap. A row's own `poolSize`
// overrides the shared default (dust_puff wants more headroom than a
// one-shot combat effect, since continuous movement re-triggers it far
// more often); `all` is every slot ever created (across every effect),
// used for the global particle budget below. `activeSlots` starts equal to
// the full built pool but can be dialed down live (setDustDebug) without
// tearing down/rebuilding systems — spawnBurst only round-robins through
// the first `activeSlots` of them.
function makePool(renderer, group, row, all) {
    const color = readColorToken(row.colorToken);
    const poolSize = row.poolSize ?? DUST_POOL_SIZE;
    const slots = [];
    for (let i = 0; i < poolSize; i++) {
        const { system, drift } = buildBurstSystem(row, color);
        renderer.addSystem(system);
        group.add(system.emitter);
        const slot = { system, drift, lastUsedAt: 0 };
        slots.push(slot);
        all.push(slot);
    }
    return { row, slots, cursor: 0, activeSlots: poolSize };
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
    let lastPlayerPos = null; // previous frame's player.pos, for this frame's drag delta (see dragDust)

    const ready = loadVfxManifest().then(rows => {
        for (const row of rows) {
            const pool = makePool(renderer, group, row, allSlots);
            const list = poolsByEvent.get(row.event) ?? [];
            list.push(pool);
            poolsByEvent.set(row.event, list);
        }
    });

    function spawnBurst(pool, worldX, worldZ, dx, dz) {
        // Round-robin only the first `activeSlots` of the built pool — lets
        // setDustDebug dial concurrency down live without tearing down any
        // ParticleSystem (see makePool's comment).
        const activeCount = Math.max(1, Math.min(pool.activeSlots ?? pool.slots.length, pool.slots.length));
        const slot = pool.slots[pool.cursor % activeCount];
        pool.cursor = (pool.cursor + 1) % activeCount;
        // Read the slot's OWN live emission count (setDustDebug may have
        // raised/lowered it above the manifest row's baseline), not the
        // static row default, so the particle-budget check stays accurate
        // under a live tweak.
        const liveCount = slot.system.emissionBursts[0].count.value ?? pool.row.count;
        const count = quality === 'low' ? Math.max(1, Math.round(liveCount / 2)) : liveCount;
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

    // Nudges every currently-alive dust particle by (dx, dz) *
    // DUST_FOLLOW_STRENGTH — called once per frame from update() with the
    // player's own per-frame movement delta, so a puff drags partway along
    // with the player instead of staying perfectly planted at its spawn
    // point (playtest request: "should follow the player a bit"). Mutates
    // particle.position directly (world-space, per buildBurstSystem) —
    // three.quarks' own per-frame integration already ran this frame inside
    // renderer.update(dt) above, so this is a plain additive offset on top,
    // not fighting the physics.
    function dragDust(dx, dz) {
        const list = poolsByEvent.get('entity_moved');
        if (!list) return;
        for (const pool of list) {
            for (const slot of pool.slots) {
                const particles = slot.system.particles;
                for (let i = 0; i < slot.system.particleNum; i++) {
                    particles[i].position.x += dx;
                    particles[i].position.z += dz;
                }
            }
        }
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
        // playerPos: { wx, wz } — the player's LIVE rendered position this
        // frame (toon.js's own continuous pursuit, already computed earlier
        // in the same render-loop tick this is called from), used only to
        // compute this frame's movement delta for dragDust. Optional: vfx.js
        // still works (dust just stays fully stationary) if the caller ever
        // omits it.
        update(dt, playerPos) {
            if (quality === 'off') return;
            renderer.update(dt);
            if (playerPos) {
                if (lastPlayerPos) {
                    const dx = playerPos.wx - lastPlayerPos.wx, dz = playerPos.wz - lastPlayerPos.wz;
                    if (dx !== 0 || dz !== 0) dragDust(dx * DUST_FOLLOW_STRENGTH, dz * DUST_FOLLOW_STRENGTH);
                }
                lastPlayerPos = { wx: playerPos.wx, wz: playerPos.wz };
            }
        },
        setQuality(q) {
            if (q === 'off' || q === 'low' || q === 'full') quality = q;
        },
        // Live movement-dust tuning (playtest request: "bigger, more often,
        // and can I tweak this live"). Scoped to the entity_moved pool(s)
        // only — the manifest currently has one (dust_puff, no style
        // filter), but this loops every pool under the event in case a
        // style-specific dust row is ever added later, same pattern
        // attack_swing/impact already use. Mutates the already-built
        // ParticleSystems in place (replacing their value-generator objects,
        // same technique spawnBurst already uses for drift) rather than
        // rebuilding pools, so a slider drag applies with no visible pop.
        setDustDebug({ sizeMult, countMult, activeSlots } = {}) {
            const list = poolsByEvent.get('entity_moved');
            if (!list) return;
            for (const pool of list) {
                if (typeof activeSlots === 'number')
                    pool.activeSlots = Math.max(1, Math.min(pool.slots.length, Math.round(activeSlots)));
                for (const slot of pool.slots) {
                    if (typeof sizeMult === 'number')
                        slot.system.startSize = new IntervalValue(
                            pool.row.size * 0.75 * sizeMult, pool.row.size * 1.25 * sizeMult);
                    if (typeof countMult === 'number')
                        slot.system.emissionBursts[0].count =
                            new ConstantValue(Math.max(1, Math.round(pool.row.count * countMult)));
                }
            }
        },
        // Current tuning, read off the first dust pool/slot — all slots in
        // a pool are always kept in sync by setDustDebug above, so any one
        // is representative. Returns null before the manifest has loaded.
        getDustDebug() {
            const pool = poolsByEvent.get('entity_moved')?.[0];
            if (!pool) return null;
            const s = pool.slots[0].system;
            return {
                sizeMult: Number((s.startSize.b / (pool.row.size * 1.25)).toFixed(3)),
                countMult: Number((s.emissionBursts[0].count.value / pool.row.count).toFixed(3)),
                activeSlots: pool.activeSlots,
                maxSlots: pool.slots.length,
            };
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
        // Playwright probe hook: every dust slot's emitter world position +
        // its live particles' own world positions, for diagnosing "spawned
        // in the wrong place" reports without guessing from a screenshot.
        _debugDustSlots: () => {
            const pool = poolsByEvent.get('entity_moved')?.[0];
            if (!pool) return [];
            return pool.slots.map((slot, i) => ({
                i,
                emitter: slot.system.emitter.position.toArray().map(n => Number(n.toFixed(3))),
                particleNum: slot.system.particleNum,
                particles: slot.system.particles.slice(0, slot.system.particleNum).map(p => ({
                    pos: [p.position.x, p.position.y, p.position.z].map(n => Number(n.toFixed(3))),
                    age: Number(p.age?.toFixed(3)),
                    life: Number(p.life?.toFixed(3)),
                })),
            }));
        },
    };
}
