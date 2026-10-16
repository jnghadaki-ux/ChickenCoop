using System.Text.Json;
using System.Text.Json.Serialization;
using ChickenCoop.Model;

namespace ChickenCoop.Logic;

/// <summary>
/// All pets in one JSON file (data/pets.json next to the plugin). Small enough to keep in memory; written atomically
/// (temp file + move) so a crash mid-save never corrupts it. A copy of the previous file is kept as pets.json.bak.
/// </summary>
public sealed class PetStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly Dictionary<ulong, PetData> _pets = new();
    public bool Dirty { get; private set; }

    public PetStore(string path) => _path = path;

    public int Count => _pets.Count;

    public void Load()
    {
        _pets.Clear();
        foreach (var candidate in new[] { _path, _path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var list = JsonSerializer.Deserialize<List<PetData>>(File.ReadAllText(candidate), Json) ?? new();
                foreach (var p in list.Where(p => p.SteamId != 0)) _pets[p.SteamId] = p;
                return;
            }
            catch (JsonException)
            {
                // fall through to the backup
            }
        }
    }

    public PetData Get(ulong steamId, string ownerName)
    {
        if (!_pets.TryGetValue(steamId, out var p))
        {
            p = new PetData { SteamId = steamId, OwnerName = ownerName };
            _pets[steamId] = p;
            Dirty = true;
        }
        else if (ownerName.Length > 0 && p.OwnerName != ownerName)
        {
            p.OwnerName = ownerName;
            Dirty = true;
        }

        return p;
    }

    public bool TryGet(ulong steamId, out PetData pet) => _pets.TryGetValue(steamId, out pet!);

    public void MarkDirty() => Dirty = true;

    public IEnumerable<PetData> All => _pets.Values;

    public void Save(bool force = false)
    {
        if (!Dirty && !force) return;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_pets.Values.ToList(), Json));
        if (File.Exists(_path)) File.Copy(_path, _path + ".bak", true);
        File.Move(tmp, _path, true);
        Dirty = false;
    }
}
