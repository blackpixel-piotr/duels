// ── Toon battle renderer (Borderlands-style PoC) ─────────────────────────
// A WebGL implementation of the same battle API window.voxel exposes, so the
// C# sim and BattleScene stay untouched. Selected per battle by
// renderer-switch.js when localStorage['duels_renderer'] === 'toon'.
//
// The style thesis: hard 3-band toon ramp (MeshToonMaterial + gradient map),
// ink outlines (inverted-hull OutlineEffect), diagonal hatching multiplied
// into the darkest band, and a paper-grain overlay. Characters are the
// Quaternius Universal Base glTF driven by Universal Animation Library clips
// (extracted by tools/extract_anims.mjs) through a speed blend space +
// one-shot overlays; authored clips and a procedural SkinnedMesh remain as
// fallbacks if the assets fail to load.
//
// PoC parity gaps vs the voxel renderer (documented, not bugs):
//  - whip rope physics, HP-driven erosion, overhead prayer icons;
//    projectiles are a moving glow sphere without trails.
// Equipment: weapons with a modeled asset (WEAPON_ASSETS) render in the
// right hand and switch the idle to a sword stance; armor items with a
// modeled asset (ARMOR_ASSETS) are Quaternius modular-outfit pieces that
// share the Universal Base skeleton, so their SkinnedMeshes rebind onto the
// live character's bones by name. Items without assets keep the old
// behavior (no mesh, animation-only).
import * as THREE from '../lib/three.module.min.js';
import { OutlineEffect } from '../lib/OutlineEffect.js';
import { GLTFLoader } from '../lib/GLTFLoader.js';
import * as SkeletonUtils from '../lib/SkeletonUtils.js';

const TILE = 1.75;            // must match voxel.js — sim tiles are shared
const WALK_R = 5;
const TILE_MS = 600;
const MOVE_SPEED = { player: 2 * TILE / TILE_MS, enemy: TILE / TILE_MS };
const SNAP_DIST = 4.5 * TILE;

const battles = new Map();    // canvasId → battle state

// UI bible §1 doctrine palette — melee red-orange / ranged green / magic
// blue. RGB (0-1 floats) for OutlineEffect's per-material color override;
// hex for everything else (projectile tint, etc). Compound style-shift
// telegraphs (style undecided until cast time) reuse 'ranged' green — the
// Boss Bible's own literal color for that transition ("Mandibles glow
// green"), not an invented neutral.
const DOCTRINE_RGB = {
    melee:  [0.878, 0.337, 0.102],
    ranged: [0.224, 0.827, 0.224],
    magic:  [0.357, 0.659, 0.878],
};
const DOCTRINE_HEX = { melee: '#e0561a', ranged: '#39d339', magic: '#5ba8e0' };

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
// darkest band — the comic "shadow ink". An optional base-color map (the
// character pack's textures) multiplies under the same banded ramp.
function toonMat(color, map = null) {
    const m = new THREE.MeshToonMaterial({ color, map, gradientMap: getGradientMap() });
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

// ── glTF character (Superhero model, assets/models/) ─────────────────────
// The model ships in T-pose with a UE-style skeleton (pelvis, spine_01..03,
// thigh/calf/foot_l/r, clavicle/upperarm/lowerarm/hand_l/r) and NO baked
// animations, so we author clips in code against its real bones. Base-color
// textures ship in assets/models/; normal/roughness maps do not — the toon
// ramp ignores them, so a URL modifier feeds the loader a 1px placeholder
// instead of ~12MB of PBR maps.
const noTex = () => {
    const c = document.createElement('canvas'); c.width = c.height = 1;
    c.getContext('2d').fillRect(0, 0, 1, 1);
    return c.toDataURL('image/png');
};
const gltfManager = new THREE.LoadingManager();
gltfManager.setURLModifier(url =>
    /(normal|roughness|orm)[^/]*\.(png|jpe?g)(\?.*)?$/i.test(url) ? noTex() : url);
const gltfLoader = new GLTFLoader(gltfManager);
let gltfPromise = null;
function loadToonCharacterGltf() {
    gltfPromise ??= new Promise((resolve, reject) =>
        gltfLoader.load('assets/models/superhero.gltf', resolve, undefined, reject));
    return gltfPromise;
}

// Quaternius Universal Animation Library clips (extracted by
// tools/extract_anims.mjs). Same universal skeleton as the character, so the
// clips bind by bone name — no retargeting. Role names are what the rest of
// the renderer speaks; clip names are the library's.
const LIB_ROLES = {
    idle: 'Idle_Loop', walk: 'Walk_Loop', jog: 'Jog_Fwd_Loop', sprint: 'Sprint_Loop',
    punchA: 'Punch_Jab', punchB: 'Punch_Cross',
    swordA: 'Sword_Regular_A', swordB: 'Sword_Regular_B',
    spec: 'Sword_Attack', throw: 'OverhandThrow', cast: 'Spell_Simple_Shoot',
    hitA: 'Hit_Chest', hitB: 'Hit_Head', hitBig: 'Hit_Knockback',
    eat: 'Consume', death: 'Death01', swordIdle: 'Sword_Idle',
};
let animLibPromise = null;
function loadAnimLibrary() {
    animLibPromise ??= Promise.all(
        ['assets/models/anims1.glb', 'assets/models/anims2.glb'].map(u =>
            new Promise((res, rej) => gltfLoader.load(u, res, undefined, rej))),
    ).then(docs => {
        const byName = {};
        for (const d of docs) for (const c of d.animations) byName[c.name] = c;
        const clips = {};
        for (const [role, name] of Object.entries(LIB_ROLES))
            if (byName[name]) clips[role] = byName[name];
        return clips;
    });
    return animLibPromise;
}

// Keep only tracks that resolve on this character (the library rig has a few
// extra leaf nodes) — unresolvable tracks make THREE warn every bind.
function fitClip(clip, bones) {
    const tracks = clip.tracks.filter(t => bones[t.name.split('.')[0]]);
    return tracks.length === clip.tracks.length
        ? clip : new THREE.AnimationClip(clip.name, clip.duration, tracks);
}

// ── Equipment assets (weapons + armor with real meshes) ──────────────────
// Item ids the C# sim speaks, mapped to renderable assets under
// assets/models/equip/. Ids without an entry render nothing (animation-only),
// which is the pre-existing behavior for the whole OSRS-style catalog.
// The mapping itself is data (M0 asset manifest loader) — see
// data/asset-manifest.json (mirrored for humans in /asset-map.md at the repo
// root) — never hard-coded here, so adding a modeled item is a data commit.
const EQUIP_DIR = 'assets/models/equip/';

async function loadAssetManifest() {
    try {
        const res = await fetch('data/asset-manifest.json');
        if (!res.ok) throw new Error(`asset-manifest.json responded ${res.status}`);
        return await res.json();
    } catch (e) {
        console.error('toon.js: failed to load asset-manifest.json — all equipment renders animation-only.', e);
        return { weapons: [], armor: [] };
    }
}

const assetManifest = await loadAssetManifest();

// length: world-unit blade+grip span the model is normalized to.
const WEAPON_ASSETS = Object.fromEntries(
    assetManifest.weapons.map(w => [w.itemId, { url: EQUIP_DIR + w.modelRef, length: w.length }]));
const ARMOR_ASSETS = Object.fromEntries(
    assetManifest.armor.map(a => [a.itemId, EQUIP_DIR + a.modelRef]));
const equipCache = new Map(); // url → Promise<gltf>
function loadEquipGltf(url) {
    if (!equipCache.has(url))
        equipCache.set(url, new Promise((res, rej) => gltfLoader.load(url, res, undefined, rej)));
    return equipCache.get(url);
}

// The outfit pieces are body-segment REPLACEMENTS (they carry their own skin
// where flesh shows), authored for the pack's slimmer Regular build — layered
// over the muscular base body they get swallowed by it. So each equipped
// piece hides the base skin underneath: triangles of the body mesh whose
// dominant bone falls in the piece's region are dropped from the index.
// Head/neck belong to no region, so the face always survives (hoods only
// cover the scalp).
const REGION_BONES = {
    body:  /^(spine_0[123]|clavicle_[lr])$/,
    legs:  /^(pelvis|thigh_[lr]|calf_[lr])$/,
    boots: /^(foot_[lr]|ball(_leaf)?_[lr])$/,
    arms:  /^(upperarm_[lr]|lowerarm_[lr]|hand_[lr]|(index|middle|pinky|ring|thumb)_\d+(_leaf)?_[lr])$/,
};
const ARMOR_REGION = Object.fromEntries(
    assetManifest.armor.filter(a => a.region).map(a => [a.itemId, a.region]));
function maskBodySkin(ch, regions) {
    const mesh = ch.bodyMesh;
    if (!mesh) return;
    const orig = mesh.userData.origGeometry ??= mesh.geometry;
    if (!regions.length) { mesh.geometry = orig; return; }
    const pats = regions.map(r => REGION_BONES[r]).filter(Boolean);
    const bones = mesh.skeleton.bones;
    const si = orig.getAttribute('skinIndex'), sw = orig.getAttribute('skinWeight');
    const hidden = new Uint8Array(si.count);
    for (let v = 0; v < si.count; v++) {
        let best = -1, bi = 0;
        for (let k = 0; k < 4; k++) {
            const w = sw.getComponent(v, k);
            if (w > best) { best = w; bi = si.getComponent(v, k); }
        }
        const nm = bones[bi]?.name ?? '';
        if (pats.some(re => re.test(nm))) hidden[v] = 1;
    }
    const idx = orig.index.array, keep = [];
    for (let t = 0; t < idx.length; t += 3)
        if (!(hidden[idx[t]] || hidden[idx[t + 1]] || hidden[idx[t + 2]]))
            keep.push(idx[t], idx[t + 1], idx[t + 2]);
    // New index over the SAME attribute buffers — no vertex data duplicated.
    const g = new THREE.BufferGeometry();
    for (const [name, attr] of Object.entries(orig.attributes)) g.setAttribute(name, attr);
    g.setIndex(keep);
    mesh.geometry = g;
}

// Rotate `bone` about a character-space axis while keeping everything else in
// its rest pose: newLocal = (P⁻¹ · R · P) · restLocal, where P is the parent
// chain's rest orientation in character space. Used both for one-off pose
// fixes (T-pose → arms down) and for every authored keyframe below.
function charSpaceQuat(parentWorldQ, axis, deg, restLocal) {
    const r = new THREE.Quaternion().setFromAxisAngle(axis, deg * Math.PI / 180);
    return new THREE.Quaternion()
        .copy(parentWorldQ).invert().multiply(r).multiply(parentWorldQ).multiply(restLocal);
}

function buildToonCharacterFromGltf(gltf, { suit = '#4a6a8a', tint = '#ffffff', scale = 1 } = {}) {
    // SkeletonUtils.clone, NOT Object3D.clone: a plain clone leaves the new
    // SkinnedMeshes bound to the ORIGINAL skeleton, whose bones never get
    // world-matrix updates — the mesh collapses to a speck at the origin.
    const model = SkeletonUtils.clone(gltf.scene);

    const bones = {};
    let bodyMesh = null; // largest skinned mesh = the base body (skin+face)
    model.traverse(n => {
        if (n.isBone) bones[n.name] = n;
        if (n.isSkinnedMesh && (!bodyMesh ||
            n.geometry.attributes.position.count > bodyMesh.geometry.attributes.position.count))
            bodyMesh = n;
        if (n.isMesh || n.isSkinnedMesh) {
            const src = Array.isArray(n.material) ? n.material[0] : n.material;
            const isFace = /eye(s|brow)/i.test(n.name);
            if (src?.map) {
                // real base-color texture; tint multiplies the body only so
                // player/enemy stay tellable apart while faces keep true color
                n.material = toonMat(isFace ? '#ffffff' : tint, src.map);
                n.material.side = src.side; // hair/brow cards are double-sided
            } else {
                n.material = toonMat(isFace
                    ? (/brow/i.test(n.name) ? '#241a12' : '#e8e4da')
                    : suit);
            }
            n.frustumCulled = false; // skinned bounds don't track the pose
        }
    });

    // T-pose → relaxed A-pose. Side detected from bind world-x, not the name
    // suffix, so the fix survives mirrored exports.
    model.updateWorldMatrix(true, true);
    const Zc = new THREE.Vector3(0, 0, 1), Xc = new THREE.Vector3(1, 0, 0);
    const pw = new THREE.Vector3(), pq = new THREE.Quaternion();
    for (const nm of ['upperarm_l', 'upperarm_r']) {
        const b = bones[nm]; if (!b) continue;
        b.getWorldPosition(pw);
        b.parent.getWorldQuaternion(pq);
        b.quaternion.copy(charSpaceQuat(pq, Zc, pw.x > 0 ? -62 : 62, b.quaternion));
    }
    for (const nm of ['lowerarm_l', 'lowerarm_r']) {         // slight elbow bend
        const b = bones[nm]; if (!b) continue;
        b.getWorldPosition(pw);
        b.parent.getWorldQuaternion(pq);
        b.quaternion.copy(charSpaceQuat(pq, Zc, pw.x > 0 ? -12 : 12, b.quaternion));
    }

    // Capture the corrected rest pose — every keyframe composes on top of it.
    model.updateWorldMatrix(true, true);
    const rest = {};
    for (const [nm, b] of Object.entries(bones)) {
        const pWorldQ = new THREE.Quaternion();
        b.parent.getWorldQuaternion(pWorldQ);
        rest[nm] = { local: b.quaternion.clone(), parentQ: pWorldQ };
    }

    // Weapon socket: a Group childed to hand_r whose +Y is the grip axis.
    // A closed fist's grip tunnel runs along the knuckle line, so aim +Y
    // from the pinky knuckle through the index knuckle (blade exits on the
    // thumb side) and park the origin between them — all measured in the
    // rest pose, so the socket rides the hand through every clip with no
    // per-animation tuning.
    let weaponSocket = null;
    if (bones.hand_r && bones.index_01_r && bones.pinky_01_r) {
        weaponSocket = new THREE.Group();
        weaponSocket.name = 'weaponSocketR';
        const hand = bones.hand_r;
        hand.add(weaponSocket);
        const iW = bones.index_01_r.getWorldPosition(new THREE.Vector3());
        const pW = bones.pinky_01_r.getWorldPosition(new THREE.Vector3());
        const bladeDir = iW.clone().sub(pW).normalize();
        const qAlign = new THREE.Quaternion()
            .setFromUnitVectors(new THREE.Vector3(0, 1, 0), bladeDir);
        const hq = hand.getWorldQuaternion(new THREE.Quaternion());
        weaponSocket.quaternion.copy(hq.invert().multiply(qAlign));
        weaponSocket.position.copy(
            hand.worldToLocal(iW.clone().add(pW).multiplyScalar(0.5)));
    }

    // Normalize size: feet on the ground, height 2.8 wu ≈ 1.6× TILE — the
    // OSRS/RS3 character-to-tile ratio. (1.9 wu read as a figurine on a
    // chessboard: barely taller than a tile is wide.)
    const bb = new THREE.Box3().setFromObject(model);
    const rawH = Math.max(0.01, bb.max.y - bb.min.y);
    const height = 2.8 * scale;
    const s = height / rawH;
    model.scale.setScalar(s);
    model.position.y = -bb.min.y * s;

    // fallPivot carries the death fall (and could carry knockback later) so
    // clips never fight the group's facing/position, which the loop owns.
    const pivot = new THREE.Group(); pivot.name = 'fallPivot';
    pivot.add(model);
    const group = new THREE.Group();
    group.add(pivot);

    // ── authored clips on the real rig ──
    // charTrack: keyframes as [axis, deg] deltas in character space.
    const charTrack = (nm, times, keys) => {
        const r = rest[nm];
        return new THREE.QuaternionKeyframeTrack(`${bones[nm].name}.quaternion`, times,
            keys.flatMap(([axis, deg]) => charSpaceQuat(r.parentQ, axis, deg, r.local).toArray()));
    };
    const has = nm => !!bones[nm];
    const tracks = arr => arr.filter(Boolean);

    // walk: legs swing about character X, arms counter-swing, body bobs.
    const walk = new THREE.AnimationClip('walk', 0.7, tracks([
        has('thigh_l') && charTrack('thigh_l', [0, 0.35, 0.7], [[Xc, 26], [Xc, -26], [Xc, 26]]),
        has('thigh_r') && charTrack('thigh_r', [0, 0.35, 0.7], [[Xc, -26], [Xc, 26], [Xc, -26]]),
        // knee bends as the leg recovers (opposite phase to the thigh's peak)
        has('calf_l') && charTrack('calf_l', [0, 0.175, 0.35, 0.525, 0.7],
            [[Xc, -8], [Xc, -34], [Xc, -8], [Xc, -4], [Xc, -8]]),
        has('calf_r') && charTrack('calf_r', [0, 0.175, 0.35, 0.525, 0.7],
            [[Xc, -8], [Xc, -4], [Xc, -8], [Xc, -34], [Xc, -8]]),
        has('upperarm_l') && charTrack('upperarm_l', [0, 0.35, 0.7], [[Xc, -14], [Xc, 14], [Xc, -14]]),
        has('upperarm_r') && charTrack('upperarm_r', [0, 0.35, 0.7], [[Xc, 14], [Xc, -14], [Xc, 14]]),
        new THREE.VectorKeyframeTrack('fallPivot.position', [0, 0.175, 0.35, 0.525, 0.7],
            [0, 0, 0, 0, 0.04, 0, 0, 0, 0, 0, 0.04, 0, 0, 0, 0]),
    ]));
    // idle: breathing sway
    const idle = new THREE.AnimationClip('idle', 2.4, tracks([
        has('spine_02') && charTrack('spine_02', [0, 1.2, 2.4], [[Xc, 1], [Xc, 4], [Xc, 1]]),
        has('Head') && charTrack('Head', [0, 1.2, 2.4], [[Xc, 0], [Xc, -3], [Xc, 0]]),
    ]));
    // attack: right arm cocks then snaps forward, spine twists in
    const Yc = new THREE.Vector3(0, 1, 0);
    const attack = new THREE.AnimationClip('attack', 0.34, tracks([
        has('upperarm_r') && charTrack('upperarm_r', [0, 0.13, 0.19, 0.34],
            [[Xc, 0], [Xc, 38], [Xc, -78], [Xc, 0]]),
        has('lowerarm_r') && charTrack('lowerarm_r', [0, 0.13, 0.19, 0.34],
            [[Xc, 0], [Xc, 30], [Xc, -12], [Xc, 0]]),
        has('spine_02') && charTrack('spine_02', [0, 0.13, 0.19, 0.34],
            [[Yc, 0], [Yc, 10], [Yc, -9], [Yc, 0]]),
    ]));
    // death: whole body tips over backwards via the pivot (no rig math needed)
    const qx = deg => new THREE.Quaternion()
        .setFromAxisAngle(Xc, deg * Math.PI / 180).toArray();
    const death = new THREE.AnimationClip('death', 0.7, [
        new THREE.QuaternionKeyframeTrack('fallPivot.quaternion', [0, 0.55, 0.7],
            [...qx(0), ...qx(-84), ...qx(-88)]),
        new THREE.VectorKeyframeTrack('fallPivot.position', [0, 0.55, 0.7],
            [0, 0, 0, 0, 0.12, 0, 0, 0.10, 0]),
    ]);

    return { group, pivot, model, bones, weaponSocket, bodyMesh,
             clips: { idle, walk, attack, death }, height };
}

// ── Procedural toon character (fallback) ──────────────────────────────────
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
function splatSprite(dmg, tier, styleHex) {
    const c = document.createElement('canvas'); c.width = c.height = 64;
    const g = c.getContext('2d');
    // A prayer-negated hit ("blocked") is a distinct outcome from a plain
    // 0-damage hitsplat — a slashed ring in the doctrine color that
    // blocked it, not a numeral, so it reads as "prevented" rather than
    // "weak hit" or a miss.
    if (tier === 'blocked') {
        const color = styleHex ?? '#ffffff';
        g.strokeStyle = '#111'; g.lineWidth = 9;
        g.beginPath(); g.arc(32, 32, 21, 0, Math.PI * 2); g.stroke(); // ink rim
        g.strokeStyle = color; g.lineWidth = 5;
        g.beginPath(); g.arc(32, 32, 21, 0, Math.PI * 2); g.stroke(); // doctrine ring
        g.beginPath(); g.moveTo(32 - 14, 32 + 14); g.lineTo(32 + 14, 32 - 14); g.stroke(); // slash
        const tex = new THREE.CanvasTexture(c);
        const sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, transparent: true, depthTest: false }));
        sp.scale.set(0.75, 0.75, 1); // a touch smaller than a real hit — a non-event, not damage
        return sp;
    }
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

// Locomotion is a 1D blend space over ground speed (wu/s): each node is a
// looping action whose weight ramps triangularly between its neighbors, and
// each moving clip's timeScale stretches so its feet keep the actual speed —
// how Unity's blend trees make the test character read as "natural".
// One-shots (attacks, hits, eat, death) overlay via LoopOnce actions that
// duck the locomotion weights while they run.
async function makeActor(st, key, colors) {
    let ch, lib = null;
    try {
        const [gltf, clips] = await Promise.all([
            loadToonCharacterGltf(),
            loadAnimLibrary().catch(e => { console.warn('toon: anim library failed, using authored clips', e); return null; }),
        ]);
        ch = buildToonCharacterFromGltf(gltf, colors);
        lib = clips;
    } catch (e) {
        console.warn('toon: glTF character failed, using procedural fallback', e);
        ch = buildToonCharacter(colors);
    }
    st.scene.add(ch.group);
    const mixer = new THREE.AnimationMixer(ch.group);

    // role → clip: full library when it loaded, else the authored minimal set
    const clips = lib
        ? Object.fromEntries(Object.entries(lib).map(([r, c]) => [r, fitClip(c, ch.bones)]))
        : { idle: ch.clips.idle, walk: ch.clips.walk,
            punchA: ch.clips.attack, swordA: ch.clips.attack, spec: ch.clips.attack,
            throw: ch.clips.attack, cast: ch.clips.attack, death: ch.clips.death };

    // blend-space nodes: [anchor speed wu/s, role]; anchors double as the
    // clip's natural speed so timeScale = speed/anchor ≈ 1 at the anchor.
    // Scaled ×1.47 with the 2.8 wu character (longer legs cover more ground
    // per cycle) — player run sits jog-leaning, enemies mostly walk.
    const nodes = [[0, 'idle'], [2.2, 'walk'], [4.7, 'jog'], [8.5, 'sprint']]
        .filter(([, r]) => clips[r]);
    const loco = nodes.map(([anchor, role]) => {
        const a = mixer.clipAction(clips[role]);
        a.setEffectiveWeight(anchor === 0 ? 1 : 0).play();
        // gait clips don't self-advance — updateActorAnim drives them all
        // from one shared phase so blended clips never mismatch feet
        if (anchor > 0) a.setEffectiveTimeScale(0);
        return { anchor, role, action: a, dur: clips[role].duration, w: anchor === 0 ? 1 : 0 };
    });

    // Ground against the ANIMATED idle, not the bind pose: library clips
    // carry the mannequin's pelvis height, and the character's slightly
    // different leg length otherwise sinks the feet below the floor plane —
    // toes visibly sliced off by the ground. Measure the foot bones in bind
    // pose, apply one idle frame, and lift the model by the difference.
    if (lib && ch.model) {
        const v = new THREE.Vector3();
        const soleY = () => {
            ch.group.updateWorldMatrix(true, true);
            let y = Infinity;
            for (const n of ['foot_l', 'foot_r', 'ball_l', 'ball_r']) {
                const b = ch.bones[n];
                if (b) y = Math.min(y, b.getWorldPosition(v).y);
            }
            return y;
        };
        const bindY = soleY();
        mixer.update(0.001); // apply the idle pose once
        const idleY = soleY();
        if (isFinite(bindY) && isFinite(idleY)) ch.model.position.y += bindY - idleY;
    }

    const actor = {
        key, ch, mixer, clips, loco, overlay: null, speedSm: 0, gaitPhase: 0, swingAlt: 0,
        pos: { wx: 0, wz: 0 }, target: { wx: 0, wz: 0 },
        facing: 0, crumbled: false, hp: 1,
        weaponMesh: null, weaponToken: 0, armorMeshes: [], armorKey: '',
        overheadSprite: null, overheadKey: null,
    };
    mixer.addEventListener('finished', e => {
        if (actor.overlay && e.action === actor.overlay.action && !actor.overlay.hold) {
            e.action.fadeOut(0.18);
            actor.overlay = null;
        }
    });
    return actor;
}

// One-shot overlay: attacks / hit reactions / eat / death.
function playOverlay(actor, role, { ts = 1, fade = 0.08, hold = false, force = true } = {}) {
    const clip = actor.clips[role];
    if (!clip || actor.crumbled && role !== 'death') return;
    if (actor.overlay) {
        if (!force) return;
        actor.overlay.action.fadeOut(0.08);
    }
    const a = actor.mixer.clipAction(clip);
    a.reset().setLoop(THREE.LoopOnce, 1);
    a.clampWhenFinished = hold;
    a.setEffectiveTimeScale(ts).setEffectiveWeight(1).fadeIn(fade).play();
    actor.overlay = { action: a, role, hold };
}

// ── StyleTelegraphSystem (shared, boss-agnostic) ──────────────────────────
// Boss Bible "prayer grammar": style changes telegraph 2 ticks ahead via
// "weapon glow / stance" — an in-world tell, not a text pop-up. Driven by
// setBattleTelegraph below; any boss that sets a forecast gets this for
// free, no boss-specific code.

// Windup pose: the same attack-role clip the real swing will use, played at
// a fraction of speed so it visibly "winds up" rather than immediately
// resolving — the actual attack (played at full speed, force:true) then
// naturally interrupts and reads as the sudden release.
function windupRoleForStyle(actor, style) {
    if (style === 'ranged') return actor.clips.throw ? 'throw' : 'swordA';
    if (style === 'magic') return actor.clips.cast ? 'cast' : 'swordA';
    actor.swingAlt ^= 1;
    return actor.swingAlt ? 'swordA' : 'swordB';
}

// Sets (rgb != null) or clears (rgb == null) a pulsing rim-outline color on
// every material in the actor's hierarchy — OutlineEffect reads
// material.userData.outlineParameters per-mesh (see OutlineEffect.js), so
// this is the "weapon glow / stance" telegraph's actual paint step.
function setActorTelegraphGlow(actor, rgb, alpha) {
    actor.ch.group.traverse(n => {
        if (!n.isMesh && !n.isSkinnedMesh) return;
        const mats = Array.isArray(n.material) ? n.material : [n.material];
        for (const m of mats) {
            if (!m) continue;
            if (rgb) m.userData.outlineParameters = { color: rgb, alpha, thickness: 0.015, keepAlive: true };
            else if (m.userData.outlineParameters) delete m.userData.outlineParameters;
        }
    });
}

// Idle stance: a modeled weapon swaps the resting loop for the library's
// sword-ready pose. Only the blend-space's anchor-0 node changes — walk/jog/
// sprint and every one-shot stay shared, and the node keeps its current
// weight so the swap never pops the locomotion blend.
function setActorStance(actor, armed) {
    const want = armed && actor.clips.swordIdle ? 'swordIdle' : 'idle';
    const n0 = actor.loco[0];
    if (!n0 || n0.anchor !== 0 || n0.role === want) return;
    const a = actor.mixer.clipAction(actor.clips[want]);
    n0.action.stop();
    a.reset().setEffectiveWeight(n0.w).play();
    n0.action = a; n0.role = want; n0.dur = actor.clips[want].duration;
}

// Put a modeled weapon in (or clear it from) the actor's right hand.
async function setActorWeapon(actor, weaponId) {
    const def = weaponId ? WEAPON_ASSETS[weaponId] : null;
    setActorStance(actor, !!def);
    const socket = actor.ch.weaponSocket;
    if (!socket) return;
    const token = ++actor.weaponToken; // supersedes any in-flight load
    if (actor.weaponMesh) { socket.remove(actor.weaponMesh); actor.weaponMesh = null; }
    if (!def) return;
    let gltf;
    try { gltf = await loadEquipGltf(def.url); }
    catch (e) { console.warn('toon: weapon load failed', weaponId, e); return; }
    if (token !== actor.weaponToken) return;
    const obj = gltf.scene.clone(true);
    obj.traverse(n => {
        if (n.isMesh) {
            const src = Array.isArray(n.material) ? n.material[0] : n.material;
            n.material = toonMat(src?.color ? `#${src.color.getHexString()}` : '#8a8a8a');
        }
    });
    // The export keeps the grip at the origin with the blade up +Y — the
    // socket's own axes — so only size needs normalizing: span def.length
    // world units after the skeleton's inherited scale.
    const size = new THREE.Box3().setFromObject(obj).getSize(new THREE.Vector3());
    const raw = Math.max(size.x, size.y, size.z) || 1;
    socket.updateWorldMatrix(true, false);
    const ws = socket.getWorldScale(new THREE.Vector3()).y || 1;
    obj.scale.multiplyScalar((def.length ?? 1.2) / (raw * ws));
    socket.add(obj);
    actor.weaponMesh = obj;
}

// Dress the actor in every armor item that has a modeled asset. The pieces
// share the character's Universal Base rig, so each part SkinnedMesh rebinds
// onto the LIVE skeleton: same bone names, and the part's own inverse-bind
// matrices are valid against it because both exports share one bind pose.
async function setActorArmor(actor, itemIds) {
    const want = (itemIds ?? []).filter(id => ARMOR_ASSETS[id]).sort();
    const key = want.join();
    if (actor.armorKey === key) return;
    actor.armorKey = key;
    for (const m of actor.armorMeshes) m.parent?.remove(m);
    actor.armorMeshes = [];
    const bones = actor.ch.bones;
    if (!actor.ch.model || !bones.pelvis) return; // procedural fallback: no universal rig
    maskBodySkin(actor.ch, want.map(id => ARMOR_REGION[id]).filter(Boolean));
    let parts;
    try { parts = await Promise.all(want.map(id => loadEquipGltf(ARMOR_ASSETS[id]))); }
    catch (e) { console.warn('toon: armor load failed', e); return; }
    if (actor.armorKey !== key) return; // superseded while loading
    for (const gltf of parts) {
        gltf.scene.traverse(src => {
            if (!src.isSkinnedMesh) return;
            const mapped = src.skeleton.bones.map(b => bones[b.name] ?? bones.root);
            const srcMat = Array.isArray(src.material) ? src.material[0] : src.material;
            const mesh = new THREE.SkinnedMesh(src.geometry,
                toonMat('#ffffff', srcMat?.map ?? null));
            mesh.material.side = srcMat?.side ?? THREE.FrontSide;
            mesh.frustumCulled = false; // skinned bounds don't track the pose
            actor.ch.model.add(mesh);
            mesh.bind(new THREE.Skeleton(mapped, src.skeleton.boneInverses), src.bindMatrix);
            actor.armorMeshes.push(mesh);
        });
    }
}

// Shared overhead-prayer renderer (M1 playtest revision — replaces the old
// left-arc protection BUTTONS with a left-edge touch strip; this is the
// in-world payoff: whichever protection/boost is active gets a small
// billboarded icon-in-a-ring above the actor's head, doctrine-colored, so
// it reads at a glance in the 3D scene itself — not just the HUD. Built
// once as a generic (actor, styleKey) pair so the same function drives both
// the player today and bosses later (Boss Bible mirror-match fights give
// the boss its own visible prayer).
const OVERHEAD_STYLES = {
    melee: { glyph: '⚔', hex: DOCTRINE_HEX.melee },
    ranged: { glyph: '➶', hex: DOCTRINE_HEX.ranged },
    magic: { glyph: '✦', hex: DOCTRINE_HEX.magic },
    piety: { glyph: '⚡', hex: '#ffd700' }, // boost prayer — "safe/reward" gold, not a combat doctrine color
};
const overheadTexCache = new Map(); // styleKey → THREE.CanvasTexture (baked once, shared by every actor)
function getOverheadTexture(styleKey) {
    if (overheadTexCache.has(styleKey)) return overheadTexCache.get(styleKey);
    const def = OVERHEAD_STYLES[styleKey];
    const size = 64;
    const c = document.createElement('canvas'); c.width = c.height = size;
    const ctx = c.getContext('2d');
    ctx.fillStyle = 'rgba(8,6,12,0.72)'; // dark plate so the icon pops on any background
    ctx.beginPath(); ctx.arc(size / 2, size / 2, size / 2 - 6, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = def.hex;
    ctx.lineWidth = 5;
    ctx.beginPath(); ctx.arc(size / 2, size / 2, size / 2 - 4, 0, Math.PI * 2); ctx.stroke(); // colored ring
    ctx.fillStyle = def.hex;
    ctx.font = 'bold 30px sans-serif';
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.fillText(def.glyph, size / 2, size / 2 + 2);
    const tex = new THREE.CanvasTexture(c);
    tex.needsUpdate = true;
    overheadTexCache.set(styleKey, tex);
    return tex;
}
// Sprite auto-billboards (always faces camera) and is parented to the
// actor's HEAD BONE, not the actor's root group — a fixed height above the
// group looked right in an idle pose but drifted badly during a crouched
// attack lunge (the group's own Y never moves, only the skeleton bends), and
// even in idle it needed re-tuning per camera zoom to clear the head without
// the same offset reading as "too far" at a different zoom. Parenting to the
// bone means it inherits the head's real per-frame position for free, the
// same way weapon/armor already ride the skeleton instead of a flat offset.
function setActorOverhead(actor, styleKey) {
    if (actor.overheadKey === styleKey) return;
    actor.overheadKey = styleKey;
    if (actor.overheadSprite) { actor.overheadSprite.parent?.remove(actor.overheadSprite); actor.overheadSprite = null; }
    if (!styleKey || !OVERHEAD_STYLES[styleKey]) return;
    const headBone = actor.ch.bones?.Head ?? actor.ch.bones?.head ?? actor.ch.group;
    // depthTest: false, same as the splat sprites — without it the icon
    // partially disappears behind/into the head mesh at some camera angles
    // instead of reading as a clean disc floating above it.
    const mat = new THREE.SpriteMaterial({ map: getOverheadTexture(styleKey), transparent: true, depthTest: false });
    const sprite = new THREE.Sprite(mat);
    sprite.renderOrder = 10;
    sprite.scale.set(0.4, 0.4, 1);
    sprite.position.set(0, 0.4, 0); // small clearance above the head bone's own origin
    headBone.add(sprite);
    actor.overheadSprite = sprite;
}

// Per-frame locomotion blending: ease the displayed speed (the sim moves at
// constant velocity — easing is what makes starts/stops read naturally),
// distribute triangular weights, duck under overlays, and advance one shared
// gait phase that every moving clip samples — cycle sync, so a blend that
// sweeps walk→jog→sprint during acceleration morphs stride length instead of
// stumbling over three clips at unrelated foot phases.
function updateActorAnim(actor, instSpeed, dt) {
    // quick off the mark, softer settle — sim velocity is a step function
    const k = 1 - Math.exp(-dt / (instSpeed > actor.speedSm ? 0.07 : 0.14));
    if (instSpeed > 0.1 && actor.speedSm < 0.1) actor.gaitPhase = 0; // fresh stride on launch
    actor.speedSm += (instSpeed - actor.speedSm) * k;
    const s = actor.speedSm;
    const damp = actor.crumbled ? 0 : actor.overlay ? (s > 0.4 ? 0.35 : 0) : 1;
    const L = actor.loco;
    // segment weights: 1 at a node's anchor, fading linearly to its neighbors
    const targets = new Array(L.length).fill(0);
    if (s <= L[0].anchor) targets[0] = 1;
    else if (s >= L[L.length - 1].anchor) targets[L.length - 1] = 1;
    else for (let i = 0; i < L.length - 1; i++)
        if (s >= L[i].anchor && s < L[i + 1].anchor) {
            const f = (s - L[i].anchor) / (L[i + 1].anchor - L[i].anchor);
            targets[i] = 1 - f; targets[i + 1] = f;
            break;
        }
    let rate = 0, wsum = 0;
    for (let i = 0; i < L.length; i++) {
        L[i].w += (targets[i] * damp - L[i].w) * k;
        L[i].action.setEffectiveWeight(L[i].w);
        if (L[i].anchor > 0) {
            // cadence: this clip's cycles/sec if feet were to track the
            // actual ground speed; the blend averages them by weight
            const ts = Math.max(0.7, Math.min(1.7, (s || L[i].anchor) / L[i].anchor));
            rate += L[i].w * (ts / L[i].dur);
            wsum += L[i].w;
        }
    }
    if (wsum > 1e-3) {
        actor.gaitPhase = (actor.gaitPhase + dt * rate / wsum) % 1;
        for (const n of L)
            if (n.anchor > 0) n.action.time = actor.gaitPhase * n.dur;
    }
}

// ── battle lifecycle ─────────────────────────────────────────────────────
async function initBattle(canvasId, opts) {
    destroyBattle(canvasId);
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;

    const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    const effect = new OutlineEffect(renderer, { defaultThickness: 0.0065, defaultColor: [0.05, 0.04, 0.03] });
    const scene = new THREE.Scene();
    scene.background = new THREE.Color('#1a2314');
    scene.fog = new THREE.Fog('#1a2314', 22, 40);

    // Long lens up close: FOV 15 with the dolly pulled back gives the flat,
    // compressed perspective of an isometric-ish tactics view while framing
    // the fight at 2x the old size.
    const camera = new THREE.PerspectiveCamera(15, 1, 0.1, 160);
    const sun = new THREE.DirectionalLight('#fff4dd', 2.8);
    sun.position.set(6, 10, 4);
    scene.add(sun, new THREE.AmbientLight('#9aa8b8', 0.9));

    const st = {
        canvas, renderer, effect, scene, camera,
        yaw: 0.6, zoom: 1, camPitch: 0.62,
        viewRot: 0, // 0 or 90: CSS view rotation of the portrait fight
        player: null, enemy: null, dotnet: opts.dotnetRef ?? null,
        splats: [], hazardQuads: new Map(), marker: null, targetTile: null,
        obstacles: [], flags: {}, projectiles: [], addMeshes: new Map(), telegraph: null,
        clock: new THREE.Clock(), raf: 0, drag: null,
        enemyId: opts.enemyId,
    };

    // ── ground: flat toon plane + ink tile grid + square edge ──
    const ground = new THREE.Mesh(
        new THREE.PlaneGeometry(60, 60), toonMat('#4a7038'));
    ground.rotation.x = -Math.PI / 2;
    // Flat scenery never reads better with an ink silhouette — skip the
    // outline pass's backface-hull draw for it (halves its draw calls).
    ground.material.userData.outlineParameters = { visible: false };
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

    // actors — textured glTF characters; the enemy gets a rust tint over the
    // texture so the two stay instantly tellable apart. suit/skin colors are
    // the untextured fallbacks.
    [st.player, st.enemy] = await Promise.all([
        makeActor(st, 'player', { tint: '#ffffff', suit: '#3e6a8e', skin: '#e0b088', shirt: '#c8b090', pants: '#3d5a35', boots: '#4a3020', scale: 1.0 }),
        makeActor(st, 'enemy',  { tint: '#e8a284', suit: '#8e4a32', skin: '#d09a70', shirt: '#8a4030', pants: '#463830', boots: '#332218', scale: 1.08 }),
    ]);
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
    // Punish window ring (m1-plan Workstream C.6): pulses gold under the
    // boss while it can't act and takes +25% damage.
    st.punishRing = mkTileOutline('#ffcc33'); st.punishRing.visible = false;
    st.punishRing.material.transparent = true;

    // Paper-grain atmosphere is applied via the CSS `.battle-scene::after`
    // overlay (SVG feTurbulence, browser-composited for free) — a second
    // WebGL full-screen render pass here would just duplicate that cost
    // every frame for no visual gain, so the scene doesn't render one.

    battles.set(canvasId, st);

    // Pointer input, mirroring voxel.js's contract exactly: the fight is
    // CSS-rotated 90° in portrait (see watchOrientation/viewRot), so every
    // client coordinate passes through the same rotation-undo the classic
    // renderer uses (clientToCanvas), and the orbit axis is screen-Y when
    // rotated. Single drag = orbit yaw; two-finger pinch = zoom + pitch;
    // tap (< 6px travel) = ground/enemy click via raycast.
    const ray = new THREE.Raycaster(), ndc = new THREE.Vector2();
    st.pointers = new Map();
    const localFrac = (clientX, clientY) => {
        const r = canvas.getBoundingClientRect();
        let fx = (clientX - r.left) / (r.width || 1);
        let fy = (clientY - r.top) / (r.height || 1);
        if (st.viewRot === 90) { const nx = fy, ny = 1 - fx; fx = nx; fy = ny; }
        return { fx, fy };
    };
    st.onDown = e => {
        st.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
        canvas.setPointerCapture?.(e.pointerId);
        if (st.pointers.size === 2) {
            const pts = [...st.pointers.values()];
            st.pinch = {
                dist0: Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y) || 1,
                midY0: (pts[0].y + pts[1].y) / 2,
                zoom0: st.zoom, pitch0: st.camPitch,
            };
            st.drag = null;
        } else if (st.pointers.size === 1) {
            st.drag = { x: e.clientX, y: e.clientY, yaw0: st.yaw, moved: 0 };
        }
    };
    st.onMove = e => {
        const p = st.pointers.get(e.pointerId);
        if (p) { p.x = e.clientX; p.y = e.clientY; }
        if (st.pointers.size >= 2 && st.pinch) {
            const pts = [...st.pointers.values()];
            const dist = Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y);
            const midY = (pts[0].y + pts[1].y) / 2;
            st.zoom = Math.max(0.4, Math.min(2.5, st.pinch.zoom0 * dist / st.pinch.dist0));
            st.camPitch = Math.max(0.25, Math.min(1.4,
                st.pinch.pitch0 + (st.pinch.midY0 - midY) * 0.004));
        } else if (st.drag && st.pointers.size === 1) {
            st.drag.moved = Math.max(st.drag.moved,
                Math.hypot(e.clientX - st.drag.x, e.clientY - st.drag.y));
            if (st.drag.moved > 6) {
                const along = st.viewRot === 90
                    ? (e.clientY - st.drag.y) : (e.clientX - st.drag.x);
                st.yaw = st.drag.yaw0 + along * 0.010;
            }
        }
    };
    st.onUp = e => {
        const wasSingle = st.pointers.size === 1;
        st.pointers.delete(e.pointerId);
        if (st.pointers.size < 2) st.pinch = null;
        const d = st.drag;
        st.drag = null;
        if (!wasSingle || !d || d.moved > 6) return;
        const { fx, fy } = localFrac(e.clientX, e.clientY);
        ndc.set(fx * 2 - 1, -(fy * 2 - 1));
        ray.setFromCamera(ndc, st.camera);
        // enemy first: hit test its meshes (recursive — glTF meshes are nested)
        const hitEnemy = ray.intersectObjects(st.enemy.ch.group.children, true).length > 0;
        if (hitEnemy && !st.enemy.crumbled) { st.dotnet?.invokeMethodAsync('OnEnemyClick'); return; }
        // swarm adds next (m1-plan Workstream C.7 targeting)
        if (st.addMeshes.size > 0) {
            const addMeshList = [...st.addMeshes.values()];
            const hit = ray.intersectObjects(addMeshList, false)[0];
            if (hit) { st.dotnet?.invokeMethodAsync('OnAddClick', hit.object.userData.id); return; }
        }
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
    st.onCancel = e => { st.pointers.delete(e.pointerId); if (st.pointers.size < 2) st.pinch = null; st.drag = null; };
    st.onWheel = e => { e.preventDefault(); st.zoom = Math.max(0.4, Math.min(2.5, st.zoom * (e.deltaY > 0 ? 0.92 : 1.08))); };
    canvas.addEventListener('pointerdown', st.onDown);
    canvas.addEventListener('pointermove', st.onMove);
    canvas.addEventListener('pointerup', st.onUp);
    canvas.addEventListener('pointercancel', st.onCancel);
    canvas.addEventListener('wheel', st.onWheel, { passive: false });

    const loop = () => {
        const dt = Math.min(0.05, st.clock.getDelta());
        const now = performance.now();
        // movement: constant-speed pursuit of the sim tile (same law as voxel.js)
        for (const actor of [st.player, st.enemy]) {
            let instSpeed = 0;
            if (!actor.crumbled) {
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
                    instSpeed = step / dt;
                } else if (actor === st.enemy || !st.flags.holdPosition) {
                    // Face the target while actively engaging it. The player
                    // only reaches this stationary branch already attacking
                    // or about to (holdPosition false means Engage() has run —
                    // see GameState's targeting model), so this doubles as
                    // "queued or attacking" without a separate flag.
                    destFacing = Math.atan2(other.pos.wx - actor.pos.wx, other.pos.wz - actor.pos.wz);
                } else {
                    // Holding position (walked away, not re-engaged): keep
                    // whatever facing the walk left the player with instead
                    // of snapping back to stare at the still-targeted enemy.
                    destFacing = actor.facing;
                }
                let da = destFacing - actor.facing;
                while (da > Math.PI) da -= Math.PI * 2;
                while (da < -Math.PI) da += Math.PI * 2;
                actor.facing += da * Math.min(1, dt * 14);
                actor.ch.group.position.set(actor.pos.wx, 0, actor.pos.wz);
                actor.ch.group.rotation.y = actor.facing;
            }
            updateActorAnim(actor, instSpeed, dt);
            // mixer ALWAYS ticks — a dead actor still needs its fall to play out
            actor.mixer.update(dt);
        }

        // camera: rigid lock on the player, orbit by yaw. 18.3 wu at FOV 15
        // frames the same scene the old 14 wu / FOV 38 did, at twice the size.
        const p = st.player.pos;
        const dist = 18.3 / st.zoom;
        st.camera.position.set(
            p.wx + Math.sin(st.yaw) * dist * Math.cos(st.camPitch),
            dist * Math.sin(st.camPitch) + 1,
            p.wz + Math.cos(st.yaw) * dist * Math.cos(st.camPitch));
        st.camera.lookAt(p.wx, 1, p.wz);
        // fog rides the dolly so zooming never washes out the arena itself
        st.scene.fog.near = dist + 8;
        st.scene.fog.far = dist + 28;

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

        // hazards pulse (scorch stays a steady safe glow, no urgency pulse)
        for (const [, q] of st.hazardQuads) {
            const d = q.userData;
            q.material.opacity = d.scorch ? 0.22
                : 0.25 + 0.2 * Math.sin(now * (!d.pool && d.t <= 1 ? 0.022 : d.pool ? 0.004 : 0.009));
        }

        // punish window ring: pulses under the boss while it can't act
        st.punishRing.visible = !!st.flags.punished && !st.enemy.crumbled;
        if (st.punishRing.visible) {
            st.punishRing.position.set(st.enemy.pos.wx, 0, st.enemy.pos.wz);
            st.punishRing.material.opacity = 0.5 + 0.35 * Math.sin(now * 0.012);
        }

        // swarm adds: small bob so they read as alive
        for (const [id, m] of st.addMeshes)
            m.position.y = 0.3 + Math.sin(now * 0.006 + id.length) * 0.05;

        // StyleTelegraphSystem: pulsing doctrine-color rim glow while a
        // telegraph is live (boss bible "weapon glow / stance").
        if (st.telegraph?.active && st.telegraph.style) {
            const rgb = DOCTRINE_RGB[st.telegraph.style] ?? [1, 1, 1];
            setActorTelegraphGlow(st.enemy, rgb, 0.55 + 0.35 * Math.sin(now * 0.014));
        } else {
            setActorTelegraphGlow(st.enemy, null);
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
    st.canvas.removeEventListener('pointercancel', st.onCancel);
    st.canvas.removeEventListener('wheel', st.onWheel);
    st.renderer.dispose();
    battles.delete(canvasId);
}

const api = {
    _battles: battles, // debug handle for Playwright probes
    initBattle, destroyBattle,
    resetBattle(canvasId) {
        const st = battles.get(canvasId);
        if (!st) return;
        for (const a of [st.player, st.enemy]) {
            a.crumbled = false; a.ch.group.visible = true;
            if (a.overlay) { a.overlay.action.stop(); a.overlay = null; }
            a.speedSm = 0; a.gaitPhase = 0;
            for (const n of a.loco) {
                n.w = n.anchor === 0 ? 1 : 0;
                n.action.reset().setEffectiveWeight(n.w).play();
            }
            if (a.ch.pivot) { a.ch.pivot.quaternion.identity(); a.ch.pivot.position.set(0, 0, 0); }
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
    // StyleTelegraphSystem entry point: t = { active, style, projectile, ticks }.
    // Fires the windup pose + (for a committed ranged/magic read) a
    // style-colored projectile on the rising edge only; the render loop owns
    // the rim-glow paint every frame from st.telegraph directly.
    setBattleTelegraph(canvasId, t) {
        const st = battles.get(canvasId);
        if (!st) return;
        const rising = t?.active && !st.telegraph?.active;
        st.telegraph = t;
        if (!rising) return;

        const style = t.style ?? 'melee';
        playOverlay(st.enemy, windupRoleForStyle(st.enemy, style), { ts: 0.35, fade: 0.15, hold: true });

        if (t.projectile && (style === 'ranged' || style === 'magic')) {
            const mesh = new THREE.Mesh(new THREE.SphereGeometry(0.16, 8, 8),
                new THREE.MeshBasicMaterial({ color: DOCTRINE_HEX[style] ?? '#ffffff' }));
            const from = new THREE.Vector3(st.enemy.pos.wx, 1.3, st.enemy.pos.wz);
            mesh.position.copy(from);
            st.scene.add(mesh);
            st.projectiles.push({
                mesh, t0: performance.now(), dur: (t.ticks ?? 2) * TILE_MS,
                from, to: new THREE.Vector3(st.player.pos.wx, 1.2, st.player.pos.wz),
            });
        }
    },
    setBattleVitals(canvasId, v) {
        const st = battles.get(canvasId);
        if (st) { st.player.hp = v.player; st.enemy.hp = v.enemy; }
    },
    setBattleWeapon(canvasId, weaponId) {
        // The id picks the swing clip family; ids in WEAPON_ASSETS also get
        // their mesh socketed into the player's right hand + a sword stance.
        const st = battles.get(canvasId);
        if (!st) return;
        st.weaponId = weaponId;
        setActorWeapon(st.player, weaponId);
    },
    setBattleEquipment(canvasId, itemIds) {
        // Armor item ids (non-weapon slots). Ids in ARMOR_ASSETS render as
        // modular outfit pieces skinned onto the player's skeleton.
        const st = battles.get(canvasId);
        if (st) setActorArmor(st.player, itemIds);
    },
    setBattleOverheads(canvasId, { player, enemy } = {}) {
        // { player, enemy } are style keys ('melee'/'range'/'magic'/'piety')
        // or null — BattleScene.razor's PlayerOverhead/NpcOverhead already
        // resolve GameState.Player.ActiveProtection/BoostPrayerActive down
        // to that key each frame. Bosses don't grant one yet (NpcOverhead
        // is always null in M1) but the renderer is generic either way.
        const st = battles.get(canvasId);
        if (!st) return;
        if (st.player) setActorOverhead(st.player, player ?? null);
        if (st.enemy) setActorOverhead(st.enemy, enemy ?? null);
    },
    // Keep viewRot in sync with device orientation — the fight is CSS-rotated
    // 90° in portrait, so taps/drags must be mapped back (same as voxel.js).
    watchOrientation(canvasId) {
        const mq = window.matchMedia('(orientation: portrait)');
        const apply = () => { const st = battles.get(canvasId); if (st) st.viewRot = mq.matches ? 90 : 0; };
        apply();
        (mq.addEventListener ? mq.addEventListener.bind(mq, 'change') : mq.addListener.bind(mq))(apply);
    },
    getMovementDebug() { return window.voxelClassic?.getMovementDebug?.() ?? {}; },
    setMovementDebug(id, t) { window.voxelClassic?.setMovementDebug?.(id, t); },
    getCameraDebug(canvasId) {
        const st = battles.get(canvasId);
        return { fov: 15, tilt: 0.3, pitch: st?.camPitch ?? 0.62, zoom: st?.zoom ?? 1 };
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
        // Tile hazards v2 (m1-plan Workstream C.4): warning fuse (red, urgent
        // at the final tick) -> pool (poison-green) -> scorch (permanent,
        // gold-safe — also the Rot Burst shelter tile).
        const st = battles.get(canvasId);
        if (!st) return;
        const want = new Map();
        for (const t of hz?.pending ?? []) want.set(`${t.x},${t.z}`, { pool: false, scorch: false, t: t.t });
        for (const t of hz?.pools ?? []) want.set(`${t.x},${t.z}`, { pool: true, scorch: false, t: 0 });
        for (const t of hz?.scorch ?? []) want.set(`${t.x},${t.z}`, { pool: false, scorch: true, t: 0 });
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
            q.material.color.set(info.scorch ? '#e8c23d' : info.pool ? '#5a7a1e' : info.t <= 1 ? '#ff5555' : '#ffd166');
        }
    },
    setBattleAdds(canvasId, adds) {
        // Swarm adds (m1-plan Workstream C.7): small placeholder blobs at
        // their tile positions, tinted by remaining HP.
        const st = battles.get(canvasId);
        if (!st) return;
        const seen = new Set();
        for (const a of adds ?? []) {
            seen.add(a.id);
            let m = st.addMeshes.get(a.id);
            if (!m) {
                m = new THREE.Mesh(new THREE.SphereGeometry(TILE * 0.22, 8, 8), toonMat('#7a9e3a'));
                m.userData = { id: a.id };
                st.scene.add(m); st.addMeshes.set(a.id, m);
            }
            m.position.x = a.x * TILE; m.position.z = a.z * TILE;
            const hpPct = Math.max(0, Math.min(100, a.hpPct ?? 100));
            m.material.color.set(hpPct <= 50 ? '#b23a3a' : '#7a9e3a');
        }
        for (const [id, m] of st.addMeshes)
            if (!seen.has(id)) { st.scene.remove(m); st.addMeshes.delete(id); }
    },
    battleEvent(canvasId, evt) {
        const st = battles.get(canvasId);
        if (!st) return;
        const now = performance.now();
        const splatOn = (actor, dmg, tier, style) => {
            const sp = splatSprite(dmg, tier, DOCTRINE_HEX[style]);
            sp.position.set(actor.pos.wx, actor.ch.height * 0.72, actor.pos.wz);
            st.scene.add(sp);
            st.splats.push({ sprite: sp, t0: now });
        };
        // Attack clip choice: spec flourish > style (throw/cast) > armed sword
        // combo (A/B alternating for variety) > unarmed jab/cross.
        const attackRole = (actor, style, weaponId, tier) => {
            if (tier === 'spec') return 'spec';
            if (style === 'ranged') return 'throw';
            if (style === 'magic') return 'cast';
            actor.swingAlt ^= 1;
            const armed = !!weaponId;
            return armed ? (actor.swingAlt ? 'swordA' : 'swordB')
                         : (actor.swingAlt ? 'punchA' : 'punchB');
        };
        // Flinches never interrupt a swing — the hit still splats, and the
        // swing reads better than a mid-swing twitch.
        const flinch = (actor, tier, dmg) => {
            if (actor.crumbled || tier === 'miss' || tier === 'poison' || tier === 'blocked') return;
            if (actor.overlay && actor.overlay.role !== 'hitA' && actor.overlay.role !== 'hitB') return;
            const big = tier === 'heavy' || tier === 'spec' || tier === 'boss';
            playOverlay(actor, big ? 'hitB' : 'hitA', { ts: 1.25, fade: 0.05 });
        };
        switch (evt.type) {
            case 'playerAttack': {
                playOverlay(st.player,
                    attackRole(st.player, evt.style, evt.weapon ?? st.weaponId, evt.tier),
                    { ts: 1.15 });
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
            case 'enemyAttack': {
                const style = evt.style ?? 'melee';
                playOverlay(st.enemy, attackRole(st.enemy, style, true, evt.tier), { ts: 1.1 });
                // Impact-resolution prayer (Global Combat Grammar): ranged/
                // magic boss attacks travel as a doctrine-colored projectile
                // — "the projectile is the primary flick cue," not a text
                // popup. Duration matches evt.ticks (the real cast->impact
                // delay from GameTickService), not a cosmetic guess, so the
                // projectile lands exactly on the tick the hitsplat does.
                if (style === 'ranged' || style === 'magic') {
                    const mesh = new THREE.Mesh(new THREE.SphereGeometry(0.14, 8, 8),
                        new THREE.MeshBasicMaterial({ color: DOCTRINE_HEX[style] ?? '#ffffff' }));
                    st.scene.add(mesh);
                    st.projectiles.push({
                        mesh, t0: now, dur: (evt.ticks ?? 2) * TILE_MS,
                        from: new THREE.Vector3(st.enemy.pos.wx, 1.3, st.enemy.pos.wz),
                        to: new THREE.Vector3(st.player.pos.wx, 1.2, st.player.pos.wz),
                    });
                }
                break;
            }
            case 'enemyHit':
                splatOn(st.enemy, evt.dmg | 0, evt.tier ?? 'normal', evt.style);
                flinch(st.enemy, evt.tier, evt.dmg | 0);
                break;
            case 'playerHit':
                splatOn(st.player, evt.dmg | 0, evt.tier ?? 'normal', evt.style);
                flinch(st.player, evt.tier, evt.dmg | 0);
                break;
            case 'playerSip':
                // Flask belt (m1-plan Workstream E) reuses the raise-to-mouth clip.
                playOverlay(st.player, 'eat', { ts: 1.3 });
                break;
            case 'perfectDodge': {
                // Perfect Dodge (m1-plan Workstream C.8): gold glint at the
                // player's feet — always a reward, never required. Reuses the
                // splat fade/rise/remove lifecycle (st.splats) for a gentle
                // sparkle-up-and-fade instead of a thrown-projectile arc.
                const ring = new THREE.Mesh(new THREE.RingGeometry(TILE * 0.1, TILE * 0.5, 16),
                    new THREE.MeshBasicMaterial({ color: '#ffd700', transparent: true, opacity: 0.85, side: THREE.DoubleSide }));
                ring.rotation.x = -Math.PI / 2;
                ring.position.set(st.player.pos.wx, 0.03, st.player.pos.wz);
                st.scene.add(ring);
                st.splats.push({ sprite: ring, t0: now });
                break;
            }
            case 'enemyDeath': case 'playerDeath': {
                const dying = evt.type === 'enemyDeath' ? st.enemy : st.player;
                dying.crumbled = true;
                playOverlay(dying, 'death', { ts: 1, fade: 0.06, hold: true });
                break;
            }
        }
    },
};

window.voxelToon = api; // probe handle (verify scripts) + explicit name

// The toon renderer IS the battle renderer — the classic voxel painter stays
// loaded solely for item icons, the character preview, and the anim editor,
// so any call outside the battle API forwards to it. (toon.js is a deferred
// module: this runs after voxel.js but before Blazor's first interop call.)
window.voxel = new Proxy(api, {
    get: (t, prop) => prop in t ? t[prop] : window.voxelClassic?.[prop],
    has: () => true,
});
