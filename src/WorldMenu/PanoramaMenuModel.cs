namespace Swbs.Common;

/// <summary>
/// The Panorama HUD menu: a <see cref="WorldMenuView"/> as the dialog variables and classes of the panels in
/// Workshop/panorama/layout/custom_game/swbs_menu.xml (engine-free, unit-tested). <see cref="PanoramaMenuRenderer"/>
/// sends only what changed.
/// </summary>
public static class PanoramaMenuModel
{
    public const int Rows = 6;
    public const int Tabs = 6;
    public const int Stats = 4;

    /// <summary>The panel that carries the theme, position, size and is-open classes.</summary>
    public const string Root = "swbs-menu";

    public static readonly string[] Positions = { "left", "center", "right" };
    public static readonly string[] Sizes = { "small", "normal", "large" };

    /// <summary>
    /// The colours an item may ask for, as classes in swbs_menu.css (tc-* for the right-hand tag, lc-* for the text).
    /// Any "#RRGGBB" maps to the nearest.
    /// </summary>
    public static readonly (string Name, int R, int G, int B)[] Palette =
    {
        ("white", 0xF2, 0xF4, 0xF7),
        ("grey", 0x9A, 0xA4, 0xB2),
        ("green", 0x5B, 0xE3, 0x7D),
        ("red", 0xFF, 0x5C, 0x5C),
        ("gold", 0xFF, 0xD7, 0x00),
        ("orange", 0xFF, 0x9B, 0x50),
        ("blue", 0x6C, 0xC8, 0xFF),
        ("purple", 0xC7, 0x9B, 0xFF)
    };

    /// <summary>Per-player look: a theme key (<see cref="WorldMenuThemes"/>), a position, a size, and whether the details pane shows.</summary>
    public sealed record Options(string Theme, string Position, string Size, bool Details);

    /// <summary>
    /// What a menu looks like on the HUD. A group is a set of classes of which at most one is on (theme-*, pos-* ...).
    /// The renderer turns <see cref="Root"/>'s is-open class on after all of this.
    /// </summary>
    public sealed class State
    {
        public List<(string Panel, string Name, string Value)> Vars { get; } = new();
        public List<(string Panel, string Class, bool On)> Classes { get; } = new();
        public List<(string Panel, string Group, string? Active)> Groups { get; } = new();

        internal void Var(string panel, string name, string value) => Vars.Add((panel, name, value));
        internal void Class(string panel, string cls, bool on) => Classes.Add((panel, cls, on));
        internal void Group(string panel, string group, string? active) => Groups.Add((panel, group, active));
    }

    /// <summary>Every class of a group, so a renderer that knows nothing yet can turn the others off.</summary>
    public static IReadOnlyList<string> GroupMembers(string group) => group switch
    {
        "theme" => WorldMenuThemes.All.Select(t => "theme-" + t.Key).ToList(),
        "pos" => Positions.Select(p => "pos-" + p).ToList(),
        "size" => Sizes.Select(s => "size-" + s).ToList(),
        "tc" => new[] { "tc-default", "tc-good", "tc-bad" }.Concat(Palette.Select(c => "tc-" + c.Name)).ToList(),
        "lc" => Palette.Select(c => "lc-" + c.Name).ToList(),
        _ => Array.Empty<string>()
    };

    public static string RowId(int i) => $"swbs-row-{i}";
    public static string TabId(int i) => $"swbs-tab-{i}";
    public static string StatId(int i) => $"swbs-stat-{i}";

    /// <summary>Every panel id <see cref="Build"/> writes to (the layout must have them all).</summary>
    public static IEnumerable<string> PanelIds()
    {
        yield return Root;
        foreach (var id in new[] { "swbs-title", "swbs-page", "swbs-crumb", "swbs-sub", "swbs-tabs", "swbs-foot", "swbs-dots",
                     "swbs-details", "swbs-dtitle", "swbs-dsub", "swbs-dbody", "swbs-dact" })
            yield return id;
        for (var i = 0; i < Tabs; i++) yield return TabId(i);
        for (var i = 0; i < Rows; i++) yield return RowId(i);
        for (var i = 0; i < Stats; i++) yield return StatId(i);
    }

    public static State Build(WorldMenuView view, Options o)
    {
        var st = new State();
        st.Group(Root, "theme", "theme-" + WorldMenuThemes.Get(o.Theme).Key);
        st.Group(Root, "pos", "pos-" + (Positions.Contains(o.Position) ? o.Position : "left"));
        st.Group(Root, "size", "size-" + (Sizes.Contains(o.Size) ? o.Size : "normal"));

        // Header: title, page, the menus above this one, subtitle.
        st.Var("swbs-title", "title", WorldMenuText.Clip(WorldMenuText.Plain(view.Title), 40));
        st.Var("swbs-page", "page", view.Pages > 1 ? $"{view.Page + 1}/{view.Pages}" : "");
        Optional(st, "swbs-crumb", "crumb", WorldMenuText.Plain(view.Breadcrumb), 60);
        Optional(st, "swbs-sub", "sub", WorldMenuText.Plain(view.Subtitle), 70);

        // Tabs.
        var tabCount = Math.Min(view.Tabs.Count, Tabs);
        st.Class("swbs-tabs", "is-hidden", tabCount == 0);
        for (var i = 0; i < Tabs; i++)
        {
            var id = TabId(i);
            st.Class(id, "is-hidden", i >= tabCount);
            if (i >= tabCount) continue;
            st.Var(id, "tab", WorldMenuText.Clip(WorldMenuText.Plain(view.Tabs[i]), 16));
            st.Class(id, "is-active", i == view.ActiveTab);
        }

        // Rows: number (or ► on the selected row, drawn by the layout), text, and a right-hand tag.
        for (var i = 0; i < Rows; i++)
        {
            var id = RowId(i);
            if (i >= view.Rows.Count)
            {
                st.Class(id, "is-empty", true);
                continue;
            }

            var row = view.Rows[i];
            var split = WorldMenuText.Split(row.Text);
            var tag = row.Tag ?? split.Tag;
            var tagClass = split.OnOff && row.Tag == null
                ? split.On ? "tc-good" : "tc-bad"
                : ColorClass(row.TagColor ?? split.TagColor) is { } tc ? "tc-" + tc : "tc-default";
            var textClass = ColorClass(row.Color ?? split.Color) is { } lc ? "lc-" + lc : null;

            st.Class(id, "is-empty", false);
            st.Var(id, "num", row.State == WorldMenuRowState.Header ? "" : row.Number ?? "");
            st.Var(id, "label", WorldMenuText.Clip(split.Text, 42));
            st.Var(id, "tag", WorldMenuText.Clip(WorldMenuText.Plain(tag), 22));
            st.Class(id, "is-selected", row.State == WorldMenuRowState.Selected);
            st.Class(id, "is-disabled", row.State == WorldMenuRowState.Disabled);
            st.Class(id, "is-header", row.State == WorldMenuRowState.Header);
            st.Group(id, "tc", tagClass);
            st.Group(id, "lc", textClass);
        }

        // Footer: key hints and page dots.
        st.Var("swbs-foot", "foot", WorldMenuText.Plain(view.Footer ?? WorldMenuLayout.DefaultFooter(view)));
        st.Var("swbs-dots", "dots", Dots(view.Page, view.Pages));

        // Details pane for the row under the cursor.
        var d = o.Details ? view.Details : null;
        st.Class(Root, "has-details", d != null);
        st.Class("swbs-details", "is-hidden", d == null);
        if (d != null)
        {
            st.Var("swbs-dtitle", "dtitle", WorldMenuText.Clip(WorldMenuText.Plain(d.Title), 34));
            Optional(st, "swbs-dsub", "dsub", WorldMenuText.Plain(d.Subtitle), 44);
            Optional(st, "swbs-dbody", "dbody", WorldMenuText.Plain(d.Body), 260);
            Optional(st, "swbs-dact", "dact", WorldMenuText.Plain(d.Actions), 60);
            for (var i = 0; i < Stats; i++)
            {
                var id = StatId(i);
                var has = i < d.Stats.Count;
                st.Class(id, "is-hidden", !has);
                if (!has) continue;
                st.Var(id, "k", WorldMenuText.Clip(WorldMenuText.Plain(d.Stats[i].Key), 18));
                st.Var(id, "v", WorldMenuText.Clip(WorldMenuText.Plain(d.Stats[i].Value), 22));
            }
        }

        return st;
    }

    private static void Optional(State st, string panel, string name, string value, int max)
    {
        st.Class(panel, "is-hidden", value.Length == 0);
        if (value.Length > 0) st.Var(panel, name, WorldMenuText.Clip(value, max));
    }

    /// <summary>"● ○ ○" for up to 8 pages; nothing for one page.</summary>
    public static string Dots(int page, int pages)
    {
        if (pages <= 1 || pages > 8) return "";
        return string.Join(" ", Enumerable.Range(0, pages).Select(p => p == page ? "●" : "○"));
    }

    /// <summary>The palette colour nearest to "#RRGGBB", or null for none / not a colour.</summary>
    public static string? ColorClass(string? hex)
    {
        var h = (hex ?? "").Trim().TrimStart('#');
        if (h.Length != 6) return null;
        int r, g, b;
        try
        {
            r = Convert.ToInt32(h[..2], 16);
            g = Convert.ToInt32(h[2..4], 16);
            b = Convert.ToInt32(h[4..], 16);
        }
        catch
        {
            return null;
        }

        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var (name, pr, pg, pb) in Palette)
        {
            var distance = (r - pr) * (r - pr) + (g - pg) * (g - pg) + (b - pb) * (b - pb);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = name;
        }

        return best;
    }
}
