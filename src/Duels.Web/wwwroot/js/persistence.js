// M0 persistence service: a single string-keyed key/value IndexedDB store
// backing ISaveStore (Duels.Application) via IndexedDbSaveStore
// (Duels.Infrastructure). Deliberately game-agnostic — it knows nothing about
// SaveData or save envelopes, just "key in, JSON string out".
(function () {
    const DB_NAME = 'duels-db';
    const DB_VERSION = 1;
    const STORE = 'kv';

    let dbPromise = null;

    function openDb() {
        if (dbPromise) return dbPromise;
        dbPromise = new Promise((resolve, reject) => {
            const req = indexedDB.open(DB_NAME, DB_VERSION);
            req.onupgradeneeded = () => {
                const db = req.result;
                if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE);
            };
            req.onsuccess = () => resolve(req.result);
            req.onerror = () => reject(req.error);
        });
        return dbPromise;
    }

    window.idbGet = async (key) => {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(STORE, 'readonly');
            const req = tx.objectStore(STORE).get(key);
            req.onsuccess = () => resolve(req.result === undefined ? null : req.result);
            req.onerror = () => reject(req.error);
        });
    };

    window.idbSet = async (key, value) => {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(STORE, 'readwrite');
            tx.objectStore(STORE).put(value, key);
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    };

    window.idbDelete = async (key) => {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(STORE, 'readwrite');
            tx.objectStore(STORE).delete(key);
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    };
})();
