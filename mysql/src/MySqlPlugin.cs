using ChickenCoop.Shared;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;

namespace ChickenCoop.MySql;

/// <summary>
/// Chicken Coop MySQL, by Gdaki: stores Chicken Coop pets in MySQL or MariaDB instead of only pets.json. Load it next
/// to Chicken Coop; on the first run it uploads everyone from pets.json, after that the database is the main copy and
/// pets.json a local backup.
/// </summary>
[MinimumApiVersion(300)]
public sealed class MySqlPlugin : BasePlugin, IPluginConfig<MySqlConfig>
{
    public override string ModuleName => "Chicken Coop MySQL";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Gdaki";
    public override string ModuleDescription => "MySQL / MariaDB storage for Chicken Coop. Co-authored with Claude (Anthropic).";

    public MySqlConfig Config { get; set; } = new();

    public void OnConfigParsed(MySqlConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        if (string.IsNullOrWhiteSpace(Config.Password))
            Logger.LogWarning("Chicken Coop MySQL: no password set in configs/plugins/ChickenCoop.MySql/ChickenCoop.MySql.json");
        try
        {
            var storage = new MySqlStorage(MySqlStorage.ConnectionString(Config), Config.Table, $"MySQL ({Config.Database}.{Config.Table}@{Config.Host})");
            Capabilities.RegisterPluginCapability(IChickenCoopStorage.Capability, () => storage);
            Logger.LogInformation("Chicken Coop MySQL ready: {Name}", storage.Name);
            if (hotReload) Logger.LogWarning("Chicken Coop MySQL was loaded late: reload Chicken Coop (or change map after a restart) to use it");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Chicken Coop MySQL is not set up right; Chicken Coop keeps using pets.json");
        }
    }
}
