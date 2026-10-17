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

/// <summary>How a pet died last, for the journal and the hats and emotes that reward bad luck.</summary>
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

/// <summary>Seasonal events, switched on by date.</summary>
public enum Season
{
    None = 0,
    Halloween = 1,
    Christmas = 2,
}

/// <summary>What a leaderboard ranks by.</summary>
public enum Board
{
    Age = 0,
    Rounds = 1,
    Revenges = 2,
    Tricks = 3,
    Pets = 4,
    Eggs = 5,
    Arena = 6,
}

/// <summary>Everything the plugin remembers about one player and their pet. Saved as JSON, keyed by SteamID64.</summary>
public sealed class PetData
{
    public ulong SteamId { get; set; }
    public string OwnerName { get; set; } = "";

    public PetStage Stage { get; set; } = PetStage.None;
    public string? Name { get; set; }
    public int RenamesThisStage { get; set; }

    /// <summary>Rolled when the pet is created; decides breed, coat and shiny (revealed at the pullet stage).</summary>
    public int Seed { get; set; }
    public Breed Breed { get; set; }
    public int Coat { get; set; }
    /// <summary>Shiny variant id (golden, rainbow, shadow, celestial) or null. Rolled with the egg, shown from the pullet stage.</summary>
    public string? Shiny { get; set; }

    /// <summary>Played seconds with no pet yet (counts toward finding an egg).</summary>
    public double SecondsTowardEgg { get; set; }
    /// <summary>Played seconds the pet has grown in its current stage.</summary>
    public double StageSeconds { get; set; }
    /// <summary>Played seconds the pet has been alive since hatching.</summary>
    public double LifeSeconds { get; set; }
    /// <summary>Played seconds of food left in the bowl.</summary>
    public double FoodSeconds { get; set; }
    /// <summary>Played seconds the pet has gone without food in a row.</summary>
    public double StarvingSeconds { get; set; }
    /// <summary>Played, fed seconds toward the next laid egg (hens).</summary>
    public double LaySeconds { get; set; }

    public int FeedBags { get; set; }
    public int RoundsTowardFeedBag { get; set; }
    /// <summary>Eggs collected from your hen, to gift to friends or trade for feed.</summary>
    public int BasketEggs { get; set; }

    public string? Hat { get; set; }
    /// <summary>Chosen look: null = natural, "shiny", or an unlocked style id (ghost, frost).</summary>
    public string? Style { get; set; }
    public List<string> UnlockedStyles { get; set; } = new();
    public bool Hidden { get; set; }

    // Player settings (kept even without a pet).
    public bool ShowOtherPets { get; set; } = true;
    public bool ShowNameTags { get; set; } = true;

    /// <summary>Menu look (null = the server default): "world" or "center", a theme key, "left/center/right", "small/normal/large".</summary>
    public string? MenuStyle { get; set; }
    public string? MenuTheme { get; set; }
    public string? MenuPosition { get; set; }
    public string? MenuSize { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? HatchedUtc { get; set; }

    public PetStats Stats { get; set; } = new();
    public List<PastPet> PastPets { get; set; } = new();

    /// <summary>Hats handed out by an admin, another plugin or a seasonal event, on top of the ones earned.</summary>
    public List<string> GiftedHats { get; set; } = new();

    [JsonIgnore] public bool HasPet => Stage != PetStage.None;
    [JsonIgnore] public bool IsHatched => Stage >= PetStage.Chick;
    [JsonIgnore] public bool ShowsBreed => Stage >= PetStage.Pullet;
    [JsonIgnore] public bool IsShiny => Shiny != null && ShowsBreed;
}

public sealed class PetStats
{
    public int Feeds { get; set; }
    public int Tricks { get; set; }
    public int Pets { get; set; }
    public int Emotes { get; set; }
    public int RoundsTogether { get; set; }
    public int OwnerKillsWithPet { get; set; }
    public int Revenges { get; set; }
    public int Deaths { get; set; }
    public int DeathsBurn { get; set; }
    public int DeathsTaser { get; set; }
    public int DeathsBomb { get; set; }
    public int DeathsExplosive { get; set; }
    public int EggsLaid { get; set; }
    public int EggsGifted { get; set; }
    public int EggsTraded { get; set; }
    public int Perches { get; set; }
    public int ArenaWins { get; set; }
    public int ArenaLosses { get; set; }
    public int ArenaDraws { get; set; }
    public double LongestLifeSeconds { get; set; }
}

/// <summary>A pet that retired, kept for the journal.</summary>
public sealed class PastPet
{
    public string Name { get; set; } = "";
    public Breed Breed { get; set; }
    public int Coat { get; set; }
    public string? Shiny { get; set; }
    public PetStage Stage { get; set; }
    public DateTime BornUtc { get; set; }
    public DateTime RetiredUtc { get; set; }
    public string Reason { get; set; } = "";
    public int RoundsTogether { get; set; }
}
