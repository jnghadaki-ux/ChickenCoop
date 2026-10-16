using System.Text.Json.Serialization;

namespace ChickenCoop.Model;

/// <summary>Life stages, in the same order as the stock CS2 pet: egg, chick, pullet, hen.</summary>
public enum PetStage
{
    None = 0,
    Egg = 1,
    Chick = 2,
    Pullet = 3,
    Hen = 4,
}

/// <summary>The three breeds CS2 ships models for.</summary>
public enum Breed
{
    Catalana = 0,
    Polish = 1,
    Silkie = 2,
}

/// <summary>How a pet died last, for the journal and the hats that reward bad luck.</summary>
public enum DeathCause
{
    Other = 0,
    Burn = 1,
    Taser = 2,
    Bomb = 3,
    Explosive = 4,
    Knife = 5,
    Gun = 6,
}

/// <summary>Everything the plugin remembers about one player's pet. Saved as JSON, keyed by SteamID64.</summary>
public sealed class PetData
{
    public ulong SteamId { get; set; }
    public string OwnerName { get; set; } = "";

    public PetStage Stage { get; set; } = PetStage.None;
    public string? Name { get; set; }
    public int RenamesThisStage { get; set; }

    /// <summary>Rolled when the pet is created; decides breed and coat (revealed at the pullet stage).</summary>
    public int Seed { get; set; }
    public Breed Breed { get; set; }
    public int Coat { get; set; }

    /// <summary>Played seconds with no pet yet (counts toward finding an egg).</summary>
    public double SecondsTowardEgg { get; set; }
    /// <summary>Played seconds the pet has grown in its current stage.</summary>
    public double StageSeconds { get; set; }
    /// <summary>Played seconds the pet has been alive since hatching (for retirement).</summary>
    public double LifeSeconds { get; set; }
    /// <summary>Played seconds of food left in the bowl.</summary>
    public double FoodSeconds { get; set; }
    /// <summary>Played seconds the pet has gone without food in a row.</summary>
    public double StarvingSeconds { get; set; }

    public int FeedBags { get; set; }
    public int RoundsTowardFeedBag { get; set; }

    public string? Hat { get; set; }
    public bool Hidden { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? HatchedUtc { get; set; }

    public PetStats Stats { get; set; } = new();
    public List<PastPet> PastPets { get; set; } = new();

    /// <summary>Hats an admin handed out by command, on top of the ones earned.</summary>
    public List<string> GiftedHats { get; set; } = new();

    [JsonIgnore] public bool HasPet => Stage != PetStage.None;
    [JsonIgnore] public bool IsHatched => Stage >= PetStage.Chick;
    [JsonIgnore] public bool ShowsBreed => Stage >= PetStage.Pullet;
}

public sealed class PetStats
{
    public int Feeds { get; set; }
    public int Tricks { get; set; }
    public int Pets { get; set; }
    public int RoundsTogether { get; set; }
    public int OwnerKillsWithPet { get; set; }
    public int Revenges { get; set; }
    public int Deaths { get; set; }
    public int DeathsBurn { get; set; }
    public int DeathsTaser { get; set; }
    public int DeathsBomb { get; set; }
    public int DeathsExplosive { get; set; }
    public double LongestLifeSeconds { get; set; }
}

/// <summary>A pet that retired, kept for the journal.</summary>
public sealed class PastPet
{
    public string Name { get; set; } = "";
    public Breed Breed { get; set; }
    public int Coat { get; set; }
    public PetStage Stage { get; set; }
    public DateTime BornUtc { get; set; }
    public DateTime RetiredUtc { get; set; }
    public string Reason { get; set; } = "";
    public int RoundsTogether { get; set; }
}
