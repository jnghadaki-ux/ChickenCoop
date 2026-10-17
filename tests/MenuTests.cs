using ChickenCoop.Menus;
using Xunit;

namespace ChickenCoop.Tests;

public class MenuTests
{
    private static CoopMenu Menu(params bool[] selectable)
    {
        var m = new CoopMenu("Test");
        for (var i = 0; i < selectable.Length; i++)
        {
            if (selectable[i]) m.Add("item " + i, _ => { });
            else m.Info("info " + i);
        }

        return m;
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(6, 1)]
    [InlineData(7, 2)]
    [InlineData(13, 3)]
    public void Pages_hold_six_items(int items, int pages) => Assert.Equal(pages, MenuNav.PageCount(items));

    [Fact]
    public void Number_keys_pick_items_on_the_current_page()
    {
        Assert.Equal(0, MenuNav.KeyToIndex(0, 1, 10));
        Assert.Equal(5, MenuNav.KeyToIndex(0, 6, 10));
        Assert.Equal(6, MenuNav.KeyToIndex(1, 1, 10));
        Assert.Null(MenuNav.KeyToIndex(1, 5, 10));  // past the last item
        Assert.Null(MenuNav.KeyToIndex(0, 7, 10));  // 7-9 are back / next / exit
        Assert.Null(MenuNav.KeyToIndex(0, 0, 10));
    }

    [Fact]
    public void The_cursor_skips_info_lines_and_greyed_items_and_wraps()
    {
        var m = Menu(false, true, false, true, true);
        m.Items[4].Enabled = false; // greyed out
        Assert.Equal(1, MenuNav.FirstSelectable(m.Items));
        Assert.Equal(3, MenuNav.Step(m.Items, 1, +1));
        Assert.Equal(1, MenuNav.Step(m.Items, 3, +1)); // wraps past the greyed item and the info line
        Assert.Equal(3, MenuNav.Step(m.Items, 1, -1));
    }

    [Fact]
    public void A_menu_of_only_info_lines_keeps_the_cursor_still()
    {
        var m = Menu(false, false);
        Assert.Equal(0, MenuNav.FirstSelectable(m.Items));
        Assert.Equal(0, MenuNav.Step(m.Items, 0, +1));
        Assert.Equal(0, MenuNav.Step(Menu().Items, 0, +1));
    }

    [Fact]
    public void Cursor_moves_change_the_page()
    {
        Assert.Equal(0, MenuNav.PageOf(5));
        Assert.Equal(1, MenuNav.PageOf(6));
        Assert.Equal(2, MenuNav.PageOf(12));
    }

    [Fact]
    public void Menu_text_is_escaped_and_bars_fill_up()
    {
        Assert.Equal("&lt;b&gt;Nugget &amp; co&lt;/b&gt;", MenuService.Esc("<b>Nugget & co</b>"));
        var half = MenuService.Bar(0.5, "#fff", 4);
        Assert.Contains("<font color='#fff'>██</font>", half);
        Assert.Equal("Nugget & co", MenuService.Plain(MenuService.Color(MenuService.Esc("Nugget & co"), "#fff")));
    }

    [Fact]
    public void Info_lines_are_not_selectable()
    {
        var m = new CoopMenu("T", "sub");
        var info = m.Info("just text");
        var item = m.Add("pick me", _ => { }, enabled: true, keepOpen: true);
        Assert.False(MenuNav.Selectable(info));
        Assert.True(MenuNav.Selectable(item));
        Assert.True(item.KeepOpen);
    }
}
