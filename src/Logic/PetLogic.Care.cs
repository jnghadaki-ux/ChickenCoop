using System.Globalization;
using ChickenCoop.Model;

namespace ChickenCoop.Logic;

/// <summary>What collecting a pet's coat and shine added: new entries, breeds now complete, and feed bags given.</summary>
public sealed record CollectResult(List<string> Added, List<Breed> Completed, int Bags);

/// <summary>Per-map / per-mode switches after applying <see cref="CoopConfig.MapRules"/>.</summary>
public sealed class ActiveRules
{
    public bool Pets { get; set; } = true;
    public bool Perch { get; set; } = true;
    public bool Arena { get; set; } = true;
    public bool NameTags { get; set; } = true;
    public bool Abilities { get; set; } = true;
    public bool Emotes { get; set; } = true;
    public int? MaxActivePets { get; set; }
}

/// <summary>Moods, abilities, collections, map rules, admin restores and limits.</summary>
public static partial class PetLogic
{
    // ================================================================ moods

    public static Mood MoodOf(double happiness) => happiness switch
    {
        >= 85 => Mood.Ecstatic,
        >= 60 => Mood.Happy,
        >= 35 => Mood.Content,
        >= 15 => Mood.Grumpy,
        _ => Mood.Sulking,
    };

    public static Mood MoodOf(PetData p) => MoodOf(p.Happiness);

    /// <summary>Changes happiness (negative to upset it). Returns true when the mood changed.</summary>
    public static bool Cheer(PetData p, double amount, CoopConfig cfg)
    {
        if (!cfg.Moods.Enabled || !p.IsHatched || amount == 0) return false;
        var before = MoodOf(p);
        p.Happiness = Math.Clamp(p.Happiness + amount, 0, 100);
        return MoodOf(p) != before;
    }

    /// <summary>A sulking pet won't do tricks or perch (when the config says so).</summary>
    public static bool Refuses(PetData p, CoopConfig cfg) =>
        cfg.Moods.Enabled && cfg.Moods.SulkingRefuses && p.IsHatched && MoodOf(p) == Mood.Sulking;

    /// <summary>Growth speed: ecstatic pets grow a little faster.</summary>
    public static double GrowthMultiplier(PetData p, CoopConfig cfg) =>
        cfg.Moods.Enabled && MoodOf(p) == Mood.Ecstatic ? 1 + Math.Max(0, cfg.Moods.EcstaticGrowthBonus) : 1;

    /// <summary>"♥♥♥♡♡": one heart per mood step above sulking.</summary>
    public static string Hearts(PetData p)
    {
        var full = (int)MoodOf(p);
        return new string('♥', full) + new string('♡', 4 - full);
    }

    // ================================================================ abilities

    public static bool AbilityUnlocked(PetData p, Ability a)
    {
        if (!p.IsHatched) return false;
        var s = p.Stats;
        return a.Id switch
        {
            "cheer" => true,
            "forager" => p.Stage >= PetStage.Pullet,
            "broody" => p.Stage >= PetStage.Hen,
            "charmer" => s.Pets >= 50,
            "sentry" => p.Stage >= PetStage.Pullet && s.RoundsTogether >= 25,
            "lookout" => p.Stage >= PetStage.Hen && s.RoundsTogether >= 100,
            _ => false,
        };
    }

    public static bool AbilityAllowed(Ability a, CoopConfig cfg) =>
        cfg.Abilities.Enabled && !cfg.AbilitiesSuspended
        && cfg.Abilities.Allowed.Any(id => id.Trim().Equals(a.Id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The ability actually working: picked, unlocked and allowed on this server. Null otherwise.</summary>
    public static string? EffectiveAbility(PetData p, CoopConfig cfg) =>
        Catalog.FindAbility(p.Ability) is { } a && AbilityAllowed(a, cfg) && AbilityUnlocked(p, a) ? a.Id : null;

    /// <summary>Forager: with <paramref name="roll"/> (0-1) under the chance, the pet finds a feed bag. True if it did.</summary>
    public static bool Forage(PetData p, CoopConfig cfg, double roll)
    {
        if (EffectiveAbility(p, cfg) != "forager" || roll >= cfg.Abilities.ForagerChance) return false;
        if (p.FeedBags >= Math.Max(cfg.Food.MaxBags, 0)) return false;
        p.FeedBags++;
        p.Stats.Forages++;
        return true;
    }

    // ================================================================ collections

    /// <summary>
    /// Adds the current pet's coat and shine (once revealed) and every past pet's to the collection, and pays feed bags
    /// for new entries and completed breeds (capped at MaxBags).
    /// </summary>
    public static CollectResult Collect(PetData p, CoopConfig cfg)
    {
        var added = new List<string>();
        if (!cfg.Collections.Enabled) return new CollectResult(added, new List<Breed>(), 0);

        void Add(string id)
        {
            if (p.Collection.Contains(id, StringComparer.OrdinalIgnoreCase)) return;
            p.Collection.Add(id);
            added.Add(id);
        }

        foreach (var old in p.PastPets.Where(o => o.Stage >= PetStage.Pullet))
        {
            Add(Catalog.CoatEntry(old.Breed, Catalog.CoatOf(old.Breed, old.Coat).Skin));
            if (Catalog.FindShiny(old.Shiny) is { } s) Add(Catalog.ShinyEntry(s.Id));
        }

        if (p.ShowsBreed)
        {
            Add(Catalog.CoatEntry(p.Breed, Catalog.CoatOf(p.Breed, p.Coat).Skin));
            if (p.IsShiny && Catalog.FindShiny(p.Shiny) is { } s) Add(Catalog.ShinyEntry(s.Id));
        }

        var completed = Enum.GetValues<Breed>()
            .Where(b => added.Any(id => id.StartsWith($"coat:{b}/", StringComparison.OrdinalIgnoreCase)))
            .Where(b => CollectionProgress(p, b).Have == Catalog.CoatNames(b).Count)
            .ToList();

        var bags = added.Count * Math.Max(0, cfg.Collections.BagsPerNewEntry) + completed.Count * Math.Max(0, cfg.Collections.BreedCompleteBags);
        var before = p.FeedBags;
        if (bags > 0) p.FeedBags = Math.Min(Math.Max(cfg.Food.MaxBags, 0), p.FeedBags + bags);
        return new CollectResult(added, completed, Math.Max(0, p.FeedBags - before));
    }

    /// <summary>Coats of one breed collected, out of how many there are.</summary>
    public static (int Have, int Total) CollectionProgress(PetData p, Breed breed)
    {
        var total = Catalog.CoatNames(breed).Count;
        var have = Enumerable.Range(0, total).Count(i => p.Collection.Contains(Catalog.CoatEntry(breed, i), StringComparer.OrdinalIgnoreCase));
        return (have, total);
    }

    public static bool Collected(PetData p, string id) => p.Collection.Contains(id, StringComparer.OrdinalIgnoreCase);

    /// <summary>All collection entries there are (coats and shinies), and how many this player has.</summary>
    public static (int Have, int Total) CollectionTotal(PetData p)
    {
        var all = Catalog.AllCollectionIds().ToList();
        return (all.Count(id => Collected(p, id)), all.Count);
    }

    // ================================================================ map and mode rules

    /// <summary>The game mode name for game_type / game_mode, as MapRule.Modes uses it.</summary>
    public static string GameModeName(int gameType, int gameMode) => (gameType, gameMode) switch
    {
        (0, 0) => "casual",
        (0, 1) => "competitive",
        (0, 2) => "wingman",
        (1, 0) => "armsrace",
        (1, 2) => "deathmatch",
        (3, _) => "custom",
        _ => "other",
    };

    public static ActiveRules ResolveRules(string map, string mode, IEnumerable<MapRule> rules)
    {
        var r = new ActiveRules();
        foreach (var rule in rules)
        {
            if (rule.Maps.Count > 0 && !MapDisabled(map, rule.Maps)) continue; // MapDisabled = "matches a pattern"
            if (rule.Modes.Count > 0 && !rule.Modes.Any(m => m.Trim().Equals(mode, StringComparison.OrdinalIgnoreCase))) continue;
            r.Pets = rule.Pets ?? r.Pets;
            r.Perch = rule.Perch ?? r.Perch;
            r.Arena = rule.Arena ?? r.Arena;
            r.NameTags = rule.NameTags ?? r.NameTags;
            r.Abilities = rule.Abilities ?? r.Abilities;
            r.Emotes = rule.Emotes ?? r.Emotes;
            r.MaxActivePets = rule.MaxActivePets ?? r.MaxActivePets;
        }

        return r;
    }

    // ================================================================ limits and admin restores

    public static TimeSpan GiftCooldownLeft(PetData from, CoopConfig cfg, DateTime now)
    {
        if (from.LastGiftUtc is not { } last || cfg.Eggs.GiftCooldownMinutes <= 0) return TimeSpan.Zero;
        var left = last.AddMinutes(cfg.Eggs.GiftCooldownMinutes) - now;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>Has the player played long enough here to wager or bet feed bags?</summary>
    public static bool MayStake(PetData p, CoopConfig cfg) => p.PlayedSeconds >= cfg.Arena.MinPlayMinutesForStakes * 60;

    /// <summary>
    /// Brings back a retired pet from the journal (<paramref name="index"/> 0 = most recent). The current pet, if any,
    /// goes to the journal in its place. Returns false for a bad index.
    /// </summary>
    public static bool Unretire(PetData p, int index, CoopConfig cfg, DateTime now)
    {
        if (index < 0 || index >= p.PastPets.Count) return false;
        var old = p.PastPets[p.PastPets.Count - 1 - index];
        p.PastPets.Remove(old);
        if (p.HasPet) Retire(p, "swapped out by an admin", now);

        p.Seed = HashCode.Combine(old.Name, old.BornUtc.Ticks);
        p.Stage = old.Stage;
        p.Name = old.Name;
        p.Breed = old.Breed;
        p.Coat = old.Coat;
        p.Shiny = old.Shiny;
        p.RenamesThisStage = 0;
        p.StageSeconds = 0;
        p.LifeSeconds = 0;
        p.LaySeconds = 0;
        p.StarvingSeconds = 0;
        p.FoodSeconds = cfg.Food.Enabled ? Math.Max(0, cfg.Food.StarterFoodMinutes) * 60 : 0;
        p.Happiness = Math.Clamp(cfg.Moods.Start, 0, 100);
        p.CreatedUtc = old.BornUtc;
        p.HatchedUtc = old.BornUtc;
        p.Hat = null;
        p.Style = null;
        p.Stats = new PetStats { RoundsTogether = old.RoundsTogether };
        return true;
    }

    /// <summary>Fixes up records saved by older versions (missing play time, out-of-range happiness).</summary>
    public static void Normalize(PetData p)
    {
        var known = p.LifeSeconds + p.SecondsTowardEgg + (p.Stage == PetStage.Egg ? p.StageSeconds : 0);
        if (p.PlayedSeconds < known) p.PlayedSeconds = known;
        if (double.IsNaN(p.Happiness)) p.Happiness = 70;
        p.Happiness = Math.Clamp(p.Happiness, 0, 100);
        p.Collection ??= new List<string>();
    }

    /// <summary>"2026-10-17" for the gift counter (UTC).</summary>
    public static string Day(DateTime utc) => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
