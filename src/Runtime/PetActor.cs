using ChickenCoop.Model;
using CounterStrikeSharp.API.Core;

namespace ChickenCoop.Runtime;

/// <summary>
/// The entities one owner's pet has in the world right now. The CSS wrappers resolve their pointer from the entity
/// handle on every access, so holding them is safe: IsValid turns false once the game removes the entity (round restart,
/// death).
/// </summary>
public sealed class PetActor
{
    public PetActor(int slot, ulong steamId)
    {
        Slot = slot;
        SteamId = steamId;
    }

    public int Slot { get; }
    public ulong SteamId { get; }

    public CChicken? Chicken;
    public CBaseModelEntity? Hat;
    public CPointWorldText? Tag;
    public CBaseModelEntity? Nest;
    public CBaseModelEntity? Egg;
    public CBaseModelEntity? Feed;

    /// <summary>What the spawned entities currently show, to know when to rebuild them.</summary>
    public PetStage Shown;
    public string? ShownHat;
    public string? ShownTag;

    public bool Staying;
    public bool Hatching;
    public bool DiedThisRound;
    public int KillerSlot = -1;

    public float SpawnedAt;
    public float LastProgressAt;
    public float LastDistance;
    public float NextIdleSound;
    public float NextHungrySound;
    public float NextEggWiggle;
    public float TrickReadyAt;
    public float PetReadyAt;
    public float FeedReadyAt;
    public float FeedingUntil;
    public bool UseWasDown;

    public bool ChickenAlive => Chicken is { IsValid: true };
    public bool EggOut => Egg is { IsValid: true };
    public bool Out => ChickenAlive || EggOut;
}
