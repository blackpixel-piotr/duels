// window.voxel dispatcher: Blazor keeps calling voxel.* unchanged; battle
// calls route to the classic voxel renderer or the toon (WebGL) renderer
// per battle, chosen at initBattle time from localStorage['duels_renderer']
// ('voxel' default | 'toon'). Everything non-battle (previews, icons, anim
// editor) always goes to the classic renderer.
//
// Backends resolve lazily at CALL time: toon.js is an ES module (deferred),
// so it may attach window.voxelToon after this classic script runs — but
// always before Blazor's first interop call.
(function () {
    const BATTLE_FNS = new Set([
        'initBattle', 'destroyBattle', 'resetBattle', 'setBattleEnemy',
        'setBattleFlags', 'setBattleVitals', 'setBattleWeapon',
        'setBattlePositions', 'setBattleObstacles', 'setBattleHazards',
        'setBattleOverheads', 'battleEvent', 'watchOrientation',
        'getCameraDebug', 'setCameraDebug', 'getMovementDebug', 'setMovementDebug',
    ]);
    const chosen = new Map(); // canvasId → backend for the mounted battle

    const classic = () => window.voxelClassic;
    const pick = canvasId => {
        if (chosen.has(canvasId)) return chosen.get(canvasId);
        // Not a mounted battle (yet) — route by the current flag so calls
        // that can precede initBattle (e.g. getMovementDebug) still land.
        return localStorage.getItem('duels_renderer') === 'toon' && window.voxelToon
            ? window.voxelToon : classic();
    };

    window.voxel = new Proxy({}, {
        get(_, prop) {
            return (...args) => {
                let backend = classic();
                if (BATTLE_FNS.has(prop)) {
                    const canvasId = args[0];
                    if (prop === 'initBattle') {
                        backend = localStorage.getItem('duels_renderer') === 'toon' && window.voxelToon
                            ? window.voxelToon : classic();
                        chosen.set(canvasId, backend);
                    } else {
                        backend = pick(canvasId);
                        if (prop === 'destroyBattle') chosen.delete(canvasId);
                    }
                }
                return backend?.[prop]?.(...args);
            };
        },
        has(_, prop) { return true; },
    });

    // Hub STYLE toggle: flip the flag, return the new label.
    window.toggleRendererStyle = () => {
        const next = localStorage.getItem('duels_renderer') === 'toon' ? 'voxel' : 'toon';
        localStorage.setItem('duels_renderer', next);
        return next;
    };
    window.getRendererStyle = () => localStorage.getItem('duels_renderer') === 'toon' ? 'toon' : 'voxel';
})();
