using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ChickenCoop.Menus;

public sealed class CoopMenuItem
{
    public required string Text { get; set; }
    public Action<CCSPlayerController>? OnSelect { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Keep the menu open (re-rendered) after this item is chosen.</summary>
    public bool KeepOpen { get; set; }
}

/// <summary>
/// A menu in the style of the SWBS Jailbreak menus: drawn in the centre of the screen (or as chat lines), controlled with
///  - W/S to move the cursor, E (+use) to select, R (+reload) to go back or exit;
///  - chat/console number keys !1 .. !9 (CSS's css_1..css_9): 1-6 items, 7 back/previous page, 8 next page, 9 exit.
/// Item text is HTML (escape anything a player typed with <see cref="MenuService.Esc"/>).
/// </summary>
public sealed class CoopMenu
{
    public CoopMenu(string title, string? subtitle = null)
    {
        Title = title;
        Subtitle = subtitle;
    }

    public string Title { get; set; }
    public string? Subtitle { get; set; }
    public List<CoopMenuItem> Items { get; } = new();
    public bool ExitButton { get; set; } = true;

    /// <summary>R / key 7 on the first page goes back to this menu. Set it lazily so it is rebuilt fresh.</summary>
    public Func<CoopMenu>? Back { get; set; }

    /// <summary>Rebuilds this menu (used after KeepOpen actions so values like "Feed (3 bags)" stay current).</summary>
    public Func<CoopMenu>? Refresh { get; set; }

    public CoopMenuItem Add(string text, Action<CCSPlayerController>? onSelect, bool enabled = true, bool keepOpen = false)
    {
        var item = new CoopMenuItem { Text = text, OnSelect = onSelect, Enabled = enabled, KeepOpen = keepOpen };
        Items.Add(item);
        return item;
    }

    /// <summary>A line that cannot be selected (information).</summary>
    public CoopMenuItem Info(string text) => Add(text, null, false);

    public void Open(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid || player.IsBot) return;
        MenuService.Open(player, this);
    }
}

/// <summary>Menu paging and cursor rules, engine-free so they are unit-tested.</summary>
public static class MenuNav
{
    public const int ItemsPerPage = 6;

    public static int PageCount(int items) => Math.Max(1, (items + ItemsPerPage - 1) / ItemsPerPage);

    /// <summary>The item a number key picks on a page (keys 1-6), or null for a key or slot with nothing on it.</summary>
    public static int? KeyToIndex(int page, int key, int count)
    {
        var index = page * ItemsPerPage + key - 1;
        if (key is < 1 or > ItemsPerPage || index < 0 || index >= count) return null;
        return index;
    }

    public static bool Selectable(CoopMenuItem item) => item.Enabled && item.OnSelect != null;

    /// <summary>Where the cursor starts: the first item that can be picked (0 if none).</summary>
    public static int FirstSelectable(IReadOnlyList<CoopMenuItem> items)
    {
        for (var i = 0; i < items.Count; i++)
            if (Selectable(items[i]))
                return i;
        return 0;
    }

    /// <summary>Moves the cursor up (-1) or down (+1), wrapping and skipping information lines and greyed items.</summary>
    public static int Step(IReadOnlyList<CoopMenuItem> items, int cursor, int dir)
    {
        var i = cursor;
        for (var n = 0; n < items.Count; n++)
        {
            i = (i + dir + items.Count) % items.Count;
            if (Selectable(items[i])) return i;
        }

        return cursor;
    }

    public static int PageOf(int cursor) => Math.Max(0, cursor) / ItemsPerPage;
}

/// <summary>Active menu state for one player. Registered with CSS's MenuManager so css_1..css_9 reach it.</summary>
internal sealed class CoopMenuInstance : IMenuInstance
{
    public const int ItemsPerPage = MenuNav.ItemsPerPage;

    private static readonly IMenu DummyMenu = new ChatMenu("");

    public CoopMenuInstance(CCSPlayerController player, CoopMenu menu)
    {
        Player = player;
        Menu = menu;
        LastInput = Server.CurrentTime;
        Cursor = MenuNav.FirstSelectable(menu.Items);
    }

    internal string? LastEcho;
    internal string? LastHtml;
    internal float LastSentAt;

    public CCSPlayerController Player { get; }
    public CoopMenu Menu { get; }
    public int PageIndex { get; set; }
    public int Cursor { get; set; }
    public bool Closed { get; set; }
    public float LastInput { get; set; }

    public int PageCount => MenuNav.PageCount(Menu.Items.Count);

    IMenu IMenuInstance.Menu => DummyMenu;
    CCSPlayerController? IMenuInstance.Player => Player;
    bool IMenuInstance.CloseOnSelect => true;
    int IMenuInstance.Page => PageIndex;
    int IMenuInstance.CurrentOffset => PageIndex * ItemsPerPage;
    int IMenuInstance.NumPerPage => ItemsPerPage;
    Stack<int> IMenuInstance.PrevPageOffsets { get; } = new();

    public void NextPage()
    {
        if (PageIndex + 1 >= PageCount) return;
        PageIndex++;
        Cursor = PageIndex * ItemsPerPage;
    }

    public void PrevPage()
    {
        if (PageIndex <= 0) return;
        PageIndex--;
        Cursor = PageIndex * ItemsPerPage;
    }

    /// <summary>Called by CSS when another plugin's menu replaces ours.</summary>
    public void Reset()
    {
        if (Closed) return;
        Closed = true;
        MenuService.Forget(this);
    }

    void IMenuInstance.Close() => MenuService.Close(Player);

    public void Display() => MenuService.Render(this, true);

    public void OnKeyPress(CCSPlayerController player, int key) => MenuService.HandleKey(this, key);
}

public enum MenuStyle
{
    Center,
    Chat,
    Both,
}

/// <summary>Owns menu instances, draws them and handles input.</summary>
public static class MenuService
{
    private const int MaxSlots = 65;
    private static readonly CoopMenuInstance?[] Active = new CoopMenuInstance?[MaxSlots];
    private static CCSGameRulesProxy? _rules;
    private static float _nextRulesLookup;
    private static bool _steadied;

    private static ChickenCoopPlugin? Plugin => ChickenCoopPlugin.Instance;

    private static MenuStyle Style =>
        Enum.TryParse<MenuStyle>(Plugin?.Config.Menu.Style, true, out var s) ? s : MenuStyle.Center;

    public static bool UsesKeys => Style != MenuStyle.Chat && (Plugin?.Config.Menu.Wasd ?? true);

    public static bool HasMenu(CCSPlayerController? player) =>
        player is { IsValid: true } && player.Slot < MaxSlots && Active[player.Slot] is { Closed: false };

    public static bool AnyOpen => Active.Any(a => a is { Closed: false });

    public static void Open(CCSPlayerController player, CoopMenu menu)
    {
        if (player.Slot >= MaxSlots) return;
        if (Active[player.Slot] is { } prev)
        {
            prev.Closed = true;
            Active[player.Slot] = null;
        }

        MenuManager.CloseActiveMenu(player);
        var inst = new CoopMenuInstance(player, menu);
        Active[player.Slot] = inst;
        MenuManager.GetActiveMenus()[player.Handle] = inst;
        Render(inst, true);
    }

    /// <summary>Re-open the player's current menu rebuilt from its Refresh function (keeps the cursor and page).</summary>
    public static void RefreshOpen(CCSPlayerController player)
    {
        if (!HasMenu(player)) return;
        var inst = Active[player.Slot]!;
        if (inst.Menu.Refresh == null) return;
        var fresh = inst.Menu.Refresh();
        fresh.Refresh ??= inst.Menu.Refresh;
        var cursor = inst.Cursor;
        var page = inst.PageIndex;
        Open(player, fresh);
        var now = Active[player.Slot]!;
        now.Cursor = Math.Clamp(cursor, 0, Math.Max(0, fresh.Items.Count - 1));
        now.PageIndex = Math.Clamp(page, 0, now.PageCount - 1);
        Render(now, true);
    }

    public static void Close(CCSPlayerController? player)
    {
        if (player is not { IsValid: true } || player.Slot >= MaxSlots) return;
        var inst = Active[player.Slot];
        if (inst == null) return;
        inst.Closed = true;
        Active[player.Slot] = null;
        if (MenuManager.GetActiveMenus().TryGetValue(player.Handle, out var m) && ReferenceEquals(m, inst))
            MenuManager.GetActiveMenus().Remove(player.Handle);
    }

    internal static void Forget(CoopMenuInstance inst)
    {
        if (inst.Player.IsValid && inst.Player.Slot < MaxSlots && ReferenceEquals(Active[inst.Player.Slot], inst))
            Active[inst.Player.Slot] = null;
    }

    public static void ForgetSlot(int slot)
    {
        if (slot is >= 0 and < MaxSlots) Active[slot] = null;
    }

    public static void Reset()
    {
        Array.Clear(Active);
        _rules = null;
        _steadied = false;
    }

    internal static void HandleKey(CoopMenuInstance inst, int key)
    {
        if (inst.Closed || !inst.Player.IsValid) return;
        inst.LastInput = Server.CurrentTime;
        var menu = inst.Menu;
        switch (key)
        {
            case 7:
                if (inst.PageIndex > 0)
                {
                    inst.PrevPage();
                    Render(inst, true);
                }
                else
                {
                    GoBack(inst);
                }

                return;
            case 8:
                inst.NextPage();
                Render(inst, true);
                return;
            case 9:
                if (menu.ExitButton) Close(inst.Player);
                return;
        }

        if (MenuNav.KeyToIndex(inst.PageIndex, key, menu.Items.Count) is { } index) Select(inst, index);
    }

    private static void GoBack(CoopMenuInstance inst)
    {
        if (inst.Menu.Back == null)
        {
            if (inst.Menu.ExitButton) Close(inst.Player);
            return;
        }

        var parent = inst.Menu.Back();
        Open(inst.Player, parent);
    }

    private static void Select(CoopMenuInstance inst, int index)
    {
        var item = inst.Menu.Items[index];
        if (!item.Enabled || item.OnSelect == null) return;
        var player = inst.Player;
        inst.Cursor = index;
        PlayClick(player);
        if (!item.KeepOpen) Close(player);
        try
        {
            item.OnSelect(player);
        }
        catch (Exception ex)
        {
            Plugin?.Logger.LogError(ex, "Chicken Coop menu action failed");
        }

        if (item.KeepOpen && !inst.Closed && ReferenceEquals(Active[player.Slot], inst))
        {
            if (inst.Menu.Refresh != null) RefreshOpen(player);
            else Render(inst, true);
        }
    }

    private static void PlayClick(CCSPlayerController player)
    {
        try
        {
            player.ExecuteClientCommand("play sounds/ui/buttonclick.vsnd_c");
        }
        catch
        {
            // cosmetic
        }
    }

    /// <summary>W/S/E/R navigation, from the OnPlayerButtonsChanged listener.</summary>
    public static void OnButtons(CCSPlayerController player, PlayerButtons pressed)
    {
        if (player.Slot >= MaxSlots || Active[player.Slot] is not { Closed: false } inst || !UsesKeys) return;
        var count = inst.Menu.Items.Count;
        if ((pressed & PlayerButtons.Forward) != 0 && count > 0)
        {
            inst.Cursor = MenuNav.Step(inst.Menu.Items, inst.Cursor, -1);
        }
        else if ((pressed & PlayerButtons.Back) != 0 && count > 0)
        {
            inst.Cursor = MenuNav.Step(inst.Menu.Items, inst.Cursor, +1);
        }
        else if ((pressed & PlayerButtons.Use) != 0 && count > 0)
        {
            inst.LastInput = Server.CurrentTime;
            Select(inst, Math.Clamp(inst.Cursor, 0, count - 1));
            return;
        }
        else if ((pressed & PlayerButtons.Reload) != 0)
        {
            inst.LastInput = Server.CurrentTime;
            GoBack(inst);
            return;
        }
        else
        {
            return;
        }

        inst.PageIndex = MenuNav.PageOf(inst.Cursor);
        inst.LastInput = Server.CurrentTime;
        Render(inst, true);
    }

    /// <summary>Every tick: timeouts, keep the centre menu on screen, and hold the panel steady while one is open.</summary>
    public static void Tick()
    {
        var cfg = Plugin?.Config.Menu;
        var timeout = cfg?.TimeoutSeconds ?? 40;
        var now = Server.CurrentTime;
        var any = false;
        for (var slot = 0; slot < MaxSlots; slot++)
        {
            var inst = Active[slot];
            if (inst == null) continue;
            if (inst.Closed || !inst.Player.IsValid)
            {
                Active[slot] = null;
                continue;
            }

            if (timeout > 0 && now - inst.LastInput > timeout)
            {
                Close(inst.Player);
                continue;
            }

            any = true;
            Render(inst, false);
        }

        SteadyPanel(any && (cfg?.FixFlashing ?? true));
    }

    internal static void Render(CoopMenuInstance inst, bool force)
    {
        if (inst.Closed || !inst.Player.IsValid) return;
        var style = Style;
        if (style != MenuStyle.Chat)
        {
            var html = BuildHtml(inst);
            var now = Server.CurrentTime;
            if (force || html != inst.LastHtml || now - inst.LastSentAt > 0.5f)
            {
                inst.LastHtml = html;
                inst.LastSentAt = now;
                inst.Player.PrintToCenterHtml(html, 1);
            }
        }

        if (style != MenuStyle.Center && force) EchoToChat(inst);
    }

    private static string BuildHtml(CoopMenuInstance inst)
    {
        var menu = inst.Menu;
        var wasd = UsesKeys;
        var sb = new System.Text.StringBuilder();
        sb.Append("<font color='#FFD24A'><b>").Append(menu.Title).Append("</b></font>");
        if (inst.PageCount > 1) sb.Append($" <font color='#AAAAAA' class='fontSize-s'>({inst.PageIndex + 1}/{inst.PageCount})</font>");
        sb.Append("<br>");
        if (!string.IsNullOrEmpty(menu.Subtitle)) sb.Append("<font color='#CFCFCF' class='fontSize-s'>").Append(menu.Subtitle).Append("</font><br>");

        var start = inst.PageIndex * CoopMenuInstance.ItemsPerPage;
        var end = Math.Min(start + CoopMenuInstance.ItemsPerPage, menu.Items.Count);
        for (var i = start; i < end; i++)
        {
            var item = menu.Items[i];
            var selectable = item.Enabled && item.OnSelect != null;
            var selected = wasd && selectable && i == inst.Cursor;
            var color = item.OnSelect == null ? "#BDBDBD" : !item.Enabled ? "#6E6E6E" : selected ? "#7CFC7C" : "#FFFFFF";
            sb.Append($"<font color='{color}'>");
            if (selected) sb.Append("&#9658; ");
            if (item.OnSelect != null) sb.Append($"{i - start + 1}. ");
            sb.Append(item.Text).Append("</font><br>");
        }

        var footer = new List<string>();
        if (inst.PageIndex > 0 || menu.Back != null) footer.Add("7.Back");
        if (inst.PageIndex + 1 < inst.PageCount) footer.Add("8.Next");
        if (menu.ExitButton) footer.Add("9.Exit");
        var hint = wasd ? "W/S move · E select · R back" : "type !1-!9";
        sb.Append($"<font color='#9A9A9A' class='fontSize-s'>{string.Join(" ", footer)} | {hint}</font>");
        return sb.ToString();
    }

    private static void EchoToChat(CoopMenuInstance inst)
    {
        var menu = inst.Menu;
        var lines = new List<string>();
        var title = Plain(menu.Title);
        if (inst.PageCount > 1) title += $" ({inst.PageIndex + 1}/{inst.PageCount})";
        lines.Add($" {ChatColors.Gold}{title}");
        if (!string.IsNullOrEmpty(menu.Subtitle)) lines.Add($" {ChatColors.Grey}{Plain(menu.Subtitle!)}");
        var start = inst.PageIndex * CoopMenuInstance.ItemsPerPage;
        var end = Math.Min(start + CoopMenuInstance.ItemsPerPage, menu.Items.Count);
        for (var i = start; i < end; i++)
        {
            var it = menu.Items[i];
            if (it.OnSelect == null) lines.Add($" {ChatColors.Grey}   {Plain(it.Text)}");
            else if (!it.Enabled) lines.Add($" {ChatColors.Grey}!{i - start + 1} {Plain(it.Text)}");
            else lines.Add($" {ChatColors.Green}!{i - start + 1}{ChatColors.Default} {Plain(it.Text)}");
        }

        var footer = new List<string>();
        if (inst.PageIndex > 0 || menu.Back != null) footer.Add("!7 Back");
        if (inst.PageIndex + 1 < inst.PageCount) footer.Add("!8 Next");
        if (menu.ExitButton) footer.Add("!9 Exit");
        if (footer.Count > 0) lines.Add($" {ChatColors.Grey}{string.Join("   ", footer)}");

        var text = string.Join("\n", lines);
        if (text == inst.LastEcho) return;
        inst.LastEcho = text;
        foreach (var line in lines) inst.Player.PrintToChat(line);
    }

    /// <summary>
    /// CS2 re-fades the centre HTML panel about once a second, so menus flash. Holding CCSGameRules.GameRestart true
    /// while no restart is pending keeps the panel steady (the workaround found by Poggu, used by most CS2 menu
    /// plugins). Only applied while a Chicken Coop menu is open; released afterwards.
    /// </summary>
    private static void SteadyPanel(bool want)
    {
        if (!want && !_steadied) return;
        try
        {
            var now = Server.CurrentTime;
            if (_rules == null || !_rules.IsValid || now >= _nextRulesLookup)
            {
                _rules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
                _nextRulesLookup = now + 5f;
            }

            var rules = _rules?.GameRules;
            if (rules == null) return;
            var steady = want && rules.RestartRoundTime < now;
            _steadied = want;
            if (rules.GameRestart == steady) return;
            rules.GameRestart = steady;
            Utilities.SetStateChanged(_rules!, "CCSGameRulesProxy", "m_pGameRules");
        }
        catch
        {
            _rules = null;
        }
    }

    public static string Esc(string? text) => System.Net.WebUtility.HtmlEncode(text ?? "");

    public static string Color(string text, string color) => $"<font color='{color}'>{text}</font>";

    public static string Small(string text) => $"<font class='fontSize-s'>{text}</font>";

    /// <summary>A coloured progress bar for menus.</summary>
    public static string Bar(double fraction, string color = "#7CFC7C", int cells = 12)
    {
        var filled = (int)Math.Round(Math.Clamp(fraction, 0, 1) * cells);
        return $"<font color='{color}'>{new string('█', filled)}</font><font color='#555555'>{new string('█', cells - filled)}</font>";
    }

    public static string Plain(string s) =>
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", ""));
}
