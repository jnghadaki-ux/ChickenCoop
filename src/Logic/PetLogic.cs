using System.Globalization;
using System.Text;
using ChickenCoop.Model;

namespace ChickenCoop.Logic;

public enum GrowthEvent
{
    FoundEgg,
    Hatched,
    BecamePullet,
    BecameHen,
    Hungry,
    Starving,
    LaidEgg,
    RetiredOldAge,
    RetiredHunger,
}

public enum FeedResult { NoPet, IsEgg, NoBags, Full, Fed }

public enum RenameResult { NoPet, IsEgg, Invalid, NoRenamesLeft, Renamed }

public enum GiftResult { Disabled, NoEggs, Self, TargetHasPet, Gifted }

/// <summary>
/// Engine-free pet rules: growth, food, eggs, breeds, shiny pets, hats, looks, emotes, seasons, names and leaderboards.
/// Everything here is unit-tested; the plugin only feeds it played seconds and round results and acts on what it returns.
/// </summary>
public static class PetLogic
{
    // ================================================================ growth

    public static double StageSeconds(PetStage stage, CoopConfig cfg) => stage switch
    {
        PetStage.Egg => Math.Max(0, cfg.Growth.EggMinutes) * 60,
        PetStage.Chick => Math.Max(0, cfg.Growth.ChickMinutes) * 60,
        PetStage.Pullet => Math.Max(0, cfg.Growth.PulletMinutes) * 60,
        PetStage.Hen => Math.Max(0, cfg.Growth.HenRetireMinutes) * 60,
        _ => Math.Max(0, cfg.Growth.MinutesToFindEgg) * 60,
    };

    /// <summary>0..1 progress through the current stage (hens without retirement report 1).</summary>
    public static double Progress(PetData p, CoopConfig cfg)
    {
        var total = StageSeconds(p.Stage, cfg);
        var done = p.Stage == PetStage.None ? p.SecondsTowardEgg : p.StageSeconds;
        if (total <= 0) return 1;
        return Math.Clamp(done / total, 0, 1);
    }

    /// <summary>Seconds of play left in the current stage.</summary>
    public static double SecondsLeft(PetData p, CoopConfig cfg)
    {
        var done = p.Stage == PetStage.None ? p.SecondsTowardEgg : p.StageSeconds;
        return Math.Max(0, StageSeconds(p.Stage, cfg) - done);
    }

    /// <summary>Gives the player a new egg. Breed, coat and shiny are rolled now from the seed but only shown at the pullet stage.</summary>
    public static void NewEgg(PetData p, int seed, CoopConfig cfg, DateTime now)
    {
        p.Stage = PetStage.Egg;
        p.Seed = seed;
        (p.Breed, p.Coat) = Roll(seed, cfg);
        p.Shiny = RollShiny(seed, cfg);
        p.Name = null;
        p.RenamesThisStage = 0;
        p.StageSeconds = 0;
        p.LifeSeconds = 0;
        p.FoodSeconds = 0;
        p.StarvingSeconds = 0;
        p.LaySeconds = 0;
        p.SecondsTowardEgg = 0;
        p.Hat = null;
        p.Style = null;
        p.CreatedUtc = now;
        p.HatchedUtc = null;
        p.Stats = new PetStats();
        p.FeedBags = Math.Min(Math.Max(p.FeedBags, cfg.Food.StarterBags), Math.Max(cfg.Food.MaxBags, 0));
    }

    /// <summary>Deterministic breed + coat roll from a seed, using the configured breed weights.</summary>
    public static (Breed breed, int coat) Roll(int seed, CoopConfig cfg)
    {
        var rng = new Random(seed);
        var weights = Enum.GetValues<Breed>()
            .Select(b => (b, w: cfg.Growth.BreedWeights.TryGetValue(b.ToString(), out var w) ? Math.Max(0, w) : 0))
            .ToList();
        var total = weights.Sum(x => x.w);
        var breed = Breed.Catalana;
        if (total > 0)
        {
            var pick = rng.Next(total);
            foreach (var (b, w) in weights)
            {
                if (pick < w)
                {
                    breed = b;
                    break;
                }

                pick -= w;
            }
        }

        return (breed, rng.Next(Catalog.CoatNames(breed).Count));
    }

    /// <summary>Deterministic shiny roll (independent of the breed roll). Null = not shiny.</summary>
    public static string? RollShiny(int seed, CoopConfig cfg)
    {
        var variants = cfg.Shiny.Variants.Where(v => Catalog.FindShiny(v) != null).ToList();
        if (variants.Count == 0 || cfg.Shiny.Chance <= 0) return null;
        var rng = new Random(unchecked(seed * 31 + 0x5EED));
        if (rng.NextDouble() >= cfg.Shiny.Chance) return null;
        return Catalog.FindShiny(variants[rng.Next(variants.Count)])!.Id;
    }

    /// <summary>
    /// Ages a pet by <paramref name="seconds"/> of play. Returns what happened so the plugin can play sounds, swap models
    /// and tell the owner. At most one stage change per call (the plugin calls this every few seconds).
    /// </summary>
    public static List<GrowthEvent> Advance(PetData p, double seconds, CoopConfig cfg, DateTime now, int seedForNewEgg)
    {
        var events = new List<GrowthEvent>();
        if (seconds <= 0) return events;

        switch (p.Stage)
        {
            case PetStage.None:
                p.SecondsTowardEgg += seconds;
                if (p.SecondsTowardEgg >= StageSeconds(PetStage.None, cfg))
                {
                    NewEgg(p, seedForNewEgg, cfg, now);
                    events.Add(GrowthEvent.FoundEgg);
                }

                return events;

            case PetStage.Egg:
                p.StageSeconds += seconds;
                if (p.StageSeconds >= StageSeconds(PetStage.Egg, cfg)) Hatch(p, cfg, now, events);
                return events;
        }

        // Hatched: growth (and egg laying) runs on food.
        var grow = seconds;
        if (cfg.Food.Enabled)
        {
            var warn = cfg.Food.HungryWarningMinutes * 60;
            var before = p.FoodSeconds;
            grow = Math.Min(seconds, Math.Max(0, p.FoodSeconds));
            p.FoodSeconds = Math.Max(0, p.FoodSeconds - seconds);
            if (grow < seconds) p.StarvingSeconds += seconds - grow;
            else p.StarvingSeconds = 0;
            if (before > warn && p.FoodSeconds <= warn && p.FoodSeconds > 0) events.Add(GrowthEvent.Hungry);
            if (before > 0 && p.FoodSeconds <= 0) events.Add(GrowthEvent.Starving);
        }

        p.LifeSeconds += seconds;
        p.StageSeconds += grow;

        if (cfg.Eggs.Laying && grow > 0 && StageListed(cfg.Eggs.LayingStages, p.Stage) && cfg.Eggs.MinutesPerEgg > 0)
        {
            p.LaySeconds += grow;
            if (p.LaySeconds >= cfg.Eggs.MinutesPerEgg * 60)
            {
                p.LaySeconds = 0;
                p.Stats.EggsLaid++;
                events.Add(GrowthEvent.LaidEgg);
            }
        }

        switch (p.Stage)
        {
            case PetStage.Chick when p.StageSeconds >= StageSeconds(PetStage.Chick, cfg):
                NextStage(p, PetStage.Pullet);
                events.Add(GrowthEvent.BecamePullet);
                break;
            case PetStage.Pullet when p.StageSeconds >= StageSeconds(PetStage.Pullet, cfg):
                NextStage(p, PetStage.Hen);
                events.Add(GrowthEvent.BecameHen);
                break;
            case PetStage.Hen when cfg.Growth.HenRetireMinutes > 0 && p.StageSeconds >= StageSeconds(PetStage.Hen, cfg):
                Retire(p, "old age", now);
                events.Add(GrowthEvent.RetiredOldAge);
                return events;
        }

        if (cfg.Food.Enabled && cfg.Food.StarveRetireMinutes > 0 && p.StarvingSeconds >= cfg.Food.StarveRetireMinutes * 60)
        {
            Retire(p, "hunger", now);
            events.Add(GrowthEvent.RetiredHunger);
        }

        return events;
    }

    public static bool StageListed(IEnumerable<string> stages, PetStage stage) =>
        stages.Any(s => s.Trim().Equals(stage.ToString(), StringComparison.OrdinalIgnoreCase));

    private static void Hatch(PetData p, CoopConfig cfg, DateTime now, List<GrowthEvent> events)
    {
        NextStage(p, PetStage.Chick);
        p.HatchedUtc = now;
        p.FoodSeconds = cfg.Food.Enabled ? Math.Max(0, cfg.Food.StarterFoodMinutes) * 60 : 0;
        p.StarvingSeconds = 0;
        if (string.IsNullOrWhiteSpace(p.Name)) p.Name = DefaultName(p.Seed, cfg);
        events.Add(GrowthEvent.Hatched);
    }

    private static void NextStage(PetData p, PetStage stage)
    {
        p.Stage = stage;
        p.StageSeconds = 0;
        p.RenamesThisStage = 0;
    }

    /// <summary>Forces a stage (admin command / API). Hatching through this gives starter food and a name.</summary>
    public static void SetStage(PetData p, PetStage stage, CoopConfig cfg, DateTime now, int seed)
    {
        if (stage == PetStage.None)
        {
            p.Stage = PetStage.None;
            p.SecondsTowardEgg = 0;
            return;
        }

        if (!p.HasPet) NewEgg(p, seed, cfg, now);
        if (stage >= PetStage.Chick && !p.IsHatched) Hatch(p, cfg, now, new List<GrowthEvent>());
        p.Stage = stage;
        p.StageSeconds = 0;
        p.RenamesThisStage = 0;
    }

    public static string DefaultName(int seed, CoopConfig cfg)
    {
        var names = cfg.Naming.DefaultNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (names.Count == 0) return "Chicken";
        return names[(int)((uint)seed % (uint)names.Count)];
    }

    public static void Retire(PetData p, string reason, DateTime now)
    {
        if (!p.HasPet) return;
        if (p.IsHatched)
        {
            p.PastPets.Add(new PastPet
            {
                Name = p.Name ?? "Chicken",
                Breed = p.Breed,
                Coat = p.Coat,
                Shiny = p.ShowsBreed ? p.Shiny : null,
                Stage = p.Stage,
                BornUtc = p.HatchedUtc ?? p.CreatedUtc,
                RetiredUtc = now,
                Reason = reason,
                RoundsTogether = p.Stats.RoundsTogether,
            });
            if (p.PastPets.Count > 25) p.PastPets.RemoveAt(0);
        }

        p.Stage = PetStage.None;
        p.SecondsTowardEgg = 0;
        p.StageSeconds = 0;
        p.Hat = null;
        p.Style = null;
        p.Name = null;
        p.Shiny = null;
    }

    // ================================================================ food and rounds

    public static FeedResult Feed(PetData p, CoopConfig cfg)
    {
        if (!p.HasPet) return FeedResult.NoPet;
        if (!p.IsHatched) return FeedResult.IsEgg;
        if (p.FeedBags <= 0) return FeedResult.NoBags;
        var max = MaxFoodSeconds(cfg);
        if (cfg.Food.Enabled && p.FoodSeconds >= max - 60) return FeedResult.Full;
        p.FeedBags--;
        p.FoodSeconds = Math.Min(max, p.FoodSeconds + cfg.Food.MinutesPerBag * 60);
        p.StarvingSeconds = 0;
        p.Stats.Feeds++;
        return FeedResult.Fed;
    }

    public static double MaxFoodSeconds(CoopConfig cfg) => Math.Max(cfg.Food.MaxFoodMinutes, cfg.Food.MinutesPerBag) * 60;

    /// <summary>A round ended with this player on a team. Returns how many feed bags it earned.</summary>
    public static int RoundPlayed(PetData p, CoopConfig cfg, bool won)
    {
        var max = Math.Max(cfg.Food.MaxBags, 0);
        var gained = 0;
        if (cfg.Food.RoundsPerBag > 0)
        {
            p.RoundsTowardFeedBag++;
            if (p.RoundsTowardFeedBag >= cfg.Food.RoundsPerBag)
            {
                p.RoundsTowardFeedBag = 0;
                gained++;
            }
        }

        if (won) gained += Math.Max(cfg.Food.BagsPerRoundWin, 0);
        var before = p.FeedBags;
        p.FeedBags = Math.Min(max, p.FeedBags + gained);
        if (p.IsHatched) p.Stats.RoundsTogether++;
        return p.FeedBags - before;
    }

    // ================================================================ eggs

    /// <summary>Puts one egg in the basket. False when the basket is full.</summary>
    public static bool CollectEgg(PetData p, CoopConfig cfg)
    {
        if (p.BasketEggs >= Math.Max(cfg.Eggs.MaxBasket, 0)) return false;
        p.BasketEggs++;
        return true;
    }

    /// <summary>Gives a basket egg to someone without a pet: it becomes their new egg.</summary>
    public static GiftResult GiftEgg(PetData from, PetData to, CoopConfig cfg, DateTime now, int seed)
    {
        if (!cfg.Eggs.Gifting) return GiftResult.Disabled;
        if (from.BasketEggs <= 0) return GiftResult.NoEggs;
        if (from.SteamId != 0 && from.SteamId == to.SteamId) return GiftResult.Self;
        if (to.HasPet) return GiftResult.TargetHasPet;
        from.BasketEggs--;
        from.Stats.EggsGifted++;
        NewEgg(to, seed, cfg, now);
        return GiftResult.Gifted;
    }

    /// <summary>Trades basket eggs for feed bags. Returns how many eggs were traded (stops when bags are full).</summary>
    public static int TradeEggs(PetData p, int count, CoopConfig cfg)
    {
        if (cfg.Eggs.BagsPerEgg <= 0) return 0;
        var traded = 0;
        while (traded < count && p.BasketEggs > 0 && p.FeedBags + cfg.Eggs.BagsPerEgg <= Math.Max(cfg.Food.MaxBags, 0))
        {
            p.BasketEggs--;
            p.FeedBags += cfg.Eggs.BagsPerEgg;
            p.Stats.EggsTraded++;
            traded++;
        }

        return traded;
    }

    // ================================================================ hats, looks, emotes

    public static bool HatUnlocked(PetData p, Hat hat, CoopConfig cfg)
    {
        if (p.GiftedHats.Contains(hat.Id, StringComparer.OrdinalIgnoreCase)) return true;
        if (cfg.Looks.AllHatsUnlocked && hat.Id != Catalog.SnowmanHatId) return true;
        var s = p.Stats;
        return hat.Id switch
        {
            "sprout" => p.IsHatched,
            "party" => s.Feeds >= 5,
            "glasses" => s.Pets >= 25,
            "helmet" => p.Stage >= PetStage.Pullet,
            "armor_helmet" => s.RoundsTogether >= 50,
            "nose_glasses" => s.Tricks + s.Emotes >= 30,
            "banana" => s.OwnerKillsWithPet >= 50,
            "top_hat" => p.Stage >= PetStage.Hen,
            "wizard_hat" => s.Revenges >= 3,
            "alien" => s.DeathsBurn > 0 && s.DeathsTaser > 0 && s.DeathsBomb > 0,
            _ => false,
        };
    }

    public static List<Hat> UnlockedHats(PetData p, CoopConfig cfg) => Catalog.Hats.Where(h => HatUnlocked(p, h, cfg)).ToList();

    /// <summary>Ids of everything unlocked right now (hats, emotes, looks), to announce what a change unlocked.</summary>
    public static HashSet<string> UnlockedIds(PetData p, CoopConfig cfg)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in UnlockedHats(p, cfg)) set.Add("hat:" + h.Id);
        foreach (var e in Catalog.Emotes.Where(e => EmoteUnlocked(p, e, cfg))) set.Add("emote:" + e.Id);
        foreach (var s in p.UnlockedStyles) set.Add("style:" + s);
        return set;
    }

    public static bool EmoteUnlocked(PetData p, Emote e, CoopConfig cfg)
    {
        if (!p.IsHatched) return false;
        if (cfg.Looks.AllHatsUnlocked) return true;
        var s = p.Stats;
        return e.Id switch
        {
            "confetti" => p.IsHatched,
            "bubbles" => p.Stage >= PetStage.Pullet,
            "sparks" => s.Tricks + s.Emotes >= 10,
            "fire" => s.DeathsBurn > 0,
            "lightning" => s.DeathsTaser > 0,
            "explosion" => s.DeathsBomb > 0,
            "lasers" => s.Revenges > 0,
            "ufo" => HatUnlocked(p, Catalog.FindHat("alien")!, cfg),
            _ => true,
        };
    }

    /// <summary>Looks a pet can wear: natural, its shiny variant (once revealed), and unlocked seasonal styles.</summary>
    public static List<string> AvailableStyles(PetData p)
    {
        var list = new List<string>();
        if (p.IsShiny) list.Add(Catalog.ShinyStyleId);
        list.AddRange(Catalog.Styles.Where(s => p.UnlockedStyles.Contains(s.Id, StringComparer.OrdinalIgnoreCase)).Select(s => s.Id));
        return list;
    }

    /// <summary>The look actually shown: the chosen style if still available, else shiny pets show their shine.</summary>
    public static string? EffectiveStyle(PetData p)
    {
        if (p.Style == "natural") return null;
        if (p.Style != null && AvailableStyles(p).Contains(p.Style, StringComparer.OrdinalIgnoreCase)) return p.Style;
        return p.IsShiny ? Catalog.ShinyStyleId : null;
    }

    // ================================================================ seasons

    public static Season ActiveSeason(DateTime date, SeasonConfig cfg)
    {
        if (!cfg.Enabled) return Season.None;
        if (InRange(date, cfg.HalloweenStart, cfg.HalloweenEnd)) return Season.Halloween;
        if (InRange(date, cfg.ChristmasStart, cfg.ChristmasEnd)) return Season.Christmas;
        return Season.None;
    }

    private static bool InRange(DateTime date, string start, string end)
    {
        if (!TryMonthDay(start, out var s) || !TryMonthDay(end, out var e)) return false;
        var d = date.Month * 100 + date.Day;
        return s <= e ? d >= s && d <= e : d >= s || d <= e;
    }

    private static bool TryMonthDay(string text, out int value)
    {
        value = 0;
        if (!DateTime.TryParseExact((text ?? "").Trim(), "MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            && !DateTime.TryParseExact("2000-" + (text ?? "").Trim(), "yyyy-M-d", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            return false;
        value = d.Month * 100 + d.Day;
        return true;
    }

    /// <summary>Unlocks this season's keepsakes for a player. Returns the names of new unlocks (to announce).</summary>
    public static List<string> SeasonRewards(PetData p, Season season)
    {
        var added = new List<string>();
        void Style(string id)
        {
            if (p.UnlockedStyles.Contains(id, StringComparer.OrdinalIgnoreCase)) return;
            p.UnlockedStyles.Add(id);
            added.Add(Catalog.FindStyle(id)!.Name);
        }

        switch (season)
        {
            case Season.Halloween:
                Style(Catalog.GhostStyleId);
                break;
            case Season.Christmas:
                Style(Catalog.FrostStyleId);
                if (!p.GiftedHats.Contains(Catalog.SnowmanHatId, StringComparer.OrdinalIgnoreCase))
                {
                    p.GiftedHats.Add(Catalog.SnowmanHatId);
                    added.Add(Catalog.FindHat(Catalog.SnowmanHatId)!.Name);
                }

                break;
        }

        return added;
    }

    // ================================================================ leaderboards

    public static double BoardValue(PetData p, Board board) => board switch
    {
        Board.Age => p.LifeSeconds,
        Board.Rounds => p.Stats.RoundsTogether,
        Board.Revenges => p.Stats.Revenges,
        Board.Tricks => p.Stats.Tricks + p.Stats.Emotes,
        Board.Pets => p.Stats.Pets,
        Board.Eggs => p.Stats.EggsLaid,
        _ => 0,
    };

    /// <summary>Top hatched pets by a metric (ties: older pet first).</summary>
    public static List<PetData> Leaderboard(IEnumerable<PetData> pets, Board board, int take) =>
        pets.Where(p => p.IsHatched && BoardValue(p, board) > 0)
            .OrderByDescending(p => BoardValue(p, board))
            .ThenByDescending(p => p.LifeSeconds)
            .Take(Math.Max(1, take))
            .ToList();

    // ================================================================ names

    /// <summary>Cleans a player-typed name: no control characters, colour braces or HTML, single spaces, length limit, blocked words.</summary>
    public static string? SanitizeName(string raw, CoopConfig cfg, out string? reason)
    {
        reason = null;
        var sb = new StringBuilder();
        foreach (var ch in raw.Trim().Trim('"'))
        {
            if (char.IsControl(ch) || ch is '{' or '}' or '<' or '>' or '"' or '\\' or '&') continue;
            if (char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                continue;
            }

            sb.Append(ch);
        }

        var name = sb.ToString().Trim();
        var max = Math.Clamp(cfg.Naming.MaxLength, 1, 32);
        if (name.Length == 0)
        {
            reason = "empty";
            return null;
        }

        if (name.Length > max)
        {
            reason = "long";
            return null;
        }

        if (cfg.Naming.BlockedWords.Any(w => !string.IsNullOrWhiteSpace(w) && name.Contains(w.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            reason = "blocked";
            return null;
        }

        return name;
    }

    public static RenameResult Rename(PetData p, string raw, CoopConfig cfg, out string? reason)
    {
        reason = null;
        if (!p.HasPet) return RenameResult.NoPet;
        if (!p.IsHatched) return RenameResult.IsEgg;
        if (cfg.Naming.RenamesPerStage > 0 && p.RenamesThisStage >= cfg.Naming.RenamesPerStage) return RenameResult.NoRenamesLeft;
        var name = SanitizeName(raw, cfg, out reason);
        if (name == null) return RenameResult.Invalid;
        p.Name = name;
        p.RenamesThisStage++;
        return RenameResult.Renamed;
    }

    public static int RenamesLeft(PetData p, CoopConfig cfg) =>
        cfg.Naming.RenamesPerStage <= 0 ? int.MaxValue : Math.Max(0, cfg.Naming.RenamesPerStage - p.RenamesThisStage);

    // ================================================================ deaths

    public static DeathCause CauseFromWeapon(string? weapon)
    {
        var w = (weapon ?? "").ToLowerInvariant();
        if (w.Contains("inferno") || w.Contains("molotov") || w.Contains("incgrenade") || w.Contains("fire")) return DeathCause.Burn;
        if (w.Contains("taser")) return DeathCause.Taser;
        if (w.Contains("c4") || w.Contains("planted")) return DeathCause.Bomb;
        if (w.Contains("hegrenade") || w.Contains("explo")) return DeathCause.Explosive;
        if (w.Contains("knife") || w.Contains("bayonet")) return DeathCause.Knife;
        return w.Length == 0 || w == "world" ? DeathCause.Other : DeathCause.Gun;
    }

    public static void RecordDeath(PetData p, DeathCause cause, double secondsAlive)
    {
        p.Stats.Deaths++;
        switch (cause)
        {
            case DeathCause.Burn: p.Stats.DeathsBurn++; break;
            case DeathCause.Taser: p.Stats.DeathsTaser++; break;
            case DeathCause.Bomb: p.Stats.DeathsBomb++; break;
            case DeathCause.Explosive: p.Stats.DeathsExplosive++; break;
        }

        p.Stats.LongestLifeSeconds = Math.Max(p.Stats.LongestLifeSeconds, secondsAlive);
    }

    // ================================================================ text helpers

    public static string Duration(double seconds)
    {
        if (seconds < 60) return $"{Math.Max(0, (int)seconds)}s";
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        return $"{t.Minutes}m";
    }

    /// <summary>A 10-cell text bar for chat, e.g. ■■■■□□□□□□.</summary>
    public static string Bar(double fraction, int cells = 10)
    {
        var filled = (int)Math.Round(Math.Clamp(fraction, 0, 1) * cells);
        return new string('■', filled) + new string('□', cells - filled);
    }

    public static string StageName(PetStage stage) => stage switch
    {
        PetStage.Egg => "Egg",
        PetStage.Chick => "Chick",
        PetStage.Pullet => "Pullet",
        PetStage.Hen => "Hen",
        _ => "No pet",
    };

    /// <summary>"Shiny Golden Silkie (Leopard)" once revealed, "?" before.</summary>
    public static string Description(PetData p)
    {
        if (!p.ShowsBreed) return "?";
        var shiny = p.IsShiny ? Catalog.FindShiny(p.Shiny)!.Name + " " : "";
        return $"{shiny}{Catalog.BreedName(p.Breed)} ({Catalog.CoatOf(p.Breed, p.Coat).Name})";
    }

    public static bool MapDisabled(string map, IEnumerable<string> patterns)
    {
        foreach (var raw in patterns)
        {
            var pat = raw.Trim();
            if (pat.Length == 0) continue;
            if (pat.EndsWith('*') ? map.StartsWith(pat[..^1], StringComparison.OrdinalIgnoreCase) : map.Equals(pat, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
