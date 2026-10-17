using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Swbs.Common;

/// <summary>
/// One point_worldtext of a panel: a column of text in one colour (every layer has the same number of lines). X is the
/// left edge, or the right edge for a <see cref="Right"/>-aligned column.
/// </summary>
public sealed record WorldMenuLayer(string Key, float X, string Text, string Color, int Alpha, float Depth, bool Background, bool Right = false);

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

    /// <summary>Word-wraps to lines no wider than <paramref name="maxEm"/> (estimated); long words are clipped.</summary>
    public static List<string> Wrap(string? text, float maxEm, float charWidth = 0.56f)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in Plain(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = line.Length == 0 ? word : line + " " + word;
            if (Width(next, charWidth) <= maxEm || line.Length == 0)
            {
                line = next;
                continue;
            }

            lines.Add(line);
            line = word;
        }

        if (line.Length > 0) lines.Add(line);
        return lines.Select(l => Width(l, charWidth) <= maxEm ? l : Clip(l, Math.Max(4, (int)(maxEm / (0.6f * charWidth / 0.56f))))).ToList();
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

        // ---- tab bar (drawn separately below: one entity per tab, the active one on an accent plate)
        var tabs = view.Tabs.Select(t => WorldMenuText.Clip(WorldMenuText.Plain(t), 14)).Take(7).ToList();
        var tabLine = -1;
        if (tabs.Count > 0)
        {
            tabLine = lines.Count;
            lines.Add((null, null, null, false));
        }

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

        const float tabGap = 1.3f;
        var tabXs = new List<float>();
        var tx = numX;
        foreach (var t in tabs)
        {
            tabXs.Add(tx);
            tx += WorldMenuText.Width(t, cw) + tabGap;
        }

        if (tabs.Count > 0) width = Math.Max(width, tx);
        width = Math.Max(width, textX + 10f) + 0.4f;
        var dashes = (int)Math.Ceiling((width - numX) / WorldMenuText.Width(Dash, cw));
        var divider = string.Concat(Enumerable.Repeat(Dash, Math.Clamp(dashes, 4, 120)));

        var layers = new Dictionary<string, (float X, string[] Lines, string Color, int Alpha, float Depth, bool Bg, bool Right)>();
        void Put(string key, float x, int line, string text, string color, int alpha = 255, float depth = 0f, bool bg = false, bool right = false)
        {
            if (!layers.TryGetValue(key, out var l))
            {
                l = (x, Enumerable.Repeat(Blank, lines.Count).ToArray(), color, alpha, depth, bg, right);
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

        for (var i = 0; i < tabs.Count; i++)
        {
            var active = i == view.ActiveTab;
            Put("tab" + i, tabXs[i], tabLine, tabs[i], active ? theme.Panel : theme.Muted);
            if (active) Put("tabbg", tabXs[i], tabLine, tabs[i], theme.Accent, 235, -0.002f, true);
        }

        // ---- details pane (the row under the cursor), beside the list on its own plate
        if (view.Details is { } d)
        {
            var paneX = width + 0.9f;
            var title2 = WorldMenuText.Clip(WorldMenuText.Plain(d.Title), 26);
            var stats = d.Stats.Take(4).Select(kv => (K: WorldMenuText.Clip(WorldMenuText.Plain(kv.Key), 16), V: WorldMenuText.Clip(WorldMenuText.Plain(kv.Value), 16))).ToList();
            var paneW = 13f;
            paneW = Math.Max(paneW, WorldMenuText.Width(title2, cw));
            foreach (var (k, v) in stats) paneW = Math.Max(paneW, WorldMenuText.Width(k, cw) + WorldMenuText.Width(v, cw) + 1.5f);
            paneW = Math.Min(paneW, 18f);

            var li = 0;
            var last = lines.Count - 1;
            Put("p" + theme.Title, paneX, li++, title2, theme.Title);
            if (!string.IsNullOrWhiteSpace(d.Subtitle)) Put("p" + theme.Accent, paneX, li++, WorldMenuText.Clip(WorldMenuText.Plain(d.Subtitle), 30), theme.Accent);
            var actions = string.IsNullOrWhiteSpace(d.Actions) ? null : WorldMenuText.Clip(WorldMenuText.Plain(d.Actions), 34);
            // Room for the body: everything except the stats, a blank line before them and the actions line.
            var bodyRoom = last - li - (stats.Count > 0 ? stats.Count + 1 : 0) - (actions != null ? 1 : 0);
            var body = WorldMenuText.Wrap(d.Body, paneW, cw);
            if (body.Count > Math.Max(0, bodyRoom))
            {
                body = body.Take(Math.Max(0, bodyRoom)).ToList();
                if (body.Count > 0) body[^1] = WorldMenuText.Clip(body[^1].TrimEnd('.') + "…", 60);
            }

            foreach (var b in body) Put("p" + theme.Tag, paneX, li++, b, theme.Tag);
            if (stats.Count > 0 && li < last)
            {
                li++;
                foreach (var (k, v) in stats)
                {
                    if (li >= last + (actions == null ? 1 : 0)) break;
                    Put("p" + theme.Muted, paneX, li, k, theme.Muted);
                    Put("pv" + theme.Text, paneX + paneW, li, v, theme.Text, right: true);
                    li++;
                }
            }

            if (actions != null) Put("p" + theme.Accent, paneX, last, actions, theme.Accent);

            var paneDashes = (int)Math.Ceiling((paneW + 0.7f) / WorldMenuText.Width(Dash, cw));
            var paneFill = string.Concat(Enumerable.Repeat(Dash, Math.Clamp(paneDashes, 4, 80)));
            for (var i = 0; i < lines.Count; i++)
            {
                Put("dbg", paneX - 0.35f, i, paneFill, theme.Panel, Math.Min(255, theme.PanelAlpha + 15), -0.004f, true);
                Put("dbar", paneX - 0.65f, i, "│", theme.Accent);
            }

            width = paneX + paneW + 0.35f;
        }

        // Background plate: one line as wide as the panel on every line, in the panel colour.
        var bgLines = Enumerable.Repeat(divider, lines.Count).ToArray();
        var result = new List<WorldMenuLayer>
        {
            new("bg", numX, string.Join("\n", bgLines), theme.Panel, theme.PanelAlpha, -0.004f, true)
        };
        result.AddRange(layers.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new WorldMenuLayer(kv.Key, kv.Value.X, string.Join("\n", kv.Value.Lines), kv.Value.Color, kv.Value.Alpha, kv.Value.Depth, kv.Value.Bg, kv.Value.Right)));
        return new WorldMenuPanel(result, lines.Count, width);
    }

    public static string DefaultFooter(WorldMenuView v)
    {
        var parts = new List<string>();
        if (v.Adjustable) parts.Add("[A/D] Change");
        else if (v.Tabs.Count > 1) parts.Add("[A/D] Tab");
        parts.Add("[W/S] Move");
        parts.Add("[E] Select");
        if (v.CanGoBack) parts.Add("[R] Back");
        else if (v.CanExit) parts.Add("[R] Close");
        if (v.Pages > 1) parts.Add("[7/8] Page");
        return string.Join("   ", parts);
    }
}
