namespace Swbs.Common;

/// <summary>How a menu row is drawn.</summary>
public enum WorldMenuRowState
{
    Normal,

    /// <summary>The row under the cursor.</summary>
    Selected,

    /// <summary>Locked / informational (grey, no number).</summary>
    Disabled,

    /// <summary>A section heading inside the list (accent, no number).</summary>
    Header
}

/// <summary>One line of the list part of a world menu.</summary>
public sealed class WorldMenuRow
{
    public string Text { get; set; } = "";

    /// <summary>Right-hand column (price, state, level ...). Null = none (parsed from the text when possible).</summary>
    public string? Tag { get; set; }

    /// <summary>The key that selects it ("1".."6"), or null.</summary>
    public string? Number { get; set; }

    public WorldMenuRowState State { get; set; }

    /// <summary>"#RRGGBB" for the text, or null for the theme colour.</summary>
    public string? Color { get; set; }

    /// <summary>"#RRGGBB" for the tag, or null for the theme colour.</summary>
    public string? TagColor { get; set; }
}

/// <summary>
/// Everything a world menu shows, independent of the menu system that produced it (SWBS menus, jailbreak menus,
/// ChickenCoop menus all convert to this).
/// </summary>
public sealed class WorldMenuView
{
    public string Title { get; set; } = "";

    /// <summary>The menus above this one, e.g. "Store › Trails" (optional).</summary>
    public string? Breadcrumb { get; set; }

    public string? Subtitle { get; set; }

    public int Page { get; set; }
    public int Pages { get; set; } = 1;

    public List<WorldMenuRow> Rows { get; } = new();

    /// <summary>Key hints; null = the default hints (W/S, E, R, and 7/8/9 when they apply).</summary>
    public string? Footer { get; set; }

    public bool CanGoBack { get; set; }
    public bool HasNext { get; set; }
    public bool CanExit { get; set; } = true;

    /// <summary>Rows reserved on every page so the panel never changes size between pages.</summary>
    public int RowsPerPage { get; set; } = 6;
}
