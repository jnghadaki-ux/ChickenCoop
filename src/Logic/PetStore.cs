using System.Text.Json;
using System.Text.Json.Serialization;
using ChickenCoop.Model;
using ChickenCoop.Shared;

namespace ChickenCoop.Logic;

/// <summary>
/// All pets, kept in memory and saved to data/pets.json next to the plugin (and to a storage module such as MySQL when
/// one is loaded). Saving is cheap and safe:
/// <list type="bullet">
/// <item>only players whose data was used since the last save are serialized again, the rest reuse their last JSON;</item>
/// <item>the file is written on a background thread, atomically (temp file + move), keeping the previous file as
/// pets.json.bak, so a crash mid-save never corrupts it;</item>
/// <item>a dated copy goes to data/backups once a day, for admins to restore a player from.</item>
/// </list>
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
    /// <summary>Last saved JSON of each player.</summary>
    private readonly Dictionary<ulong, string> _json = new();
    /// <summary>Players read or changed since the last save (only these can have changed).</summary>
    private readonly HashSet<ulong> _touched = new();
    /// <summary>Changes a storage module has not taken yet (it failed, or is not loaded yet).</summary>
    private readonly Dictionary<ulong, string> _pendingBackend = new();
    private readonly object _writeLock = new();
    private Task _writer = Task.CompletedTask;

    public PetStore(string path) => _path = path;

    public bool Dirty { get; private set; }
    public int Count => _pets.Count;
    public string FilePath => _path;
    public string BackupDirectory => Path.Combine(Path.GetDirectoryName(_path) ?? ".", "backups");

    /// <summary>Daily copies to keep (0 = none).</summary>
    public int KeepDailyBackups { get; set; } = 7;
    /// <summary>Write pets.json even when a storage module is loaded.</summary>
    public bool KeepJsonCopy { get; set; } = true;
    /// <summary>A storage module (MySQL...), or null for pets.json only.</summary>
    public IChickenCoopStorage? Backend { get; private set; }
    /// <summary>Background save errors (the plugin logs them).</summary>
    public Action<Exception>? OnError { get; set; }
    /// <summary>Today's date for the daily backup name; replaceable in tests.</summary>
    public Func<DateTime> Today { get; set; } = () => DateTime.Now.Date;

    public void Load()
    {
        _pets.Clear();
        _json.Clear();
        _touched.Clear();
        foreach (var candidate in new[] { _path, _path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                foreach (var (id, pet, raw) in ReadFile(candidate))
                {
                    _pets[id] = pet;
                    _json[id] = raw;
                }

                return;
            }
            catch (JsonException)
            {
                _pets.Clear();
                _json.Clear(); // fall through to the backup
            }
        }
    }

    /// <summary>Reads a pets file: each player, with the JSON it was saved as.</summary>
    private static IEnumerable<(ulong Id, PetData Pet, string Raw)> ReadFile(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var list = new List<(ulong, PetData, string)>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var pet = el.Deserialize<PetData>(Json);
            if (pet == null || pet.SteamId == 0) continue;
            PetLogic.Normalize(pet);
            list.Add((pet.SteamId, pet, el.GetRawText()));
        }

        return list;
    }

    public PetData Get(ulong steamId, string ownerName)
    {
        _touched.Add(steamId);
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

    public bool TryGet(ulong steamId, out PetData pet)
    {
        if (!_pets.TryGetValue(steamId, out pet!)) return false;
        _touched.Add(steamId);
        return true;
    }

    /// <summary>Replaces a player's record (admin restore from a backup).</summary>
    public void Put(PetData pet)
    {
        _pets[pet.SteamId] = pet;
        _touched.Add(pet.SteamId);
        Dirty = true;
    }

    public void MarkDirty() => Dirty = true;

    public IEnumerable<PetData> All => _pets.Values;

    /// <summary>
    /// Serializes the players that may have changed (on the calling thread, which owns the data) and writes on a
    /// background thread. <paramref name="force"/> re-serializes everyone and writes even if nothing changed.
    /// </summary>
    public void Save(bool force = false)
    {
        bool pending;
        lock (_writeLock) pending = Backend != null && _pendingBackend.Count > 0;
        if (!Dirty && !force && _touched.Count == 0 && !pending) return;
        var changed = new Dictionary<ulong, string>();
        foreach (var id in force ? _pets.Keys.ToList() : _touched.ToList())
        {
            if (!_pets.TryGetValue(id, out var pet)) continue;
            var text = JsonSerializer.Serialize(pet, Json);
            if (_json.TryGetValue(id, out var old) && old == text) continue;
            _json[id] = text;
            changed[id] = text;
        }

        _touched.Clear();
        Dirty = false;
        Dictionary<ulong, string>? toBackend = null;
        if (Backend != null)
        {
            lock (_writeLock)
            {
                foreach (var (id, text) in changed) _pendingBackend[id] = text;
                if (_pendingBackend.Count > 0) toBackend = new Dictionary<ulong, string>(_pendingBackend);
            }
        }

        if (changed.Count == 0 && !force && toBackend == null) return;
        string? file = null;
        if ((Backend == null || KeepJsonCopy) && (changed.Count > 0 || force)) file = "[\n" + string.Join(",\n", _json.Values) + "\n]";

        Enqueue(file, Backend, toBackend, Today());
    }

    private void Enqueue(string? file, IChickenCoopStorage? backend, Dictionary<ulong, string>? toBackend, DateTime today)
    {
        lock (_writeLock)
        {
            _writer = _writer.ContinueWith(_ =>
            {
                if (file != null)
                {
                    try
                    {
                        WriteFile(file, today);
                    }
                    catch (Exception ex)
                    {
                        OnError?.Invoke(ex);
                    }
                }

                if (backend == null || toBackend == null || toBackend.Count == 0) return;
                try
                {
                    backend.SaveChanged(toBackend);
                    lock (_writeLock)
                        foreach (var (id, text) in toBackend)
                            if (_pendingBackend.TryGetValue(id, out var now) && now == text)
                                _pendingBackend.Remove(id);
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex); // still pending: sent again with the next save
                }
            }, TaskScheduler.Default);
        }
    }

    private void WriteFile(string text, DateTime today)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, text);
        if (File.Exists(_path)) File.Copy(_path, _path + ".bak", true);
        File.Move(tmp, _path, true);

        if (KeepDailyBackups <= 0) return;
        Directory.CreateDirectory(BackupDirectory);
        var daily = Path.Combine(BackupDirectory, $"pets-{PetLogic.Day(today)}.json");
        if (!File.Exists(daily)) File.Copy(_path, daily);
        foreach (var old in DailyFiles().Skip(KeepDailyBackups)) File.Delete(old);
    }

    private IEnumerable<string> DailyFiles() =>
        Directory.Exists(BackupDirectory)
            ? Directory.GetFiles(BackupDirectory, "pets-*.json").OrderByDescending(f => f, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    /// <summary>Waits for background writes to finish (map end, unload, tests).</summary>
    public void Flush(int timeoutMs = 10000)
    {
        Task t;
        lock (_writeLock) t = _writer;
        t.Wait(timeoutMs);
    }

    // ================================================================ backups

    /// <summary>Backups a player can be restored from, newest first: "bak" (the previous save), then dates.</summary>
    public List<string> Backups()
    {
        var list = new List<string>();
        if (File.Exists(_path + ".bak")) list.Add("bak");
        list.AddRange(DailyFiles().Select(f => Path.GetFileNameWithoutExtension(f)["pets-".Length..]));
        return list;
    }

    /// <summary>A player's record from a backup ("bak" or a date from <see cref="Backups"/>), or null.</summary>
    public PetData? FromBackup(ulong steamId, string backup)
    {
        var key = backup.Trim();
        var file = key.Equals("bak", StringComparison.OrdinalIgnoreCase)
            ? _path + ".bak"
            : Path.Combine(BackupDirectory, $"pets-{key}.json");
        if (key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || key.Contains("..") || !File.Exists(file)) return null;
        try
        {
            return ReadFile(file).Where(x => x.Id == steamId).Select(x => x.Pet).FirstOrDefault();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ================================================================ storage modules

    /// <summary>Reads everything from a storage module (call on a background thread).</summary>
    public static Dictionary<ulong, PetData> ReadBackend(IChickenCoopStorage backend)
    {
        var result = new Dictionary<ulong, PetData>();
        foreach (var (id, json) in backend.LoadAll())
        {
            try
            {
                var pet = JsonSerializer.Deserialize<PetData>(json, Json);
                if (pet == null) continue;
                pet.SteamId = id;
                PetLogic.Normalize(pet);
                result[id] = pet;
            }
            catch (JsonException)
            {
                // one bad row does not stop the rest
            }
        }

        return result;
    }

    /// <summary>
    /// Switches to a storage module, with what it loaded. For each player the record with more play time wins (play time
    /// only goes up, so it is the newer one): normally the module's, but pets.json's if the database was unreachable
    /// last session. Players it lacks (all of them on the first run) and newer local records are uploaded next save.
    /// </summary>
    public void UseBackend(IChickenCoopStorage backend, Dictionary<ulong, PetData> loaded)
    {
        var upload = new List<ulong>();
        foreach (var (id, pet) in loaded)
        {
            if (_pets.TryGetValue(id, out var local) && local.PlayedSeconds > pet.PlayedSeconds)
            {
                upload.Add(id);
                continue;
            }

            _pets[id] = pet;
            _json[id] = JsonSerializer.Serialize(pet, Json);
        }

        upload.AddRange(_pets.Keys.Where(id => !loaded.ContainsKey(id)));
        Backend = backend;
        lock (_writeLock)
            foreach (var id in upload)
                _pendingBackend[id] = _json.TryGetValue(id, out var j) ? j : JsonSerializer.Serialize(_pets[id], Json);
        Dirty = true;
    }
}
