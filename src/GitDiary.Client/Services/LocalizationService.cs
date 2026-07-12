using System.Net.Http.Json;
using GitDiary.Client.Models;
using GitDiary.Client.Stores;
using Microsoft.JSInterop;

namespace GitDiary.Client.Services;

/// <summary>
/// Runtime UI localization. Translation dictionaries live in <c>wwwroot/i18n/{code}.json</c>
/// and are fetched lazily via <see cref="HttpClient"/>; loaded dictionaries are cached for
/// the app lifetime. Missing keys fall back to English, then to the key itself.
///
/// Persistence key: <c>gitdiary_language</c> in localStorage (BCP-47 code).
/// </summary>
public sealed class LocalizationService : StoreBase
{
    private const string StorageKey = "gitdiary_language";

    private readonly IJSRuntime _jsRuntime;
    private readonly HttpClient _http;
    private readonly Dictionary<Language, Dictionary<string, string>> _cache = new();
    private readonly Dictionary<string, string> _empty = new();

    private Language _currentLanguage = Language.English;

    public LocalizationService(IJSRuntime jsRuntime, HttpClient http)
    {
        _jsRuntime = jsRuntime;
        _http = http;
    }

    public Language CurrentLanguage
    {
        get => _currentLanguage;
        private set
        {
            if (_currentLanguage == value) return;
            _currentLanguage = value;
            NotifyStateChanged();
        }
    }

    public IReadOnlyList<Language> AvailableLanguages { get; } = new[]
    {
        Language.English,
        Language.SimplifiedChinese,
        Language.TraditionalChinese,
        Language.Japanese,
        Language.Korean
    };

    /// <summary>
    /// Read the persisted language and preload it plus English (as the fallback baseline).
    /// Safe to call once at startup. Any failure leaves the service returning keys verbatim
    /// rather than crashing the app.
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
            // localStorage unavailable (e.g. prerender) — keep default.
        }

        var initial = LanguageExtensions.FromCode(code);

        // English is always loaded as the fallback dictionary.
        await LoadLanguageAsync(Language.English);
        if (initial != Language.English)
        {
            await LoadLanguageAsync(initial);
        }

        CurrentLanguage = initial;
    }

    /// <summary>Persist and switch the active language, fetching its dictionary if needed.</summary>
    public async Task SetLanguageAsync(Language language)
    {
        await LoadLanguageAsync(language);
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, language.Code());
        }
        catch
        {
            // Ignore — in-memory state still reflects the choice.
        }
        CurrentLanguage = language;
    }

    /// <summary>Lookup by key with English fallback. Returns the key when nothing matches.</summary>
    public string this[string key]
    {
        get
        {
            var current = _cache.GetValueOrDefault(CurrentLanguage) ?? _empty;
            if (current.TryGetValue(key, out var value))
            {
                return value;
            }
            var english = _cache.GetValueOrDefault(Language.English) ?? _empty;
            if (english.TryGetValue(key, out var en))
            {
                return en;
            }
            return key;
        }
    }

    /// <summary>Composite-formatted lookup: <c>L.Format("setup.errorConnection", ex.Message)</c>.</summary>
    public string Format(string key, params object?[] args)
    {
        return string.Format(this[key], args);
    }

    private async Task LoadLanguageAsync(Language language)
    {
        if (_cache.ContainsKey(language)) return;

        try
        {
            var dict = await _http.GetFromJsonAsync<Dictionary<string, string>>($"i18n/{language.Code()}.json");
            _cache[language] = dict ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            // Do NOT insert an empty dict on failure. Caching an empty dict
            // would permanently blank-out this language for the session — the
            // user could re-select it from the language picker and we'd never
            // retry the fetch. Leaving the cache untouched means the next
            // SetLanguageAsync (or a fresh InitializeAsync) will re-attempt
            // the load. Lookups fall back to English / the raw key in the
            // meantime, which is what we want.
            Console.Error.WriteLine($"[GitDiary] LocalizationService failed to load '{language.Code()}': {ex.GetType().Name}: {ex.Message}");
        }
    }
}
