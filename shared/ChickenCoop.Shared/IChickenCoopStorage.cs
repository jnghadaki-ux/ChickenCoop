using CounterStrikeSharp.API.Core.Capabilities;

namespace ChickenCoop.Shared;

/// <summary>
/// Where Chicken Coop keeps pets, for storage modules (a MySQL module ships with Chicken Coop). Register it as the
/// "chickencoop:storage" capability from your plugin's Load; Chicken Coop picks it up once all plugins have loaded and
/// keeps pets.json as a local copy. Every call runs on a background thread, so never touch game objects in them.
/// Each player is one JSON document keyed by SteamID64; treat the JSON as opaque.
/// </summary>
public interface IChickenCoopStorage
{
    public static readonly PluginCapability<IChickenCoopStorage> Capability = new("chickencoop:storage");

    /// <summary>Shown in the server log, e.g. "MySQL (pets@db.example.com)".</summary>
    string Name { get; }

    /// <summary>Every saved player. An empty result makes Chicken Coop upload everything it has (first run).</summary>
    IReadOnlyDictionary<ulong, string> LoadAll();

    /// <summary>Players whose data changed since the last save. Throw on failure: the same players are sent again next time.</summary>
    void SaveChanged(IReadOnlyDictionary<ulong, string> players);
}
