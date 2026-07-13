// GitDiary — Preview innerHTML interop.
//
// The Markdown preview <div> is JS-owned: Blazor renders it as an empty
// element and we push the rendered HTML in from C# via this bridge. This
// exists because `mermaid-interop.js` replaces the raw `<pre><code>` blocks
// with fresh <div> hosts (via `replaceWith`), which detaches nodes that
// Blazor's render tree still holds refs to. On the next re-render Blazor
// walks those orphaned refs and calls `parent.removeChild(node)` — where
// `parent` is now null → TypeError.
//
// By taking ownership of the inner HTML in JS, Blazor never diffs the
// preview's contents, so mermaid's mutations never conflict with the
// component render loop.
(function () {
    "use strict";

    function setHtml(container, html) {
        if (!container) return;
        container.innerHTML = html || "";
    }

    window.gitdiaryPreview = {
        setHtml: setHtml
    };
})();
