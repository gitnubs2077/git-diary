namespace GitDiary.Client.Models;

/// <summary>
/// Languages supported by the UI. English is the default fallback.
/// The <see cref="Code"/> extension returns the value persisted to localStorage.
/// </summary>
public enum Language
{
    English,
    SimplifiedChinese,
    TraditionalChinese,
    Japanese,
    Korean
}

public static class LanguageExtensions
{
    /// <summary>Stable string code used for persistence and BCP-47 alignment.</summary>
    public static string Code(this Language language) => language switch
    {
        Language.SimplifiedChinese => "zh-CN",
        Language.TraditionalChinese => "zh-TW",
        Language.Japanese => "ja",
        Language.Korean => "ko",
        _ => "en"
    };

    /// <summary>Human-readable label shown in language pickers (native names).</summary>
    public static string DisplayName(this Language language) => language switch
    {
        Language.SimplifiedChinese => "简体中文",
        Language.TraditionalChinese => "繁體中文",
        Language.Japanese => "日本語",
        Language.Korean => "한국어",
        _ => "English"
    };

    public static Language FromCode(string? code) => code switch
    {
        "zh-CN" => Language.SimplifiedChinese,
        "zh-TW" => Language.TraditionalChinese,
        "ja" => Language.Japanese,
        "ko" => Language.Korean,
        _ => Language.English
    };
}
