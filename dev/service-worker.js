// Battle-asset cache: the toon renderer's character model, animation
// library, equip meshes/textures, and the vendored three.js libs are ~8MB
// combined and never change between sessions, but nothing about them is
// fingerprinted (unlike Blazor's own .wasm/.dll resources, which the
// framework already caches itself) — a fresh visit re-downloads and
// re-decodes all of it, which is most of the multi-second wait before the
// TEST FIGHT character is ready. Cache-first here skips the network
// entirely on repeat visits. Bump CACHE_VERSION whenever a file in
// ASSET_URLS changes on disk — that's what forces a clean re-fetch instead
// of serving a stale cached copy forever.
const CACHE_VERSION = 'duels-assets-v2';
const ASSET_URLS = [
    'data/asset-manifest.json',
    'assets/models/superhero.gltf',
    'assets/models/Superhero_Male_FullBody.bin',
    'assets/models/T_Eye_Brown.png',
    'assets/models/T_Hair_1_BaseColor.png',
    'assets/models/T_Superhero_Male_Dark.png',
    'assets/models/anims1.glb',
    'assets/models/anims2.glb',
    'assets/models/equip/sword.glb',
    'assets/models/equip/Male_Ranger_Head_Hood.gltf',
    'assets/models/equip/Male_Ranger_Head_Hood.bin',
    'assets/models/equip/Male_Ranger_Body.gltf',
    'assets/models/equip/Male_Ranger_Body.bin',
    'assets/models/equip/Male_Ranger_Legs.gltf',
    'assets/models/equip/Male_Ranger_Legs.bin',
    'assets/models/equip/Male_Ranger_Feet_Boots.gltf',
    'assets/models/equip/Male_Ranger_Feet_Boots.bin',
    'assets/models/equip/Male_Ranger_Arms.gltf',
    'assets/models/equip/Male_Ranger_Arms.bin',
    'assets/models/equip/Male_Ranger_Acc_Pauldron.gltf',
    'assets/models/equip/Male_Ranger_Acc_Pauldron.bin',
    'assets/models/equip/T_Ranger_BaseColor.png',
    'assets/models/equip/T_Regular_Male_Dark_BaseColor.png',
    'lib/three.module.min.js',
    'lib/OutlineEffect.js',
    'lib/GLTFLoader.js',
    'lib/SkeletonUtils.js',
    'lib/BufferGeometryUtils.js',
    'lib/CCDIKSolver.js',
];

// Relative URLs resolve against this script's own location, so this works
// unmodified whether deployed at the site root or under /duels/dev/.
self.addEventListener('install', e => {
    e.waitUntil(
        caches.open(CACHE_VERSION)
            .then(cache => cache.addAll(ASSET_URLS))
            .then(() => self.skipWaiting())
            .catch(err => console.warn('duels sw: precache failed', err)),
    );
});

self.addEventListener('activate', e => {
    e.waitUntil(
        caches.keys()
            .then(keys => Promise.all(keys.filter(k => k !== CACHE_VERSION).map(k => caches.delete(k))))
            .then(() => self.clients.claim()),
    );
});

self.addEventListener('fetch', e => {
    const path = new URL(e.request.url).pathname;
    if (!ASSET_URLS.some(p => path.endsWith(p))) return; // not ours — let the browser handle it normally
    e.respondWith(
        caches.match(e.request).then(cached => cached ?? fetch(e.request).then(res => {
            const copy = res.clone();
            caches.open(CACHE_VERSION).then(cache => cache.put(e.request, copy));
            return res;
        })),
    );
});
