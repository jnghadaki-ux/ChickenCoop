using CounterStrikeSharp.API.Core;

namespace Swbs.Common;

/// <summary>
/// Tabs for a menu (A/D switches): the titles, the one showing, and how to build the menu of any tab. The menu core
/// opens the built menu in place of the current one, keeping its parent.
/// </summary>
public sealed class MenuTabs<TMenu> where TMenu : class
{
    public MenuTabs(IEnumerable<string> titles, int active, Func<CCSPlayerController, int, TMenu> build)
    {
        Titles = titles.ToList();
        Active = Titles.Count == 0 ? -1 : Math.Clamp(active, 0, Titles.Count - 1);
        Build = build;
    }

    public List<string> Titles { get; }
    public int Active { get; }
    public Func<CCSPlayerController, int, TMenu> Build { get; }

    /// <summary>The tab <paramref name="step"/> away from the active one, wrapping around.</summary>
    public int Step(int step) => Titles.Count == 0 ? -1 : ((Active + step) % Titles.Count + Titles.Count) % Titles.Count;
}
