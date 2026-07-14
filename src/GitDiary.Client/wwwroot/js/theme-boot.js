// Theme boot + interop. Runs before the Blazor loader so the persisted (or
// OS-preferred) theme is applied to <html> before first paint — no dark flash.
// The whole IIFE is wrapped in try/catch so a single unsupported API on an
// ancient UA (e.g. matchMedia returning null on legacy embedded WebViews) can't
// take down every gitdiary* bridge and leave the app with no keyboard shortcuts,
// no beforeunload guard, no theme sync, etc.
(function () {
    const html = document.documentElement;
    // matchMedia is present in every browser Blazor WASM supports, but guard
    // against embedded WebViews / test doubles that return null.
    const mqlRaw = (typeof window.matchMedia === 'function')
        ? window.matchMedia('(prefers-color-scheme: dark)')
        : null;
    const mql = mqlRaw || { matches: false, addEventListener: null, addListener: null };

    // Two orthogonal knobs:
    //   * mode = light | dark  (brightness — `system` collapses to one of these)
    //   * skin = default | fluent | windows-xp | mac  (design language)
    // Kept in separate localStorage keys so each can be changed independently.
    const VALID_SKINS = ['default', 'fluent', 'windows-xp', 'mac', 'solarized', 'sepia'];

    function resolveMode(stored) {
        if (stored === 'light' || stored === 'dark') return stored;
        // `system` and anything else fall through to the OS preference.
        return mql.matches ? 'dark' : 'light';
    }
    function resolveSkin(stored) {
        return VALID_SKINS.indexOf(stored) !== -1 ? stored : 'default';
    }

    // Legacy migration (v1 → v2): a single `gitdiary_theme` key used to encode
    // one of light/dark/system/fluent/windows-xp/mac. Split it here so the
    // pre-Blazor paint is already correct; ThemeService performs the same
    // migration server-side and clears the legacy key.
    function splitLegacy(v) {
        if (v === 'light' || v === 'dark' || v === 'system') return { mode: v, skin: 'default' };
        if (v === 'fluent' || v === 'windows-xp' || v === 'mac') return { mode: 'light', skin: v };
        return null;
    }

    let storedMode = null, storedSkin = null;
    try {
        storedMode = localStorage.getItem('gitdiary_mode');
        storedSkin = localStorage.getItem('gitdiary_skin');
        if (storedMode === null && storedSkin === null) {
            const legacySplit = splitLegacy(localStorage.getItem('gitdiary_theme'));
            if (legacySplit) {
                storedMode = legacySplit.mode;
                storedSkin = legacySplit.skin;
            }
        }
    } catch (_) { /* ignore */ }

    html.dataset.mode = resolveMode(storedMode);
    html.dataset.skin = resolveSkin(storedSkin);

    window.gitdiaryTheme = {
        getSystemPrefersDark: function () { return mql.matches; },
        applyMode: function (resolved) {
            if (resolved === 'light' || resolved === 'dark') {
                html.dataset.mode = resolved;
            }
        },
        applySkin: function (skin) {
            if (VALID_SKINS.indexOf(skin) !== -1) {
                html.dataset.skin = skin;
            }
        },
        _watchHandler: null,
        watchSystem: function (dotNetRef) {
            if (this._watchHandler) return;
            const self = this;
            this._watchHandler = function (e) {
                try { dotNetRef.invokeMethodAsync('OnSystemThemeChanged', e.matches); }
                catch (_) { /* .NET side torn down */ }
            };
            if (mql.addEventListener) {
                mql.addEventListener('change', this._watchHandler);
            } else if (mql.addListener) {
                mql.addListener(this._watchHandler);
            }
        },
        unwatchSystem: function () {
            if (!this._watchHandler) return;
            if (mql.removeEventListener) {
                mql.removeEventListener('change', this._watchHandler);
            } else if (mql.removeListener) {
                mql.removeListener(this._watchHandler);
            }
            this._watchHandler = null;
        }
    };

    // Sidebar width boot + drag interop. Apply persisted width before first
    // paint so the layout doesn't flash from the CSS default (280px) to the
    // user's saved value.
    const SIDEBAR_MIN = 180;
    const SIDEBAR_MAX = 560;
    const SIDEBAR_DEFAULT = 350;
    const SIDEBAR_KEY = 'gitdiary_sidebar_width';

    function readSidebarWidth() {
        const cs = getComputedStyle(html).getPropertyValue('--sidebar-width');
        const w = parseInt(cs, 10);
        return isNaN(w) ? SIDEBAR_DEFAULT : w;
    }
    function clampSidebar(v) {
        return Math.max(SIDEBAR_MIN, Math.min(SIDEBAR_MAX, v));
    }

    try {
        const storedW = localStorage.getItem(SIDEBAR_KEY);
        if (storedW !== null) {
            const w = parseInt(storedW, 10);
            if (!isNaN(w)) {
                html.style.setProperty('--sidebar-width', clampSidebar(w) + 'px');
            }
        }
    } catch (_) { /* ignore */ }

    window.gitdiarySidebar = {
        MIN: SIDEBAR_MIN,
        MAX: SIDEBAR_MAX,
        DEFAULT: SIDEBAR_DEFAULT,
        attachResizer: function (handle) {
            if (!handle || handle._gdAttached) return;
            handle._gdAttached = true;

            let dragging = false;
            let startX = 0;
            let startW = 0;

            handle.addEventListener('pointerdown', function (e) {
                if (e.pointerType === 'mouse' && e.button !== 0) return;
                dragging = true;
                startX = e.clientX;
                startW = readSidebarWidth();
                try { handle.setPointerCapture(e.pointerId); } catch (_) { /* ignore */ }
                document.body.style.cursor = 'col-resize';
                document.body.style.userSelect = 'none';
                handle.classList.add('is-dragging');
                e.preventDefault();
            });

            handle.addEventListener('pointermove', function (e) {
                if (!dragging) return;
                const w = clampSidebar(startW + (e.clientX - startX));
                html.style.setProperty('--sidebar-width', w + 'px');
            });

            function endDrag(e) {
                if (!dragging) return;
                dragging = false;
                document.body.style.cursor = '';
                document.body.style.userSelect = '';
                handle.classList.remove('is-dragging');
                try { handle.releasePointerCapture(e.pointerId); } catch (_) { /* ignore */ }
                try { localStorage.setItem(SIDEBAR_KEY, String(readSidebarWidth())); } catch (_) { /* ignore */ }
            }
            handle.addEventListener('pointerup', endDrag);
            handle.addEventListener('pointercancel', endDrag);

            // Double-click resets to the default width.
            handle.addEventListener('dblclick', function () {
                html.style.setProperty('--sidebar-width', SIDEBAR_DEFAULT + 'px');
                try { localStorage.setItem(SIDEBAR_KEY, String(SIDEBAR_DEFAULT)); } catch (_) { /* ignore */ }
            });
        }
    };

    // Online-status bridge. When the browser reports `online`, we notify
    // .NET so SyncService can flush pending drafts. `register` is idempotent —
    // subsequent calls are ignored while a handler is already attached.
    window.gitdiaryOnline = {
        _handler: null,
        register: function (dotNetRef) {
            if (this._handler) return;
            const self = this;
            this._handler = function () {
                try { dotNetRef.invokeMethodAsync('OnBrowserOnline'); }
                catch (_) { /* .NET side gone */ }
            };
            window.addEventListener('online', this._handler);
        },
        unregister: function () {
            if (this._handler) {
                window.removeEventListener('online', this._handler);
                this._handler = null;
            }
        },
        isOnline: function () { return navigator.onLine !== false; }
    };

    // Global Ctrl/Cmd+S interceptor. Runs at the document level so the
    // shortcut works no matter which element has focus — the sidebar,
    // the search box, the preview pane, or the editor textarea itself.
    // preventDefault stops the browser's Save-As dialog. Registered
    // once from Home.OnAfterRenderAsync via a DotNetObjectReference.
    window.gitdiaryKeys = {
        _handler: null,
        _ref: null,
        register: function (dotNetRef) {
            if (this._handler) return;
            this._ref = dotNetRef;
            const self = this;
            this._handler = function (e) {
                const isS = e.key === 's' || e.key === 'S';
                if (isS && (e.ctrlKey || e.metaKey) && !e.altKey) {
                    e.preventDefault();
                    try { self._ref && self._ref.invokeMethodAsync('OnCtrlS'); }
                    catch (_) { /* ignore */ }
                }
            };
            window.addEventListener('keydown', this._handler);
        },
        unregister: function () {
            if (this._handler) {
                window.removeEventListener('keydown', this._handler);
                this._handler = null;
                this._ref = null;
            }
        }
    };

    // beforeunload guard. .NET pushes the current dirty state whenever
    // it changes; the handler consults the cached flag synchronously
    // (beforeunload is not allowed to await). A blank returnValue is
    // enough to trigger every modern browser's generic prompt.
    window.gitdiaryDirty = {
        _dirty: false,
        _installed: false,
        set: function (v) { this._dirty = !!v; },
        install: function () {
            if (this._installed) return;
            this._installed = true;
            const self = this;
            window.addEventListener('beforeunload', function (e) {
                if (!self._dirty) return;
                e.preventDefault();
                e.returnValue = '';
                return '';
            });
        }
    };

    // Keep <html lang> in sync with the active UI language so screen
    // readers, spell-checkers, and search engines get the right hint.
    window.gitdiaryLang = {
        set: function (code) {
            if (typeof code === 'string' && code.length > 0) {
                html.setAttribute('lang', code);
            }
        }
    };

    // Reachability watcher for the UI-side offline banner. Distinct
    // from gitdiaryOnline (which only listens for `online` to trigger
    // draft sync); this one reports *both* directions so Home can flip
    // an in-app banner. isOffline() is a synchronous startup probe.
    window.gitdiaryNetStatus = {
        _online: null,
        _offline: null,
        _ref: null,
        subscribe: function (dotNetRef) {
            if (this._online || this._offline) return;
            this._ref = dotNetRef;
            const self = this;
            this._online = function () {
                try { self._ref && self._ref.invokeMethodAsync('OnNetOnline'); }
                catch (_) { /* ignore */ }
            };
            this._offline = function () {
                try { self._ref && self._ref.invokeMethodAsync('OnNetOffline'); }
                catch (_) { /* ignore */ }
            };
            window.addEventListener('online', this._online);
            window.addEventListener('offline', this._offline);
        },
        unsubscribe: function () {
            if (this._online) window.removeEventListener('online', this._online);
            if (this._offline) window.removeEventListener('offline', this._offline);
            this._online = null;
            this._offline = null;
            this._ref = null;
        },
        isOffline: function () { return navigator.onLine === false; }
    };
})();
