using ChickenCoop.Model;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Swbs.Common;

namespace ChickenCoop.Menus;

/// <summary>
/// The Chicken Coop side of the world menus (src/WorldMenu, shared with the SWBS servers): each player's choice of
/// style, theme, position and size lives in their pet data; the server defaults in Menu.World.
/// </summary>
internal static class CoopWorldMenus
{
    private static ChickenCoopPlugin? Plugin => ChickenCoopPlugin.Instance;

    private static WorldMenuSettings Settings => Plugin?.Config.Menu.World ?? WorldMenuSettingsHolder.Current;

    private static PetData? Prefs(CCSPlayerController c) => c.IsValid && !c.IsBot ? Plugin?.Data(c) : null;

    public static string StyleOf(CCSPlayerController c) => Pick(Prefs(c)?.MenuStyle, Settings.DefaultStyle, "world", "center");
    public static string PositionOf(CCSPlayerController c) => Pick(Prefs(c)?.MenuPosition, Settings.DefaultPosition, "left", "center", "right");
    public static string SizeOf(CCSPlayerController c) => Pick(Prefs(c)?.MenuSize, Settings.DefaultSize, "normal", "small", "large");

    public static string ThemeOf(CCSPlayerController c)
    {
        var t = Prefs(c)?.MenuTheme ?? Settings.DefaultTheme;
        return string.IsNullOrWhiteSpace(t) || t.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "coop" : WorldMenuThemes.Get(t).Key;
    }

    private static string Pick(string? value, string fallback, params string[] allowed)
    {
        foreach (var v in new[] { value, fallback })
            if (v != null && allowed.Contains(v.Trim().ToLowerInvariant())) return v.Trim().ToLowerInvariant();
        return allowed[0];
    }

    public static bool Wants(CCSPlayerController c) => Settings.Enabled && c is { IsValid: true, IsBot: false } && StyleOf(c) == "world";

    public static bool Draw(CCSPlayerController c, WorldMenuView view)
    {
        var s = Settings;
        var theme = WorldMenuThemes.Get(ThemeOf(c));
        var pulse = s.Animations && s.PulseSeconds > 0 && (int)(Server.CurrentTime / s.PulseSeconds) % 2 == 1;
        var size = SizeOf(c) switch { "small" => 0.85f, "large" => 1.2f, _ => 1f };
        return WorldMenuRenderer.Show(c, WorldMenuLayout.Build(view, theme, s, pulse), s, PositionOf(c), size);
    }

    public static void Hide(CCSPlayerController c)
    {
        if (c.IsValid) WorldMenuRenderer.Hide(c.Slot);
    }

    public static void Feedback(CCSPlayerController c, string what)
    {
        if (!c.IsValid || !WorldMenuRenderer.IsOpen(c.Slot)) return;
        var s = Settings;
        var sound = what switch { "move" => s.MoveSound, "select" => s.SelectSound, _ => s.OpenSound };
        if (sound.Length == 0) return;
        try { c.EmitSound(sound); }
        catch { /* cosmetic */ }
    }

    // ---- Settings menu rows

    public static void CycleStyle(PetData d, CCSPlayerController c) => d.MenuStyle = StyleOf(c) == "world" ? "center" : "world";
    public static void CycleTheme(PetData d, CCSPlayerController c) => d.MenuTheme = WorldMenuThemes.Cycle(ThemeOf(c), 1).Key;
    public static void CyclePosition(PetData d, CCSPlayerController c) =>
        d.MenuPosition = PositionOf(c) switch { "left" => "center", "center" => "right", _ => "left" };
    public static void CycleSize(PetData d, CCSPlayerController c) =>
        d.MenuSize = SizeOf(c) switch { "small" => "normal", "normal" => "large", _ => "small" };
}
