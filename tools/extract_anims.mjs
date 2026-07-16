// Trim the Quaternius Universal Animation Library GLBs down to the clips the
// battle renderer uses, dropping the mannequin mesh/skin/materials so only
// the skeleton + animation samplers remain (7.8MB → ~100s of KB each).
//
// The library mannequin shares the Universal Base Characters skeleton, so the
// extracted clips drive assets/models/superhero.gltf directly by bone name.
//
// Usage (deps are not vendored — install anywhere and point NODE_PATH at it):
//   npm i @gltf-transform/core @gltf-transform/functions
//   NODE_PATH=<that>/node_modules node tools/extract_anims.mjs
import { NodeIO } from '@gltf-transform/core';
import { prune, resample } from '@gltf-transform/functions';

const KEEP = {
    'resources/assets/Universal Animation Library[Standard]/Unreal-Godot/UAL1_Standard.glb': {
        out: 'src/Duels.Web/wwwroot/assets/models/anims1.glb',
        clips: ['Idle_Loop', 'Walk_Loop', 'Jog_Fwd_Loop', 'Sprint_Loop',
                'Punch_Jab', 'Punch_Cross', 'Sword_Attack', 'Sword_Idle',
                'Death01', 'Hit_Chest', 'Hit_Head',
                'Spell_Simple_Shoot', 'Roll'],
    },
    'resources/assets/Universal Animation Library 2[Standard]/Unreal-Godot/UAL2_Standard.glb': {
        out: 'src/Duels.Web/wwwroot/assets/models/anims2.glb',
        clips: ['Consume', 'Sword_Regular_A', 'Sword_Regular_B',
                'Melee_Hook', 'Hit_Knockback', 'OverhandThrow', 'Sword_Block'],
    },
};

const io = new NodeIO();
for (const [src, { out, clips }] of Object.entries(KEEP)) {
    const doc = await io.read(src);
    const root = doc.getRoot();
    const found = [];
    // Animation.dispose() leaves its channels/samplers (and through them the
    // baked accessors) alive — dispose the whole chain or prune keeps ~90%
    // of the buffer.
    const disposeAnim = anim => {
        for (const ch of anim.listChannels()) ch.dispose();
        for (const s of anim.listSamplers()) s.dispose();
        anim.dispose();
    };
    for (const anim of root.listAnimations()) {
        if (clips.includes(anim.getName())) found.push(anim.getName());
        else disposeAnim(anim);
    }
    const missing = clips.filter(c => !found.includes(c));
    if (missing.length) throw new Error(`${src}: missing clips ${missing}`);
    for (const mesh of root.listMeshes()) mesh.dispose();
    for (const skin of root.listSkins()) skin.dispose();
    for (const tex of root.listTextures()) tex.dispose();
    // Bones only ever rotate in these clips — scale is always 1 and
    // translation only matters on the pelvis (bob / crouch). Dropping the
    // rest removes two thirds of the baked samplers.
    for (const anim of root.listAnimations())
        for (const ch of anim.listChannels()) {
            const path = ch.getTargetPath(), node = ch.getTargetNode()?.getName();
            if (path === 'scale' || (path === 'translation' && node !== 'pelvis')) {
                const s = ch.getSampler();
                ch.dispose(); s?.dispose();
            }
        }
    await doc.transform(resample(), prune({ keepLeaves: false }));
    await io.write(out, doc);
    console.log(`${out}: ${found.length} clips`);
}
