// KILL SWITCH (M1 in active development, 2026-07): the battle-asset
// precache below this comment used to cache-first the toon renderer's
// model/equip/texture files under a hand-bumped CACHE_VERSION string.
// During active development those files change practically every commit,
// and a hand-bumped version is one "forgot to bump it" away from serving a
// stale build to a real device with no way to clear it (mobile browsers
// don't offer an easy per-site cache-clear the way desktop devtools do —
// this bit a playtester on mobile). Rather than switch to an
// auto-versioned scheme (e.g. stamping the git SHA into the cache name at
// build time), the simplest fix for a phase where content is still
// churning is to just not cache these assets at all: every request goes
// straight to the network, same as if there were no service worker. This
// also self-heals anyone who already has the old caching version
// installed — install/activate below purge every cache this origin owns
// and unregister the worker so subsequent loads skip service-worker
// interception entirely (no fetch handler is registered).
// Revisit once M1's assets stabilize: worth reintroducing cache-first with
// a version that's actually tied to the content (a build-time git-SHA
// stamp, not a manually-bumped string) rather than deleting this outright.
//
// No 'fetch' listener is registered below — per spec, that alone means this
// worker never intercepts a single request once active, so it's safe to
// stay installed indefinitely as a permanent no-op rather than unregister
// itself (which would just make the browser re-run this same install →
// activate → cleanup dance on every visit, since index.html still calls
// register() unconditionally on load).
self.addEventListener('install', () => self.skipWaiting());

self.addEventListener('activate', e => {
    e.waitUntil(
        caches.keys().then(keys => {
            if (keys.length === 0) return; // steady state: nothing stale to flush, no reload needed
            return Promise.all(keys.map(k => caches.delete(k)))
                .then(() => self.clients.matchAll())
                .then(clients => clients.forEach(c => c.navigate(c.url))); // one-time reload past the now-flushed stale assets
        }),
    );
});
