using GitDiary.Client.Models;
using GitDiary.Client.Stores;
using Microsoft.JSInterop;

namespace GitDiary.Client.Services;

/// <summary>
/// Runtime UI theme (Light / Dark / System). Persisted under <c>gitdiary_theme</c> in
/// localStorage. When <see cref="Theme.System"/> is active, the OS preference is
/// observed via a <c>prefers-color-scheme</c> media query listener and forwarded here
/// through JS interop so the app repaints when the user toggles their OS theme.
///
/// The actual paint is a single <c>data-theme</c> attribute set on
/// <c>document.documentElement</c>; CSS variables in <c>app.css</c> do the rest.
/// </summary>
public sealed class ThemeService : StoreBase, IDisposable
{
    private const string StorageKey = "gitdiary_theme";

    private readonly IJSRuntime _jsRuntime;
    private DotNetObjectReference<ThemeService>? _selfRef;

    private Theme _currentTheme = Theme.System;
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

    /// <summary>The effective theme after collapsing <see cref="Theme.System"/> against the OS preference.</summary>
    public Theme ResolvedTheme =>
        CurrentTheme == Theme.System
            ? (_systemPrefersDark ? Theme.Dark : Theme.Light)
            : CurrentTheme;

    /// <summary>
    /// Read the persisted theme, sample the OS preference, paint the document, and start
    /// listening for OS-level changes. Safe to call once at startup; any interop failure
    /// leaves the app on the current DOM attribute (the inline boot script in index.html
    /// already applied one before WASM loaded).
    /// </summary>
    public async Task InitializeAsync()
    {
        string? code = null;
        try
        {
            code = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        }
        catch
        {
            // localStorage unavailable — keep default.
        }

        _currentTheme = ThemeExtensions.FromCode(code);

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

    /// <summary>Persist and switch the active theme, repainting the document.</summary>
    public async Task SetThemeAsync(Theme theme)
    {
        CurrentTheme = theme;

        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, theme.Code());
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

    private async Task ApplyAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("gitdiaryTheme.applyTheme", ResolvedTheme.Code());
        }
        catch
        {
            // Best-effort paint.
        }
    }

    public void Dispose()
    {
        _selfRef?.Dispose();
        _selfRef = null;
    }
}
