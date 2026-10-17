namespace Swbs.Common;

/// <summary>
/// Section "WorldMenu": menus drawn in the world, in front of the player's eyes (point_worldtext panels that follow the
/// view), with coloured text and themes. Players pick their own style, theme, position and size with !menustyle.
/// </summary>
public sealed class WorldMenuSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Style for players who never chose one: "world" or "center" (the classic centre-screen menu).</summary>
    public string DefaultStyle { get; set; } = "world";

    /// <summary>A theme key (gold, crimson, azure, emerald, violet, ember, coop, neon, mono) or "auto" (by game mode).</summary>
    public string DefaultTheme { get; set; } = "auto";

    /// <summary>"left", "center" or "right".</summary>
    public string DefaultPosition { get; set; } = "left";

    /// <summary>"small", "normal" or "large".</summary>
    public string DefaultSize { get; set; } = "normal";

    /// <summary>A font the CS2 client has ("Tahoma Bold", "Arial Bold", "Verdana" ...).</summary>
    public string FontName { get; set; } = "Tahoma Bold";

    /// <summary>Text size at "normal" (small = x0.85, large = x1.2).</summary>
    public float FontSize { get; set; } = 24f;

    public float WorldUnitsPerPx { get; set; } = 0.0085f;

    /// <summary>Units in front of the eyes. Closer than ~5 clips into the camera.</summary>
    public float Distance { get; set; } = 7f;

    /// <summary>The panel follows the view up and down in steps of this many degrees (it re-anchors when the view moves further).</summary>
    public float PitchStep { get; set; } = 10f;

    /// <summary>The panel never tilts beyond this pitch (looking straight up or down keeps it in reach).</summary>
    public float MaxPitch { get; set; } = 45f;

    /// <summary>Horizontal start of a left-placed panel (units right of the view centre; negative = left). -8.6 suits 16:9.</summary>
    public float LeftEdge { get; set; } = -8.6f;

    /// <summary>Height of the panel's top edge above the view centre.</summary>
    public float TopEdge { get; set; } = 1.8f;

    /// <summary>Average glyph width in em for the font (used to line up the columns). Tune if tags drift.</summary>
    public float CharWidth { get; set; } = 0.56f;

    /// <summary>Background plate padding (point_worldtext background border).</summary>
    public float PanelBorder { get; set; } = 0.12f;

    public bool Animations { get; set; } = true;

    /// <summary>The selected row gently pulses between two shades (seconds per shade).</summary>
    public float PulseSeconds { get; set; } = 0.7f;

    /// <summary>Fade-in when a menu opens.</summary>
    public float FadeInSeconds { get; set; } = 0.15f;

    public bool ShowNumbers { get; set; } = true;
    public bool ShowAccentBar { get; set; } = true;
    public bool ShowFooter { get; set; } = true;
    public bool UppercaseTitles { get; set; } = true;

    /// <summary>Sound events (empty = silent).</summary>
    public string MoveSound { get; set; } = "";
    public string SelectSound { get; set; } = "";
    public string OpenSound { get; set; } = "";
}

/// <summary>One player's menu preferences.</summary>
public sealed class WorldMenuPrefs
{
    /// <summary>"world" or "center"; null = the server default.</summary>
    public string? Style { get; set; }
    public string? Theme { get; set; }
    public string? Position { get; set; }
    public string? Size { get; set; }
}
