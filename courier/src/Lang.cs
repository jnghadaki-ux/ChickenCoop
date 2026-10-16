using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Utils;

namespace ChickenCourier;

/// <summary>
/// Messages from lang/*.json, per player: each player gets their own CS2 language (the CSS !lang setting) when a file
/// for it exists, otherwise the configured fallback, otherwise English. Our own loader instead of the CSS localizer,
/// because that one runs string.Format over the text and chokes on colour tags like {green}. Placeholders: {0}, {1}...
/// </summary>
public sealed class Lang
{
    private static readonly Dictionary<string, char> Colors = typeof(ChatColors)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(char))
        .ToDictionary(f => f.Name.ToLowerInvariant(), f => (char)f.GetValue(null)!);

    private readonly Dictionary<string, Dictionary<string, string>> _files = new(StringComparer.OrdinalIgnoreCase);
    private string _fallback = "en";

    public IReadOnlyCollection<string> Languages => _files.Keys;

    public void Load(string langDir, string fallback)
    {
        _files.Clear();
        _fallback = (fallback ?? "en").Trim().ToLowerInvariant();
        if (!Directory.Exists(langDir)) return;
        foreach (var file in Directory.GetFiles(langDir, "*.json"))
        {
            try
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                if (map != null) _files[Path.GetFileNameWithoutExtension(file).ToLowerInvariant()] = map;
            }
            catch (JsonException)
            {
                // a broken translation file falls back to English
            }
        }
    }

    /// <summary>Loads from an in-memory set (tests).</summary>
    public void LoadFrom(Dictionary<string, Dictionary<string, string>> files, string fallback)
    {
        _files.Clear();
        foreach (var (k, v) in files) _files[k] = v;
        _fallback = fallback;
    }

    public static string LanguageOf(CCSPlayerController? player)
    {
        if (player is not { IsValid: true } || player.IsBot) return "";
        try
        {
            return player.GetLanguage().TwoLetterISOLanguageName.ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Text for a player (their language) with {0}.. filled in and colour tags turned into chat colours.</summary>
    public string T(CCSPlayerController? player, string key, params object?[] args) => Colorize(Raw(LanguageOf(player), key, args));

    /// <summary>Same as <see cref="T"/> without colour tags (centre text, menus, world text, console).</summary>
    public string P(CCSPlayerController? player, string key, params object?[] args) => StripTags(Raw(LanguageOf(player), key, args));

    public string Raw(string language, string key, params object?[] args)
    {
        var text = Find(language, key) ?? Find(_fallback, key) ?? Find("en", key) ?? key;
        for (var i = 0; i < args.Length; i++) text = text.Replace("{" + i + "}", args[i]?.ToString() ?? "");
        return text;
    }

    private string? Find(string language, string key)
    {
        if (language.Length == 0 || !_files.TryGetValue(language, out var map)) return null;
        return map.TryGetValue(key, out var text) ? text : null;
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
