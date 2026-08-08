// Build a same-origin blob: URL for a PDF's bytes so it can be framed by <iframe> and
// shown in the browser's native PDF viewer. A blob: URL is required because the CSP
// forbids <embed>/<object> (object-src 'none') and framing data: URLs (default-src
// 'self'); `frame-src 'self' blob:` allows exactly this.
window.gitdiaryPdf = (function () {
    "use strict";
    return {
        toBlobUrl: function (base64, mime) {
            var bin = atob(base64);
            var bytes = new Uint8Array(bin.length);
            for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
            var blob = new Blob([bytes], { type: mime || "application/pdf" });
            return URL.createObjectURL(blob);
        },
        revoke: function (url) {
            try { URL.revokeObjectURL(url); } catch (e) { /* already gone */ }
        }
    };
})();
