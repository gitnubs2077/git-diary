// Dev-time service worker: no offline caching. Passes every request straight to
// the network so hot reload and iterative changes are never served stale.
// The published build swaps this for `service-worker.published.js`.
self.addEventListener('fetch', () => { });
