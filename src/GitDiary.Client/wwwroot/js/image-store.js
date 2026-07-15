// IndexedDB-backed blob store for pending (not-yet-committed) diary images.
//
// Why IndexedDB and not localStorage (which the draft store uses): localStorage caps
// at ~5 MB for the whole origin, and a single phone photo blows past that. IndexedDB
// has no such practical limit and stores the base64 payloads out of the draft budget.
//
// Values are opaque strings (JSON, possibly a vault ciphertext envelope) keyed by the
// image's absolute repo path. This module knows nothing about encryption — the .NET
// ImageService encrypts before put() and decrypts after get() when a vault is active.
window.gitdiaryImageStore = (function () {
    "use strict";

    const DB_NAME = "gitdiary";
    const STORE = "pending_images";
    let dbPromise = null;

    function open() {
        if (dbPromise) return dbPromise;
        dbPromise = new Promise((resolve, reject) => {
            const req = indexedDB.open(DB_NAME, 1);
            req.onupgradeneeded = () => {
                const db = req.result;
                if (!db.objectStoreNames.contains(STORE)) {
                    db.createObjectStore(STORE);
                }
            };
            req.onsuccess = () => resolve(req.result);
            req.onerror = () => reject(req.error);
        });
        return dbPromise;
    }

    function tx(mode, fn) {
        return open().then(db => new Promise((resolve, reject) => {
            const t = db.transaction(STORE, mode);
            const store = t.objectStore(STORE);
            const request = fn(store);
            t.oncomplete = () => resolve(request ? request.result : undefined);
            t.onerror = () => reject(t.error);
            t.onabort = () => reject(t.error);
        }));
    }

    return {
        put: function (key, value) {
            return tx("readwrite", store => store.put(value, key));
        },
        get: function (key) {
            // Returns the stored string, or null if absent.
            return tx("readonly", store => store.get(key)).then(v => v ?? null);
        },
        remove: function (key) {
            return tx("readwrite", store => store.delete(key));
        },
        keys: function () {
            return tx("readonly", store => store.getAllKeys()).then(k => k || []);
        }
    };
})();
