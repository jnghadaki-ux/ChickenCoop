using ChickenCoop;
using ChickenCoop.Logic;
using ChickenCoop.Model;
using ChickenCoop.Runtime;
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
        c.Shiny.Chance = 0;
        return c;
    }

    private static List<GrowthEvent> Play(PetData p, CoopConfig c, double minutes, double step = 5)
    {
        var all = new List<GrowthEvent>();
        for (double t = 0; t < minutes * 60; t += step) all.AddRange(PetLogic.Advance(p, step, c, Now, 1234));
        return all;
    }

    // ================================================================ growth

    [Fact]
    public void New_player_finds_an_egg_after_the_configured_play_time()
    {
        var c = Cfg();
        var p = new PetData { SteamId = 1 };
        Assert.Empty(Play(p, c, 4.9));
        Assert.Equal(PetStage.None, p.Stage);
        Assert.Contains(GrowthEvent.FoundEgg, Play(p, c, 0.2));
        Assert.Equal(PetStage.Egg, p.Stage);
        Assert.Equal(c.Food.StarterBags, p.FeedBags);
    }

    [Fact]
    public void Egg_hatches_into_a_named_chick_with_starter_food()
    {
        var c = Cfg();
        var p = new PetData();
        PetLogic.NewEgg(p, 42, c, Now);
        Assert.Contains(GrowthEvent.Hatched, Play(p, c, 30));
        Assert.Equal(PetStage.Chick, p.Stage);
        Assert.False(string.IsNullOrWhiteSpace(p.Name));
        Assert.Equal(c.Food.StarterFoodMinutes * 60, p.FoodSeconds, 3);
        Assert.Equal(Now, p.HatchedUtc);
    }

    [Fact]
    public void Full_life_egg_chick_pullet_hen()
    {
        var c = Cfg();
        c.Food.Enabled = false;
        c.Eggs.Laying = false;
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

        Play(p, c, 30);
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
        c.Eggs.Laying = false;
        c.Growth.HenRetireMinutes = 10;
        var p = new PetData();
        PetLogic.SetStage(p, PetStage.Hen, c, Now, 3);
        p.Name = "Mabel";
        Assert.Contains(GrowthEvent.RetiredOldAge, Play(p, c, 11));
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
        Assert.Contains(GrowthEvent.RetiredHunger, Play(p, c, 1));
        Assert.Equal(PetStage.None, p.Stage);
    }

    // ================================================================ breeds and shiny

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
        Assert.Equal(3, Enumerable.Range(0, 2000).Select(s => PetLogic.Roll(s, c).breed).ToHashSet().Count);
    }

    [Fact]
    public void Shiny_roll_follows_the_chance_and_is_deterministic()
    {
        var c = Cfg();
        c.Shiny.Chance = 0;
        Assert.All(Enumerable.Range(0, 500), s => Assert.Null(PetLogic.RollShiny(s, c)));

        c.Shiny.Chance = 1;
        c.Shiny.Variants = new() { "golden", "rainbow" };
        var rolled = Enumerable.Range(0, 500).Select(s => PetLogic.RollShiny(s, c)).ToList();
        Assert.All(rolled, v => Assert.Contains(v, new[] { "golden", "rainbow" }));
        Assert.Equal(2, rolled.Distinct().Count());
        Assert.Equal(PetLogic.RollShiny(77, c), PetLogic.RollShiny(77, c));

        c.Shiny.Chance = 0.02;
        c.Shiny.Variants = new() { "golden", "rainbow", "shadow", "celestial" };
        var hits = Enumerable.Range(0, 20000).Count(s => PetLogic.RollShiny(s, c) != null);
        Assert.InRange(hits, 250, 560); // about 2%
    }

    [Fact]
    public void Shiny_is_hidden_until_the_pullet_stage()
    {
        var c = Cfg();
        var p = new PetData();
        PetLogic.SetStage(p, PetStage.Chick, c, Now, 1);
        p.Shiny = "golden";
        Assert.False(p.IsShiny);
        Assert.Null(PetLogic.EffectiveStyle(p));
        Assert.Equal("?", PetLogic.Description(p));
        p.Stage = PetStage.Pullet;
        Assert.True(p.IsShiny);
        Assert.Equal(Catalog.ShinyStyleId, PetLogic.EffectiveStyle(p));
        Assert.StartsWith("Golden ", PetLogic.Description(p));
        p.Style = "natural";
        Assert.Null(PetLogic.EffectiveStyle(p));
    }

    [Fact]
    public void Catalog_matches_the_game_files()
    {
        Assert.Equal(15, Catalog.CoatNames(Breed.Catalana).Count);
        Assert.Equal(12, Catalog.CoatNames(Breed.Polish).Count);
        Assert.Equal(13, Catalog.CoatNames(Breed.Silkie).Count);
        Assert.Equal(11, Catalog.Hats.Count);
        Assert.Equal(10, Catalog.Hats.Count(h => h.Model.Contains("/silly_hats/")));
        Assert.Equal(14, Catalog.Emotes.Count);
        Assert.Equal("models/chicken/chicken_silkie.vmdl", Catalog.ModelFor(PetStage.Hen, Breed.Silkie));
        Assert.Equal(Catalog.ChickModel, Catalog.ModelFor(PetStage.Chick, Breed.Silkie));
        Assert.Equal(Catalog.Hats.Count, Catalog.Hats.Select(h => h.Id).Distinct().Count());
        Assert.Equal(Catalog.Emotes.Count, Catalog.Emotes.Select(e => e.Id).Distinct().Count());
    }

    // ================================================================ eggs

    [Fact]
    public void Fed_hens_lay_eggs_on_a_timer()
    {
        var c = Cfg();
        c.Eggs.MinutesPerEgg = 10;
        var p = new PetData();
        PetLogic.SetStage(p, PetStage.Hen, c, Now, 1);
        p.FoodSeconds = 3600;
        var laid = Play(p, c, 31).Count(e => e == GrowthEvent.LaidEgg);
        Assert.Equal(3, laid);
        Assert.Equal(3, p.Stats.EggsLaid);

        // Chicks don't lay, and hungry hens don't either.
        var chick = new PetData();
        PetLogic.SetStage(chick, PetStage.Chick, c, Now, 1);
        Assert.DoesNotContain(GrowthEvent.LaidEgg, Play(chick, c, 31));
        var hungry = new PetData();
        PetLogic.SetStage(hungry, PetStage.Hen, c, Now, 1);
        hungry.FoodSeconds = 0;
        Assert.DoesNotContain(GrowthEvent.LaidEgg, Play(hungry, c, 31));
    }

    [Fact]
    public void Basket_is_capped()
    {
        var c = Cfg();
        c.Eggs.MaxBasket = 2;
        var p = new PetData();
        Assert.True(PetLogic.CollectEgg(p, c));
        Assert.True(PetLogic.CollectEgg(p, c));
        Assert.False(PetLogic.CollectEgg(p, c));
        Assert.Equal(2, p.BasketEggs);
    }

    [Fact]
    public void Gifting_an_egg_gives_a_friend_a_new_pet()
    {
        var c = Cfg();
        var giver = new PetData { SteamId = 1, BasketEggs = 1 };
        var friend = new PetData { SteamId = 2 };
        var owner = new PetData { SteamId = 3 };
        PetLogic.SetStage(owner, PetStage.Chick, c, Now, 1);

        Assert.Equal(GiftResult.Self, PetLogic.GiftEgg(giver, giver, c, Now, 5));
        Assert.Equal(GiftResult.TargetHasPet, PetLogic.GiftEgg(giver, owner, c, Now, 5));
        Assert.Equal(GiftResult.Gifted, PetLogic.GiftEgg(giver, friend, c, Now, 5));
        Assert.Equal(PetStage.Egg, friend.Stage);
        Assert.Equal(0, giver.BasketEggs);
        Assert.Equal(1, giver.Stats.EggsGifted);
        Assert.Equal(GiftResult.NoEggs, PetLogic.GiftEgg(giver, new PetData { SteamId = 4 }, c, Now, 5));
        c.Eggs.Gifting = false;
        Assert.Equal(GiftResult.Disabled, PetLogic.GiftEgg(new PetData { BasketEggs = 1 }, new PetData(), c, Now, 5));
    }

    [Fact]
    public void Trading_eggs_for_feed_stops_at_the_bag_cap()
    {
        var c = Cfg();
        c.Eggs.BagsPerEgg = 1;
        c.Food.MaxBags = 5;
        var p = new PetData { BasketEggs = 4, FeedBags = 3 };
        Assert.Equal(2, PetLogic.TradeEggs(p, int.MaxValue, c));
        Assert.Equal(5, p.FeedBags);
        Assert.Equal(2, p.BasketEggs);
        c.Eggs.BagsPerEgg = 0;
        Assert.Equal(0, PetLogic.TradeEggs(p, 1, c));
    }

    // ================================================================ hats, emotes, looks, seasons

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
        Assert.Contains("hat:party", ids);
        Assert.Contains("hat:helmet", ids);
        Assert.Contains("hat:top_hat", ids);
        Assert.Contains("hat:alien", ids);
        Assert.DoesNotContain("hat:banana", ids);
        p.GiftedHats.Add("banana");
        Assert.Contains("hat:banana", PetLogic.UnlockedIds(p, c));
        c.Looks.AllHatsUnlocked = true;
        Assert.Equal(10, PetLogic.UnlockedHats(p, c).Count); // the seasonal snowman hat still needs the event
    }

    [Fact]
    public void Emotes_unlock_with_the_pets_story()
    {
        var c = Cfg();
        var p = new PetData();
        PetLogic.SetStage(p, PetStage.Chick, c, Now, 1);
        bool Open(string id) => PetLogic.EmoteUnlocked(p, Catalog.FindEmote(id)!, c);
        Assert.True(Open("sit"));
        Assert.True(Open("confetti"));
        Assert.False(Open("bubbles"));
        Assert.False(Open("fire"));
        p.Stage = PetStage.Pullet;
        p.Stats.DeathsBurn = 1;
        Assert.True(Open("bubbles"));
        Assert.True(Open("fire"));
        Assert.False(Open("ufo"));
        p.GiftedHats.Add("alien");
        Assert.True(Open("ufo"));
    }

    [Theory]
    [InlineData(2026, 10, 20, Season.Halloween)]
    [InlineData(2026, 11, 5, Season.Halloween)]
    [InlineData(2026, 11, 6, Season.None)]
    [InlineData(2026, 12, 25, Season.Christmas)]
    [InlineData(2027, 1, 3, Season.Christmas)]
    [InlineData(2027, 1, 7, Season.None)]
    [InlineData(2026, 7, 1, Season.None)]
    public void Seasons_by_date(int y, int m, int d, Season expected)
    {
        Assert.Equal(expected, PetLogic.ActiveSeason(new DateTime(y, m, d), new SeasonConfig()));
    }

    [Fact]
    public void Season_rewards_unlock_once()
    {
        var p = new PetData();
        Assert.Equal(new[] { "Ghost" }, PetLogic.SeasonRewards(p, Season.Halloween));
        Assert.Empty(PetLogic.SeasonRewards(p, Season.Halloween));
        var xmas = PetLogic.SeasonRewards(p, Season.Christmas);
        Assert.Contains("Frost", xmas);
        Assert.Contains("Snowman Hat", xmas);
        Assert.Contains(Catalog.SnowmanHatId, p.GiftedHats);
        Assert.Equal(new[] { "ghost", "frost" }, PetLogic.AvailableStyles(p));
    }

    // ================================================================ leaderboards and names

    [Fact]
    public void Leaderboards_rank_hatched_pets()
    {
        var c = Cfg();
        var a = new PetData { SteamId = 1 };
        var b = new PetData { SteamId = 2 };
        var egg = new PetData { SteamId = 3 };
        PetLogic.SetStage(a, PetStage.Hen, c, Now, 1);
        PetLogic.SetStage(b, PetStage.Chick, c, Now, 1);
        PetLogic.NewEgg(egg, 1, c, Now);
        a.Name = "A";
        b.Name = "B";
        a.Stats.Revenges = 2;
        b.Stats.Revenges = 5;
        egg.Stats.Revenges = 9;
        var top = PetLogic.Leaderboard(new[] { a, b, egg }, Board.Revenges, 10);
        Assert.Equal(new[] { "B", "A" }, top.Select(x => x.Name));
        Assert.Empty(PetLogic.Leaderboard(new[] { a, b }, Board.Eggs, 10));
    }

    [Theory]
    [InlineData("  Sir   Clucks  ", "Sir Clucks")]
    [InlineData("{red}Evil", "redEvil")]
    [InlineData("\"Quoted\"", "Quoted")]
    [InlineData("<b>Bold</b>", "bBold/b")]
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
        c.Food.Enabled = false;
        var p = new PetData();
        PetLogic.NewEgg(p, 1, c, Now);
        Assert.Equal(RenameResult.IsEgg, PetLogic.Rename(p, "Eggy", c, out _));
        PetLogic.SetStage(p, PetStage.Chick, c, Now, 1);
        Assert.Equal(RenameResult.Renamed, PetLogic.Rename(p, "Nugget", c, out _));
        Assert.Equal(RenameResult.NoRenamesLeft, PetLogic.Rename(p, "Other", c, out _));
        Assert.Equal("Nugget", p.Name);
        Play(p, c, 121);
        Assert.Equal(PetStage.Pullet, p.Stage);
        Assert.Equal(RenameResult.Renamed, PetLogic.Rename(p, "Big Nugget", c, out _));
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

    // ================================================================ storage and messages

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
            p.Shiny = "rainbow";
            p.BasketEggs = 3;
            p.ShowOtherPets = false;
            p.UnlockedStyles.Add("ghost");
            store.Save(true);
            store.Save(true); // second save leaves a .bak copy

            var again = new PetStore(path);
            again.Load();
            Assert.True(again.TryGet(76561198000000001, out var loaded));
            Assert.Equal("Nugget", loaded.Name);
            Assert.Equal(PetStage.Pullet, loaded.Stage);
            Assert.Equal(p.Breed, loaded.Breed);
            Assert.Equal("party", loaded.Hat);
            Assert.Equal("rainbow", loaded.Shiny);
            Assert.Equal(3, loaded.BasketEggs);
            Assert.False(loaded.ShowOtherPets);
            Assert.Contains("ghost", loaded.UnlockedStyles);

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
    public void Messages_fall_back_to_the_server_language_then_english()
    {
        var lang = new Lang();
        lang.LoadFrom(new()
        {
            ["en"] = new() { ["a"] = "Hello {0}", ["b"] = "Only English" },
            ["de"] = new() { ["a"] = "Hallo {0}" },
        }, "de");
        Assert.Equal("Hallo Gdaki", lang.Raw("de", "a", "Gdaki"));
        Assert.Equal("Only English", lang.Raw("de", "b"));
        Assert.Equal("Hallo Gdaki", lang.Raw("xx", "a", "Gdaki")); // unknown language: server fallback (de)
        Assert.Equal("missing.key", lang.Raw("en", "missing.key"));
    }

    [Fact]
    public void Shipped_translations_cover_every_english_message()
    {
        var langDir = Path.Combine(AppContext.BaseDirectory, "lang");
        var en = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(langDir, "en.json")))!;
        foreach (var file in Directory.GetFiles(langDir, "*.json"))
        {
            var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!;
            Assert.Empty(en.Keys.Except(map.Keys));
            foreach (var (key, text) in en)
            {
                var want = System.Text.RegularExpressions.Regex.Matches(text, @"\{\d\}").Select(m => m.Value).ToHashSet();
                var got = System.Text.RegularExpressions.Regex.Matches(map[key], @"\{\d\}").Select(m => m.Value).ToHashSet();
                Assert.True(want.SetEquals(got), $"{Path.GetFileName(file)}: {key}");
            }
        }

        Assert.True(Directory.GetFiles(langDir, "*.json").Length >= 6);
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
