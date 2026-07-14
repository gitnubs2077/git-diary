namespace GitDiary.Client.Models;

/// <summary>
/// UI brightness mode — orthogonal to <see cref="Skin"/>. <see cref="System"/> follows
/// the OS <c>prefers-color-scheme</c> and updates live when the OS toggles.
/// Persisted under <c>gitdiary_mode</c>; mirrored to <c>data-mode</c> on
/// <c>document.documentElement</c>.
/// </summary>
public enum Theme
{
    System,
    Light,
    Dark
}

public static class ThemeExtensions
{
    /// <summary>Modes in the order they appear in the picker.</summary>
    public static readonly IReadOnlyList<Theme> All = new[]
    {
        Theme.System,
        Theme.Light,
        Theme.Dark
    };

    /// <summary>Stable string code used for persistence and the <c>data-mode</c> attribute.</summary>
    public static string Code(this Theme theme) => theme switch
    {
        Theme.Light => "light",
        Theme.Dark => "dark",
        _ => "system"
    };

    /// <summary>Icon shown on the sidebar picker trigger; brightness is what the icon signals.</summary>
    public static string DisplayIcon(this Theme theme) => theme switch
    {
        Theme.Light => "☀️",
        Theme.Dark => "🌙",
        _ => "🖥️"
    };

    public static string LabelKey(this Theme theme) => theme switch
    {
        Theme.Light => "theme.light",
        Theme.Dark => "theme.dark",
        _ => "theme.system"
    };

    public static Theme FromCode(string? code) => code switch
    {
        "light" => Theme.Light,
        "dark" => Theme.Dark,
        _ => Theme.System
    };
}

/// <summary>
/// UI design language / "skin" — orthogonal to <see cref="Theme"/>. Every skin
/// carries both a light and a dark palette; the mode picks which one paints.
/// Persisted under <c>gitdiary_skin</c>; mirrored to <c>data-skin</c> on
/// <c>document.documentElement</c>.
/// </summary>
public enum Skin
{
    Default,
    Fluent,
    WindowsXp,
    Mac,
    Solarized,
    Sepia
}

public static class SkinExtensions
{
    /// <summary>Skins in the order they appear in the picker.</summary>
    public static readonly IReadOnlyList<Skin> All = new[]
    {
        Skin.Default,
        Skin.Fluent,
        Skin.WindowsXp,
        Skin.Mac,
        Skin.Solarized,
        Skin.Sepia
    };

    public static string Code(this Skin skin) => skin switch
    {
        Skin.Fluent => "fluent",
        Skin.WindowsXp => "windows-xp",
        Skin.Mac => "mac",
        Skin.Solarized => "solarized",
        Skin.Sepia => "sepia",
        _ => "default"
    };

    public static string DisplayIcon(this Skin skin) => skin switch
    {
        Skin.Fluent => "🪟",
        Skin.WindowsXp => "🌄",
        Skin.Mac => "🍎",
        Skin.Solarized => "🌗",
        Skin.Sepia => "📜",
        _ => "📔"
    };

    public static string LabelKey(this Skin skin) => skin switch
    {
        Skin.Fluent => "skin.fluent",
        Skin.WindowsXp => "skin.windowsXp",
        Skin.Mac => "skin.mac",
        Skin.Solarized => "skin.solarized",
        Skin.Sepia => "skin.sepia",
        _ => "skin.default"
    };

    public static Skin FromCode(string? code) => code switch
    {
        "fluent" => Skin.Fluent,
        "windows-xp" => Skin.WindowsXp,
        "mac" => Skin.Mac,
        "solarized" => Skin.Solarized,
        "sepia" => Skin.Sepia,
        _ => Skin.Default
    };
}
