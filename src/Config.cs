using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace ChickenCoop;

/// <summary>
/// configs/plugins/ChickenCoop/ChickenCoop.json. Created with these defaults on first load. Times are minutes of
/// play on this server (a player in a team, not spectating), not real-world time, so a pet grows by playing.
/// </summary>
public sealed class CoopConfig : BasePluginConfig
{
    /// <summary>Turn the whole plugin off without unloading it (css_pet_toggle flips this live).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Chat prefix. Colour tags: {default} {green} {lime} {gold} {yellow} {orange} {red} {lightred} {blue}
    /// {lightblue} {purple} {grey} {olive}.</summary>
    public string ChatPrefix { get; set; } = "{gold}[Coop]{default}";

    /// <summary>Message file in lang/ (en.json ships with the plugin; copy it to add a language).</summary>
    public string Language { get; set; } = "en";

    /// <summary>Who may own a pet. Empty = everyone. Example: ["@css/vip"]. Players with the AdminPermissions flags always can.</summary>
    public List<string> OwnerPermissions { get; set; } = new();

    /// <summary>Flags for the admin commands (css_pet_give, css_pet_reset, ...).</summary>
    public List<string> AdminPermissions { get; set; } = new() { "@css/generic" };

    /// <summary>Maps (or map prefixes ending in *) where pets stay at home. Example: ["aim_*", "de_nuke"].</summary>
    public List<string> DisabledMaps { get; set; } = new();

    /// <summary>Pets stay at home during warmup.</summary>
    public bool HideDuringWarmup { get; set; } = false;

    /// <summary>Most pets out on the server at once (entity budget). Owners past the cap are told their pet is napping.</summary>
    public int MaxActivePets { get; set; } = 24;

    /// <summary>Give bots a random, unsaved pet. Handy for testing on a local server.</summary>
    public bool BotsGetPets { get; set; } = false;

    /// <summary>Use the centre-screen menu (WASD to move) instead of the chat menu.</summary>
    public bool UseCenterMenu { get; set; } = true;

    public GrowthConfig Growth { get; set; } = new();
    public FoodConfig Food { get; set; } = new();
    public BehaviourConfig Behaviour { get; set; } = new();
    public CombatConfig Combat { get; set; } = new();
    public LooksConfig Looks { get; set; } = new();
    public NamingConfig Naming { get; set; } = new();
}

public sealed class GrowthConfig
{
    /// <summary>Minutes of play before a new player finds their egg (0 = straight away).</summary>
    public double MinutesToFindEgg { get; set; } = 5;
    /// <summary>Minutes from egg to chick.</summary>
    public double EggMinutes { get; set; } = 30;
    /// <summary>Minutes from chick to pullet (breed and coat are revealed at the pullet stage).</summary>
    public double ChickMinutes { get; set; } = 120;
    /// <summary>Minutes from pullet to hen.</summary>
    public double PulletMinutes { get; set; } = 240;
    /// <summary>Hens retire (and a new egg comes) after this many minutes as a hen. 0 = never.</summary>
    public double HenRetireMinutes { get; set; } = 0;
    /// <summary>Chance weights for the breed rolled at the pullet stage.</summary>
    public Dictionary<string, int> BreedWeights { get; set; } = new() { ["Catalana"] = 50, ["Polish"] = 30, ["Silkie"] = 20 };
}

public sealed class FoodConfig
{
    /// <summary>Turn hunger off entirely (pets never need feeding).</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Food a fresh chick starts with, in minutes of play.</summary>
    public double StarterFoodMinutes { get; set; } = 120;
    /// <summary>Food one feed bag adds, in minutes of play.</summary>
    public double MinutesPerBag { get; set; } = 90;
    /// <summary>The most food a bowl holds, in minutes of play.</summary>
    public double MaxFoodMinutes { get; set; } = 360;
    /// <summary>Feed bags a new owner starts with.</summary>
    public int StarterBags { get; set; } = 2;
    /// <summary>One feed bag for every this many rounds played (0 = never).</summary>
    public int RoundsPerBag { get; set; } = 4;
    /// <summary>Bonus bag for each round your team wins (0/1).</summary>
    public int BagsPerRoundWin { get; set; } = 0;
    public int MaxBags { get; set; } = 10;
    /// <summary>Warn the owner when food drops below this many minutes.</summary>
    public double HungryWarningMinutes { get; set; } = 20;
    /// <summary>A pet out of food stops growing. After this many minutes of play without food it retires. 0 = never.</summary>
    public double StarveRetireMinutes { get; set; } = 0;
}

public sealed class BehaviourConfig
{
    /// <summary>Teleport the pet back beside its owner when it falls this far behind (units).</summary>
    public float TeleportDistance { get; set; } = 650f;
    /// <summary>Seconds a following pet may make no progress toward its owner before it is teleported.</summary>
    public float StuckSeconds { get; set; } = 4f;
    /// <summary>Petting: look at your pet from this close and press E (+use).</summary>
    public float PetDistance { get; set; } = 110f;
    public float PetCooldownSeconds { get; set; } = 2f;
    public float TrickCooldownSeconds { get; set; } = 4f;
    public float FeedCooldownSeconds { get; set; } = 8f;
    /// <summary>When the owner dies the pet stays by them; it is removed after this many seconds (0 = stays all round).</summary>
    public float StayWithBodySeconds { get; set; } = 0f;
    /// <summary>Stop other players from leading someone else's pet away with E.</summary>
    public bool OwnerOnlyFollow { get; set; } = true;
    /// <summary>Random idle clucks while following (seconds between, 0 = off).</summary>
    public float IdleSoundSeconds { get; set; } = 25f;
    /// <summary>Pets do not block players (best effort: CS2 may still nudge players that walk into them).</summary>
    public bool NoBlock { get; set; } = true;
}

public sealed class CombatConfig
{
    /// <summary>Who can hurt pets: "everyone", "enemies" (the owner's enemies) or "nobody".</summary>
    public string WhoCanHurtPets { get; set; } = "enemies";
    /// <summary>Health of a chick / pullet / hen.</summary>
    public int ChickHealth { get; set; } = 20;
    public int PulletHealth { get; set; } = 35;
    public int HenHealth { get; set; } = 50;
    /// <summary>0 = a killed pet comes back next round; above 0 = it comes back after this many seconds.</summary>
    public float RespawnSeconds { get; set; } = 0f;
    /// <summary>Tell the whole server when a pet dies (false = only the owner and the killer).</summary>
    public bool AnnounceDeaths { get; set; } = false;
    /// <summary>Tell the owner when they kill the player who killed their pet.</summary>
    public bool Revenge { get; set; } = true;
    /// <summary>Spawn the stock feather / roast death effects.</summary>
    public bool DeathParticles { get; set; } = true;
}

public sealed class LooksConfig
{
    /// <summary>Floating name tag above each pet.</summary>
    public bool NameTags { get; set; } = true;
    /// <summary>{owner} and {pet}. Example: "{pet}" or "{owner}'s {pet}".</summary>
    public string NameTagFormat { get; set; } = "{owner}'s {pet}";
    public float NameTagFontSize { get; set; } = 40f;
    public string NameTagColor { get; set; } = "255,236,170";
    /// <summary>Scale of the pullet model (hens are 1.0, the chick model is already small).</summary>
    public float PulletScale { get; set; } = 0.78f;
    public float ChickScale { get; set; } = 1.0f;
    public float HenScale { get; set; } = 1.0f;
    /// <summary>The egg (in its nest) waits at the owner's spawn point while it is an egg.</summary>
    public bool ShowEggAtSpawn { get; set; } = true;
    /// <summary>Hats unlocked for everyone, no milestones needed.</summary>
    public bool AllHatsUnlocked { get; set; } = false;
    /// <summary>Model attachment to sit hats on. Empty = follow the body (always works). Try "head" on your server.</summary>
    public string HatAttachment { get; set; } = "";
    /// <summary>Hat height above the pet's feet for each stage (units), and the hat scale.</summary>
    public float ChickHatHeight { get; set; } = 13f;
    public float PulletHatHeight { get; set; } = 19f;
    public float HenHatHeight { get; set; } = 24f;
    public float HatForward { get; set; } = 4f;
    public float ChickHatScale { get; set; } = 0.45f;
    public float AdultHatScale { get; set; } = 0.6f;
    /// <summary>"Skin" = switch coats with the model's skin index (default). "MaterialGroup" = use Overrides as group names.</summary>
    public string CoatMode { get; set; } = "Skin";
    /// <summary>Fix a coat that shows the wrong colour: key "Breed/Coat Name", value the skin index or material group
    /// name. Example: { "Silkie/Leopard": "5" }. Use css_pet_skin to find the right index.</summary>
    public Dictionary<string, string> CoatOverrides { get; set; } = new();
}

public sealed class NamingConfig
{
    public int MaxLength { get; set; } = 20;
    /// <summary>Renames allowed per life stage (0 = unlimited). The stock game allows one per stage.</summary>
    public int RenamesPerStage { get; set; } = 1;
    /// <summary>Words a name may not contain (case-insensitive).</summary>
    public List<string> BlockedWords { get; set; } = new();
    /// <summary>Names given to a pet that hatches without one.</summary>
    public List<string> DefaultNames { get; set; } = new()
    {
        "Nugget", "Clucky", "Pecky", "Omelette", "Drumstick", "Eggbert", "Henrietta", "Sunny", "Biscuit", "Popcorn",
        "Kevin", "Feathers", "Scrambles", "Waffles", "Pebbles", "Cluckers", "Yolko", "Mabel", "Dot", "Chirpy",
    };
}
