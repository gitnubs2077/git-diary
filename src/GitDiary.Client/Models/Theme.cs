namespace GitDiary.Client.Models;

/// <summary>
/// UI color themes. <see cref="System"/> follows the OS <c>prefers-color-scheme</c>
/// setting and updates live if the OS toggles. The <see cref="LanguageExtensions.Code"/>
/// counterpart, <see cref="ThemeExtensions.Code"/>, produces the value persisted to
/// localStorage under <c>gitdiary_theme</c>.
/// </summary>
public enum Theme
{
    System,
    Light,
    Dark
}

public static class ThemeExtensions
{
    /// <summary>Stable string code used for persistence and the <c>data-theme</c> attribute.</summary>
    public static string Code(this Theme theme) => theme switch
    {
        Theme.Light => "light",
        Theme.Dark => "dark",
        _ => "system"
    };

    /// <summary>Icon shown on the sidebar toggle button.</summary>
    public static string DisplayIcon(this Theme theme) => theme switch
    {
        Theme.Light => "☀️",
        Theme.Dark => "🌙",
        _ => "🖥️"
    };

    public static Theme FromCode(string? code) => code switch
    {
        "light" => Theme.Light,
        "dark" => Theme.Dark,
        _ => Theme.System
    };
}
