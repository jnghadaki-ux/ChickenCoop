using System.Drawing;

namespace Swbs.Common;

/// <summary>The colours of a world menu. Built-in themes are in <see cref="WorldMenuThemes"/>.</summary>
public sealed record WorldMenuTheme(
    string Key,
    string Name,
    string Accent,
    string Title,
    string Text,
    string Muted,
    string Disabled,
    string Selected,
    string SelectedPulse,
    string Tag,
    string Good,
    string Bad,
    string Divider,
    string Panel,
    int PanelAlpha)
{
    public static Color Parse(string hex, int alpha = 255)
    {
        var h = (hex ?? "").Trim().TrimStart('#');
        try
        {
            if (h.Length == 8)
            {
                alpha = Convert.ToInt32(h[..2], 16);
                h = h[2..];
            }

            if (h.Length != 6) return Color.FromArgb(Math.Clamp(alpha, 0, 255), 255, 255, 255);
            return Color.FromArgb(Math.Clamp(alpha, 0, 255), Convert.ToInt32(h[..2], 16), Convert.ToInt32(h[2..4], 16), Convert.ToInt32(h[4..], 16));
        }
        catch
        {
            return Color.FromArgb(Math.Clamp(alpha, 0, 255), 255, 255, 255);
        }
    }
}

public static class WorldMenuThemes
{
    public static readonly IReadOnlyList<WorldMenuTheme> All = new[]
    {
        new WorldMenuTheme("gold", "SWBS Gold", "#FFB000", "#FFD978", "#F2F4F7", "#9AA4B2", "#5B6370", "#FFE7A6", "#FFC341", "#C9D3DF", "#5BE37D", "#FF5C5C", "#4A4F59", "#0B0E13", 185),
        new WorldMenuTheme("crimson", "Crimson", "#FF4655", "#FF9AA2", "#F4F1F1", "#A89A9C", "#62585A", "#FFD0D4", "#FF7782", "#D9C9CB", "#5BE37D", "#FF5C5C", "#57393D", "#120A0C", 190),
        new WorldMenuTheme("azure", "Azure", "#36B3FF", "#9FDAFF", "#EEF6FC", "#8FA3B5", "#566472", "#CFEFFF", "#6CC8FF", "#BFD2E2", "#5BE37D", "#FF6B6B", "#34495A", "#081019", 185),
        new WorldMenuTheme("emerald", "Emerald", "#2EE59D", "#9CF5CF", "#EEF7F2", "#8CA89A", "#56685E", "#C9FBE4", "#5CEDB4", "#BBD7C9", "#5BE37D", "#FF6B6B", "#2F4D40", "#07120D", 185),
        new WorldMenuTheme("violet", "Violet", "#B57CFF", "#DCC2FF", "#F3F0FA", "#A49BB5", "#625A70", "#EADBFF", "#C79BFF", "#D0C6E0", "#5BE37D", "#FF6B6B", "#463A5A", "#0E0A16", 190),
        new WorldMenuTheme("ember", "Ember", "#FF7A1A", "#FFC08A", "#F7F2EE", "#AE9D90", "#685C53", "#FFE0C4", "#FF9B50", "#DCCBBE", "#5BE37D", "#FF5C5C", "#5A4130", "#140C07", 190),
        new WorldMenuTheme("coop", "Coop", "#F6C744", "#FFE7A3", "#FFF8EA", "#B8A88A", "#6F6450", "#FFF1C9", "#FFD86B", "#E4D6B8", "#8BE36B", "#FF7058", "#5E4C2C", "#1A140C", 195),
        new WorldMenuTheme("neon", "Neon", "#FF3DCB", "#7DF9FF", "#F5F5FF", "#9C9CC0", "#5E5E7A", "#B6FCFF", "#FF7BDE", "#C7C7EA", "#66FF99", "#FF5577", "#3F3560", "#09071A", 195),
        new WorldMenuTheme("mono", "Mono", "#FFFFFF", "#FFFFFF", "#E6E6E6", "#9A9A9A", "#5E5E5E", "#FFFFFF", "#CFCFCF", "#C4C4C4", "#BFFFBF", "#FFBFBF", "#4A4A4A", "#0A0A0A", 185),
    };

    public static WorldMenuTheme Default => All[0];

    public static WorldMenuTheme Get(string? key) =>
        All.FirstOrDefault(t => t.Key.Equals(key ?? "", StringComparison.OrdinalIgnoreCase)) ?? Default;

    /// <summary>The theme after (or before) this one, for a ◄ ► picker.</summary>
    public static WorldMenuTheme Cycle(string? key, int step)
    {
        var i = Math.Max(0, All.ToList().FindIndex(t => t.Key.Equals(key ?? "", StringComparison.OrdinalIgnoreCase)));
        return All[((i + step) % All.Count + All.Count) % All.Count];
    }

    /// <summary>A sensible theme for a plugin from its name ("SWBS DM" → crimson, "SWBS Surf" → azure ...).</summary>
    public static string ForPlugin(string pluginName)
    {
        var n = (pluginName ?? "").ToLowerInvariant();
        if (n.Contains("chicken") || n.Contains("coop")) return "coop";
        if (n.Contains("jail")) return "ember";
        if (n.Contains("surf") || n.Contains("bhop")) return "azure";
        if (n.Contains("dm") || n.Contains("deathmatch")) return "crimson";
        if (n.Contains("kz") || n.Contains("course")) return "emerald";
        if (n.Contains("pug")) return "emerald";
        if (n.Contains("1v1") || n.Contains("arena")) return "violet";
        return "gold";
    }
}
