// Markdown formatting toolbar (+ image attach) for the diary <textarea>.
//
// Every text mutation goes through `document.execCommand("insertText", ...)` on the
// focused textarea. That is what preserves the browser's native undo/redo stack:
// assigning `el.value = ...` directly is NOT an undoable operation in any browser,
// so a user pressing Ctrl+Z after clicking Bold would wipe out every keystroke back
// to the last checkpoint. `insertText` also fires the `input` event itself, which is
// what Blazor's `@bind:event="oninput"` listens for — so the edit flows into
// DiaryStore.CurrentContent through the exact same path as a keystroke, and Blazor
// treats it as user input (it does not rewrite the value / reset the caret on the
// next render). Do not set CurrentContent from C# instead; that round-trip would
// clobber the selection.
//
// All logic lives here (not in C#) because it is coupled to the live
// selectionStart/selectionEnd of the DOM element and to the browser-native undo
// history, which only responds to `document.execCommand` on the focused element.
//
// `document.execCommand("insertText")` is marked "deprecated" in MDN but is the only
// way to programmatically insert text into a <textarea> while preserving the undo
// stack; every major browser still ships it for exactly this case. If a browser ever
// refuses it, replaceRange() returns false and the action becomes a no-op rather than
// silently regressing undo.
window.gitdiaryEditor = (function () {
    "use strict";

    // Replace [start, end) in the focused textarea with `text`, preserving undo.
    // Returns false if the browser refused the command — callers then no-op.
    function replaceRange(el, start, end, text) {
        el.focus();
        el.setSelectionRange(start, end);
        // insertText fires a trusted "input" event (bubbles), so Blazor's
        // @bind:event="oninput" picks it up without us dispatching one.
        try {
            return document.execCommand("insertText", false, text);
        } catch (e) {
            return false;
        }
    }

    function reselect(el, start, end) {
        try { el.setSelectionRange(start, end); }
        catch (e) { /* element detached or not focusable — ignore */ }
    }

    function select(el, start, end) {
        el.focus();
        el.setSelectionRange(start, end);
        // Defense-in-depth: should any re-render rewrite the value and reset the
        // caret/scroll to the end, re-apply the selection after it. rAF restores it
        // before the next paint (no flash) when focused; setTimeout is the fallback
        // for when rAF is throttled (backgrounded tab).
        requestAnimationFrame(function () { reselect(el, start, end); });
        setTimeout(function () { reselect(el, start, end); }, 0);
    }

    // Wrap the selection with `before`/`after`. Toggles off if the selection is
    // already wrapped by exactly these markers. With no selection, inserts the
    // placeholder pre-selected so the user can type over it.
    function wrapToggle(el, before, after, placeholder) {
        const val = el.value;
        const s = el.selectionStart;
        const e = el.selectionEnd;
        const inner = val.slice(s, e);

        // Already wrapped (markers just outside the selection) → unwrap by replacing
        // the marker+inner+marker span with just the inner text.
        if (inner.length > 0 &&
            val.slice(s - before.length, s) === before &&
            val.slice(e, e + after.length) === after) {
            if (!replaceRange(el, s - before.length, e + after.length, inner)) return;
            select(el, s - before.length, s - before.length + inner.length);
            return;
        }

        // Selection already contains its own markers → unwrap them.
        if (inner.startsWith(before) && inner.endsWith(after) &&
            inner.length >= before.length + after.length) {
            const stripped = inner.slice(before.length, inner.length - after.length);
            if (!replaceRange(el, s, e, stripped)) return;
            select(el, s, s + stripped.length);
            return;
        }

        const text = inner.length > 0 ? inner : (placeholder || "");
        if (!replaceRange(el, s, e, before + text + after)) return;
        // Select the inner text so repeated formatting / typing-over works.
        select(el, s + before.length, s + before.length + text.length);
    }

    // Expand the selection to whole lines, then toggle a line-start prefix on each.
    function linePrefixToggle(el, prefix) {
        const val = el.value;
        const s = el.selectionStart;
        const e = el.selectionEnd;
        const lineStart = val.lastIndexOf("\n", s - 1) + 1;
        let lineEnd = val.indexOf("\n", e);
        if (lineEnd === -1) lineEnd = val.length;

        const lines = val.slice(lineStart, lineEnd).split("\n");
        const allHave = lines.every(l => l.startsWith(prefix));
        const out = lines.map(l => allHave ? l.slice(prefix.length) : prefix + l).join("\n");

        if (!replaceRange(el, lineStart, lineEnd, out)) return;
        select(el, lineStart, lineStart + out.length);
    }

    // Ordered list: number each selected line sequentially, or strip if all numbered.
    function orderedList(el) {
        const val = el.value;
        const s = el.selectionStart;
        const e = el.selectionEnd;
        const lineStart = val.lastIndexOf("\n", s - 1) + 1;
        let lineEnd = val.indexOf("\n", e);
        if (lineEnd === -1) lineEnd = val.length;

        const lines = val.slice(lineStart, lineEnd).split("\n");
        const numbered = /^\d+\.\s/;
        const allHave = lines.every(l => numbered.test(l));
        const out = lines
            .map((l, i) => allHave ? l.replace(numbered, "") : `${i + 1}. ${l}`)
            .join("\n");

        if (!replaceRange(el, lineStart, lineEnd, out)) return;
        select(el, lineStart, lineStart + out.length);
    }

    // Cycle the current line's heading level: none → # → ## → ### → none.
    function cycleHeading(el) {
        const val = el.value;
        const s = el.selectionStart;
        const lineStart = val.lastIndexOf("\n", s - 1) + 1;
        let lineEnd = val.indexOf("\n", s);
        if (lineEnd === -1) lineEnd = val.length;

        const line = val.slice(lineStart, lineEnd);
        const m = line.match(/^(#{1,3})\s/);
        const level = m ? m[1].length : 0;
        const body = m ? line.slice(m[0].length) : line;
        const next = level >= 3 ? 0 : level + 1;
        const out = next === 0 ? body : "#".repeat(next) + " " + body;

        if (!replaceRange(el, lineStart, lineEnd, out)) return;
        const caret = lineStart + out.length;
        select(el, caret, caret);
    }

    // Insert a link. Selection becomes the link text; caret lands inside the URL.
    function insertLink(el) {
        const val = el.value;
        const s = el.selectionStart;
        const e = el.selectionEnd;
        const text = val.slice(s, e) || "link text";
        if (!replaceRange(el, s, e, `[${text}](url)`)) return;
        const urlStart = s + text.length + 3; // past "[text]("
        select(el, urlStart, urlStart + 3);   // selects "url"
    }

    // Fenced code block wrapping the selection (or a placeholder).
    function codeBlock(el) {
        const val = el.value;
        const s = el.selectionStart;
        const e = el.selectionEnd;
        const body = val.slice(s, e) || "code";
        // Ensure the fences sit on their own lines.
        const lead = (s > 0 && val[s - 1] !== "\n") ? "\n" : "";
        const snippet = `${lead}\`\`\`\n${body}\n\`\`\`\n`;
        if (!replaceRange(el, s, e, snippet)) return;
        const bodyStart = s + lead.length + 4; // past leading \n + "```\n"
        select(el, bodyStart, bodyStart + body.length);
    }

    function insertTable(el) {
        const val = el.value;
        const s = el.selectionStart;
        const e = el.selectionEnd;
        const lead = (s > 0 && val[s - 1] !== "\n") ? "\n" : "";
        const snippet =
            lead +
            "| Column A | Column B |\n" +
            "| --- | --- |\n" +
            "| Cell 1 | Cell 2 |\n";
        if (!replaceRange(el, s, e, snippet)) return;
        const caret = s + lead.length + 2; // just inside the first cell "| "
        select(el, caret, caret + 8);      // selects "Column A"
    }

    const handlers = {
        heading: cycleHeading,
        bold: el => wrapToggle(el, "**", "**", "bold text"),
        italic: el => wrapToggle(el, "*", "*", "italic text"),
        // Underline renders via Markdig's Inserted extension (++text++ → <ins>);
        // Markdown has no native underline and DisableHtml() escapes raw <u>.
        underline: el => wrapToggle(el, "++", "++", "underline text"),
        strike: el => wrapToggle(el, "~~", "~~", "strikethrough"),
        code: el => wrapToggle(el, "`", "`", "code"),
        ul: el => linePrefixToggle(el, "- "),
        ol: orderedList,
        // GFM task list: toggles a "- [ ] " checkbox prefix on each selected line.
        tasklist: el => linePrefixToggle(el, "- [ ] "),
        quote: el => linePrefixToggle(el, "> "),
        codeblock: codeBlock,
        link: insertLink,
        table: insertTable,
    };

    // --- Image attachment ------------------------------------------------

    // Read a File/Blob into raw base64 (no data: prefix), which is what the
    // GitHub Contents API and our IndexedDB store both want.
    function readAsBase64(file) {
        return new Promise((resolve, reject) => {
            const r = new FileReader();
            r.onload = () => {
                const s = String(r.result || "");
                const comma = s.indexOf(",");
                resolve(comma >= 0 ? s.slice(comma + 1) : s);
            };
            r.onerror = () => reject(r.error);
            r.readAsDataURL(file);
        });
    }

    function describe(file, base64) {
        return { name: file.name || "", mime: file.type || "", base64: base64 };
    }

    // --- Compression -----------------------------------------------------
    // A >3 MB image is downscaled + re-encoded IN THE BROWSER before it is ever
    // stored or uploaded, so the diary repo doesn't accumulate multi-megabyte
    // originals. Anything already small, plus GIFs (animation would be lost) and
    // SVGs (vector/text — rasterizing would wreck them), passes through untouched.
    // Every failure path falls back to the original bytes: compression is an
    // optimization, never a gate on attaching the image.
    const COMPRESS_ABOVE = 3 * 1024 * 1024; // only touch images larger than this
    const MAX_EDGE = 2048;                   // clamp the longest side to this many px
    const TARGET_BYTES = 2 * 1024 * 1024;    // stop lowering quality once under this
    const QUALITY_STEPS = [0.85, 0.75, 0.65, 0.55];

    // WebP keeps transparency AND compresses better than JPEG, but not every engine
    // can ENCODE it (toBlob silently yields PNG/null). Probe once and cache.
    let _webpOk = null;
    function canEncodeWebp() {
        if (_webpOk === null) {
            try {
                const c = document.createElement("canvas");
                c.width = c.height = 1;
                _webpOk = c.toDataURL("image/webp").indexOf("data:image/webp") === 0;
            } catch { _webpOk = false; }
        }
        return _webpOk;
    }

    function loadBitmap(file) {
        if (window.createImageBitmap) return createImageBitmap(file);
        return new Promise((resolve, reject) => {
            const url = URL.createObjectURL(file);
            const img = new Image();
            img.onload = () => { URL.revokeObjectURL(url); resolve(img); };
            img.onerror = (e) => { URL.revokeObjectURL(url); reject(e); };
            img.src = url;
        });
    }

    function canvasToBlob(canvas, type, quality) {
        return new Promise(resolve => canvas.toBlob(resolve, type, quality));
    }

    function swapExtension(name, mime) {
        const ext = mime === "image/webp" ? "webp" : "jpg";
        const dot = (name || "").lastIndexOf(".");
        const base = dot > 0 ? name.slice(0, dot) : (name || "image");
        return base + "." + ext;
    }

    async function compressIfNeeded(file) {
        const passthrough = async () => describe(file, await readAsBase64(file));

        if (!file.type || file.type.indexOf("image/") !== 0 ||
            file.type === "image/gif" || file.type === "image/svg+xml" ||
            file.size <= COMPRESS_ABOVE) {
            return passthrough();
        }

        try {
            const bmp = await loadBitmap(file);
            const w0 = bmp.width, h0 = bmp.height;
            const scale = Math.min(1, MAX_EDGE / Math.max(w0, h0));
            const w = Math.max(1, Math.round(w0 * scale));
            const h = Math.max(1, Math.round(h0 * scale));

            const outType = canEncodeWebp() ? "image/webp" : "image/jpeg";

            const canvas = document.createElement("canvas");
            canvas.width = w;
            canvas.height = h;
            const ctx = canvas.getContext("2d");
            // JPEG has no alpha channel — without a matte, transparent pixels encode
            // as black. Paint white underneath first. WebP keeps alpha, so skip it there.
            if (outType === "image/jpeg") {
                ctx.fillStyle = "#ffffff";
                ctx.fillRect(0, 0, w, h);
            }
            ctx.drawImage(bmp, 0, 0, w, h);
            if (bmp.close) bmp.close();
            let best = null;
            for (const q of QUALITY_STEPS) {
                const blob = await canvasToBlob(canvas, outType, q);
                if (!blob) break;
                best = blob;
                if (blob.size <= TARGET_BYTES) break;
            }

            // Encoding failed, or somehow didn't shrink it — keep the pristine original.
            if (!best || best.size >= file.size) return passthrough();

            const base64 = await readAsBase64(best);
            try {
                console.info("[GitDiary] compressed image " +
                    Math.round(file.size / 1024) + "KB -> " + Math.round(best.size / 1024) +
                    "KB (" + w0 + "x" + h0 + " -> " + w + "x" + h + ", " + outType + ")");
            } catch { }
            return { name: swapExtension(file.name, outType), mime: outType, base64 };
        } catch {
            return passthrough();
        }
    }

    // Open the OS file picker and return {name, mime, base64} for the chosen image,
    // or null if the user cancels. Used by the toolbar image button.
    function pickImage() {
        return new Promise((resolve) => {
            const input = document.createElement("input");
            input.type = "file";
            input.accept = "image/*";
            input.style.display = "none";
            let settled = false;
            const done = (v) => { if (!settled) { settled = true; input.remove(); resolve(v); } };
            input.addEventListener("change", async () => {
                const file = input.files && input.files[0];
                if (!file) return done(null);
                try { done(await compressIfNeeded(file)); }
                catch { done(null); }
            });
            // Fired by modern browsers when the picker is dismissed with no selection.
            input.addEventListener("cancel", () => done(null));
            document.body.appendChild(input);
            input.click();
        });
    }

    // --- Rich paste: convert pasted HTML → Markdown ---------------------------
    // Pasting from a web page / doc puts an HTML fragment on the clipboard. A
    // <textarea> can only hold plain text, so the browser would drop that to
    // unformatted text. Instead we convert the HTML to Markdown (Turndown, vendored
    // in wwwroot/js) and insert THAT — so **bold**, links, headings, lists, etc.
    // survive as the editor's own native format.
    let _td = null;
    function getTurndown() {
        if (_td) return _td;
        if (typeof TurndownService === "undefined") return null; // library missing → plain paste
        const td = new TurndownService({
            headingStyle: "atx",
            hr: "---",
            bulletListMarker: "-",
            codeBlockStyle: "fenced",
            emDelimiter: "*",       // *italic*  — matches the toolbar
            strongDelimiter: "**",  // **bold**
            linkStyle: "inlined"
        });
        // GitHub tables, task lists ([ ] / [x]), and strikethrough.
        if (typeof turndownPluginGfm !== "undefined") {
            td.use(turndownPluginGfm.gfm);
        }
        // Override the GFM strikethrough rule (it emits a single "~", which Markdig
        // reads as *subscript*). addRule prepends, so this wins over the plugin's.
        td.addRule("strikethroughDouble", {
            filter: ["del", "s", "strike"],
            replacement: function (content) {
                return content ? "~~" + content + "~~" : "";
            }
        });

        // Underline. Markdown has no native underline; this editor writes ++text++
        // (Markdig EmphasisExtras → <ins>). Cover <u>, <ins>, and inline
        // text-decoration:underline (Google Docs / Office / arbitrary web pages).
        td.addRule("underline", {
            filter: function (node) {
                if (node.nodeName === "U" || node.nodeName === "INS") return true;
                const d = node.style && (node.style.textDecoration || node.style.textDecorationLine || "");
                return /underline/.test(d || "");
            },
            replacement: function (content) {
                return content ? "++" + content + "++" : "";
            }
        });

        // Google Docs / Office express bold/italic/strike as inline styles on <span>,
        // which Turndown's semantic-tag rules miss. Map them so formatting survives
        // no matter how the source marked it up. A span may carry several at once.
        td.addRule("styledSpan", {
            filter: function (node) {
                if (node.nodeName !== "SPAN" || !node.style) return false;
                const fw = (node.style.fontWeight || "") + "";
                const bold = fw === "bold" || parseInt(fw, 10) >= 600;
                const italic = /italic|oblique/.test(node.style.fontStyle || "");
                const deco = (node.style.textDecoration || node.style.textDecorationLine || "") + "";
                return bold || italic || /underline|line-through/.test(deco);
            },
            replacement: function (content, node) {
                if (!content || !content.trim()) return content;
                const fw = (node.style.fontWeight || "") + "";
                const deco = (node.style.textDecoration || node.style.textDecorationLine || "") + "";
                let out = content;
                if (/line-through/.test(deco)) out = "~~" + out + "~~";
                if (/underline/.test(deco)) out = "++" + out + "++";
                if (/italic|oblique/.test(node.style.fontStyle || "")) out = "*" + out + "*";
                if (fw === "bold" || parseInt(fw, 10) >= 600) out = "**" + out + "**";
                return out;
            }
        });

        _td = td;
        return _td;
    }

    // Turndown collapses HTML whitespace the way a browser renders it, which silently
    // eats the line breaks in white-space:pre-wrap content — e.g. an X/Twitter post,
    // where each line is a raw "\n" inside a <span>, not a <br>. Convert those content
    // newlines to <br> first so they survive as hard breaks. Only text nodes that carry
    // real text are touched; whitespace-only nodes (inter-tag formatting) are left alone
    // so normal pasted HTML doesn't gain spurious breaks.
    function convertContentNewlines(root) {
        const d = root.ownerDocument;
        const walker = d.createTreeWalker(root, NodeFilter.SHOW_TEXT, null);
        const targets = [];
        let n;
        while ((n = walker.nextNode())) {
            if (n.nodeValue.indexOf("\n") >= 0 && /\S/.test(n.nodeValue)) targets.push(n);
        }
        for (const node of targets) {
            const parts = node.nodeValue.split("\n");
            const frag = d.createDocumentFragment();
            for (let j = 0; j < parts.length; j++) {
                if (j > 0) frag.appendChild(d.createElement("br"));
                if (parts[j]) frag.appendChild(d.createTextNode(parts[j]));
            }
            node.parentNode.replaceChild(frag, node);
        }
    }

    function htmlToMarkdown(html) {
        const td = getTurndown();
        if (!td) return null;
        try {
            const doc = new DOMParser().parseFromString(html, "text/html");
            convertContentNewlines(doc.body);
            // Trim trailing block padding so a mid-line paste doesn't add blank lines.
            return td.turndown(doc.body.innerHTML).replace(/\s+$/, "");
        } catch (e) {
            return null; // conversion failed → caller falls back to plain paste
        }
    }

    // Decode a data: URL into a File so an embedded image can reuse the same
    // compress + upload path as a pasted screenshot.
    function dataUrlToFile(dataUrl, filename) {
        const m = /^data:([^;,]*)(;base64)?,([\s\S]*)$/i.exec(dataUrl);
        if (!m) return null;
        const mime = m[1] || "application/octet-stream";
        let bytes;
        try {
            if (m[2]) {
                const bin = atob(m[3]);
                bytes = new Uint8Array(bin.length);
                for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
            } else {
                bytes = new TextEncoder().encode(decodeURIComponent(m[3]));
            }
        } catch (e) { return null; }
        return new File([bytes], filename || "image", { type: mime });
    }

    // Rich HTML that contains <img> tags. Embedded data: images are decoded and
    // uploaded to the gallery (pending commit), then their <img> src is rewritten to the
    // local assets/ reference before conversion — so pasted pictures are captured, not
    // left as fragile external links. Remote-URL images can't be fetched under the app's
    // CSP, so they degrade to their alt text. Async: inserts once uploads finish.
    async function pasteHtmlWithImages(el, html, start, end) {
        let md = null;
        try {
            const doc = new DOMParser().parseFromString(html, "text/html");
            const imgs = Array.prototype.slice.call(doc.querySelectorAll("img"));
            for (const img of imgs) {
                const src = img.getAttribute("src") || "";
                if (/^data:/i.test(src)) {
                    const file = dataUrlToFile(src, img.getAttribute("alt") || "image");
                    let ref = null;
                    if (file) {
                        try {
                            const info = await compressIfNeeded(file);
                            ref = await pasteRef.invokeMethodAsync(
                                "AttachPastedImageAsync", info.mime, info.base64,
                                info.name || img.getAttribute("alt") || "image");
                        } catch (e) { ref = null; }
                    }
                    if (ref) { img.setAttribute("src", ref); img.removeAttribute("srcset"); }
                    else img.remove();
                } else {
                    // Remote image: unreachable under the CSP — keep alt text, not a broken ref.
                    const alt = img.getAttribute("alt");
                    if (alt && alt.trim()) img.replaceWith(doc.createTextNode(alt));
                    else img.remove();
                }
            }
            md = htmlToMarkdown(doc.body.innerHTML);
        } catch (e) {
            md = htmlToMarkdown(html); // any failure → plain conversion, no image capture
        }
        if (md && md.trim() && replaceRange(el, start, end, md)) {
            const caret = start + md.length;
            select(el, caret, caret);
        }
    }

    // Cmd/Ctrl+Shift+V arms a one-shot "paste as plain text" that skips the HTML→MD
    // conversion (handy for code, or when the source formatting is unwanted).
    let plainPasteArmed = false;
    function onKeydownForPlainPaste(e) {
        if ((e.metaKey || e.ctrlKey) && e.shiftKey && e.code === "KeyV") {
            plainPasteArmed = true;
            setTimeout(function () { plainPasteArmed = false; }, 1000);
        }
    }

    // Document-level paste handler. There is only ever one editor mounted, so a single
    // global listener (kept in sync with the current .NET ref) is simpler and survives
    // the textarea being torn down and rebuilt across view-mode switches.
    let pasteRef = null;
    let pasteBound = false;
    function onPaste(e) {
        if (!pasteRef) return;
        const el = e.target;
        if (!el || !el.classList || !el.classList.contains("editor-textarea")) return;
        const items = (e.clipboardData && e.clipboardData.items) || [];
        for (const item of items) {
            if (item.kind === "file" && item.type.startsWith("image/")) {
                const file = item.getAsFile();
                if (!file) continue;
                e.preventDefault(); // don't also paste a filename / blob URL
                compressIfNeeded(file)
                    .then(info => pasteRef.invokeMethodAsync("OnImagePasted", info.mime, info.base64, info.name))
                    .catch(() => { });
                return;
            }
        }

        // Plain-text escape hatch → let the browser's default plain paste run.
        if (plainPasteArmed) { plainPasteArmed = false; return; }

        // Rich HTML on the clipboard → convert to Markdown and insert it.
        const html = e.clipboardData ? e.clipboardData.getData("text/html") : "";
        if (html && html.trim()) {
            if (/<img\b/i.test(html)) {
                // Contains images → async path (uploads embedded images to the gallery).
                e.preventDefault();
                pasteHtmlWithImages(el, html, el.selectionStart, el.selectionEnd);
            } else {
                const md = htmlToMarkdown(html);
                if (md && md.trim()) {
                    e.preventDefault();
                    const s = el.selectionStart, en = el.selectionEnd;
                    if (replaceRange(el, s, en, md)) {
                        const caret = s + md.length;
                        select(el, caret, caret);
                    }
                }
            }
        }
        // No HTML (plain text / code) → fall through to the browser's default paste.
    }

    return {
        format: function (el, action) {
            if (!el) return;
            const fn = handlers[action];
            if (fn) fn(el);
            el.focus();
        },
        // Insert text at the caret (replacing any selection) via execCommand so the
        // insert stays on the undo stack, then place the caret after it.
        insertAtCursor: function (el, text) {
            if (!el) return;
            const s = el.selectionStart, e = el.selectionEnd;
            if (!replaceRange(el, s, e, text)) return;
            const caret = s + text.length;
            select(el, caret, caret);
        },
        pickImage: pickImage,
        enableImagePaste: function (dotNetRef) {
            pasteRef = dotNetRef;
            if (!pasteBound) {
                document.addEventListener("paste", onPaste);
                document.addEventListener("keydown", onKeydownForPlainPaste, true);
                pasteBound = true;
            }
        },
        disableImagePaste: function () { pasteRef = null; }
    };
})();
