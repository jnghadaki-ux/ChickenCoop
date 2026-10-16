using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace ChickenCoop.Runtime;

/// <summary>Stock CS2 chicken particle effects (particles/critters/chicken/...).</summary>
public static class Particles
{
    public const string Feathers = "particles/critters/chicken/chicken_gone_feathers.vpcf";
    public const string Dust = "particles/critters/chicken/chicken_crumble_dust.vpcf";
    public const string Burst = "particles/critters/chicken/chicken_impact_burst2.vpcf";
    public const string Dirt = "particles/critters/chicken/chicken_rez_dirt_smoke_puff.vpcf";

    public static readonly string[] All = { Feathers, Dust, Burst, Dirt };

    /// <summary>A one-off effect at a point, removed after <paramref name="seconds"/>.</summary>
    public static void Spawn(BasePlugin plugin, string effect, Vector at, float seconds)
    {
        try
        {
            var fx = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
            if (fx == null) return;
            fx.Teleport(at, new QAngle(0, 0, 0), new Vector(0, 0, 0));
            var kv = new CEntityKeyValues();
            kv.SetString("effect_name", effect);
            kv.SetBool("start_active", true);
            fx.DispatchSpawn(kv);
            fx.AcceptInput("Start");
            plugin.AddTimer(seconds, () =>
            {
                if (fx.IsValid) fx.Remove();
            });
        }
        catch
        {
            // purely cosmetic
        }
    }
}
