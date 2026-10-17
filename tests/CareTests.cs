using ChickenCoop.Logic;
using ChickenCoop.Model;
using Xunit;

namespace ChickenCoop.Tests;

public class CareTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    private static CoopConfig Cfg()
    {
        var c = new CoopConfig();
        c.Shiny.Chance = 0;
        return c;
    }

    private static PetData Pet(PetStage stage, CoopConfig c)
    {
        var p = new PetData { SteamId = 9 };
        PetLogic.SetStage(p, stage, c, Now, 3);
        return p;
    }

    // ================================================================ moods

    [Theory]
    [InlineData(100, Mood.Ecstatic)]
    [InlineData(85, Mood.Ecstatic)]
    [InlineData(70, Mood.Happy)]
    [InlineData(40, Mood.Content)]
    [InlineData(20, Mood.Grumpy)]
    [InlineData(0, Mood.Sulking)]
    public void Happiness_maps_to_moods(double h, Mood mood) => Assert.Equal(mood, PetLogic.MoodOf(h));

    [Fact]
    public void Mood_wears_down_with_play_and_faster_when_hungry()
    {
        var c = Cfg();
        var fed = Pet(PetStage.Chick, c);
        var hungry = Pet(PetStage.Chick, c);
        hungry.FoodSeconds = 0;
        for (var i = 0; i < 720; i++) // an hour
        {
            PetLogic.Advance(fed, 5, c, Now, 1);
            PetLogic.Advance(hungry, 5, c, Now, 1);
        }

        Assert.Equal(70 - 8, fed.Happiness, 3);
        Assert.Equal(70 - 40, hungry.Happiness, 3);
        Assert.Equal(Mood.Grumpy, PetLogic.MoodOf(hungry));
    }

    [Fact]
    public void Mood_changes_are_reported_once()
    {
        var c = Cfg();
        var p = Pet(PetStage.Chick, c);
        p.Happiness = 60.001;
        var events = new List<GrowthEvent>();
        for (var i = 0; i < 20; i++) events.AddRange(PetLogic.Advance(p, 5, c, Now, 1));
        Assert.Single(events, e => e == GrowthEvent.MoodChanged);
        Assert.Equal(Mood.Content, PetLogic.MoodOf(p));
    }

    [Fact]
    public void Care_cheers_it_up_and_dying_upsets_it()
    {
        var c = Cfg();
        c.Food.MaxFoodMinutes = 600;
        var p = Pet(PetStage.Pullet, c);
        p.FeedBags = 1;
        Assert.Equal(FeedResult.Fed, PetLogic.Feed(p, c));
        Assert.Equal(82, p.Happiness, 3);
        Assert.True(PetLogic.Cheer(p, 4, c)); // 86: Happy -> Ecstatic
        PetLogic.RecordDeath(p, DeathCause.Gun, 30, c);
        Assert.Equal(71, p.Happiness, 3);
        PetLogic.RoundPlayed(p, c, false);
        Assert.Equal(71.5, p.Happiness, 3);
        Assert.False(PetLogic.Cheer(new PetData(), 10, c)); // no pet, no mood
    }

    [Fact]
    public void Ecstatic_pets_grow_faster_and_sulking_ones_refuse_tricks()
    {
        var c = Cfg();
        c.Moods.DecayPerHour = 0;
        var happy = Pet(PetStage.Chick, c);
        var plain = Pet(PetStage.Chick, c);
        happy.Happiness = 95;
        PetLogic.Advance(happy, 100, c, Now, 1);
        PetLogic.Advance(plain, 100, c, Now, 1);
        Assert.Equal(110, happy.StageSeconds, 3);
        Assert.Equal(100, plain.StageSeconds, 3);

        plain.Happiness = 5;
        Assert.True(PetLogic.Refuses(plain, c));
        c.Moods.SulkingRefuses = false;
        Assert.False(PetLogic.Refuses(plain, c));
        Assert.Equal("♥♥♥♥", PetLogic.Hearts(happy));
        Assert.Equal("♡♡♡♡", PetLogic.Hearts(plain));
    }

    [Fact]
    public void Moods_can_be_switched_off()
    {
        var c = Cfg();
        c.Moods.Enabled = false;
        var p = Pet(PetStage.Chick, c);
        for (var i = 0; i < 100; i++) PetLogic.Advance(p, 60, c, Now, 1);
        Assert.Equal(70, p.Happiness);
        Assert.False(PetLogic.Cheer(p, 50, c));
    }

    // ================================================================ abilities

    [Fact]
    public void Abilities_unlock_as_the_pet_grows_and_must_be_allowed()
    {
        var c = Cfg();
        var chick = Pet(PetStage.Chick, c);
        var hen = Pet(PetStage.Hen, c);
        Assert.True(PetLogic.AbilityUnlocked(chick, Catalog.FindAbility("cheer")!));
        Assert.False(PetLogic.AbilityUnlocked(chick, Catalog.FindAbility("forager")!));
        Assert.True(PetLogic.AbilityUnlocked(hen, Catalog.FindAbility("broody")!));
        Assert.False(PetLogic.AbilityUnlocked(hen, Catalog.FindAbility("lookout")!));
        hen.Stats.RoundsTogether = 100;
        Assert.True(PetLogic.AbilityUnlocked(hen, Catalog.FindAbility("lookout")!));

        hen.Ability = "lookout";
        Assert.Null(PetLogic.EffectiveAbility(hen, c)); // reveals enemies: not allowed by default
        c.Abilities.Allowed.Add("lookout");
        Assert.Equal("lookout", PetLogic.EffectiveAbility(hen, c));
        c.AbilitiesSuspended = true; // a map rule
        Assert.Null(PetLogic.EffectiveAbility(hen, c));

        chick.Ability = "broody";
        c.AbilitiesSuspended = false;
        Assert.Null(PetLogic.EffectiveAbility(chick, c)); // picked but not unlocked
    }

    [Fact]
    public void Broody_hens_lay_faster_and_charmers_stay_happy_longer()
    {
        var c = Cfg();
        c.Eggs.MinutesPerEgg = 10;
        var plain = Pet(PetStage.Hen, c);
        var broody = Pet(PetStage.Hen, c);
        broody.Ability = "broody";
        PetLogic.Advance(plain, 60, c, Now, 1);
        PetLogic.Advance(broody, 60, c, Now, 1);
        Assert.Equal(60, plain.LaySeconds, 3);
        Assert.Equal(78, broody.LaySeconds, 3);

        var charmer = Pet(PetStage.Pullet, c);
        charmer.Stats.Pets = 50;
        charmer.Ability = "charmer";
        PetLogic.Advance(charmer, 3600, c, Now, 1);
        Assert.Equal(66, charmer.Happiness, 3);
    }

    [Fact]
    public void Foragers_sometimes_find_a_bag()
    {
        var c = Cfg();
        var p = Pet(PetStage.Pullet, c);
        p.FeedBags = 0;
        Assert.False(PetLogic.Forage(p, c, 0.0)); // no ability picked
        p.Ability = "forager";
        Assert.False(PetLogic.Forage(p, c, 0.5)); // unlucky roll
        Assert.True(PetLogic.Forage(p, c, 0.1));
        Assert.Equal(1, p.FeedBags);
        Assert.Equal(1, p.Stats.Forages);
        p.FeedBags = c.Food.MaxBags;
        Assert.False(PetLogic.Forage(p, c, 0.1)); // bags full
    }

    // ================================================================ collections

    [Fact]
    public void Collections_record_coats_and_shinies_once_and_pay_for_new_ones()
    {
        var c = Cfg();
        var p = Pet(PetStage.Chick, c);
        p.FeedBags = 0;
        Assert.Empty(PetLogic.Collect(p, c).Added); // breed not revealed yet

        PetLogic.SetStage(p, PetStage.Pullet, c, Now, 3);
        p.Shiny = "golden";
        var r = PetLogic.Collect(p, c);
        Assert.Equal(2, r.Added.Count);
        Assert.Contains(Catalog.CoatEntry(p.Breed, p.Coat), r.Added);
        Assert.Contains("shiny:golden", r.Added);
        Assert.Equal(2, r.Bags);
        Assert.Empty(PetLogic.Collect(p, c).Added);
        Assert.Equal(2, PetLogic.CollectionTotal(p).Have);
    }

    [Fact]
    public void Past_pets_count_and_a_full_breed_pays_a_bonus()
    {
        var c = Cfg();
        c.Food.MaxBags = 999;
        var p = new PetData { SteamId = 1 };
        var coats = Catalog.CoatNames(Breed.Polish).Count;
        for (var i = 0; i < coats; i++) p.PastPets.Add(new PastPet { Breed = Breed.Polish, Coat = i, Stage = PetStage.Hen });
        p.PastPets.Add(new PastPet { Breed = Breed.Silkie, Coat = 0, Stage = PetStage.Chick }); // never showed its coat

        var r = PetLogic.Collect(p, c);
        Assert.Equal(coats, r.Added.Count);
        Assert.Equal(new[] { Breed.Polish }, r.Completed);
        Assert.Equal(coats + c.Collections.BreedCompleteBags, r.Bags);
        Assert.Equal((coats, coats), PetLogic.CollectionProgress(p, Breed.Polish));
        Assert.Equal(coats, PetLogic.BoardValue(p, Board.Collection));
    }

    // ================================================================ rules, limits, restores

    [Fact]
    public void Map_and_mode_rules_apply_in_order()
    {
        var rules = new List<MapRule>
        {
            new() { Maps = { "jb_*", "ba_jail_*" }, Perch = false, Arena = false },
            new() { Modes = { "deathmatch" }, Pets = false },
            new() { Maps = { "jb_minecraft" }, Arena = true, MaxActivePets = 10 },
        };
        var jb = PetLogic.ResolveRules("jb_minecraft", "casual", rules);
        Assert.False(jb.Perch);
        Assert.True(jb.Arena);
        Assert.True(jb.Pets);
        Assert.Equal(10, jb.MaxActivePets);
        var dm = PetLogic.ResolveRules("de_dust2", "deathmatch", rules);
        Assert.False(dm.Pets);
        Assert.True(dm.Perch);
        Assert.Equal("deathmatch", PetLogic.GameModeName(1, 2));
        Assert.Equal("competitive", PetLogic.GameModeName(0, 1));
    }

    [Fact]
    public void Gifts_have_a_cooldown_a_daily_cap_and_need_a_real_player()
    {
        var c = Cfg();
        c.Eggs.MaxGiftsPerDay = 2;
        var giver = new PetData { SteamId = 1, BasketEggs = 5 };
        PetData Friend(ulong id, double played = 3600) => new() { SteamId = id, PlayedSeconds = played };

        Assert.Equal(GiftResult.TargetTooNew, PetLogic.GiftEgg(giver, Friend(2, 60), c, Now, 1));
        Assert.Equal(GiftResult.Gifted, PetLogic.GiftEgg(giver, Friend(3), c, Now, 1));
        Assert.Equal(GiftResult.Cooldown, PetLogic.GiftEgg(giver, Friend(4), c, Now.AddMinutes(5), 1));
        Assert.Equal(TimeSpan.FromMinutes(5), PetLogic.GiftCooldownLeft(giver, c, Now.AddMinutes(5)));
        Assert.Equal(GiftResult.Gifted, PetLogic.GiftEgg(giver, Friend(5), c, Now.AddMinutes(11), 1));
        Assert.Equal(GiftResult.DailyLimit, PetLogic.GiftEgg(giver, Friend(6), c, Now.AddMinutes(30), 1));
        Assert.Equal(GiftResult.Gifted, PetLogic.GiftEgg(giver, Friend(7), c, Now.AddDays(1), 1)); // a new day
        Assert.Equal(2, giver.BasketEggs);
    }

    [Fact]
    public void Stakes_need_some_play_time()
    {
        var c = Cfg();
        Assert.False(PetLogic.MayStake(new PetData { PlayedSeconds = 60 }, c));
        Assert.True(PetLogic.MayStake(new PetData { PlayedSeconds = 20 * 60 }, c));
        var p = new PetData();
        PetLogic.Advance(p, 20 * 60, c, Now, 1);
        Assert.True(PetLogic.MayStake(p, c));
    }

    [Fact]
    public void Admins_can_bring_back_a_retired_pet()
    {
        var c = Cfg();
        var p = Pet(PetStage.Hen, c);
        p.Name = "Mabel";
        p.Shiny = "shadow";
        p.Stats.RoundsTogether = 40;
        PetLogic.Retire(p, "reset by an admin", Now);
        PetLogic.SetStage(p, PetStage.Chick, c, Now, 8);
        var chickName = p.Name;

        Assert.False(PetLogic.Unretire(p, 5, c, Now));
        Assert.True(PetLogic.Unretire(p, 0, c, Now));
        Assert.Equal("Mabel", p.Name);
        Assert.Equal(PetStage.Hen, p.Stage);
        Assert.Equal("shadow", p.Shiny);
        Assert.Equal(40, p.Stats.RoundsTogether);
        Assert.Single(p.PastPets);
        Assert.Equal(chickName, p.PastPets[0].Name); // the chick went to the journal in its place
    }
}
