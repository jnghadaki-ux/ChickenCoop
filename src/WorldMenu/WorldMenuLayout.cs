using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Swbs.Common;

/// <summary>One point_worldtext of a panel: a column of text in one colour (every layer has the same number of lines).</summary>
public sealed record WorldMenuLayer(string Key, float X, string Text, string Color, int Alpha, float Depth, bool Background);

/// <summary>The panel as layers, plus its size in em (1 em = the font size).</summary>
public sealed record WorldMenuPanel(IReadOnlyList<WorldMenuLayer> Layers, int Lines, float Width);

/// <summary>Text clean-up for menu items written as centre-HUD HTML or chat colour tags (engine-free, unit-tested).</summary>
public static class WorldMenuText
{
    private static readonly Regex FontTag = new("<font[^>]*?color\\s*=\\s*['\"]?(#[0-9a-fA-F]{6})['\"]?[^>]*>(.*?)</font>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex AnyTag = new("<[^>]+>", RegexOptions.Singleline);
    private static readonly Regex ChatColor = new("\\{(default|white|darkred|green|lightyellow|lightblue|olive|lime|red|lightpurple|purple|grey|gray|yellow|gold|silver|blue|darkblue|bluegrey|magenta|lightred|orange|prisoner|guard)\\}", RegexOptions.IgnoreCase);
    private static readonly Regex Spaces = new("\\s+");
    private static readonly Regex TrailingBracket = new("^(.*\\S)\\s+([\\(\\[][^\\(\\)\\[\\]]{1,24}[\\)\\]])$");
    private static readonly Regex TrailingBar = new("^(.*?)<font[^>]*?color\\s*=\\s*['\"]?(#[0-9a-fA-F]{6})['\"]?[^>]*>(█*)</font>\\s*<font[^>]*>(█*)</font>\\s*$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex TrailingOnOff = new("^(.*?\\S)\\s*:?\\s+(ON|OFF|On|Off|on|off)$");

    /// <summary>Plain text: tags, colour codes and entities removed, whitespace collapsed.</summary>
    public static string Plain(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var t = s.Replace("<br>", " ", StringComparison.OrdinalIgnoreCase).Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase);
        t = AnyTag.Replace(t, "");
        t = ChatColor.Replace(t, "");
        t = WebUtility.HtmlDecode(t);
        return Spaces.Replace(t, " ").Trim();
    }

    /// <summary>
    /// Splits an item into its text, an optional right-hand tag and colours:
    /// "Rainbow trail &lt;font color='#88FF88'&gt;(owned)&lt;/font&gt;" → text "Rainbow trail", tag "(owned)" in green;
    /// a text wholly inside one font tag takes that colour; "Show trails: ON" → tag "ON" (good) / "OFF" (bad);
    /// a trailing "(...)" or "[...]" becomes a tag.
    /// </summary>
    public static (string Text, string? Tag, string? Color, string? TagColor, bool OnOff, bool On) Split(string? raw)
    {
        raw ??= "";
        string? color = null, tag = null, tagColor = null;
        var trimmed = raw.Trim();

        // A two-colour progress bar at the end ("Hunger ████░░"): one tag, filled cells solid, the rest light.
        if (TrailingBar.Match(trimmed) is { Success: true } bar && Plain(bar.Groups[1].Value).Length > 0)
            return (Plain(bar.Groups[1].Value), bar.Groups[3].Value + new string('░', bar.Groups[4].Value.Length), null,
                bar.Groups[2].Value.ToUpperInvariant(), false, false);

        var matches = FontTag.Matches(trimmed);
        if (matches.Count > 0)
        {
            var last = matches[^1];
            var whole = matches.Count == 1 && last.Index == 0 && last.Length == trimmed.Length;
            if (whole) color = last.Groups[1].Value.ToUpperInvariant();
            else if (last.Index + last.Length == trimmed.Length && Plain(trimmed[..last.Index]).Length > 0)
            {
                tag = Plain(last.Groups[2].Value);
                tagColor = last.Groups[1].Value.ToUpperInvariant();
                trimmed = trimmed[..last.Index];
                if (matches.Count > 1 && matches[0].Index == 0 && matches[0].Index + matches[0].Length == trimmed.TrimEnd().Length)
                    color = matches[0].Groups[1].Value.ToUpperInvariant();
            }
        }

        var text = Plain(trimmed);
        var onOff = false;
        var on = false;
        if (tag == null)
        {
            var m = TrailingOnOff.Match(text);
            if (m.Success && m.Groups[1].Value.Length > 0)
            {
                text = m.Groups[1].Value.TrimEnd(':', ' ');
                on = m.Groups[2].Value.Equals("on", StringComparison.OrdinalIgnoreCase);
                tag = on ? "ON" : "OFF";
                onOff = true;
            }
            else if (TrailingBracket.Match(text) is { Success: true } b)
            {
                text = b.Groups[1].Value;
                tag = b.Groups[2].Value;
            }
        }

        if (tag != null && tag.Length == 0) tag = null;
        return (text, tag, color, tagColor, onOff, on);
    }

    public static string Clip(string s, int max) => s.Length <= max ? s : s[..Math.Max(1, max - 1)].TrimEnd() + "…";

    /// <summary>Estimated width in em (proportional sans-serif bold), scaled by <paramref name="charWidth"/> / 0.56.</summary>
    public static float Width(string s, float charWidth = 0.56f)
    {
        float w = 0;
        foreach (var ch in s)
        {
            w += ch switch
            {
                ' ' or '\u00A0' => 0.30f,
                'i' or 'l' or 'j' or 'I' or '.' or ',' or '\'' or '!' or '|' or ':' or ';' or '(' or ')' or '[' or ']' => 0.32f,
                'f' or 't' or 'r' or '-' => 0.42f,
                'm' or 'w' or 'M' or 'W' or '@' or '%' => 0.92f,
                >= 'A' and <= 'Z' => 0.68f,
                >= '0' and <= '9' => 0.62f,
                '─' or '━' or '●' or '○' or '►' or '◄' or '♦' or '▌' or '█' or '░' => 0.62f,
                _ => 0.56f
            };
        }

        return w * (charWidth / 0.56f);
    }
}

/// <summary>Turns a <see cref="WorldMenuView"/> into coloured layers (engine-free, unit-tested).</summary>
public static class WorldMenuLayout
{
    public const string Bullet = "♦";
    public const string Pointer = "►";
    public const string Bar = "▌";
    public const string Dash = "─";
    private const string Blank = " ";
    private const int MaxText = 34;
    private const int MaxTag = 16;

    private sealed class Cell
    {
        public string Text = "";
        public string Color = "";
    }

    public static WorldMenuPanel Build(WorldMenuView view, WorldMenuTheme theme, WorldMenuSettings s, bool pulse = false)
    {
        var cw = s.CharWidth;
        var lines = new List<(Cell? Num, Cell? Text, Cell? Tag, bool Divider)>();

        // ---- title
        var title = WorldMenuText.Clip(WorldMenuText.Plain(view.Title), MaxText);
        if (s.UppercaseTitles) title = title.ToUpperInvariant();
        string? pageTag = null;
        if (view.Pages > 1)
            pageTag = view.Pages <= 6
                ? string.Join(" ", Enumerable.Range(0, view.Pages).Select(i => i == view.Page ? "●" : "○"))
                : $"{view.Page + 1}/{view.Pages}";
        lines.Add((new Cell { Text = Bullet, Color = theme.Accent }, new Cell { Text = title, Color = theme.Title },
            pageTag == null ? null : new Cell { Text = pageTag, Color = theme.Muted }, false));

        // ---- breadcrumb / subtitle
        var crumb = WorldMenuText.Plain(view.Breadcrumb);
        var sub = WorldMenuText.Plain(view.Subtitle);
        if (crumb.Length > 0) lines.Add((null, new Cell { Text = WorldMenuText.Clip(crumb, MaxText + 8), Color = theme.Accent }, null, false));
        if (sub.Length > 0) lines.Add((null, new Cell { Text = WorldMenuText.Clip(sub, MaxText + 8), Color = theme.Muted }, null, false));
        lines.Add((null, null, null, true));

        // ---- rows (always RowsPerPage lines, so pages keep their size)
        var rowStart = lines.Count;
        var rows = view.Rows.Take(Math.Max(1, view.RowsPerPage)).ToList();
        foreach (var r in rows)
        {
            var parsed = WorldMenuText.Split(r.Text);
            var text = WorldMenuText.Clip(parsed.Text, MaxText);
            var tag = r.Tag != null ? WorldMenuText.Plain(r.Tag) : parsed.Tag;
            tag = tag == null ? null : WorldMenuText.Clip(tag, MaxTag);
            var tagColor = r.TagColor ?? parsed.TagColor
                ?? (parsed.OnOff ? (parsed.On ? theme.Good : theme.Bad) : null)
                ?? (r.State == WorldMenuRowState.Disabled ? theme.Disabled : theme.Tag);
            var rowColor = r.State switch
            {
                WorldMenuRowState.Selected => pulse ? theme.SelectedPulse : theme.Selected,
                WorldMenuRowState.Disabled => theme.Disabled,
                WorldMenuRowState.Header => theme.Accent,
                _ => r.Color ?? parsed.Color ?? theme.Text
            };
            // Locked rows keep a hint of their own colour only when it is already dim.
            Cell? num = r.State switch
            {
                WorldMenuRowState.Selected => new Cell { Text = Pointer, Color = theme.Accent },
                WorldMenuRowState.Header => new Cell { Text = Dash, Color = theme.Accent },
                _ when s.ShowNumbers && r.Number != null => new Cell { Text = r.Number, Color = theme.Muted },
                _ => null
            };
            if (r.State == WorldMenuRowState.Header) text = text.ToUpperInvariant();
            lines.Add((num, new Cell { Text = text, Color = rowColor }, tag == null ? null : new Cell { Text = tag, Color = tagColor }, false));
        }

        for (var i = rows.Count; i < Math.Max(1, view.RowsPerPage); i++) lines.Add((null, null, null, false));

        // ---- footer
        if (s.ShowFooter)
        {
            lines.Add((null, null, null, true));
            lines.Add((null, new Cell { Text = view.Footer ?? DefaultFooter(view), Color = theme.Muted }, null, false));
        }

        // ---- columns
        var barX = 0f;
        var numX = s.ShowAccentBar ? 0.75f : 0f;
        var textX = numX + (s.ShowNumbers ? 1.15f : 0.9f);
        var maxText = 0f;
        for (var i = rowStart; i < rowStart + rows.Count; i++)
            if (lines[i].Text is { } t) maxText = Math.Max(maxText, WorldMenuText.Width(t.Text, cw));
        var tagX = textX + maxText + 1.6f;
        var width = 0f;
        foreach (var (_, text, tag, _) in lines)
        {
            if (text != null) width = Math.Max(width, textX + WorldMenuText.Width(text.Text, cw));
            if (tag != null) width = Math.Max(width, tagX + WorldMenuText.Width(tag.Text, cw));
        }

        width = Math.Max(width, textX + 10f) + 0.4f;
        var dashes = (int)Math.Ceiling((width - numX) / WorldMenuText.Width(Dash, cw));
        var divider = string.Concat(Enumerable.Repeat(Dash, Math.Clamp(dashes, 4, 120)));

        var layers = new Dictionary<string, (float X, string[] Lines, string Color, int Alpha, float Depth, bool Bg)>();
        void Put(string key, float x, int line, string text, string color, int alpha = 255, float depth = 0f, bool bg = false)
        {
            if (!layers.TryGetValue(key, out var l))
            {
                l = (x, Enumerable.Repeat(Blank, lines.Count).ToArray(), color, alpha, depth, bg);
                layers[key] = l;
            }

            l.Lines[line] = text;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var (num, text, tag, isDivider) = lines[i];
            if (s.ShowAccentBar) Put("bar", barX, i, Bar, theme.Accent);
            if (isDivider)
            {
                Put("div", numX, i, divider, theme.Divider);
                continue;
            }

            if (num != null) Put("n" + num.Color, numX, i, num.Text, num.Color);
            if (text != null) Put(i >= rowStart && i < rowStart + rows.Count && rows[i - rowStart].State == WorldMenuRowState.Selected ? "sel" : "t" + text.Color, textX, i, text.Text, text.Color);
            if (tag != null) Put("g" + tag.Color, tagX, i, tag.Text, tag.Color);
        }

        // Background plate: one line as wide as the panel on every line, in the panel colour.
        var bgLines = Enumerable.Repeat(divider, lines.Count).ToArray();
        var result = new List<WorldMenuLayer>
        {
            new("bg", numX, string.Join("\n", bgLines), theme.Panel, theme.PanelAlpha, -0.004f, true)
        };
        result.AddRange(layers.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new WorldMenuLayer(kv.Key, kv.Value.X, string.Join("\n", kv.Value.Lines), kv.Value.Color, kv.Value.Alpha, kv.Value.Depth, kv.Value.Bg)));
        return new WorldMenuPanel(result, lines.Count, width);
    }

    public static string DefaultFooter(WorldMenuView v)
    {
        var parts = new List<string> { "[W/S] Move", "[E] Select" };
        if (v.CanGoBack) parts.Add("[R] Back");
        else if (v.CanExit) parts.Add("[R] Close");
        if (v.Pages > 1) parts.Add("[7/8] Page");
        return string.Join("   ", parts);
    }
}
