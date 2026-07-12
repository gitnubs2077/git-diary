// GitDiary — Mermaid interop.
// Lazy-loads /lib/mermaid/mermaid.min.js on first use, then transforms
// <pre><code class="language-mermaid">...</code></pre> blocks in the preview
// container into rendered SVG diagrams. Errors degrade to inline red panels.
//
// Theme handling: each rendered host <div> stashes both the source and the
// theme it was drawn with. On subsequent render() calls, blocks whose theme
// no longer matches are wiped and re-rendered, so a theme toggle updates
// existing diagrams without waiting for the entry to change.
(function () {
    "use strict";

    const MERMAID_SRC = "lib/mermaid/mermaid.min.js";
    let loadPromise = null;
    let currentTheme = null;
    let idCounter = 0;

    function mapTheme(theme) {
        return theme === "dark" ? "dark" : "default";
    }

    function applyMermaidConfig(mapped) {
        if (!window.mermaid) return;
        currentTheme = mapped;
        window.mermaid.initialize({
            startOnLoad: false,
            theme: mapped,
            securityLevel: "strict"
        });
    }

    function ensureLoaded(theme) {
        const wanted = mapTheme(theme);
        if (loadPromise) {
            return loadPromise.then(function () {
                if (currentTheme !== wanted) {
                    applyMermaidConfig(wanted);
                }
            });
        }
        loadPromise = new Promise(function (resolve, reject) {
            if (window.mermaid) {
                resolve();
                return;
            }
            const s = document.createElement("script");
            s.src = MERMAID_SRC;
            s.async = true;
            s.onload = function () { resolve(); };
            s.onerror = function () {
                loadPromise = null; // allow retry
                reject(new Error("Failed to load mermaid.min.js"));
            };
            document.head.appendChild(s);
        }).then(function () {
            applyMermaidConfig(wanted);
        });
        return loadPromise;
    }

    async function renderIntoHost(host, source, mapped) {
        host.classList.add("mermaid");
        host.dataset.mermaidProcessed = "true";
        host.dataset.mermaidSource = source;
        host.dataset.mermaidTheme = mapped;
        host.innerHTML = "";
        try {
            const id = "gd-mermaid-" + (++idCounter);
            const result = await window.mermaid.render(id, source, host);
            host.innerHTML = result.svg;
            if (result.bindFunctions) {
                try { result.bindFunctions(host); } catch (_) { /* ignore */ }
            }
        } catch (e) {
            const msg = (e && e.message) ? e.message : String(e);
            const err = document.createElement("pre");
            err.className = "mermaid-error";
            err.textContent = msg || "Mermaid render error";
            // Keep the original source on the error node so a subsequent theme
            // toggle can still find and retry it.
            err.dataset.mermaidProcessed = "true";
            err.dataset.mermaidSource = source;
            err.dataset.mermaidTheme = mapped;
            host.replaceWith(err);
        }
    }

    function collectFresh(container) {
        const items = [];
        const nodes = container.querySelectorAll('pre > code.language-mermaid');
        nodes.forEach(function (code) {
            const pre = code.parentElement;
            if (!pre || pre.dataset.mermaidProcessed === "true") return;
            items.push({ pre: pre, source: code.textContent || "" });
        });
        return items;
    }

    function collectStale(container, mapped) {
        const hosts = [];
        // Already-rendered diagrams and inline error panels both carry
        // data-mermaid-processed; retry either when the theme has changed.
        const nodes = container.querySelectorAll('[data-mermaid-processed="true"]');
        nodes.forEach(function (host) {
            if (host.dataset.mermaidTheme !== mapped) {
                hosts.push(host);
            }
        });
        return hosts;
    }

    async function render(container, theme) {
        if (!container) return;
        const mapped = mapTheme(theme);

        const fresh = collectFresh(container);
        const stale = collectStale(container, mapped);
        if (fresh.length === 0 && stale.length === 0) return;

        try {
            await ensureLoaded(theme);
        } catch (e) {
            const msg = "Mermaid failed to load: " + ((e && e.message) || e);
            fresh.forEach(function (item) {
                const err = document.createElement("pre");
                err.className = "mermaid-error";
                err.textContent = msg;
                item.pre.replaceWith(err);
            });
            return;
        }
        if (!window.mermaid) return;
        if (currentTheme !== mapped) {
            applyMermaidConfig(mapped);
        }

        // Sequential renders keep the id counter deterministic and avoid
        // mermaid stomping on shared internal state during concurrent runs.
        for (let i = 0; i < fresh.length; i++) {
            const host = document.createElement("div");
            fresh[i].pre.replaceWith(host);
            await renderIntoHost(host, fresh[i].source, mapped);
        }

        for (let i = 0; i < stale.length; i++) {
            const oldHost = stale[i];
            const source = oldHost.dataset.mermaidSource || "";
            // Rebuild a fresh <div> host so any error-panel <pre> gets
            // promoted back to a real mermaid container.
            const host = document.createElement("div");
            oldHost.replaceWith(host);
            await renderIntoHost(host, source, mapped);
        }
    }

    function setTheme(theme) {
        const mapped = mapTheme(theme);
        if (!window.mermaid) {
            currentTheme = mapped;
            return;
        }
        if (currentTheme === mapped) return;
        applyMermaidConfig(mapped);
    }

    window.gitdiaryMermaid = {
        render: render,
        setTheme: setTheme,
        ensureLoaded: ensureLoaded
    };
})();
