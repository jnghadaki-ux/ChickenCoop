namespace ChickenCoop.Model;

/// <summary>One coat (colour) of a breed. <see cref="Skin"/> is the model's material-group index.</summary>
public sealed record Coat(Breed Breed, string Name, int Skin);

/// <summary>A wearable hat. <see cref="Scale"/> multiplies the configured hat scale (props not made as hats need it).</summary>
public sealed record Hat(string Id, string Name, string Model, string HowToUnlock, float Scale = 1f, float Lift = 0f);

/// <summary>A rare colour variant. <see cref="Rainbow"/> cycles the colour; <see cref="Particle"/> twinkles now and then.</summary>
public sealed record ShinyVariant(string Id, string Name, byte R, byte G, byte B, bool Rainbow = false, string? Particle = null);

/// <summary>An unlockable look (seasonal). <see cref="Alpha"/> below 255 makes the pet see-through.</summary>
public sealed record PetStyle(string Id, string Name, byte R, byte G, byte B, byte Alpha, string HowToUnlock);

/// <summary>
/// A short pet animation. <see cref="Activity"/> is a CS2 chicken activity name (Squat, Sleep, Feed, Panic, Trick...),
/// <see cref="Particle"/> a stock effect played on the pet, <see cref="Hop"/> lifts it off the ground.
/// </summary>
/// <summary>
/// Something a pet does for its owner, picked in the Abilities menu. <see cref="RevealsEnemies"/> marks the ones that
/// give away enemy positions: servers have to allow those in Abilities.Allowed.
/// </summary>
public sealed record Ability(string Id, string Name, string Description, string HowToUnlock, bool RevealsEnemies = false);

public sealed record Emote(string Id, string Name, string Activity, float Seconds, string? Particle = null, bool Hop = false,
    bool Spin = false, string HowToUnlock = "");

/// <summary>
/// Stock CS2 assets used by the plugin. Every path here ships with the game (no workshop download needed): the pet
/// chicken models, coats, photo-booth hats and effects added with the chicken pets in September 2026.
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
    // wrong colour on your server, fix it with Looks.CoatOverrides in the config (see README, "Coats").
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

    // ------------------------------------------------------------------ hats

    public const string SnowmanHatId = "snowman";

    public static readonly IReadOnlyList<Hat> Hats = new[]
    {
        new Hat("sprout", "Sprout", "models/photobooth/silly_hats/sprout.vmdl", "Hatch your egg"),
        new Hat("party", "Party Hat", "models/photobooth/silly_hats/party.vmdl", "Feed your pet 5 times"),
        new Hat("glasses", "Glasses", "models/photobooth/silly_hats/glasses.vmdl", "Pet your chicken 25 times"),
        new Hat("helmet", "Helmet", "models/photobooth/silly_hats/helmet.vmdl", "Grow into a pullet"),
        new Hat("armor_helmet", "Armor Helmet", "models/photobooth/silly_hats/armor_helmet.vmdl", "Play 50 rounds together"),
        new Hat("nose_glasses", "Nose Glasses", "models/photobooth/silly_hats/nose_glasses.vmdl", "Do 30 tricks or emotes"),
        new Hat("banana", "Banana", "models/photobooth/silly_hats/banana.vmdl", "Get 50 kills with your pet out"),
        new Hat("top_hat", "Top Hat", "models/photobooth/silly_hats/top_hat.vmdl", "Grow into a hen"),
        new Hat("wizard_hat", "Wizard Hat", "models/photobooth/silly_hats/wizard_hat.vmdl", "Avenge your pet 3 times"),
        new Hat("alien", "Alien", "models/photobooth/silly_hats/alien.vmdl", "Lose your pet to fire, a Zeus and the bomb"),
        new Hat(SnowmanHatId, "Snowman Hat", "models/props/cs_office/snowman_hat.vmdl", "Play during the Christmas event", 0.45f, 1f),
    };

    public static Hat? FindHat(string idOrName)
    {
        var key = idOrName.Trim();
        return Hats.FirstOrDefault(h => h.Id.Equals(key, StringComparison.OrdinalIgnoreCase))
               ?? Hats.FirstOrDefault(h => h.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
               ?? Hats.FirstOrDefault(h => key.Length >= 3 && h.Name.StartsWith(key, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ shiny variants and styles

    public static readonly IReadOnlyList<ShinyVariant> ShinyVariants = new[]
    {
        new ShinyVariant("golden", "Golden", 255, 200, 60, Particle: Particles.Sparks),
        new ShinyVariant("rainbow", "Rainbow", 255, 255, 255, Rainbow: true),
        new ShinyVariant("shadow", "Shadow", 70, 60, 95),
        new ShinyVariant("celestial", "Celestial", 175, 205, 255, Particle: Particles.Sparks),
    };

    public static ShinyVariant? FindShiny(string? id) =>
        id == null ? null : ShinyVariants.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public const string GhostStyleId = "ghost";
    public const string FrostStyleId = "frost";
    public const string ShinyStyleId = "shiny";

    public static readonly IReadOnlyList<PetStyle> Styles = new[]
    {
        new PetStyle(GhostStyleId, "Ghost", 235, 240, 255, 115, "Play during the Halloween event"),
        new PetStyle(FrostStyleId, "Frost", 165, 215, 255, 255, "Play during the Christmas event"),
    };

    public static PetStyle? FindStyle(string? id) =>
        id == null ? null : Styles.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ emotes

    public static readonly IReadOnlyList<Emote> Emotes = new[]
    {
        new Emote("sit", "Sit", "Squat", 4f),
        new Emote("sleep", "Nap", "Sleep", 6f),
        new Emote("peck", "Peck", "Feed", 4f),
        new Emote("panic", "Panic", "Panic", 3f),
        new Emote("flap", "Flap", "Trick", 2.5f, Hop: true),
        new Emote("spin", "Spin", "Trick", 2f, Spin: true),
        new Emote("confetti", "Confetti", "Trick", 3f, Particles.Confetti, Hop: true, HowToUnlock: "Hatch your egg"),
        new Emote("bubbles", "Bubbles", "Squat", 4f, Particles.Bubbles, HowToUnlock: "Grow into a pullet"),
        new Emote("sparks", "Sparkle", "Trick", 3f, Particles.Sparks, HowToUnlock: "Do 10 tricks or emotes"),
        new Emote("fire", "Fire", "Panic", 3f, Particles.Fire, HowToUnlock: "Lose your pet to fire once"),
        new Emote("lightning", "Lightning", "Panic", 3f, Particles.Lightning, HowToUnlock: "Lose your pet to a Zeus once"),
        new Emote("explosion", "Kaboom", "Panic", 3f, Particles.Explosion, Hop: true, HowToUnlock: "Lose your pet to the bomb once"),
        new Emote("lasers", "Lasers", "Trick", 3f, Particles.Lasers, HowToUnlock: "Avenge your pet once"),
        new Emote("ufo", "Abduction", "Glide", 4f, Particles.Ufo, Hop: true, HowToUnlock: "Unlock the Alien hat"),
    };

    public static Emote? FindEmote(string idOrName)
    {
        var key = idOrName.Trim();
        return Emotes.FirstOrDefault(e => e.Id.Equals(key, StringComparison.OrdinalIgnoreCase))
               ?? Emotes.FirstOrDefault(e => e.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ abilities

    public static readonly IReadOnlyList<Ability> Abilities = new[]
    {
        new Ability("cheer", "Cheerleader", "Does a celebration trick when you get a kill or win a round", "Hatch your egg"),
        new Ability("forager", "Forager", "Sometimes finds a feed bag at the end of a round spent out with you", "Grow into a pullet"),
        new Ability("broody", "Broody", "Lays eggs 30% faster", "Grow into a hen"),
        new Ability("charmer", "Charmer", "Its mood drops half as fast", "Pet your chicken 50 times"),
        new Ability("sentry", "Sentry", "While it sits (!pet sit), warns you when an enemy walks past it", "Pullet, and 25 rounds together", true),
        new Ability("lookout", "Lookout", "Panics and warns you when an enemy comes close", "Hen, and 100 rounds together", true),
    };

    public static Ability? FindAbility(string? idOrName)
    {
        var key = (idOrName ?? "").Trim();
        return Abilities.FirstOrDefault(a => a.Id.Equals(key, StringComparison.OrdinalIgnoreCase))
               ?? Abilities.FirstOrDefault(a => a.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every collection entry there is: each breed's coats, then the shiny variants.</summary>
    public static IEnumerable<string> AllCollectionIds()
    {
        foreach (var b in Enum.GetValues<Breed>())
            for (var i = 0; i < CoatNames(b).Count; i++)
                yield return CoatEntry(b, i);
        foreach (var s in ShinyVariants) yield return ShinyEntry(s.Id);
    }

    public static string CoatEntry(Breed breed, int coat) => $"coat:{breed}/{coat}";

    public static string ShinyEntry(string shiny) => "shiny:" + shiny.ToLowerInvariant();

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

/// <summary>Stock CS2 particle effects: the chicken critter effects and the pet photo-booth effects.</summary>
public static class Particles
{
    public const string Feathers = "particles/critters/chicken/chicken_gone_feathers.vpcf";
    public const string Dust = "particles/critters/chicken/chicken_crumble_dust.vpcf";
    public const string Burst = "particles/critters/chicken/chicken_impact_burst2.vpcf";
    public const string Rise = "particles/critters/chicken/chicken_rez.vpcf";
    public const string ZombieGone = "particles/critters/chicken/chicken_gone_zombie.vpcf";
    public const string HalloweenCrumble = "particles/critters/chicken/chicken_gone_crumble_halloween.vpcf";
    public const string Snow = "particles/explosions_fx/explosion_child_snow03a.vpcf";

    public const string Confetti = "particles/pet_photo/confetti/photo_solo_confetti.vpcf";
    public const string Bubbles = "particles/pet_photo/bubbles/photo_solo_bubbles.vpcf";
    public const string Sparks = "particles/pet_photo/sparks/photo_solo_sparks.vpcf";
    public const string Fire = "particles/pet_photo/fire/photo_solo_fire.vpcf";
    public const string Lightning = "particles/pet_photo/lightning/photo_solo_lightning.vpcf";
    public const string Explosion = "particles/pet_photo/explosion/photo_solo_explosion.vpcf";
    public const string Lasers = "particles/pet_photo/lasers/photo_solo_lasers.vpcf";
    public const string Ufo = "particles/pet_photo/ufo/photo_solo_ufo.vpcf";
    public const string PhotoFeathers = "particles/pet_photo/feathers/photo_solo_feathers.vpcf";

    public static readonly string[] All =
    {
        Feathers, Dust, Burst, Rise, ZombieGone, HalloweenCrumble, Snow, Confetti, Bubbles, Sparks, Fire, Lightning,
        Explosion, Lasers, Ufo, PhotoFeathers,
    };
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
    public const string FlapLongChick = "Chicken.Flap.Long.Chick";
    public const string Panic = "Chicken.Panic";
    public const string PanicChick = "Chicken.Panic.Chick";
    public const string IdleChick = "Chicken.Idle.Chick";
    public const string IdlePullet = "Chicken.Idle.Pullet";
    public const string IdleHen = "Chicken.Idle.Hen";
    public const string Peck = "Chicken.Peck";
    public const string PeckChick = "Chicken.Peck.Chick";
    public const string Scratch = "Chicken.Scratch";
    public const string ScratchChick = "Chicken.Scratch.Chick";
    public const string Fly = "Chicken.Fly";
    public const string Popup = "Chicken.Popup.Message";

    public static string Pick(PetStage stage, string adult, string chick) => stage == PetStage.Chick ? chick : adult;

    public static string Idle(PetStage stage) => stage switch
    {
        PetStage.Chick => IdleChick,
        PetStage.Pullet => IdlePullet,
        _ => IdleHen,
    };
}
