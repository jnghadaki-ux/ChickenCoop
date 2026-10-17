using ChickenCoop.Model;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Swbs.Common;

namespace ChickenCoop.Menus;

/// <summary>
/// The Chicken Coop side of the menu panels (src/WorldMenu, shared with the SWBS servers): the HUD panel when the
/// server has the Workshop layout (Menu.World.Panorama), else the world panel. Each player's choice of style, theme,
/// position and size lives in their pet data; the server defaults in Menu.World.
/// </summary>
internal static class CoopWorldMenus
{
    private static ChickenCoopPlugin? Plugin => ChickenCoopPlugin.Instance;

    private static WorldMenuSettings Settings => Plugin?.Config.Menu.World ?? WorldMenuSettingsHolder.Current;

    private static PetData? Prefs(CCSPlayerController c) => c.IsValid && !c.IsBot ? Plugin?.Data(c) : null;

    private static string[] Styles => Settings.Panorama ? new[] { "panorama", "world", "center" } : new[] { "world", "center" };

    /// <summary>The style of players who did not pick one ("world" means the HUD panel when the server has it).</summary>
    private static string DefaultStyle
    {
        get
        {
            var d = (Settings.DefaultStyle ?? "").Trim().ToLowerInvariant();
            if (d is "center" or "centre" or "classic") return "center";
            return Settings.Panorama && d is "world" or "panorama" or "auto" or "" ? "panorama" : "world";
        }
    }

    public static string StyleOf(CCSPlayerController c) => Pick(Prefs(c)?.MenuStyle, DefaultStyle, Styles);
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

    public static bool Wants(CCSPlayerController c) => Settings.Enabled && c is { IsValid: true, IsBot: false } && StyleOf(c) != "center";

    public static bool Draw(CCSPlayerController c, WorldMenuView view)
    {
        var s = Settings;
        if (StyleOf(c) == "panorama")
        {
            if (WorldMenuRenderer.IsOpen(c.Slot)) WorldMenuRenderer.Hide(c.Slot);
            return PanoramaMenuRenderer.Show(c, view, new PanoramaMenuModel.Options(ThemeOf(c), PositionOf(c), SizeOf(c), s.DefaultDetails), s.OpenDelay);
        }

        if (PanoramaMenuRenderer.IsOpen(c.Slot)) PanoramaMenuRenderer.Hide(c.Slot);
        var theme = WorldMenuThemes.Get(ThemeOf(c));
        var pulse = s.Animations && s.PulseSeconds > 0 && (int)(Server.CurrentTime / s.PulseSeconds) % 2 == 1;
        var size = SizeOf(c) switch { "small" => 0.85f, "large" => 1.2f, _ => 1f };
        return WorldMenuRenderer.Show(c, WorldMenuLayout.Build(view, theme, s, pulse), s, PositionOf(c), size);
    }

    public static void Hide(CCSPlayerController c)
    {
        if (!c.IsValid) return;
        WorldMenuRenderer.Hide(c.Slot);
        PanoramaMenuRenderer.Hide(c.Slot);
    }

    public static void Feedback(CCSPlayerController c, string what)
    {
        if (!c.IsValid || !(WorldMenuRenderer.IsOpen(c.Slot) || PanoramaMenuRenderer.IsOpen(c.Slot))) return;
        var s = Settings;
        var sound = what switch { "move" => s.MoveSound, "select" => s.SelectSound, _ => s.OpenSound };
        if (sound.Length == 0) return;
        try { c.EmitSound(sound); }
        catch { /* cosmetic */ }
    }

    // ---- Settings menu rows

    public static void CycleStyle(PetData d, CCSPlayerController c)
    {
        var styles = Styles;
        var next = styles[(Math.Max(0, Array.IndexOf(styles, StyleOf(c))) + 1) % styles.Length];
        d.MenuStyle = next == DefaultStyle ? null : next;
        d.MenuPrefsVersion = PetData.MenuPrefsCurrent;
    }
    public static void CycleTheme(PetData d, CCSPlayerController c) => d.MenuTheme = WorldMenuThemes.Cycle(ThemeOf(c), 1).Key;
    public static void CyclePosition(PetData d, CCSPlayerController c) =>
        d.MenuPosition = PositionOf(c) switch { "left" => "center", "center" => "right", _ => "left" };
    public static void CycleSize(PetData d, CCSPlayerController c) =>
        d.MenuSize = SizeOf(c) switch { "small" => "normal", "normal" => "large", _ => "small" };
}
