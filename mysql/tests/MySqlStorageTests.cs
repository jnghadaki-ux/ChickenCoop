using ChickenCoop.Logic;
using ChickenCoop.MySql;
using MySqlConnector;
using Xunit;

namespace ChickenCoop.MySql.Tests;

/// <summary>
/// Runs against a real MySQL / MariaDB when COOP_MYSQL is set to a connection string, e.g.
/// COOP_MYSQL="Server=localhost;Database=coop_test;User ID=coop;Password=cooppass". Without it the tests pass trivially.
/// </summary>
public sealed class MySqlStorageTests : IDisposable
{
    private static readonly string? Connection = Environment.GetEnvironmentVariable("COOP_MYSQL");
    private readonly string _table = "coop_test_" + Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        if (Connection == null) return;
        using var conn = new MySqlConnection(Connection);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP TABLE IF EXISTS `{_table}`";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Creates_the_table_saves_and_loads()
    {
        if (Connection == null) return;
        var db = new MySqlStorage(Connection, _table, "test");
        Assert.Empty(db.LoadAll());
        db.SaveChanged(new Dictionary<ulong, string> { [76561198000000001] = "{\"a\":1}", [2] = "{\"b\":\"ü ✦ 🐔\"}" });
        db.SaveChanged(new Dictionary<ulong, string> { [2] = "{\"b\":2}" });
        var all = db.LoadAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("{\"a\":1}", all[76561198000000001]);
        Assert.Equal("{\"b\":2}", all[2]);
    }

    [Fact]
    public void Saves_big_batches()
    {
        if (Connection == null) return;
        var db = new MySqlStorage(Connection, _table, "test");
        var many = Enumerable.Range(1, 750).ToDictionary(i => (ulong)i, i => $"{{\"n\":{i}}}");
        db.SaveChanged(many);
        Assert.Equal(750, db.LoadAll().Count);
    }

    [Fact]
    public void Works_end_to_end_with_the_pet_store()
    {
        if (Connection == null) return;
        var dir = Path.Combine(Path.GetTempPath(), "coop-mysql-" + Guid.NewGuid().ToString("N"));
        try
        {
            var db = new MySqlStorage(Connection, _table, "test");
            var store = new PetStore(Path.Combine(dir, "pets.json")) { OnError = ex => throw ex };
            store.Get(1, "Ann").FeedBags = 4; // on pets.json before the database is used
            store.Save();
            store.Flush();
            store.UseBackend(db, PetStore.ReadBackend(db)); // first run: uploads Ann
            store.Get(2, "Bob").Name = "Clucky";
            store.Save();
            store.Flush();

            var fresh = new PetStore(Path.Combine(dir, "other.json"));
            fresh.UseBackend(db, PetStore.ReadBackend(db));
            Assert.True(fresh.TryGet(1, out var ann));
            Assert.Equal(4, ann.FeedBags);
            Assert.True(fresh.TryGet(2, out var bob));
            Assert.Equal("Clucky", bob.Name);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Rejects_odd_table_names()
    {
        Assert.Throws<ArgumentException>(() => new MySqlStorage("Server=x", "pets; DROP TABLE x", "test"));
    }
}
