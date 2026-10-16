using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;

namespace ChickenCoop.Shared;

/// <summary>
/// The Chicken Coop plugin API, by Gdaki. Other CounterStrikeSharp plugins can read pets and hand out rewards.
/// <code>
/// var coop = IChickenCoopApi.Capability.Get();   // null when Chicken Coop is not loaded
/// coop?.GiveFeedBags(player, 1);
/// </code>
/// Reference ChickenCoop.Shared.dll (do not copy it next to your plugin: it lives in counterstrikesharp/shared/).
/// </summary>
public interface IChickenCoopApi
{
    public static readonly PluginCapability<IChickenCoopApi> Capability = new("chickencoop:api");

    /// <summary>0 = no pet, 1 = egg, 2 = chick, 3 = pullet, 4 = hen.</summary>
    int GetStage(CCSPlayerController player);

    /// <summary>The pet's name, or null (no pet, or still an egg).</summary>
    string? GetPetName(CCSPlayerController player);

    /// <summary>"Silkie", "Polish", "Catalana", or null before the pullet stage.</summary>
    string? GetBreed(CCSPlayerController player);

    /// <summary>The shiny variant id (golden, rainbow, shadow, celestial) once revealed, else null.</summary>
    string? GetShiny(CCSPlayerController player);

    /// <summary>True while the player's pet (or egg) is out in the world.</summary>
    bool IsPetOut(CCSPlayerController player);

    int GetFeedBags(CCSPlayerController player);

    /// <summary>Adds (or with a negative amount removes) feed bags, capped by the config. Returns the new count.</summary>
    int GiveFeedBags(CCSPlayerController player, int amount);

    /// <summary>Adds eggs to the player's basket (capped). Returns how many were added.</summary>
    int GiveBasketEggs(CCSPlayerController player, int amount);

    /// <summary>Gives a new egg to a player without a pet. False if they already have one.</summary>
    bool GiveEgg(CCSPlayerController player);

    /// <summary>Unlocks a hat (sprout, party, glasses, helmet, armor_helmet, nose_glasses, banana, top_hat, wizard_hat,
    /// alien, snowman). False for an unknown id.</summary>
    bool UnlockHat(CCSPlayerController player, string hatId);

    /// <summary>
    /// Keeps a player's pet out of the world until released, without changing their saved settings. Use it for modes
    /// where a pet would be in the way (a duel, a special round). Pass false to release.
    /// </summary>
    void SuppressPet(CCSPlayerController player, bool suppress);

    /// <summary>Keeps every pet out of the world until released (e.g. during a special round).</summary>
    void SuppressAll(bool suppress);

    /// <summary>Fired when a pet reaches a new stage: (owner, new stage 1-4).</summary>
    event Action<CCSPlayerController, int>? StageChanged;

    /// <summary>Fired when a pet is killed: (owner, killer or null).</summary>
    event Action<CCSPlayerController, CCSPlayerController?>? PetKilled;
}
