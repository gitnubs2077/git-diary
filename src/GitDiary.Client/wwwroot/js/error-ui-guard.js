// Guards the generic Blazor "An unhandled error has occurred" bar (#blazor-error-ui).
//
// Blazor WebAssembly reveals that bar on ANY unhandled .NET exception. Unlike Blazor
// Server there is no circuit to tear down — the app keeps running and stays fully
// usable afterwards. One benign case flashes the bar for no good reason: when a
// GitHub request is rejected (an expired / revoked / mistyped token), the .NET WASM
// HTTP stack surfaces an *unobserved* background exception that never reaches app
// code through any catchable channel (the request itself is already handled — the
// app shows its own "reconnect" banner). Because C# can't observe it, we neutralise
// the misleading bar here instead.
//
// Safety: the bar is only auto-dismissed once the app has actually booted, i.e. its
// root (#app) has rendered real content in place of the initial loading spinner. A
// genuine fatal boot failure leaves the spinner in place, so the bar — and its
// Reload link — stays visible. The underlying error is always still written to the
// console, so nothing is hidden from diagnostics.
(function () {
    function appBooted() {
        var app = document.getElementById('app');
        // Blazor replaces the loading spinner with the rendered app on a successful boot.
        return app && !app.querySelector('.loading-progress');
    }

    function install() {
        var ui = document.getElementById('blazor-error-ui');
        if (!ui) return;

        var observer = new MutationObserver(function () {
            var shown = ui.style.display !== '' && ui.style.display !== 'none';
            if (shown && appBooted()) {
                ui.style.display = 'none';
                console.warn(
                    '[GitDiary] The runtime reported a background error, but the app is ' +
                    'still running, so the generic error bar was dismissed. Any real ' +
                    'failure is logged above and surfaced in-app.');
            }
        });
        observer.observe(ui, { attributes: true, attributeFilter: ['style'] });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', install);
    } else {
        install();
    }
})();
