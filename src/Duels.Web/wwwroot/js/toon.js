// ── Toon battle renderer (Borderlands-style PoC) ─────────────────────────
// A WebGL implementation of the same battle API window.voxel exposes, so the
// C# sim and BattleScene stay untouched. Selected per battle by
// renderer-switch.js when localStorage['duels_renderer'] === 'toon'.
//
// The style thesis: hard 3-band toon ramp (MeshToonMaterial + gradient map),
// ink outlines (inverted-hull OutlineEffect), diagonal hatching multiplied
// into the darkest band, and a paper-grain overlay. Characters are
// procedurally built SkinnedMeshes with authored AnimationClips (idle / walk-
// run / attack / death) — swapping in a real glTF later is a loader call
// (GLTFLoader is vendored in lib/) replacing buildToonCharacter().
//
// PoC parity gaps vs the voxel renderer (documented, not bugs):
//  - whip rope physics, HP-driven erosion, eat/food anims, overhead prayer
//    icons; projectiles are a moving glow sphere without trails.
import * as THREE from '../lib/three.module.min.js';
import { OutlineEffect } from '../lib/OutlineEffect.js';

const TILE = 1.75;            // must match voxel.js — sim tiles are shared
const WALK_R = 5;
const TILE_MS = 600;
const MOVE_SPEED = { player: 2 * TILE / TILE_MS, enemy: TILE / TILE_MS };
const SNAP_DIST = 4.5 * TILE;

const battles = new Map();    // canvasId → battle state

// 3-step toon ramp shared by every material.
let gradientMap = null;
function getGradientMap() {
    if (gradientMap) return gradientMap;
    const data = new Uint8Array([70, 160, 255]); // dark / mid / lit
    gradientMap = new THREE.DataTexture(data, 3, 1, THREE.RedFormat);
    gradientMap.minFilter = gradientMap.magFilter = THREE.NearestFilter; // hard band edges
    gradientMap.needsUpdate = true;
    return gradientMap;
}

// Toon material with screen-space diagonal hatching injected into the
// darkest band — the comic "shadow ink" without hand-painted textures.
function toonMat(color) {
    const m = new THREE.MeshToonMaterial({ color, gradientMap: getGradientMap() });
    m.onBeforeCompile = shader => {
        shader.fragmentShader = shader.fragmentShader.replace(
            '#include <dithering_fragment>',
            `#include <dithering_fragment>
             {
                 // luminance-gated hatch: only the shadowed side gets ink lines
                 float lum = dot(gl_FragColor.rgb, vec3(0.299, 0.587, 0.114));
                 float band = smoothstep(0.20, 0.10, lum);
                 float hatch = step(0.5, fract((gl_FragCoord.x + gl_FragCoord.y) / 7.0));
                 gl_FragColor.rgb *= mix(1.0, 0.55 + 0.45 * hatch, band);
             }`);
    };
    return m;
}

// ── Procedural toon character ────────────────────────────────────────────
// A chunky low-poly humanoid SkinnedMesh: every box is rigid-skinned to one
// bone, so the silhouette stays crisp under the outline pass. Returns the
// mesh, mixer-ready clips, and named bones for the attack overlay.
function buildToonCharacter({ skin, shirt, pants, boots, scale = 1 }) {
    const bones = {};
    const mk = (name, x, y, z, parent) => {
        const b = new THREE.Bone(); b.name = name; b.position.set(x, y, z);
        if (parent) parent.add(b);
        bones[name] = b; return b;
    };
    const S = scale;
    const root  = mk('root', 0, 0.95 * S, 0, null);
    const chest = mk('chest', 0, 0.35 * S, 0, root);
    const head  = mk('head', 0, 0.42 * S, 0, chest);
    const armR  = mk('armR',  0.34 * S, 0.32 * S, 0, chest);
    const armL  = mk('armL', -0.34 * S, 0.32 * S, 0, chest);
    const legR  = mk('legR',  0.16 * S, -0.02 * S, 0, root);
    const legL  = mk('legL', -0.16 * S, -0.02 * S, 0, root);

    // geometry: boxes parented (skinned) to bones, positioned bone-relative
    const geoms = [], boneList = [root, chest, head, armR, armL, legR, legL];
    const boneIndex = new Map(boneList.map((b, i) => [b, i]));
    const addBox = (bone, w, h, d, ox, oy, oz, color) => {
        const g = new THREE.BoxGeometry(w * S, h * S, d * S);
        // world offset of the bone at bind time
        bone.updateWorldMatrix(true, false);
        const wp = new THREE.Vector3().setFromMatrixPosition(bone.matrixWorld);
        g.translate(wp.x + ox * S, wp.y + oy * S, wp.z + oz * S);
        const n = g.attributes.position.count;
        const idx = new Uint16Array(n * 4), wgt = new Float32Array(n * 4);
        for (let i = 0; i < n; i++) { idx[i * 4] = boneIndex.get(bone); wgt[i * 4] = 1; }
        g.setAttribute('skinIndex', new THREE.BufferAttribute(idx, 4));
        g.setAttribute('skinWeight', new THREE.BufferAttribute(wgt, 4));
        // per-face color via groups is messy — one material per color instead
        geoms.push({ g, color });
    };

    addBox(root, 0.44, 0.18, 0.28, 0, -0.02, 0, pants);        // hips
    addBox(chest, 0.5, 0.5, 0.3, 0, 0.14, 0, shirt);           // torso
    addBox(head, 0.34, 0.34, 0.34, 0, 0.2, 0, skin);           // head
    addBox(head, 0.36, 0.12, 0.36, 0, 0.4, 0, pants);          // hat brim
    addBox(armR, 0.14, 0.52, 0.16, 0, -0.24, 0, shirt);        // arms hang from shoulders
    addBox(armL, 0.14, 0.52, 0.16, 0, -0.24, 0, shirt);
    addBox(armR, 0.15, 0.14, 0.17, 0, -0.55, 0, skin);         // hands
    addBox(armL, 0.15, 0.14, 0.17, 0, -0.55, 0, skin);
    addBox(legR, 0.17, 0.6, 0.2, 0, -0.32, 0, pants);          // legs
    addBox(legL, 0.17, 0.6, 0.2, 0, -0.32, 0, pants);
    addBox(legR, 0.18, 0.12, 0.28, 0, -0.66, 0.04, boots);     // boots
    addBox(legL, 0.18, 0.12, 0.28, 0, -0.66, 0.04, boots);

    // One skeleton shared by every part-mesh; bind-pose inverses come from the
    // bones' current world matrices (updated in addBox), geometry vertices sit
    // at those same bind-pose positions, so identity bindMatrix is exact.
    const group = new THREE.Group();
    group.add(root);
    const skeleton = new THREE.Skeleton(boneList);
    for (const { g, color } of geoms) {
        const mesh = new THREE.SkinnedMesh(g, toonMat(color));
        mesh.bind(skeleton, new THREE.Matrix4());
        mesh.frustumCulled = false; // skinned bounds don't track the pose
        group.add(mesh);
    }

    // ── clips ── quaternion tracks per bone; times in seconds
    const q = (axis, deg) => new THREE.Quaternion()
        .setFromAxisAngle(axis, deg * Math.PI / 180).toArray();
    const X = new THREE.Vector3(1, 0, 0), Z = new THREE.Vector3(0, 0, 1);
    const quatTrack = (bone, times, quats) =>
        new THREE.QuaternionKeyframeTrack(`${bone}.quaternion`, times, quats.flat());

    // walk/run: legs & arms counter-swing; root bobs
    const swing = a => [q(X, a), q(X, -a), q(X, a)];
    const walk = new THREE.AnimationClip('walk', 0.7, [
        quatTrack('legR', [0, 0.35, 0.7], swing(32)),
        quatTrack('legL', [0, 0.35, 0.7], swing(-32)),
        quatTrack('armR', [0, 0.35, 0.7], swing(-24)),
        quatTrack('armL', [0, 0.35, 0.7], swing(24)),
        new THREE.VectorKeyframeTrack('root.position', [0, 0.175, 0.35, 0.525, 0.7],
            [0, 0.95 * S, 0, 0, 0.99 * S, 0, 0, 0.95 * S, 0, 0, 0.99 * S, 0, 0, 0.95 * S, 0]),
    ]);
    const idle = new THREE.AnimationClip('idle', 2.4, [
        new THREE.VectorKeyframeTrack('root.position', [0, 1.2, 2.4],
            [0, 0.95 * S, 0, 0, 0.965 * S, 0, 0, 0.95 * S, 0]),
        quatTrack('chest', [0, 1.2, 2.4], [q(X, 1.5), q(X, 3.5), q(X, 1.5)]),
    ]);
    // attack: right arm cocks back then snaps forward, chest twists in
    const attack = new THREE.AnimationClip('attack', 0.34, [
        quatTrack('armR', [0, 0.13, 0.19, 0.34], [q(X, 0), q(X, 70), q(X, -75), q(X, 0)]),
        quatTrack('chest', [0, 0.13, 0.19, 0.34], [q(Z, 0), q(Z, -9), q(Z, 8), q(Z, 0)]),
    ]);
    const death = new THREE.AnimationClip('death', 0.7, [
        quatTrack('root', [0, 0.55, 0.7], [q(X, 0), q(X, -84), q(X, -88)]),
        new THREE.VectorKeyframeTrack('root.position', [0, 0.55, 0.7],
            [0, 0.95 * S, 0, 0, 0.28 * S, 0, 0, 0.22 * S, 0]),
    ]);

    return { group, bones, clips: { idle, walk, attack, death }, height: 1.9 * S };
}

// hitsplat sprite: canvas-texture billboard reusing the voxel splat palette
const SPLAT_COLORS = {
    normal: '#b3281e', heavy: '#d8a018', max: '#d8a018', spec: '#d86a10',
    boss: '#9932cc', poison: '#3f8f3f', hazard: '#a8781e', miss: '#2f4d8a',
};
function splatSprite(dmg, tier) {
    const c = document.createElement('canvas'); c.width = c.height = 64;
    const g = c.getContext('2d');
    g.fillStyle = SPLAT_COLORS[tier] ?? SPLAT_COLORS.normal;
    g.beginPath();
    for (let i = 0; i < 12; i++) { // starburst
        const a = i / 12 * Math.PI * 2, r = i % 2 ? 30 : 22;
        g[i ? 'lineTo' : 'moveTo'](32 + Math.cos(a) * r, 32 + Math.sin(a) * r);
    }
    g.closePath(); g.fill();
    g.strokeStyle = '#111'; g.lineWidth = 3; g.stroke(); // comic ink rim
    g.fillStyle = '#fff'; g.font = 'bold 24px monospace';
    g.textAlign = 'center'; g.textBaseline = 'middle';
    g.fillText(tier === 'miss' ? '0' : String(dmg), 32, 34);
    const tex = new THREE.CanvasTexture(c);
    const sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, depthTest: false }));
    sp.scale.set(0.9, 0.9, 1);
    return sp;
}

function makeActor(st, key, colors) {
    const ch = buildToonCharacter(colors);
    st.scene.add(ch.group);
    const mixer = new THREE.AnimationMixer(ch.group);
    const actions = {};
    for (const [name, clip] of Object.entries(ch.clips)) {
        actions[name] = mixer.clipAction(clip);
        if (name === 'attack' || name === 'death') {
            actions[name].setLoop(THREE.LoopOnce);
            actions[name].clampWhenFinished = name === 'death';
        }
    }
    actions.idle.play();
    return {
        key, ch, mixer, actions, current: 'idle',
        pos: { wx: 0, wz: 0 }, target: { wx: 0, wz: 0 },
        facing: 0, crumbled: false, hp: 1,
    };
}

function playAction(actor, name, fade = 0.12) {
    if (actor.current === name && name !== 'attack') return;
    const from = actor.actions[actor.current], to = actor.actions[name];
    to.reset();
    if (name === 'attack') to.setEffectiveWeight(1).play().crossFadeFrom(from, 0.05, false);
    else { to.play(); from.crossFadeTo(to, fade, false); }
    actor.current = name;
}

// ── battle lifecycle ─────────────────────────────────────────────────────
async function initBattle(canvasId, opts) {
    destroyBattle(canvasId);
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;

    const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    const effect = new OutlineEffect(renderer, { defaultThickness: 0.0065, defaultColor: [0.05, 0.04, 0.03] });
    const scene = new THREE.Scene();
    scene.background = new THREE.Color('#1a2314');
    scene.fog = new THREE.Fog('#1a2314', 22, 40);

    const camera = new THREE.PerspectiveCamera(38, 1, 0.1, 100);
    const sun = new THREE.DirectionalLight('#fff4dd', 2.8);
    sun.position.set(6, 10, 4);
    scene.add(sun, new THREE.AmbientLight('#9aa8b8', 0.9));

    const st = {
        canvas, renderer, effect, scene, camera,
        yaw: 0.6, zoom: 1, camPitch: 0.62,
        player: null, enemy: null, dotnet: opts.dotnetRef ?? null,
        splats: [], hazardQuads: new Map(), marker: null, targetTile: null,
        obstacles: [], flags: {}, projectiles: [],
        clock: new THREE.Clock(), raf: 0, drag: null,
        enemyId: opts.enemyId,
    };

    // ── ground: flat toon plane + ink tile grid + square edge ──
    const ground = new THREE.Mesh(
        new THREE.PlaneGeometry(60, 60), toonMat('#4a7038'));
    ground.rotation.x = -Math.PI / 2;
    scene.add(ground);
    const gridLines = [];
    const ext = (WALK_R + 0.5) * TILE;
    for (let i = -WALK_R; i <= WALK_R + 1; i++) {
        const t = (i - 0.5) * TILE;
        gridLines.push(-ext, 0.01, t, ext, 0.01, t, t, 0.01, -ext, t, 0.01, ext);
    }
    const gridGeo = new THREE.BufferGeometry();
    gridGeo.setAttribute('position', new THREE.Float32BufferAttribute(gridLines, 3));
    scene.add(new THREE.LineSegments(gridGeo,
        new THREE.LineBasicMaterial({ color: '#1c2a12', transparent: true, opacity: 0.9 })));

    // actors
    st.player = makeActor(st, 'player', { skin: '#e0b088', shirt: '#c8b090', pants: '#3d5a35', boots: '#4a3020', scale: 1.0 });
    st.enemy  = makeActor(st, 'enemy',  { skin: '#d09a70', shirt: '#8a4030', pants: '#463830', boots: '#332218', scale: 1.08 });
    st.player.pos = { wx: 0, wz: 3 * TILE }; st.player.facing = Math.PI;
    st.enemy.pos = { wx: TILE, wz: -3 * TILE };
    st.player.target = { ...st.player.pos }; st.enemy.target = { ...st.enemy.pos };

    // enemy target tile ring (red) — drawn as a thin square outline
    const mkTileOutline = color => {
        const h = TILE * 0.42, g = new THREE.BufferGeometry();
        g.setAttribute('position', new THREE.Float32BufferAttribute([
            -h, 0.02, -h, h, 0.02, -h, h, 0.02, -h, h, 0.02, h,
            h, 0.02, h, -h, 0.02, h, -h, 0.02, h, -h, 0.02, -h], 3));
        const l = new THREE.LineSegments(g, new THREE.LineBasicMaterial({ color, linewidth: 2 }));
        scene.add(l); return l;
    };
    st.targetTile = mkTileOutline('#ff4444');
    st.marker = mkTileOutline('#ffd166'); st.marker.visible = false;

    // paper grain: full-screen overlay canvas texture, additive-ish
    const grain = document.createElement('canvas'); grain.width = grain.height = 256;
    const gg = grain.getContext('2d');
    for (let i = 0; i < 6000; i++) {
        gg.fillStyle = `rgba(${Math.random() > 0.5 ? 255 : 0},${Math.random() > 0.5 ? 255 : 0},200,0.02)`;
        gg.fillRect(Math.random() * 256, Math.random() * 256, 1, 1);
    }
    const grainTex = new THREE.CanvasTexture(grain);
    grainTex.wrapS = grainTex.wrapT = THREE.RepeatWrapping; grainTex.repeat.set(3, 3);
    const grainMesh = new THREE.Mesh(new THREE.PlaneGeometry(2, 2),
        new THREE.MeshBasicMaterial({ map: grainTex, transparent: true, opacity: 0.55, depthTest: false }));
    const grainScene = new THREE.Scene(); grainScene.add(grainMesh);
    const grainCam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);

    battles.set(canvasId, st);

    // pointer: drag = orbit yaw, tap = ground/enemy click (raycast)
    const ray = new THREE.Raycaster(), ndc = new THREE.Vector2();
    st.onDown = e => { st.drag = { x: e.clientX, y: e.clientY, moved: false }; canvas.setPointerCapture?.(e.pointerId); };
    st.onMove = e => {
        if (!st.drag) return;
        const dx = e.clientX - st.drag.x;
        if (Math.abs(dx) > 6) st.drag.moved = true;
        if (st.drag.moved) { st.yaw -= dx * 0.008; st.drag.x = e.clientX; st.drag.y = e.clientY; }
    };
    st.onUp = e => {
        const wasTap = st.drag && !st.drag.moved;
        st.drag = null;
        if (!wasTap) return;
        const r = canvas.getBoundingClientRect();
        ndc.set(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1);
        ray.setFromCamera(ndc, st.camera);
        // enemy first: hit test its meshes
        const hitEnemy = ray.intersectObjects(st.enemy.ch.group.children, false).length > 0;
        if (hitEnemy && !st.enemy.crumbled) { st.dotnet?.invokeMethodAsync('OnEnemyClick'); return; }
        const p = new THREE.Vector3();
        if (ray.ray.intersectPlane(new THREE.Plane(new THREE.Vector3(0, 1, 0), 0), p)) {
            let tx = Math.round(p.x / TILE), tz = Math.round(p.z / TILE);
            tx = Math.max(-WALK_R, Math.min(WALK_R, tx));
            tz = Math.max(-WALK_R, Math.min(WALK_R, tz));
            st.marker.position.set(tx * TILE, 0, tz * TILE);
            st.marker.visible = true; st.markerT = performance.now();
            st.dotnet?.invokeMethodAsync('OnGroundClick', tx, tz);
        }
    };
    st.onWheel = e => { e.preventDefault(); st.zoom = Math.max(0.5, Math.min(2.5, st.zoom * (e.deltaY > 0 ? 0.92 : 1.08))); };
    canvas.addEventListener('pointerdown', st.onDown);
    canvas.addEventListener('pointermove', st.onMove);
    canvas.addEventListener('pointerup', st.onUp);
    canvas.addEventListener('wheel', st.onWheel, { passive: false });

    const loop = () => {
        const dt = Math.min(0.05, st.clock.getDelta());
        const now = performance.now();
        // movement: constant-speed pursuit of the sim tile (same law as voxel.js)
        for (const actor of [st.player, st.enemy]) {
            if (actor.crumbled) continue;
            const other = actor === st.player ? st.enemy : st.player;
            const rx = actor.target.wx - actor.pos.wx, rz = actor.target.wz - actor.pos.wz;
            let rem = Math.hypot(rx, rz);
            if (rem > SNAP_DIST) { actor.pos.wx = actor.target.wx; actor.pos.wz = actor.target.wz; rem = 0; }
            let destFacing;
            if (rem > 0.03) {
                const sp = MOVE_SPEED[actor.key] * 1000; // wu/s
                const step = Math.min(rem, sp * dt);
                actor.pos.wx += rx / rem * step; actor.pos.wz += rz / rem * step;
                destFacing = Math.atan2(rx, rz);
                if (actor.current === 'idle') playAction(actor, 'walk');
                // cadence: scale the walk clip to ground speed (~1.15wu per cycle)
                actor.actions.walk.timeScale = Math.min(2.6, (step / dt) / 1.15 / 1.43);
            } else {
                if (actor.current === 'walk') playAction(actor, 'idle');
                destFacing = Math.atan2(other.pos.wx - actor.pos.wx, other.pos.wz - actor.pos.wz);
            }
            let da = destFacing - actor.facing;
            while (da > Math.PI) da -= Math.PI * 2;
            while (da < -Math.PI) da += Math.PI * 2;
            actor.facing += da * Math.min(1, dt * 14);
            actor.ch.group.position.set(actor.pos.wx, 0, actor.pos.wz);
            actor.ch.group.rotation.y = actor.facing;
            actor.mixer.update(dt);
            if (actor.current === 'attack' && !actor.actions.attack.isRunning())
                { actor.current = 'idle'; actor.actions.idle.reset().play(); }
        }

        // camera: rigid lock on the player, orbit by yaw
        const p = st.player.pos;
        const dist = 14 / st.zoom;
        st.camera.position.set(
            p.wx + Math.sin(st.yaw) * dist * Math.cos(st.camPitch),
            dist * Math.sin(st.camPitch) + 1,
            p.wz + Math.cos(st.yaw) * dist * Math.cos(st.camPitch));
        st.camera.lookAt(p.wx, 1, p.wz);

        // canvas sizing follows CSS box
        const w = canvas.clientWidth | 0, h = canvas.clientHeight | 0;
        if (w && h && (canvas.width !== w || canvas.height !== h)) {
            st.renderer.setSize(w, h, false);
            st.camera.aspect = w / h; st.camera.updateProjectionMatrix();
        }

        // enemy target tile follows the live enemy; marker fades after 900ms
        st.targetTile.position.set(st.enemy.pos.wx, 0, st.enemy.pos.wz);
        st.targetTile.visible = !st.enemy.crumbled;
        if (st.marker.visible && now - st.markerT > 900) st.marker.visible = false;

        // hazards pulse
        for (const [, q] of st.hazardQuads) {
            const urgent = !q.userData.pool && q.userData.t <= 1;
            q.material.opacity = 0.25 + 0.2 * Math.sin(now * (urgent ? 0.022 : q.userData.pool ? 0.004 : 0.009));
        }

        // splats rise & fade
        for (let i = st.splats.length - 1; i >= 0; i--) {
            const s = st.splats[i];
            s.sprite.position.y += dt * 0.4;
            const age = now - s.t0;
            s.sprite.material.opacity = age > 600 ? Math.max(0, 1 - (age - 600) / 300) : 1;
            if (age > 900) { st.scene.remove(s.sprite); st.splats.splice(i, 1); }
        }
        // projectiles
        for (let i = st.projectiles.length - 1; i >= 0; i--) {
            const pr = st.projectiles[i], t = (now - pr.t0) / pr.dur;
            if (t >= 1) { st.scene.remove(pr.mesh); st.projectiles.splice(i, 1); continue; }
            pr.mesh.position.lerpVectors(pr.from, pr.to, t);
            pr.mesh.position.y += Math.sin(t * Math.PI) * 0.8;
        }

        effect.render(scene, camera);
        st.renderer.autoClear = false;
        st.renderer.render(grainScene, grainCam);
        st.renderer.autoClear = true;
        st.raf = requestAnimationFrame(loop);
    };
    st.raf = requestAnimationFrame(loop);
}

function destroyBattle(canvasId) {
    const st = battles.get(canvasId);
    if (!st) return;
    cancelAnimationFrame(st.raf);
    st.canvas.removeEventListener('pointerdown', st.onDown);
    st.canvas.removeEventListener('pointermove', st.onMove);
    st.canvas.removeEventListener('pointerup', st.onUp);
    st.canvas.removeEventListener('wheel', st.onWheel);
    st.renderer.dispose();
    battles.delete(canvasId);
}

const api = {
    initBattle, destroyBattle,
    resetBattle(canvasId) {
        const st = battles.get(canvasId);
        if (!st) return;
        for (const a of [st.player, st.enemy]) {
            a.crumbled = false; a.ch.group.visible = true;
            a.actions.death.stop(); a.actions.idle.reset().play(); a.current = 'idle';
        }
    },
    setBattleEnemy(canvasId, enemyId) {
        const st = battles.get(canvasId);
        if (st) { st.enemyId = enemyId; api.resetBattle(canvasId); }
    },
    setBattleFlags(canvasId, flags) {
        const st = battles.get(canvasId);
        if (st) Object.assign(st.flags, flags);
    },
    setBattleVitals(canvasId, v) {
        const st = battles.get(canvasId);
        if (st) { st.player.hp = v.player; st.enemy.hp = v.enemy; }
    },
    setBattleWeapon() { /* PoC: weapon models not rendered yet */ },
    setBattleOverheads() { /* PoC parity gap: overhead prayer icons */ },
    watchOrientation() { /* landscape rotation handled by CSS; PoC no-op */ },
    getMovementDebug() { return window.voxelClassic?.getMovementDebug?.() ?? {}; },
    setMovementDebug(id, t) { window.voxelClassic?.setMovementDebug?.(id, t); },
    getCameraDebug(canvasId) {
        const st = battles.get(canvasId);
        return { fov: 38, tilt: 0.3, pitch: st?.camPitch ?? 0.62, zoom: st?.zoom ?? 1 };
    },
    setCameraDebug(canvasId, d) {
        const st = battles.get(canvasId);
        if (!st) return;
        if (typeof d.pitch === 'number') st.camPitch = Math.max(0.2, Math.min(1.4, d.pitch));
        if (typeof d.zoom === 'number') st.zoom = d.zoom;
    },
    setBattlePositions(canvasId, pos) {
        const st = battles.get(canvasId);
        if (!st) return;
        if (pos.player) st.player.target = { wx: pos.player.x * TILE, wz: pos.player.z * TILE };
        if (pos.enemy) st.enemy.target = { wx: pos.enemy.x * TILE, wz: pos.enemy.z * TILE };
    },
    setBattleObstacles(canvasId, tiles) {
        const st = battles.get(canvasId);
        if (!st) return;
        for (const o of st.obstacles) st.scene.remove(o);
        st.obstacles = [];
        for (const t of tiles || []) {
            const rock = new THREE.Mesh(
                new THREE.DodecahedronGeometry(TILE * 0.34, 0), toonMat('#8a8a86'));
            rock.position.set(t.x * TILE, TILE * 0.22, t.z * TILE);
            rock.rotation.set(Math.random(), Math.random() * 3, 0);
            st.scene.add(rock); st.obstacles.push(rock);
        }
    },
    setBattleHazards(canvasId, hz) {
        const st = battles.get(canvasId);
        if (!st) return;
        const want = new Map();
        for (const t of hz?.pending ?? []) want.set(`${t.x},${t.z}`, { pool: false, t: t.t });
        for (const t of hz?.pools ?? []) want.set(`${t.x},${t.z}`, { pool: true, t: 0 });
        for (const [key, q] of st.hazardQuads)
            if (!want.has(key)) { st.scene.remove(q); st.hazardQuads.delete(key); }
        for (const [key, info] of want) {
            let q = st.hazardQuads.get(key);
            if (!q) {
                const [x, z] = key.split(',').map(Number);
                q = new THREE.Mesh(new THREE.PlaneGeometry(TILE * 0.9, TILE * 0.9),
                    new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.3, depthWrite: false }));
                q.rotation.x = -Math.PI / 2;
                q.position.set(x * TILE, 0.015, z * TILE);
                st.scene.add(q); st.hazardQuads.set(key, q);
            }
            q.userData = info;
            q.material.color.set(info.pool ? '#5a7a1e' : info.t <= 1 ? '#ff5555' : '#ffd166');
        }
    },
    battleEvent(canvasId, evt) {
        const st = battles.get(canvasId);
        if (!st) return;
        const now = performance.now();
        const splatOn = (actor, dmg, tier) => {
            const sp = splatSprite(dmg, tier);
            sp.position.set(actor.pos.wx, actor.ch.height * 0.72, actor.pos.wz);
            st.scene.add(sp);
            st.splats.push({ sprite: sp, t0: now });
        };
        switch (evt.type) {
            case 'playerAttack': {
                playAction(st.player, 'attack');
                if (evt.style === 'ranged' || evt.style === 'magic') {
                    const mesh = new THREE.Mesh(new THREE.SphereGeometry(0.14, 8, 8),
                        new THREE.MeshBasicMaterial({ color: evt.style === 'magic' ? '#7ab8ff' : '#ffd166' }));
                    st.scene.add(mesh);
                    st.projectiles.push({ mesh, t0: now,
                        dur: 300,
                        from: new THREE.Vector3(st.player.pos.wx, 1.2, st.player.pos.wz),
                        to: new THREE.Vector3(st.enemy.pos.wx, 1.2, st.enemy.pos.wz) });
                }
                break;
            }
            case 'enemyAttack': playAction(st.enemy, 'attack'); break;
            case 'enemyHit': splatOn(st.enemy, evt.dmg | 0, evt.tier ?? 'normal'); break;
            case 'playerHit': splatOn(st.player, evt.dmg | 0, evt.tier ?? 'normal'); break;
            case 'enemyDeath': case 'playerDeath': {
                const dying = evt.type === 'enemyDeath' ? st.enemy : st.player;
                dying.crumbled = true;
                playAction(dying, 'death', 0.05);
                break;
            }
        }
    },
};

window.voxelToon = api;
