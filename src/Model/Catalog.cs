namespace ChickenCoop.Model;

/// <summary>One coat (colour) of a breed. <see cref="Skin"/> is the model's material-group index.</summary>
public sealed record Coat(Breed Breed, string Name, int Skin);

/// <summary>A wearable hat from the CS2 photo booth set.</summary>
public sealed record Hat(string Id, string Name, string Model, string HowToUnlock);

/// <summary>
/// Stock CS2 assets used by the plugin. Every path here ships with the game (no workshop download needed):
/// the pet chicken models, coats and photo-booth hats added with the chicken pets in September 2026.
/// </summary>
public static class Catalog
{
    public const string ChickModel = "models/chicken/chick.vmdl";
    public const string CatalanaModel = "models/chicken/chicken.vmdl";
    public const string PolishModel = "models/chicken/chicken_polish.vmdl";
    public const string SilkieModel = "models/chicken/chicken_silkie.vmdl";
    public const string EggModel = "models/chicken/egg_pristine.vmdl";
    public const string HatchingEggModel = "models/chicken/chicknegg.vmdl";
    public const string NestModel = "models/nest/nest.vmdl";
    public const string FeedModel = "models/chicken_feed/chicken_feed.vmdl";
    public const string FeedBagModel = "models/chicken_feed/chicken_feed_bag.vmdl";

    public static string ModelFor(PetStage stage, Breed breed) => stage switch
    {
        PetStage.Chick => ChickModel,
        PetStage.Pullet or PetStage.Hen => breed switch
        {
            Breed.Polish => PolishModel,
            Breed.Silkie => SilkieModel,
            _ => CatalanaModel,
        },
        _ => EggModel,
    };

    // Coat names as the game's material files name them. The skin index is the order listed here; if a coat shows the
    // wrong colour on your server, fix it with Coats.Overrides in the config (see README, "Coats").
    private static readonly string[] CatalanaCoats =
    {
        "Black", "Black White Head", "Black with Red Head", "Blue", "Blue and Light Orange", "Dark Orange", "Grey",
        "Light Blue and Red", "Orange Blue Neck", "Red Yellow Gradient", "Tan", "White", "White Tan Head",
        "Yellow Red Gradient", "Yellow and Red",
    };

    private static readonly string[] PolishCoats =
    {
        "Black with White Head", "Blue", "Blue Yellow", "Brown", "Brown with Black Wing Tips", "Green",
        "Green with Red Head", "Orange White Blue", "Orange with Black Stripes", "Pink", "White Black Head",
        "White with Black Wing Tips",
    };

    private static readonly string[] SilkieCoats =
    {
        "Aqua with Yellow", "Black", "Blue with Red Beard", "Brown", "Brown with Black Head", "Leopard", "Orange",
        "Purple", "Red", "Red Beetle", "Splash", "Tiger", "White",
    };

    public static IReadOnlyList<string> CoatNames(Breed breed) => breed switch
    {
        Breed.Polish => PolishCoats,
        Breed.Silkie => SilkieCoats,
        _ => CatalanaCoats,
    };

    public static Coat CoatOf(Breed breed, int index)
    {
        var names = CoatNames(breed);
        var i = Math.Clamp(index, 0, names.Count - 1);
        return new Coat(breed, names[i], i);
    }

    public static string BreedName(Breed breed) => breed.ToString();

    public static readonly IReadOnlyList<Hat> Hats = new[]
    {
        new Hat("sprout", "Sprout", "models/photobooth/silly_hats/sprout.vmdl", "Hatch your egg"),
        new Hat("party", "Party Hat", "models/photobooth/silly_hats/party.vmdl", "Feed your pet 5 times"),
        new Hat("glasses", "Glasses", "models/photobooth/silly_hats/glasses.vmdl", "Pet your chicken 25 times"),
        new Hat("helmet", "Helmet", "models/photobooth/silly_hats/helmet.vmdl", "Grow into a pullet"),
        new Hat("armor_helmet", "Armor Helmet", "models/photobooth/silly_hats/armor_helmet.vmdl", "Play 50 rounds together"),
        new Hat("nose_glasses", "Nose Glasses", "models/photobooth/silly_hats/nose_glasses.vmdl", "Do 30 tricks"),
        new Hat("banana", "Banana", "models/photobooth/silly_hats/banana.vmdl", "Get 50 kills with your pet out"),
        new Hat("top_hat", "Top Hat", "models/photobooth/silly_hats/top_hat.vmdl", "Grow into a hen"),
        new Hat("wizard_hat", "Wizard Hat", "models/photobooth/silly_hats/wizard_hat.vmdl", "Avenge your pet 3 times"),
        new Hat("alien", "Alien", "models/photobooth/silly_hats/alien.vmdl", "Lose your pet to fire, a Zeus and the bomb"),
    };

    public static Hat? FindHat(string idOrName)
    {
        var key = idOrName.Trim();
        return Hats.FirstOrDefault(h => h.Id.Equals(key, StringComparison.OrdinalIgnoreCase))
               ?? Hats.FirstOrDefault(h => h.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
               ?? Hats.FirstOrDefault(h => h.Name.StartsWith(key, StringComparison.OrdinalIgnoreCase) && key.Length >= 3);
    }

    /// <summary>Every model the plugin may spawn, for precaching.</summary>
    public static IEnumerable<string> AllModels()
    {
        yield return ChickModel;
        yield return CatalanaModel;
        yield return PolishModel;
        yield return SilkieModel;
        yield return EggModel;
        yield return HatchingEggModel;
        yield return NestModel;
        yield return FeedModel;
        yield return FeedBagModel;
        foreach (var h in Hats) yield return h.Model;
    }
}

/// <summary>Sound events from the stock CS2 sound files (game_sounds_world / game_sounds_ui).</summary>
public static class Sounds
{
    public const string HatchTap = "Chicken.Egg.Hatch.Tap";
    public const string HatchTapFinal = "Chicken.Egg.Hatch.Tap.Final";
    public const string Hatch = "Chicken.Egg.Hatch";
    public const string EvolveChick = "Chicken.Evolve.To.Chick";
    public const string EvolvePullet = "Chicken.Evolve.To.Pullet";
    public const string EvolveHen = "Chicken.Evolve.To.Hen";
    public const string Feed = "Chicken.Feed";
    public const string FeedSeed = "Chicken.Feed.Seed";
    public const string Hungry = "Chicken.Hungry";
    public const string HungryChick = "Chicken.Hungry.Chick";
    public const string Interact = "Chicken.Interact";
    public const string InteractChick = "Chicken.Chick.Interact";
    public const string FlapShort = "Chicken.Flap.Short";
    public const string FlapShortChick = "Chicken.Flap.Short.Chick";
    public const string FlapLong = "Chicken.Flap.Long";
    public const string Panic = "Chicken.Panic";
    public const string PanicChick = "Chicken.Panic.Chick";
    public const string IdleChick = "Chicken.Idle.Chick";
    public const string IdlePullet = "Chicken.Idle.Pullet";
    public const string IdleHen = "Chicken.Idle.Hen";
    public const string Peck = "Chicken.Peck";
    public const string PeckChick = "Chicken.Peck.Chick";
    public const string Fly = "Chicken.Fly";

    public static string Pick(PetStage stage, string adult, string chick) => stage == PetStage.Chick ? chick : adult;

    public static string Idle(PetStage stage) => stage switch
    {
        PetStage.Chick => IdleChick,
        PetStage.Pullet => IdlePullet,
        _ => IdleHen,
    };
}
