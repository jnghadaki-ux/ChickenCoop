using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API.Modules.Utils;

namespace ChickenCoop.Runtime;

/// <summary>
/// Messages from lang/&lt;language&gt;.json. Our own tiny loader instead of the CSS localizer, because that one runs
/// string.Format over the text and chokes on colour tags like {green}. Placeholders are {0}, {1}, ...
/// </summary>
public sealed class Lang
{
    private static readonly Dictionary<string, char> Colors = typeof(ChatColors)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(char))
        .ToDictionary(f => f.Name.ToLowerInvariant(), f => (char)f.GetValue(null)!);

    private Dictionary<string, string> _strings = new();
    private Dictionary<string, string> _fallback = new();

    public void Load(string langDir, string language)
    {
        _fallback = Read(Path.Combine(langDir, "en.json"));
        var lang = (language ?? "en").Trim().ToLowerInvariant();
        _strings = lang == "en" ? _fallback : Read(Path.Combine(langDir, lang + ".json"));
    }

    private static Dictionary<string, string> Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    /// <summary>The text for a key with {0}.. filled in and colour tags turned into chat colours.</summary>
    public string T(string key, params object?[] args) => Colorize(Raw(key, args));

    /// <summary>Same as <see cref="T"/> without colours (centre text, menus, world text).</summary>
    public string Plain(string key, params object?[] args) => StripTags(Raw(key, args));

    private string Raw(string key, object?[] args)
    {
        if (!_strings.TryGetValue(key, out var text) && !_fallback.TryGetValue(key, out text)) text = key;
        for (var i = 0; i < args.Length; i++) text = text.Replace("{" + i + "}", args[i]?.ToString() ?? "");
        return text;
    }

    public static string Colorize(string text)
    {
        if (text.IndexOf('{') < 0) return text;
        foreach (var (name, ch) in Colors) text = text.Replace("{" + name + "}", ch.ToString(), StringComparison.OrdinalIgnoreCase);
        return text;
    }

    public static string StripTags(string text)
    {
        if (text.IndexOf('{') < 0) return text;
        foreach (var name in Colors.Keys) text = text.Replace("{" + name + "}", "", StringComparison.OrdinalIgnoreCase);
        return text;
    }
}
