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

    /// <summary>Shoulder perch: a model copy riding on the owner (the chicken itself is removed while perched).</summary>
    public CBaseModelEntity? Perch;
    public CBaseModelEntity? PerchHat;

    /// <summary>Laid eggs lying in the world, waiting to be picked up.</summary>
    public readonly List<CBaseModelEntity> LaidEggs = new();

    /// <summary>What the spawned entities currently show, to know when to rebuild them.</summary>
    public PetStage Shown;
    public string? ShownHat;
    public string? ShownTag;
    public string? ShownStyle;

    public bool Staying;
    public bool Perched;
    public bool Hatching;
    public bool DiedThisRound;
    public int KillerSlot = -1;

    public float SpawnedAt;
    public float LastProgressAt;
    public float LastDistance;
    public float NextIdleSound;
    public float NextHungrySound;
    public float NextEggWiggle;
    public float NextBirdWatch;
    public float NextShinyFx;
    public float EmoteReadyAt;
    public float PetReadyAt;
    public float FeedReadyAt;
    /// <summary>Feeding or an emote: the pet stays put (no leader) until then.</summary>
    public float BusyUntil;
    /// <summary>Moods and abilities: next spontaneous trick or sulk, next sentry/lookout warning, next cheer.</summary>
    public float NextMoodAct;
    public float NextWarn;
    public float CheerReadyAt;
    /// <summary>When petting, emotes and perching may cheer the pet up again (attention cooldown), by kind.</summary>
    public readonly Dictionary<string, float> AttentionReadyAt = new();
    public float RainbowHue;
    public bool UseWasDown;

    /// <summary>Entity indices for transmit filtering, rebuilt when <see cref="TransmitDirty"/>.</summary>
    public uint[] AllIndices = Array.Empty<uint>();
    public uint[] TagIndices = Array.Empty<uint>();
    public uint[] PerchIndices = Array.Empty<uint>();
    public bool TransmitDirty = true;

    public bool ChickenAlive => Chicken is { IsValid: true };
    public bool EggOut => Egg is { IsValid: true };
    public bool PerchOut => Perch is { IsValid: true };
    public bool Out => ChickenAlive || EggOut || PerchOut;

    public void RebuildIndices()
    {
        TransmitDirty = false;
        var all = new List<uint>();
        void Add(CEntityInstance? e)
        {
            if (e is { IsValid: true }) all.Add(e.Index);
        }

        Add(Chicken);
        Add(Hat);
        Add(Tag);
        Add(Nest);
        Add(Egg);
        Add(Feed);
        Add(Perch);
        Add(PerchHat);
        foreach (var e in LaidEggs) Add(e);
        AllIndices = all.ToArray();
        TagIndices = Tag is { IsValid: true } t ? new[] { t.Index } : Array.Empty<uint>();
        var perch = new List<uint>();
        if (Perch is { IsValid: true } p) perch.Add(p.Index);
        if (PerchHat is { IsValid: true } ph) perch.Add(ph.Index);
        PerchIndices = perch.ToArray();
    }
}
