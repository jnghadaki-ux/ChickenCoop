using CounterStrikeSharp.API.Core;

namespace ChickenCoop.MySql;

/// <summary>configs/plugins/ChickenCoop.MySql/ChickenCoop.MySql.json, created on first load.</summary>
public sealed class MySqlConfig : BasePluginConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "chickencoop";
    public string User { get; set; } = "chickencoop";
    public string Password { get; set; } = "";
    /// <summary>One table, created if missing. Servers sharing a database can use one table each, or share one.</summary>
    public string Table { get; set; } = "chickencoop_pets";
    /// <summary>None, Preferred, Required, VerifyCA or VerifyFull.</summary>
    public string SslMode { get; set; } = "Preferred";
}
