// Register the PWA service worker after first paint so it never competes
// with the WASM boot for bandwidth. Fails silently on unsupported UAs.
if ('serviceWorker' in navigator) {
    window.addEventListener('load', function () {
        navigator.serviceWorker.register('service-worker.js').catch(function () { /* ignore */ });
    });
}
