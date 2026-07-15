// GitDiary vault — password-derived encryption for the config (the GitHub PAT) and
// the local draft cache (which holds diary text).
//
// The password is stretched with PBKDF2 into a 256-bit AES-GCM key. That key is a
// NON-EXTRACTABLE CryptoKey held only in this module's memory — it is never written
// to localStorage, never serialized, and never crosses into .NET. Locking (an
// explicit lock, or simply reloading the tab) drops it, at which point the stored
// ciphertext is unreadable until the correct password re-derives the same key.
//
// This is real at-rest protection: without the password, the PAT cannot be
// decrypted (so the app cannot reach GitHub) and cached drafts cannot be read.
// The tradeoff the user accepted: the password is the only key, so losing it means
// re-entering the PAT (the diary itself lives in the GitHub repo and is never lost).
(function () {
    // OWASP 2023 guidance for PBKDF2-HMAC-SHA256. Web Crypto runs this natively, so
    // even 600k iterations is well under a second on unlock — a one-time cost.
    const PBKDF2_ITERATIONS = 600000;
    const SALT_BYTES = 16;
    const IV_BYTES = 12; // 96-bit nonce, the AES-GCM standard

    const enc = new TextEncoder();
    const dec = new TextDecoder();

    // The derived AES-GCM key, or null when locked. Module-scoped; unreachable from
    // outside this IIFE except through the encrypt/decrypt methods below.
    let cachedKey = null;

    function b64encode(buffer) {
        const bytes = new Uint8Array(buffer);
        let binary = '';
        for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
        return btoa(binary);
    }

    function b64decode(b64) {
        const binary = atob(b64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes;
    }

    async function deriveKey(password, saltBytes, iterations) {
        const baseKey = await crypto.subtle.importKey(
            'raw', enc.encode(password), 'PBKDF2', false, ['deriveKey']);
        return crypto.subtle.deriveKey(
            { name: 'PBKDF2', salt: saltBytes, iterations, hash: 'SHA-256' },
            baseKey,
            { name: 'AES-GCM', length: 256 },
            false,                    // non-extractable — cannot be read back out
            ['encrypt', 'decrypt']);
    }

    window.gitdiaryVault = {
        // Derive a fresh key from a NEW password and a random salt, cache it, and
        // return the salt + iteration count for the caller to persist alongside the
        // ciphertext. Used when setting or changing the password.
        async deriveNew(password) {
            const salt = crypto.getRandomValues(new Uint8Array(SALT_BYTES));
            cachedKey = await deriveKey(password, salt, PBKDF2_ITERATIONS);
            return { salt: b64encode(salt), iterations: PBKDF2_ITERATIONS };
        },

        // Derive the key from an EXISTING salt/iterations and cache it. This does NOT
        // by itself verify the password — verification happens when the caller tries
        // to decrypt (AES-GCM's auth tag fails on a wrong key). Always returns true.
        async deriveExisting(password, saltB64, iterations) {
            const salt = b64decode(saltB64);
            cachedKey = await deriveKey(password, salt, iterations);
            return true;
        },

        isUnlocked() {
            return cachedKey !== null;
        },

        lock() {
            cachedKey = null;
        },

        // Encrypt a UTF-8 string with the cached key. Returns { iv, ct } as base64.
        // A fresh random IV is generated per call (never reuse an IV with GCM).
        async encrypt(plaintext) {
            if (!cachedKey) throw new Error('gitdiaryVault: locked');
            const iv = crypto.getRandomValues(new Uint8Array(IV_BYTES));
            const ct = await crypto.subtle.encrypt(
                { name: 'AES-GCM', iv }, cachedKey, enc.encode(plaintext));
            return { iv: b64encode(iv), ct: b64encode(ct) };
        },

        // Decrypt { iv, ct } base64 with the cached key. Returns the plaintext string,
        // or null if authentication fails — i.e. wrong password or tampered data.
        async decrypt(ivB64, ctB64) {
            if (!cachedKey) throw new Error('gitdiaryVault: locked');
            try {
                const plaintext = await crypto.subtle.decrypt(
                    { name: 'AES-GCM', iv: b64decode(ivB64) }, cachedKey, b64decode(ctB64));
                return dec.decode(plaintext);
            } catch (_) {
                return null;
            }
        }
    };
})();
