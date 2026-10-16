using ChickenCoop;
using ChickenCoop.Logic;
using ChickenCoop.Model;
using Xunit;

namespace ChickenCoop.Tests;

public class PetLogicTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private static CoopConfig Cfg()
    {
        var c = new CoopConfig();
        c.Growth.MinutesToFindEgg = 5;
        c.Growth.EggMinutes = 30;
        c.Growth.ChickMinutes = 120;
        c.Growth.PulletMinutes = 240;
        return c;
    }

    private static List<GrowthEvent> Play(PetData p, CoopConfig c, double minutes, double step = 5)
    {
        var all = new List<GrowthEvent>();
        for (double t = 0; t < minutes * 60; t += step) all.AddRange(PetLogic.Advance(p, step, c, Now, 1234));
        return all;
    }

    [Fact]
    public void New_player_finds_an_egg_after_the_configured_play_time()
    {
        var c = Cfg();
        var p = new PetData { SteamId = 1 };
        Assert.Empty(Play(p, c, 4.9));
        Assert.Equal(PetStage.None, p.Stage);
        var events = Play(p, c, 0.2);
        Assert.Contains(GrowthEvent.FoundEgg, events);
        Assert.Equal(PetStage.Egg, p.Stage);
        Assert.Equal(c.Food.StarterBags, p.FeedBags);
    }

    [Fact]
    public void Egg_hatches_into_a_named_chick_with_starter_food()
    {
        var c = Cfg();
        var p = new PetData();
        PetLogic.NewEgg(p, 42, c, Now);
        var events = Play(p, c, 30);
        Assert.Contains(GrowthEvent.Hatched, events);
        Assert.Equal(PetStage.Chick, p.Stage);
        Assert.False(string.IsNullOrWhiteSpace(p.Name));
        Assert.Equal(c.Food.StarterFoodMinutes * 60, p.FoodSeconds, 3);
        Assert.Equal(Now, p.HatchedUtc);
    }

    [Fact]
    public void Full_life_egg_chick_pullet_hen_with_feeding()
    {
        var c = Cfg();
        c.Food.Enabled = false;
        var p = new PetData();
        PetLogic.NewEgg(p, 7, c, Now);
        var events = Play(p, c, 30 + 120 + 240 + 1);
        Assert.Equal(new[] { GrowthEvent.Hatched, GrowthEvent.BecamePullet, GrowthEvent.BecameHen }, events);
        Assert.Equal(PetStage.Hen, p.Stage);
        Assert.True(p.ShowsBreed);
    }

    [Fact]
    public void Hungry_pet_stops_growing_until_fed()
    {
        var c = Cfg();
        c.Food.StarterFoodMinutes = 10;
        c.Food.HungryWarningMinutes = 5;
        var p = new PetData();
        PetLogic.SetStage(p, PetStage.Chick, c, Now, 1);
        var events = Play(p, c, 12);
        Assert.Contains(GrowthEvent.Hungry, events);
        Assert.Contains(GrowthEvent.Starving, events);
        var grown = p.StageSeconds;
        Assert.Equal(600, grown, 3);

        Play(p, c, 30); // no food: no growth
        Assert.Equal(grown, p.StageSeconds, 3);
        Assert.True(p.StarvingSeconds > 0);

        Assert.Equal(FeedResult.Fed, PetLogic.Feed(p, c));
        Assert.Equal(0, p.StarvingSeconds);
        Play(p, c, 1);
        Assert.True(p.StageSeconds > grown);
    }

    [Fact]
    public void Feeding_rules()
    {
        var c = Cfg();
        var p = new PetData();
        Assert.Equal(FeedResult.NoPet, PetLogic.Feed(p, c));
        PetLogic.NewEgg(p, 1, c, Now);
        Assert.Equal(FeedResult.IsEgg, PetLogic.Feed(p, c));
        PetLogic.SetStage(p, PetStage.Chick, c, Now, 1);
        p.FeedBags = 0;
        Assert.Equal(FeedResult.NoBags, PetLogic.Feed(p, c));
        p.FeedBags = 10;
        p.FoodSeconds = c.Food.MaxFoodMinutes * 60;
        Assert.Equal(FeedResult.Full, PetLogic.Feed(p, c));
        p.FoodSeconds = 0;
        Assert.Equal(FeedResult.Fed, PetLogic.Feed(p, c));
        Assert.Equal(9, p.FeedBags);
        Assert.Equal(c.Food.MinutesPerBag * 60, p.FoodSeconds, 3);
        Assert.Equal(1, p.Stats.Feeds);
    }

    [Fact]
    public void Rounds_earn_feed_bags_up_to_the_cap()
    {
        var c = Cfg();
        c.Food.RoundsPerBag = 2;
        c.Food.MaxBags = 3;
        var p = new PetData();
        var gained = 0;
        for (var i = 0; i < 20; i++) gained += PetLogic.RoundPlayed(p, c, false);
        Assert.Equal(3, p.FeedBags);
        Assert.Equal(3, gained);
    }

    [Fact]
    public void Hens_retire_when_configured_and_go_to_the_journal()
    {
        var c = Cfg();
        c.Food.Enabled = false;
        c.Growth.HenRetireMinutes = 10;
        var p = new PetData();
        PetLogic.SetStage(p, PetStage.Hen, c, Now, 3);
        p.Name = "Mabel";
        var events = Play(p, c, 11);
        Assert.Contains(GrowthEvent.RetiredOldAge, events);
        Assert.Equal(PetStage.None, p.Stage);
        Assert.Single(p.PastPets);
        Assert.Equal("Mabel", p.PastPets[0].Name);
    }

    [Fact]
    public void Starving_retirement_only_when_enabled()
    {
        var c = Cfg();
        c.Food.StarterFoodMinutes = 0;
        var p = new PetData();
        PetLogic.SetStage(p, PetStage.Chick, c, Now, 3);
        Play(p, c, 60);
        Assert.Equal(PetStage.Chick, p.Stage);

        c.Food.StarveRetireMinutes = 30;
        var events = Play(p, c, 1);
        Assert.Contains(GrowthEvent.RetiredHunger, events);
        Assert.Equal(PetStage.None, p.Stage);
    }

    [Fact]
    public void Breed_roll_is_deterministic_and_respects_weights()
    {
        var c = Cfg();
        Assert.Equal(PetLogic.Roll(99, c), PetLogic.Roll(99, c));
        c.Growth.BreedWeights = new() { ["Silkie"] = 1 };
        for (var s = 0; s < 200; s++)
        {
            var (breed, coat) = PetLogic.Roll(s, c);
            Assert.Equal(Breed.Silkie, breed);
            Assert.InRange(coat, 0, Catalog.CoatNames(Breed.Silkie).Count - 1);
        }
    }

    [Fact]
    public void Every_breed_shows_up_with_default_weights()
    {
        var c = Cfg();
        var seen = Enumerable.Range(0, 2000).Select(s => PetLogic.Roll(s, c).breed).ToHashSet();
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void Coat_catalog_matches_the_game_files()
    {
        Assert.Equal(15, Catalog.CoatNames(Breed.Catalana).Count);
        Assert.Equal(12, Catalog.CoatNames(Breed.Polish).Count);
        Assert.Equal(13, Catalog.CoatNames(Breed.Silkie).Count);
        Assert.Equal(10, Catalog.Hats.Count);
        Assert.Equal("models/chicken/chicken_silkie.vmdl", Catalog.ModelFor(PetStage.Hen, Breed.Silkie));
        Assert.Equal(Catalog.ChickModel, Catalog.ModelFor(PetStage.Chick, Breed.Silkie));
    }

    [Theory]
    [InlineData("  Sir   Clucks  ", "Sir Clucks")]
    [InlineData("{red}Evil", "redEvil")]
    [InlineData("\"Quoted\"", "Quoted")]
    public void Names_are_cleaned(string raw, string expected)
    {
        Assert.Equal(expected, PetLogic.SanitizeName(raw, Cfg(), out _));
    }

    [Fact]
    public void Bad_names_are_rejected()
    {
        var c = Cfg();
        c.Naming.BlockedWords.Add("badword");
        Assert.Null(PetLogic.SanitizeName("   ", c, out var r1));
        Assert.Equal("empty", r1);
        Assert.Null(PetLogic.SanitizeName(new string('a', 21), c, out var r2));
        Assert.Equal("long", r2);
        Assert.Null(PetLogic.SanitizeName("my BADWORD hen", c, out var r3));
        Assert.Equal("blocked", r3);
    }

    [Fact]
    public void One_rename_per_stage_by_default()
    {
        var c = Cfg();
        var p = new PetData();
        PetLogic.NewEgg(p, 1, c, Now);
        Assert.Equal(RenameResult.IsEgg, PetLogic.Rename(p, "Eggy", c, out _));
        PetLogic.SetStage(p, PetStage.Chick, c, Now, 1);
        Assert.Equal(RenameResult.Renamed, PetLogic.Rename(p, "Nugget", c, out _));
        Assert.Equal(RenameResult.NoRenamesLeft, PetLogic.Rename(p, "Other", c, out _));
        Assert.Equal("Nugget", p.Name);
        c.Food.Enabled = false;
        Play(p, c, 121);
        Assert.Equal(PetStage.Pullet, p.Stage);
        Assert.Equal(RenameResult.Renamed, PetLogic.Rename(p, "Big Nugget", c, out _));
    }

    [Fact]
    public void Hats_unlock_from_milestones()
    {
        var c = Cfg();
        var p = new PetData();
        PetLogic.NewEgg(p, 1, c, Now);
        Assert.Empty(PetLogic.UnlockedHats(p, c));
        PetLogic.SetStage(p, PetStage.Chick, c, Now, 1);
        Assert.Equal(new[] { "sprout" }, PetLogic.UnlockedHats(p, c).Select(h => h.Id));
        p.Stats.Feeds = 5;
        p.Stage = PetStage.Hen;
        p.Stats.DeathsBurn = p.Stats.DeathsTaser = p.Stats.DeathsBomb = 1;
        var ids = PetLogic.UnlockedIds(p, c);
        Assert.Contains("party", ids);
        Assert.Contains("helmet", ids);
        Assert.Contains("top_hat", ids);
        Assert.Contains("alien", ids);
        Assert.DoesNotContain("banana", ids);
        p.GiftedHats.Add("banana");
        Assert.Contains("banana", PetLogic.UnlockedIds(p, c));
        c.Looks.AllHatsUnlocked = true;
        Assert.Equal(10, PetLogic.UnlockedHats(p, c).Count);
    }

    [Theory]
    [InlineData("inferno", DeathCause.Burn)]
    [InlineData("molotov", DeathCause.Burn)]
    [InlineData("taser", DeathCause.Taser)]
    [InlineData("planted_c4", DeathCause.Bomb)]
    [InlineData("hegrenade", DeathCause.Explosive)]
    [InlineData("knife_t", DeathCause.Knife)]
    [InlineData("ak47", DeathCause.Gun)]
    [InlineData("", DeathCause.Other)]
    public void Death_causes_from_weapon_names(string weapon, DeathCause cause)
    {
        Assert.Equal(cause, PetLogic.CauseFromWeapon(weapon));
    }

    [Theory]
    [InlineData("aim_map", true)]
    [InlineData("de_nuke", true)]
    [InlineData("de_dust2", false)]
    public void Disabled_maps(string map, bool disabled)
    {
        Assert.Equal(disabled, PetLogic.MapDisabled(map, new[] { "aim_*", "de_nuke" }));
    }

    [Fact]
    public void Store_round_trips_and_survives_a_corrupt_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coop-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "pets.json");
        try
        {
            var c = Cfg();
            var store = new PetStore(path);
            var p = store.Get(76561198000000001, "Gdaki");
            PetLogic.SetStage(p, PetStage.Pullet, c, Now, 5);
            p.Name = "Nugget";
            p.Hat = "party";
            store.Save(true);
            store.Save(true); // second save leaves a .bak copy

            var again = new PetStore(path);
            again.Load();
            Assert.True(again.TryGet(76561198000000001, out var loaded));
            Assert.Equal("Nugget", loaded.Name);
            Assert.Equal(PetStage.Pullet, loaded.Stage);
            Assert.Equal(p.Breed, loaded.Breed);
            Assert.Equal("party", loaded.Hat);

            File.WriteAllText(path, "{ not json");
            var fromBackup = new PetStore(path);
            fromBackup.Load();
            Assert.True(fromBackup.TryGet(76561198000000001, out var restored));
            Assert.Equal("Nugget", restored.Name);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Text_helpers()
    {
        Assert.Equal("■■■■■□□□□□", PetLogic.Bar(0.5));
        Assert.Equal("1h 30m", PetLogic.Duration(5400));
        Assert.Equal("45m", PetLogic.Duration(2700));
        Assert.Equal("30s", PetLogic.Duration(30));
    }
}
