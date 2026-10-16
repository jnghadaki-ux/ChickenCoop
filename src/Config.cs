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

    /// <summary>Fallback message language (a file in lang/). Players get their own CS2 language when a file for it exists.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Who may own a pet. Empty = everyone. Example: ["@css/vip"]. Players with the AdminPermissions flags always can.</summary>
    public List<string> OwnerPermissions { get; set; } = new();

    /// <summary>Flags for the admin commands and the admin menu.</summary>
    public List<string> AdminPermissions { get; set; } = new() { "@css/generic" };

    /// <summary>Maps (or map prefixes ending in *) where pets stay at home. Example: ["aim_*", "de_nuke"].</summary>
    public List<string> DisabledMaps { get; set; } = new();

    /// <summary>Pets stay at home during warmup.</summary>
    public bool HideDuringWarmup { get; set; } = false;

    /// <summary>Most pets out on the server at once (entity budget).</summary>
    public int MaxActivePets { get; set; } = 24;

    /// <summary>Give bots a random, unsaved pet. Handy for testing on a local server.</summary>
    public bool BotsGetPets { get; set; } = false;

    public MenuConfig Menu { get; set; } = new();
    public GrowthConfig Growth { get; set; } = new();
    public FoodConfig Food { get; set; } = new();
    public EggConfig Eggs { get; set; } = new();
    public BehaviourConfig Behaviour { get; set; } = new();
    public PerchConfig Perch { get; set; } = new();
    public CombatConfig Combat { get; set; } = new();
    public BirdWatchConfig BirdWatch { get; set; } = new();
    public LooksConfig Looks { get; set; } = new();
    public ShinyConfig Shiny { get; set; } = new();
    public SeasonConfig Seasons { get; set; } = new();
    public NamingConfig Naming { get; set; } = new();
}

public sealed class MenuConfig
{
    /// <summary>"Center" (centre of the screen), "Chat" (numbered chat lines) or "Both".</summary>
    public string Style { get; set; } = "Center";
    /// <summary>Centre menus: W/S move, E select, R back. Number keys (!1-!9) always work.</summary>
    public bool Wasd { get; set; } = true;
    /// <summary>Close a menu after this many seconds without input (0 = never).</summary>
    public int TimeoutSeconds { get; set; } = 40;
    /// <summary>Stop the centre panel flashing while a menu is open (the GameRestart trick most CS2 menus use).</summary>
    public bool FixFlashing { get; set; } = true;
}

public sealed class GrowthConfig
{
    /// <summary>Minutes of play before a new player finds their egg (0 = straight away).</summary>
    public double MinutesToFindEgg { get; set; } = 5;
    public double EggMinutes { get; set; } = 30;
    public double ChickMinutes { get; set; } = 120;
    public double PulletMinutes { get; set; } = 240;
    /// <summary>Hens retire (and a new egg comes) after this many minutes as a hen. 0 = never.</summary>
    public double HenRetireMinutes { get; set; } = 0;
    /// <summary>Chance weights for the breed rolled with the egg.</summary>
    public Dictionary<string, int> BreedWeights { get; set; } = new() { ["Catalana"] = 50, ["Polish"] = 30, ["Silkie"] = 20 };
}

public sealed class FoodConfig
{
    /// <summary>Turn hunger off entirely (pets never need feeding).</summary>
    public bool Enabled { get; set; } = true;
    public double StarterFoodMinutes { get; set; } = 120;
    public double MinutesPerBag { get; set; } = 90;
    public double MaxFoodMinutes { get; set; } = 360;
    public int StarterBags { get; set; } = 2;
    /// <summary>One feed bag for every this many rounds played (0 = never).</summary>
    public int RoundsPerBag { get; set; } = 4;
    /// <summary>Bonus bags for each round your team wins.</summary>
    public int BagsPerRoundWin { get; set; } = 0;
    public int MaxBags { get; set; } = 10;
    public double HungryWarningMinutes { get; set; } = 20;
    /// <summary>A pet out of food stops growing. After this many minutes of play without food it leaves. 0 = never.</summary>
    public double StarveRetireMinutes { get; set; } = 0;
}

public sealed class EggConfig
{
    /// <summary>Hens lay eggs now and then.</summary>
    public bool Laying { get; set; } = true;
    /// <summary>Stages that lay eggs.</summary>
    public List<string> LayingStages { get; set; } = new() { "Hen" };
    /// <summary>Minutes of play (with food) per laid egg.</summary>
    public double MinutesPerEgg { get; set; } = 40;
    /// <summary>Most eggs in a basket.</summary>
    public int MaxBasket { get; set; } = 6;
    /// <summary>Who can pick up a laid egg: "owner" or "anyone" (enemies can steal it!). Eggs left lying around go to
    /// the owner's basket when the round ends.</summary>
    public string Pickup { get; set; } = "owner";
    /// <summary>Let players give a basket egg to a friend who has no pet (it becomes their pet).</summary>
    public bool Gifting { get; set; } = true;
    /// <summary>Feed bags you get for trading in one egg (0 = trading off).</summary>
    public int BagsPerEgg { get; set; } = 1;
}

public sealed class BehaviourConfig
{
    public float TeleportDistance { get; set; } = 650f;
    public float StuckSeconds { get; set; } = 4f;
    /// <summary>Petting: look at your pet from this close and press E (+use).</summary>
    public float PetDistance { get; set; } = 110f;
    public float PetCooldownSeconds { get; set; } = 2f;
    /// <summary>Wait between tricks and emotes.</summary>
    public float EmoteCooldownSeconds { get; set; } = 4f;
    public float FeedCooldownSeconds { get; set; } = 8f;
    /// <summary>When the owner dies the pet stays by them; it is removed after this many seconds (0 = stays all round).</summary>
    public float StayWithBodySeconds { get; set; } = 0f;
    /// <summary>Stop other players from leading someone else's pet away with E.</summary>
    public bool OwnerOnlyFollow { get; set; } = true;
    /// <summary>Random idle clucks while following (seconds between, 0 = off).</summary>
    public float IdleSoundSeconds { get; set; } = 25f;
    /// <summary>Pets do not block players (best effort).</summary>
    public bool NoBlock { get; set; } = true;
}

public sealed class PerchConfig
{
    /// <summary>!pet perch: small pets ride on your shoulder.</summary>
    public bool Enabled { get; set; } = true;
    public List<string> Stages { get; set; } = new() { "Chick", "Pullet" };
    /// <summary>Shoulder position relative to the player's feet: up, to the right, and forward (units).</summary>
    public float Up { get; set; } = 57f;
    public float Right { get; set; } = 9f;
    public float Forward { get; set; } = -2f;
    /// <summary>Size of the perched pet compared with its normal size.</summary>
    public float Scale { get; set; } = 0.75f;
    /// <summary>Player model attachment to perch on instead of the fixed offset (empty = offset). Try "clip_limit" or a
    /// shoulder attachment your player models have.</summary>
    public string Attachment { get; set; } = "";
    /// <summary>The owner does not see their own perched pet (it would sit in front of the camera).</summary>
    public bool HideFromOwner { get; set; } = true;
}

public sealed class CombatConfig
{
    /// <summary>Who can hurt pets: "everyone", "enemies" (the owner's enemies) or "nobody".</summary>
    public string WhoCanHurtPets { get; set; } = "enemies";
    public int ChickHealth { get; set; } = 20;
    public int PulletHealth { get; set; } = 35;
    public int HenHealth { get; set; } = 50;
    /// <summary>0 = a killed pet comes back next round; above 0 = it comes back after this many seconds.</summary>
    public float RespawnSeconds { get; set; } = 0f;
    /// <summary>Tell the whole server when a pet dies.</summary>
    public bool AnnounceDeaths { get; set; } = false;
    /// <summary>Tell the owner when they kill the player who killed their pet.</summary>
    public bool Revenge { get; set; } = true;
    public bool DeathParticles { get; set; } = true;
}

public sealed class BirdWatchConfig
{
    /// <summary>Pets panic and cluck when an enemy comes close. Gives away enemies, so off by default.</summary>
    public bool Enabled { get; set; } = false;
    public float Range { get; set; } = 600f;
    public float CooldownSeconds { get; set; } = 12f;
    /// <summary>Also flash a warning on the owner's screen.</summary>
    public bool WarnOwner { get; set; } = true;
}

public sealed class LooksConfig
{
    public bool NameTags { get; set; } = true;
    /// <summary>{owner} and {pet}. Example: "{pet}" or "{owner}'s {pet}".</summary>
    public string NameTagFormat { get; set; } = "{owner}'s {pet}";
    public float NameTagFontSize { get; set; } = 40f;
    public string NameTagColor { get; set; } = "255,236,170";
    public float PulletScale { get; set; } = 0.78f;
    public float ChickScale { get; set; } = 1.0f;
    public float HenScale { get; set; } = 1.0f;
    /// <summary>The egg (in its nest) waits at the owner's spawn point while it is an egg.</summary>
    public bool ShowEggAtSpawn { get; set; } = true;
    public bool AllHatsUnlocked { get; set; } = false;
    /// <summary>
    /// How hats are put on: "bonemerge" (the hat follows the chicken's skeleton, how the stock photo booth fits one hat
    /// to every size), "attachment" (snap to HatAttachment), or "offset" (a fixed spot above the body). Try the others
    /// live with css_pet_hatmode if hats look wrong.
    /// </summary>
    public string HatMode { get; set; } = "bonemerge";
    public string HatAttachment { get; set; } = "hat";
    /// <summary>"offset" mode only: hat height above the pet's feet per stage, forward shift, and size.</summary>
    public float ChickHatHeight { get; set; } = 13f;
    public float PulletHatHeight { get; set; } = 19f;
    public float HenHatHeight { get; set; } = 24f;
    public float HatForward { get; set; } = 4f;
    public float HatScale { get; set; } = 1.0f;
    /// <summary>"Skin" = switch coats with the model's skin index (default). "MaterialGroup" = Overrides are group names.</summary>
    public string CoatMode { get; set; } = "Skin";
    /// <summary>Fix a coat that shows the wrong colour: "Breed/Coat Name" = skin index or material group name.</summary>
    public Dictionary<string, string> CoatOverrides { get; set; } = new();
}

public sealed class ShinyConfig
{
    /// <summary>Chance (0-1) that a new egg holds a shiny pet. 0.02 = 1 in 50.</summary>
    public double Chance { get; set; } = 0.02;
    /// <summary>Shiny variants that can roll: golden, rainbow, shadow, celestial.</summary>
    public List<string> Variants { get; set; } = new() { "golden", "rainbow", "shadow", "celestial" };
    /// <summary>Tell the whole server when a shiny is revealed.</summary>
    public bool Announce { get; set; } = true;
}

public sealed class SeasonConfig
{
    /// <summary>Halloween and Christmas events: themed effects, plus a look or hat everyone who plays then keeps.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Dates as MM-dd (server time). A range can wrap over New Year.</summary>
    public string HalloweenStart { get; set; } = "10-15";
    public string HalloweenEnd { get; set; } = "11-05";
    public string ChristmasStart { get; set; } = "12-10";
    public string ChristmasEnd { get; set; } = "01-06";
}

public sealed class NamingConfig
{
    public int MaxLength { get; set; } = 20;
    /// <summary>Renames allowed per life stage (0 = unlimited). The stock game allows one per stage.</summary>
    public int RenamesPerStage { get; set; } = 1;
    public List<string> BlockedWords { get; set; } = new();
    public List<string> DefaultNames { get; set; } = new()
    {
        "Nugget", "Clucky", "Pecky", "Omelette", "Drumstick", "Eggbert", "Henrietta", "Sunny", "Biscuit", "Popcorn",
        "Kevin", "Feathers", "Scrambles", "Waffles", "Pebbles", "Cluckers", "Yolko", "Mabel", "Dot", "Chirpy",
    };
}
