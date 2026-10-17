using System.Text.RegularExpressions;
using ChickenCoop.Shared;
using MySqlConnector;

namespace ChickenCoop.MySql;

/// <summary>
/// Chicken Coop pets in one MySQL / MariaDB table: one row per player (SteamID64, the pet as JSON, when it changed).
/// Chicken Coop calls this on a background thread. Several servers can share the table, but a player should only play
/// on one of them at a time (the last save wins).
/// </summary>
public sealed partial class MySqlStorage : IChickenCoopStorage
{
    private const int BatchSize = 200;
    private readonly string _connection;
    private readonly string _table;
    private bool _tableReady;

    public MySqlStorage(string connectionString, string table, string name)
    {
        if (!TableName().IsMatch(table)) throw new ArgumentException($"Bad table name \"{table}\" (letters, digits and _ only)");
        _connection = connectionString;
        _table = table;
        Name = name;
    }

    public string Name { get; }

    [GeneratedRegex("^[A-Za-z0-9_]{1,64}$")]
    private static partial Regex TableName();

    public static string ConnectionString(MySqlConfig c) => new MySqlConnectionStringBuilder
    {
        Server = c.Host,
        Port = (uint)Math.Clamp(c.Port, 1, 65535),
        Database = c.Database,
        UserID = c.User,
        Password = c.Password,
        SslMode = Enum.TryParse<MySqlSslMode>(c.SslMode, true, out var ssl) ? ssl : MySqlSslMode.Preferred,
        ConnectionTimeout = 10,
        CharacterSet = "utf8mb4",
    }.ConnectionString;

    private MySqlConnection Open()
    {
        var conn = new MySqlConnection(_connection);
        conn.Open();
        if (_tableReady) return conn;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS `{_table}` (
                  `steamid` BIGINT UNSIGNED NOT NULL PRIMARY KEY,
                  `data` MEDIUMTEXT NOT NULL,
                  `updated` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                ) DEFAULT CHARSET=utf8mb4
                """;
            cmd.ExecuteNonQuery();
        }

        _tableReady = true;
        return conn;
    }

    public IReadOnlyDictionary<ulong, string> LoadAll()
    {
        var result = new Dictionary<ulong, string>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT `steamid`, `data` FROM `{_table}`";
        using var r = cmd.ExecuteReader();
        while (r.Read()) result[r.GetUInt64(0)] = r.GetString(1);
        return result;
    }

    public void SaveChanged(IReadOnlyDictionary<ulong, string> players)
    {
        if (players.Count == 0) return;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var chunk in players.Chunk(BatchSize))
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            var rows = new List<string>();
            for (var i = 0; i < chunk.Length; i++)
            {
                rows.Add($"(@id{i}, @data{i})");
                cmd.Parameters.AddWithValue($"@id{i}", chunk[i].Key);
                cmd.Parameters.AddWithValue($"@data{i}", chunk[i].Value);
            }

            cmd.CommandText = $"INSERT INTO `{_table}` (`steamid`, `data`) VALUES {string.Join(", ", rows)} " +
                              "ON DUPLICATE KEY UPDATE `data` = VALUES(`data`)";
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }
}
