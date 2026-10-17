using ChickenCoop.Logic;
using ChickenCoop.Model;
using ChickenCoop.Shared;
using Xunit;

namespace ChickenCoop.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coop-tests-" + Guid.NewGuid().ToString("N"));

    private string File(string name = "pets.json") => Path.Combine(_dir, name);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private PetStore Store(DateTime? today = null)
    {
        var s = new PetStore(File()) { OnError = ex => throw new Exception("save failed", ex) };
        if (today != null) s.Today = () => today.Value;
        return s;
    }

    private sealed class FakeBackend : IChickenCoopStorage
    {
        public Dictionary<ulong, string> Rows { get; } = new();
        public List<int> Batches { get; } = new();
        public bool Fail { get; set; }
        public string Name => "fake";
        public IReadOnlyDictionary<ulong, string> LoadAll() => new Dictionary<ulong, string>(Rows);

        public void SaveChanged(IReadOnlyDictionary<ulong, string> players)
        {
            if (Fail) throw new InvalidOperationException("down");
            Batches.Add(players.Count);
            foreach (var (k, v) in players) Rows[k] = v;
        }
    }

    [Fact]
    public void Saves_and_loads_every_player()
    {
        var s = Store();
        s.Get(1, "Ann").FeedBags = 4;
        s.Get(2, "Bob").Name = "Clucky";
        s.Save();
        s.Flush();

        var again = Store();
        again.Load();
        Assert.Equal(2, again.Count);
        Assert.True(again.TryGet(1, out var ann));
        Assert.Equal(4, ann.FeedBags);
        Assert.True(again.TryGet(2, out var bob));
        Assert.Equal("Clucky", bob.Name);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_the_backup()
    {
        var s = Store();
        s.Get(1, "Ann").FeedBags = 1;
        s.Save();
        s.Flush();
        s.Get(1, "Ann").FeedBags = 2;
        s.Save();
        s.Flush();
        System.IO.File.WriteAllText(File(), "{ not json");

        var again = Store();
        again.Load();
        Assert.True(again.TryGet(1, out var ann));
        Assert.Equal(1, ann.FeedBags); // the previous save, from pets.json.bak
    }

    [Fact]
    public void Daily_backups_are_kept_and_pruned_and_players_restore_from_them()
    {
        var day = new DateTime(2026, 10, 1);
        var s = Store(day);
        s.KeepDailyBackups = 3;
        for (var i = 0; i < 5; i++)
        {
            var d = day.AddDays(i);
            s.Today = () => d;
            s.Get(7, "Cat").FeedBags = i;
            s.Save();
            s.Flush();
        }

        var backups = s.Backups();
        Assert.Equal(new[] { "bak", "2026-10-05", "2026-10-04", "2026-10-03" }, backups);
        Assert.Equal(2, s.FromBackup(7, "2026-10-03")!.FeedBags);
        Assert.Equal(3, s.FromBackup(7, "bak")!.FeedBags);
        Assert.Null(s.FromBackup(8, "2026-10-03"));
        Assert.Null(s.FromBackup(7, "2026-09-01"));
        Assert.Null(s.FromBackup(7, "../../etc/passwd"));
    }

    [Fact]
    public void Only_players_used_since_the_last_save_are_written_again()
    {
        var backend = new FakeBackend();
        var s = Store();
        s.UseBackend(backend, new Dictionary<ulong, PetData>());
        s.Get(1, "Ann");
        s.Get(2, "Bob");
        s.Save();
        s.Flush();
        Assert.Equal(new[] { 2 }, backend.Batches);

        s.Get(1, "Ann").FeedBags = 9; // only Ann changes
        s.Save();
        s.Flush();
        Assert.Equal(new[] { 2, 1 }, backend.Batches);

        s.Get(2, "Bob"); // read but unchanged: nothing to send
        s.Save();
        s.Flush();
        Assert.Equal(new[] { 2, 1 }, backend.Batches);
        Assert.True(System.IO.File.Exists(File())); // the JSON copy is still written
    }

    [Fact]
    public void A_storage_module_gets_everything_on_first_run_and_retries_after_a_failure()
    {
        var s = Store();
        var errors = 0;
        s.OnError = _ => errors++;
        s.Get(1, "Ann").FeedBags = 3;
        s.Get(2, "Bob");
        s.Save();
        s.Flush();

        var backend = new FakeBackend { Fail = true };
        s.UseBackend(backend, PetStore.ReadBackend(backend)); // empty database: upload everyone
        s.Save();
        s.Flush();
        Assert.Empty(backend.Rows);
        Assert.Equal(1, errors);

        backend.Fail = false;
        s.Save();
        s.Flush();
        Assert.Equal(2, backend.Rows.Count);

        var loaded = PetStore.ReadBackend(backend);
        Assert.Equal(3, loaded[1].FeedBags);
    }

    [Fact]
    public void The_storage_module_wins_over_pets_json()
    {
        var s = Store();
        s.Get(1, "Ann").FeedBags = 1;
        var backend = new FakeBackend();
        backend.Rows[1] = "{\"SteamId\":1,\"OwnerName\":\"Ann\",\"FeedBags\":8}";
        s.UseBackend(backend, PetStore.ReadBackend(backend));
        Assert.True(s.TryGet(1, out var ann));
        Assert.Equal(8, ann.FeedBags);
    }

    [Fact]
    public void A_newer_local_record_beats_a_stale_database_row()
    {
        var s = Store();
        s.Get(1, "Ann").PlayedSeconds = 5000; // played while the database was down
        s.Get(1, "Ann").FeedBags = 7;
        s.Get(2, "Bob").PlayedSeconds = 100;
        var backend = new FakeBackend();
        backend.Rows[1] = "{\"SteamId\":1,\"PlayedSeconds\":4000,\"FeedBags\":2}";
        backend.Rows[2] = "{\"SteamId\":2,\"PlayedSeconds\":900,\"FeedBags\":3}";
        s.UseBackend(backend, PetStore.ReadBackend(backend));
        Assert.True(s.TryGet(1, out var ann));
        Assert.Equal(7, ann.FeedBags);
        Assert.True(s.TryGet(2, out var bob));
        Assert.Equal(3, bob.FeedBags);
        s.Save();
        s.Flush();
        Assert.Contains("\"FeedBags\": 7", backend.Rows[1]); // uploaded
    }

    [Fact]
    public void Old_records_get_their_play_time_filled_in()
    {
        System.IO.Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File(), "[{\"SteamId\":5,\"Stage\":\"Hen\",\"LifeSeconds\":3600,\"Happiness\":250}]");
        var s = Store();
        s.Load();
        Assert.True(s.TryGet(5, out var p));
        Assert.Equal(3600, p.PlayedSeconds);
        Assert.Equal(100, p.Happiness);
        Assert.Equal(PetStage.Hen, p.Stage);
    }
}
