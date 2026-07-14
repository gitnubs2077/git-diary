using GitDiary.Client.Models;
using GitDiary.Client.Stores;
using Microsoft.JSInterop;

namespace GitDiary.Client.Services;

/// <summary>
/// Runtime UI theme, split across two orthogonal axes:
/// <list type="bullet">
///   <item><description><see cref="CurrentTheme"/>: brightness mode (System / Light / Dark), persisted under <c>gitdiary_mode</c>.</description></item>
///   <item><description><see cref="CurrentSkin"/>: design language (Default / Fluent / Windows XP / macOS), persisted under <c>gitdiary_skin</c>.</description></item>
/// </list>
/// When <see cref="Theme.System"/> is active, the OS preference is observed via a
/// <c>prefers-color-scheme</c> media query listener and forwarded here through JS
/// interop so the app repaints when the user toggles their OS theme.
///
/// The actual paint is two attributes on <c>document.documentElement</c>:
/// <c>data-mode</c> and <c>data-skin</c>. CSS variables in <c>app.css</c> do the rest.
///
/// A legacy <c>gitdiary_theme</c> key from the pre-split scheme (v1) is migrated on
/// first init and then cleared; see <see cref="SplitLegacy"/>.
/// </summary>
public sealed class ThemeService : StoreBase, IDisposable
{
    private const string ModeStorageKey = "gitdiary_mode";
    private const string SkinStorageKey = "gitdiary_skin";
    private const string LegacyStorageKey = "gitdiary_theme";

    private readonly IJSRuntime _jsRuntime;
    private DotNetObjectReference<ThemeService>? _selfRef;

    private Theme _currentTheme = Theme.System;
    private Skin _currentSkin = Skin.Default;
    private bool _systemPrefersDark;

    public ThemeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public Theme CurrentTheme
    {
        get => _currentTheme;
        private set
        {
            if (_currentTheme == value) return;
            _currentTheme = value;
            NotifyStateChanged();
        }
    }

    public Skin CurrentSkin
    {
        get => _currentSkin;
        private set
        {
            if (_currentSkin == value) return;
            _currentSkin = value;
            NotifyStateChanged();
        }
    }

    /// <summary>The effective brightness after collapsing <see cref="Theme.System"/> against the OS preference.</summary>
    public Theme ResolvedTheme =>
        CurrentTheme == Theme.System
            ? (_systemPrefersDark ? Theme.Dark : Theme.Light)
            : CurrentTheme;

    /// <summary>
    /// Read the persisted mode + skin, sample the OS preference, paint the document,
    /// and start listening for OS-level changes. Safe to call once at startup; any
    /// interop failure leaves the app on the current DOM attributes (the inline boot
    /// script in index.html already applied them before WASM loaded).
    /// </summary>
    public async Task InitializeAsync()
    {
        string? modeCode = null;
        string? skinCode = null;
        try
        {
            modeCode = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", ModeStorageKey);
            skinCode = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", SkinStorageKey);

            // v1 stored a single `gitdiary_theme` code that fused mode + skin. If
            // neither new key is present, split the legacy value and migrate. Clear
            // the legacy key so the migration doesn't repeat and confuse future
            // hand-editing of storage.
            if (modeCode is null && skinCode is null)
            {
                var legacy = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", LegacyStorageKey);
                (modeCode, skinCode) = SplitLegacy(legacy);
                if (legacy is not null)
                {
                    try { await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", LegacyStorageKey); }
                    catch { /* migration is best-effort */ }
                }
            }
        }
        catch
        {
            // localStorage unavailable — keep defaults.
        }

        _currentTheme = ThemeExtensions.FromCode(modeCode);
        _currentSkin = SkinExtensions.FromCode(skinCode);

        try
        {
            _systemPrefersDark = await _jsRuntime.InvokeAsync<bool>("gitdiaryTheme.getSystemPrefersDark");
        }
        catch
        {
            _systemPrefersDark = true;
        }

        await ApplyAsync();

        try
        {
            _selfRef = DotNetObjectReference.Create(this);
            await _jsRuntime.InvokeVoidAsync("gitdiaryTheme.watchSystem", _selfRef);
        }
        catch
        {
            // Watcher not available — the app still works, just won't react to live OS changes.
        }
    }

    /// <summary>Persist and switch the active brightness mode, repainting the document.</summary>
    public async Task SetThemeAsync(Theme theme)
    {
        CurrentTheme = theme;

        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", ModeStorageKey, theme.Code());
        }
        catch
        {
            // Ignore — in-memory state still reflects the choice.
        }

        await ApplyAsync();
    }

    /// <summary>Persist and switch the active skin, repainting the document.</summary>
    public async Task SetSkinAsync(Skin skin)
    {
        CurrentSkin = skin;

        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", SkinStorageKey, skin.Code());
        }
        catch
        {
            // Ignore — in-memory state still reflects the choice.
        }

        await ApplyAsync();
    }

    /// <summary>Invoked from JS when the OS <c>prefers-color-scheme</c> flips.</summary>
    [JSInvokable]
    public async Task OnSystemThemeChanged(bool prefersDark)
    {
        if (_systemPrefersDark == prefersDark) return;
        _systemPrefersDark = prefersDark;

        if (CurrentTheme == Theme.System)
        {
            await ApplyAsync();
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Legacy migration: v1 stored one <c>gitdiary_theme</c> code that muddled
    /// mode and skin. This split re-hydrates it into the new pair.
    /// </summary>
    private static (string? mode, string? skin) SplitLegacy(string? legacy) => legacy switch
    {
        "light" or "dark" or "system" => (legacy, "default"),
        "fluent" => ("light", "fluent"),
        "windows-xp" => ("light", "windows-xp"),
        "mac" => ("light", "mac"),
        _ => (null, null)
    };

    private async Task ApplyAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("gitdiaryTheme.applyMode", ResolvedTheme.Code());
            await _jsRuntime.InvokeVoidAsync("gitdiaryTheme.applySkin", CurrentSkin.Code());
        }
        catch
        {
            // Best-effort paint.
        }
    }

    public void Dispose()
    {
        // Detach the OS-theme media-query listener before releasing the .NET ref.
        // Without this the JS side keeps a closure over `_selfRef` and the next
        // OS-level theme flip invokes `OnSystemThemeChanged` on a disposed handle.
        try
        {
            _ = _jsRuntime.InvokeVoidAsync("gitdiaryTheme.unwatchSystem");
        }
        catch
        {
            // JS side may already be gone (page teardown) — safe to swallow.
        }
        _selfRef?.Dispose();
        _selfRef = null;
    }
}
