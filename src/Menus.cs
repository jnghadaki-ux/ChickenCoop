using ChickenCoop.Logic;
using ChickenCoop.Menus;
using ChickenCoop.Model;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace ChickenCoop;

/// <summary>
/// Every Chicken Coop menu. Built fresh each time they open (so counts and bars are current) and drawn by
/// <see cref="MenuService"/> in the SWBS Jailbreak style: W/S + E + R, or !1-!9.
/// </summary>
public sealed partial class ChickenCoopPlugin
{
    private const string Gold = "#FFD24A";
    private const string Green = "#7CFC7C";
    private const string Grey = "#9A9A9A";
    private const string Red = "#FF6B6B";
    private const string Blue = "#7FC8FF";

    private static string C(string text, string color) => MenuService.Color(text, color);

    private static string Arrow(string text) => text + " &#9656;";

    // ================================================================ main menu

    internal void OpenMain(CCSPlayerController p) => BuildMain(p).Open(p);

    private CoopMenu BuildMain(CCSPlayerController p)
    {
        var pet = Data(p)!;
        var menu = new CoopMenu(M(p, "menu.title"), MainSubtitle(p, pet)) { Refresh = () => BuildMain(p) };

        if (pet.IsHatched)
        {
            menu.Add(M(p, "menu.feed", pet.FeedBags), pl => DoFeed(pl), pet.FeedBags > 0, keepOpen: true);
            menu.Add(Arrow(M(p, "menu.commands")), pl => Sub(pl, BuildCommands, BuildMain));
            menu.Add(Arrow(M(p, "menu.emotes")), pl => Sub(pl, BuildEmotes, BuildMain));
            menu.Add(Arrow(M(p, "menu.wardrobe")), pl => Sub(pl, BuildWardrobe, BuildMain));
            if (Config.Abilities.Enabled && _rules.Abilities)
                menu.Add(Arrow(M(p, "menu.abilities", AbilityLabel(p, pet))), pl => Sub(pl, BuildAbilities, BuildMain));
            if (Config.Eggs.Laying || pet.BasketEggs > 0)
                menu.Add(Arrow(M(p, "menu.basket", pet.BasketEggs)), pl => Sub(pl, BuildBasket, BuildMain));
            var renames = PetLogic.RenamesLeft(pet, Config);
            menu.Add(M(p, "menu.rename"), pl => AskRename(pl), renames > 0);
        }
        else if (pet.HasPet)
        {
            menu.Info(C(M(p, "menu.egg.info", PetLogic.Duration(PetLogic.SecondsLeft(pet, Config))), Gold));
            if (pet.BasketEggs > 0) menu.Add(Arrow(M(p, "menu.basket", pet.BasketEggs)), pl => Sub(pl, BuildBasket, BuildMain));
        }
        else
        {
            menu.Info(C(M(p, "menu.none.info", PetLogic.Duration(PetLogic.SecondsLeft(pet, Config))), Gold));
            if (pet.BasketEggs > 0) menu.Add(Arrow(M(p, "menu.basket", pet.BasketEggs)), pl => Sub(pl, BuildBasket, BuildMain));
        }

        if (pet.HasPet) menu.Add(Arrow(M(p, "menu.info")), pl => Sub(pl, BuildInfo, BuildMain));
        menu.Add(Arrow(M(p, "menu.journal")), pl => Sub(pl, BuildJournal, BuildMain));
        if (Config.Collections.Enabled)
        {
            var (have, total) = PetLogic.CollectionTotal(pet);
            menu.Add(Arrow(M(p, "menu.collection", have, total)), pl => Sub(pl, BuildCollection, BuildMain));
        }

        if (Config.Arena.Enabled)
        {
            var challenged = _challenges.Any(c => c.To == p.Slot && c.Expires > CounterStrikeSharp.API.Server.CurrentTime);
            menu.Add(Arrow(challenged ? C(M(p, "menu.arena.challenged"), Gold) : M(p, "menu.arena")), pl => OpenArena(pl));
        }

        menu.Add(Arrow(M(p, "menu.leaderboards")), pl => Sub(pl, BuildBoards, BuildMain));
        menu.Add(Arrow(M(p, "menu.settings")), pl => Sub(pl, BuildSettings, BuildMain));
        menu.Add(Arrow(M(p, "menu.help")), pl => Sub(pl, BuildHelp, BuildMain));
        if (IsAdmin(p)) menu.Add(Arrow(C(M(p, "menu.admin"), Red)), pl => Sub(pl, BuildAdmin, BuildMain));
        return menu;
    }

    /// <summary>Opens a submenu whose R / 7 goes back to <paramref name="back"/>.</summary>
    private static void Sub(CCSPlayerController p, Func<CCSPlayerController, CoopMenu> build, Func<CCSPlayerController, CoopMenu> back)
    {
        var menu = build(p);
        menu.Back ??= () => back(p);
        menu.Refresh ??= () =>
        {
            var m = build(p);
            m.Back ??= () => back(p);
            return m;
        };
        menu.Open(p);
    }

    private string MainSubtitle(CCSPlayerController p, PetData pet)
    {
        if (!pet.HasPet) return M(p, "menu.sub.none");
        var progress = PetLogic.Progress(pet, Config);
        if (!pet.IsHatched) return $"{M(p, "menu.sub.egg")}<br>{MenuService.Bar(progress, Gold)} {(int)(progress * 100)}%";

        var name = C(MenuService.Esc(pet.Name), pet.IsShiny ? Gold : Green);
        var line1 = $"{name} · {M(p, "stage." + pet.Stage.ToString().ToLowerInvariant())} · {MenuService.Esc(PetLogic.Description(pet))}";
        if (Config.Moods.Enabled) line1 += " · " + C(PetLogic.Hearts(pet), MoodColor(pet));
        var grow = pet.Stage == PetStage.Hen && Config.Growth.HenRetireMinutes <= 0
            ? M(p, "menu.sub.grown")
            : $"{M(p, "menu.sub.growth")} {MenuService.Bar(progress)} {(int)(progress * 100)}%";
        var food = Config.Food.Enabled
            ? $" · {M(p, "menu.sub.food")} {MenuService.Bar(pet.FoodSeconds / PetLogic.MaxFoodSeconds(Config), pet.FoodSeconds <= Config.Food.HungryWarningMinutes * 60 ? Red : Blue, 6)}"
            : "";
        return $"{line1}<br>{grow}{food}";
    }

    private static string MoodColor(PetData pet) => PetLogic.MoodOf(pet) switch
    {
        Mood.Ecstatic or Mood.Happy => "#FF7FB0",
        Mood.Content => Gold,
        _ => Grey,
    };

    private string MoodText(CCSPlayerController p, PetData pet) => M(p, "mood." + PetLogic.MoodOf(pet).ToString().ToLowerInvariant());

    private string AbilityLabel(CCSPlayerController p, PetData pet) =>
        Catalog.FindAbility(PetLogic.EffectiveAbility(pet, Config))?.Name ?? M(p, "menu.none");

    private void AskRename(CCSPlayerController p)
    {
        var pet = Data(p);
        if (pet is not { IsHatched: true }) return;
        Tell(p, "name.prompt", Config.Naming.MaxLength);
        ChatInput.Ask(p, (pl, text) =>
        {
            DoRename(pl, text);
            OpenMain(pl);
        }, pl =>
        {
            Tell(pl, "name.cancelled");
            OpenMain(pl);
        });
    }

    // ================================================================ commands

    private CoopMenu BuildCommands(CCSPlayerController p)
    {
        var pet = Data(p)!;
        var a = Actor(p);
        var menu = new CoopMenu(M(p, "menu.commands"), M(p, "menu.commands.sub", MenuService.Esc(pet.Name)));
        var outNow = a.ChickenAlive;
        menu.Add(M(p, a.Staying ? "menu.cmd.come" : "menu.cmd.sit"), pl => DoStay(pl, !a.Staying), outNow, keepOpen: true);
        if (Config.Perch.Enabled)
        {
            var canPerch = CanPerch(pet);
            menu.Add(a.Perched ? M(p, "menu.cmd.unperch") : canPerch ? M(p, "menu.cmd.perch") : C(M(p, "menu.cmd.perch.big"), Grey),
                pl => DoPerch(pl, !a.Perched), (outNow || a.PerchOut) && (canPerch || a.Perched), keepOpen: true);
        }

        menu.Add(M(p, "menu.cmd.trick"), pl => DoEmote(pl, Catalog.FindEmote("flap")!), outNow, keepOpen: true);
        menu.Add(M(p, pet.Hidden ? "menu.cmd.show" : "menu.cmd.hide"), pl => DoHide(pl, !pet.Hidden), keepOpen: true);
        menu.Info(C(M(p, "menu.cmd.petting"), Grey));
        return menu;
    }

    // ================================================================ emotes

    private CoopMenu BuildEmotes(CCSPlayerController p)
    {
        var pet = Data(p)!;
        var menu = new CoopMenu(M(p, "menu.emotes"), M(p, "menu.emotes.sub",
            Catalog.Emotes.Count(e => PetLogic.EmoteUnlocked(pet, e, Config)), Catalog.Emotes.Count));
        foreach (var e in Catalog.Emotes)
        {
            var emote = e;
            if (PetLogic.EmoteUnlocked(pet, e, Config))
                menu.Add(MenuService.Esc(e.Name), pl => DoEmote(pl, emote), keepOpen: true);
            else
                menu.Add(C($"{MenuService.Esc(e.Name)} · {M(p, "menu.locked")}", Grey), pl => Tell(pl, "emote.locked", emote.Name, emote.HowToUnlock), keepOpen: true);
        }

        return menu;
    }

    // ================================================================ wardrobe: hats and looks

    private CoopMenu BuildWardrobe(CCSPlayerController p)
    {
        var pet = Data(p)!;
        var hat = pet.Hat != null ? Catalog.FindHat(pet.Hat)?.Name ?? "-" : M(p, "menu.none");
        var look = PetLogic.EffectiveStyle(pet) is { } s ? StyleName(pet, s) : M(p, "menu.looks.natural");
        var menu = new CoopMenu(M(p, "menu.wardrobe"), M(p, "menu.wardrobe.sub", MenuService.Esc(hat), MenuService.Esc(look)));
        menu.Add(Arrow(M(p, "menu.hats", PetLogic.UnlockedHats(pet, Config).Count, Catalog.Hats.Count)), pl => Sub(pl, BuildHats, BuildWardrobe));
        menu.Add(Arrow(M(p, "menu.looks", PetLogic.AvailableStyles(pet).Count + 1)), pl => Sub(pl, BuildLooks, BuildWardrobe));
        return menu;
    }

    private CoopMenu BuildHats(CCSPlayerController p)
    {
        var pet = Data(p)!;
        var menu = new CoopMenu(M(p, "menu.hats.title"), M(p, "menu.hats.sub", MenuService.Esc(pet.Name)));
        menu.Add(pet.Hat == null ? C(M(p, "menu.hats.none") + " ✔", Green) : M(p, "menu.hats.none"), pl => DoHat(pl, "off"), pet.Hat != null, keepOpen: true);
        foreach (var h in Catalog.Hats)
        {
            var hat = h;
            var open = PetLogic.HatUnlocked(pet, h, Config);
            if (!open && h.Id == Catalog.SnowmanHatId && _season != Season.Christmas) continue; // seasonal: only shown once earned
            if (open)
            {
                var worn = string.Equals(pet.Hat, h.Id, StringComparison.OrdinalIgnoreCase);
                menu.Add(worn ? C(MenuService.Esc(h.Name) + " ✔", Green) : MenuService.Esc(h.Name), pl => DoHat(pl, hat.Id), !worn, keepOpen: true);
            }
            else
            {
                menu.Add(C($"{MenuService.Esc(h.Name)} · {M(p, "menu.locked")}", Grey), pl => Tell(pl, "hat.locked", hat.Name, hat.HowToUnlock), keepOpen: true);
            }
        }

        return menu;
    }

    private string StyleName(PetData pet, string style) =>
        style == Catalog.ShinyStyleId ? (Catalog.FindShiny(pet.Shiny)?.Name ?? "Shiny") + " ✦" : Catalog.FindStyle(style)?.Name ?? style;

    private CoopMenu BuildLooks(CCSPlayerController p)
    {
        var pet = Data(p)!;
        var current = PetLogic.EffectiveStyle(pet);
        var menu = new CoopMenu(M(p, "menu.looks.title"), M(p, "menu.looks.sub"));
        menu.Add(current == null ? C(M(p, "menu.looks.natural") + " ✔", Green) : M(p, "menu.looks.natural"), pl => SetStyle(pl, "natural"), current != null, keepOpen: true);
        if (pet.Shiny != null)
        {
            if (pet.IsShiny)
            {
                var label = StyleName(pet, Catalog.ShinyStyleId);
                menu.Add(current == Catalog.ShinyStyleId ? C(label + " ✔", Gold) : C(label, Gold), pl => SetStyle(pl, Catalog.ShinyStyleId), current != Catalog.ShinyStyleId, keepOpen: true);
            }
            else
            {
                menu.Info(C(M(p, "menu.looks.mystery"), Grey));
            }
        }

        foreach (var st in Catalog.Styles)
        {
            var id = st.Id;
            if (pet.UnlockedStyles.Contains(id, StringComparer.OrdinalIgnoreCase))
                menu.Add(current == id ? C(MenuService.Esc(st.Name) + " ✔", Green) : MenuService.Esc(st.Name), pl => SetStyle(pl, id), current != id, keepOpen: true);
            else
                menu.Add(C($"{MenuService.Esc(st.Name)} · {M(p, "menu.locked")}", Grey), pl => Tell(pl, "look.locked", st.Name, st.HowToUnlock), keepOpen: true);
        }

        return menu;
    }

    private void SetStyle(CCSPlayerController p, string style)
    {
        var pet = Data(p);
        if (pet is not { IsHatched: true }) return;
        pet.Style = style;
        _store.MarkDirty();
        var name = style == "natural" ? _lang.P(p, "menu.looks.natural") : StyleName(pet, style);
        Tell(p, "look.on", pet.Name, name);
        if (_actors.TryGetValue(p.Slot, out var a))
        {
            a.ShownStyle = "?";
            if (a.PerchOut && AlivePawn(p) is { } pawn) SpawnPerch(a, pawn, pet);
            else RefreshLooks(a, pet, p);
        }
    }

    // ================================================================ abilities

    private CoopMenu BuildAbilities(CCSPlayerController p)
    {
        var pet = Data(p)!;
        var current = PetLogic.EffectiveAbility(pet, Config);
        var menu = new CoopMenu(M(p, "menu.abilities.title"), M(p, "menu.abilities.sub", MenuService.Esc(pet.Name)));
        menu.Add(current == null ? C(M(p, "menu.none") + " ✔", Green) : M(p, "menu.none"), pl => DoAbility(pl, "none"), current != null, keepOpen: true);
        foreach (var ab in Catalog.Abilities.Where(x => PetLogic.AbilityAllowed(x, Config)))
        {
            var id = ab.Id;
            if (PetLogic.AbilityUnlocked(pet, ab))
                menu.Add(current == id ? C(MenuService.Esc(ab.Name) + " ✔", Green) : MenuService.Esc(ab.Name), pl => DoAbility(pl, id), current != id, keepOpen: true);
            else
                menu.Add(C($"{MenuService.Esc(ab.Name)} · {M(p, "menu.locked")}", Grey), pl => Tell(pl, "ability.locked", ab.Name, ab.HowToUnlock), keepOpen: true);
        }

        if (Catalog.FindAbility(current) is { } a) menu.Info(C(MenuService.Esc(a.Description), Grey));
        return menu;
    }

    internal void DoAbility(CCSPlayerController p, string which)
    {
        var pet = Data(p);
        if (pet == null) return;
        if (!pet.IsHatched)
        {
            Tell(p, "pet.egg_only");
            return;
        }

        if (!Config.Abilities.Enabled || !_rules.Abilities)
        {
            Tell(p, "ability.off");
            return;
        }

        if (which.Equals("none", StringComparison.OrdinalIgnoreCase) || which.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            pet.Ability = null;
            _store.MarkDirty();
            Tell(p, "ability.cleared", pet.Name);
            return;
        }

        var ab = Catalog.FindAbility(which);
        if (ab == null || !PetLogic.AbilityAllowed(ab, Config))
        {
            Tell(p, "ability.unknown", which, string.Join(", ", Catalog.Abilities.Where(x => PetLogic.AbilityAllowed(x, Config)).Select(x => x.Id)));
            return;
        }

        if (!PetLogic.AbilityUnlocked(pet, ab))
        {
            Tell(p, "ability.locked", ab.Name, ab.HowToUnlock);
            return;
        }

        pet.Ability = ab.Id;
        _store.MarkDirty();
        Tell(p, "ability.set", pet.Name, ab.Name, ab.Description);
    }

    // ================================================================ collection

    private CoopMenu BuildCollection(CCSPlayerController p)
    {
        var pet = Data(p)!;
        var (have, total) = PetLogic.CollectionTotal(pet);
        var menu = new CoopMenu(M(p, "menu.collection.title"), M(p, "menu.collection.sub", have, total));
        foreach (var b in Enum.GetValues<Breed>())
        {
            var breed = b;
            var (h, t) = PetLogic.CollectionProgress(pet, b);
            var label = $"{Catalog.BreedName(b)} {MenuService.Bar((double)h / t, h == t ? Gold : Green, 6)} {h}/{t}";
            menu.Add(Arrow(h == t ? C(label + " ★", Gold) : label), pl => Sub(pl, x => BuildCollectionBreed(x, breed), BuildCollection));
        }

        var shinies = Catalog.ShinyVariants.Count(v => PetLogic.Collected(pet, Catalog.ShinyEntry(v.Id)));
        menu.Add(Arrow(M(p, "menu.collection.shinies", shinies, Catalog.ShinyVariants.Count)), pl => Sub(pl, BuildCollectionShinies, BuildCollection));
        menu.Info(M(p, "menu.collection.more", PetLogic.UnlockedHats(pet, Config).Count, Catalog.Hats.Count,
            Catalog.Emotes.Count(e => PetLogic.EmoteUnlocked(pet, e, Config)), Catalog.Emotes.Count,
            pet.UnlockedStyles.Count, Catalog.Styles.Count));
        return menu;
    }

    private CoopMenu BuildCollectionBreed(CCSPlayerController p, Breed breed)
    {
        var pet = Data(p)!;
        var (h, t) = PetLogic.CollectionProgress(pet, breed);
        var menu = new CoopMenu(Catalog.BreedName(breed), M(p, "menu.collection.breed.sub", h, t));
        var names = Catalog.CoatNames(breed);
        for (var i = 0; i < names.Count; i++)
            menu.Info(PetLogic.Collected(pet, Catalog.CoatEntry(breed, i)) ? C("✔ " + MenuService.Esc(names[i]), Green) : C("? ? ?", Grey));
        return menu;
    }

    private CoopMenu BuildCollectionShinies(CCSPlayerController p)
    {
        var pet = Data(p)!;
        var menu = new CoopMenu(M(p, "menu.collection.shinies.title"), M(p, "menu.collection.shinies.sub"));
        foreach (var v in Catalog.ShinyVariants)
            menu.Info(PetLogic.Collected(pet, Catalog.ShinyEntry(v.Id)) ? C("✦ " + v.Name, Gold) : C("? ? ?", Grey));
        return menu;
    }

    // ================================================================ egg basket

    private CoopMenu BuildBasket(CCSPlayerController p)
    {
        var pet = Data(p)!;
        var menu = new CoopMenu(M(p, "menu.basket.title"), M(p, "menu.basket.sub", pet.BasketEggs, Config.Eggs.MaxBasket));
        if (pet.Stage == PetStage.Hen && Config.Eggs.Laying && PetLogic.StageListed(Config.Eggs.LayingStages, pet.Stage))
        {
            var frac = pet.LaySeconds / Math.Max(1, Config.Eggs.MinutesPerEgg * 60);
            menu.Info($"{M(p, "menu.basket.next")} {MenuService.Bar(frac, Gold, 10)} {PetLogic.Duration(Math.Max(0, Config.Eggs.MinutesPerEgg * 60 - pet.LaySeconds))}");
        }
        else if (Config.Eggs.Laying)
        {
            menu.Info(C(M(p, "menu.basket.hens"), Grey));
        }

        if (Config.Eggs.Gifting) menu.Add(Arrow(M(p, "menu.basket.gift")), pl => Sub(pl, BuildGift, BuildBasket), pet.BasketEggs > 0);
        if (Config.Eggs.BagsPerEgg > 0)
        {
            menu.Add(M(p, "menu.basket.trade1", Config.Eggs.BagsPerEgg), pl => DoTrade(pl, 1), pet.BasketEggs > 0, keepOpen: true);
            menu.Add(M(p, "menu.basket.tradeall"), pl => DoTrade(pl, int.MaxValue), pet.BasketEggs > 1, keepOpen: true);
        }

        return menu;
    }

    private CoopMenu BuildGift(CCSPlayerController p)
    {
        var menu = new CoopMenu(M(p, "menu.gift.title"), M(p, "menu.gift.sub"));
        var any = false;
        foreach (var t in Players().Where(t => !t.IsBot && t.Slot != p.Slot && MayOwn(t)).OrderBy(t => t.PlayerName))
        {
            if (Data(t) is not { HasPet: false }) continue;
            any = true;
            var target = t;
            menu.Add(MenuService.Esc(t.PlayerName), pl =>
            {
                if (target.IsValid) DoGift(pl, target);
            });
        }

        if (!any) menu.Info(C(M(p, "menu.gift.nobody"), Grey));
        return menu;
    }

    // ================================================================ info, journal, boards

    private CoopMenu BuildInfo(CCSPlayerController p)
    {
        var pet = Data(p)!;
        var menu = new CoopMenu(M(p, "menu.info.title"), pet.IsHatched ? MenuService.Esc(pet.Name) : M(p, "stage.egg"));
        var progress = PetLogic.Progress(pet, Config);
        var left = PetLogic.SecondsLeft(pet, Config);
        if (!pet.IsHatched)
        {
            menu.Info($"{M(p, "info.hatch")} {MenuService.Bar(progress, Gold)} {PetLogic.Duration(left)}");
            menu.Info(C(M(p, "info.mystery"), Grey));
        }
        else
        {
            menu.Info($"{M(p, "info.stage")} {C(M(p, "stage." + pet.Stage.ToString().ToLowerInvariant()), Gold)} · {MenuService.Esc(PetLogic.Description(pet))}");
            if (pet.Stage < PetStage.Hen)
                menu.Info($"{M(p, "info.next", M(p, "stage." + (pet.Stage + 1).ToString().ToLowerInvariant()))} {MenuService.Bar(progress)} {PetLogic.Duration(left)}");
            else if (Config.Growth.HenRetireMinutes > 0)
                menu.Info($"{M(p, "info.retire")} {PetLogic.Duration(left)}");
            if (Config.Food.Enabled)
                menu.Info($"{M(p, "info.food")} {MenuService.Bar(pet.FoodSeconds / PetLogic.MaxFoodSeconds(Config), Blue)} {PetLogic.Duration(pet.FoodSeconds)}");
            if (Config.Moods.Enabled)
                menu.Info($"{M(p, "info.mood")} {C(PetLogic.Hearts(pet), MoodColor(pet))} {MoodText(p, pet)} {C($"({(int)pet.Happiness}%)", Grey)}");
            menu.Info(M(p, "info.bags", pet.FeedBags, Config.Food.RoundsPerBag > 0 ? Config.Food.RoundsPerBag - pet.RoundsTowardFeedBag : 0));
            menu.Info(M(p, "info.hats", PetLogic.UnlockedHats(pet, Config).Count, Catalog.Hats.Count,
                Catalog.Emotes.Count(e => PetLogic.EmoteUnlocked(pet, e, Config)), Catalog.Emotes.Count));
        }

        menu.Add(M(p, "menu.info.chat"), pl => ShowStatus(pl));
        return menu;
    }

    private CoopMenu BuildJournal(CCSPlayerController p)
    {
        var pet = Data(p)!;
        var menu = new CoopMenu(M(p, "menu.journal.title", MenuService.Esc(p.PlayerName)));
        if (pet.IsHatched)
        {
            var s = pet.Stats;
            menu.Subtitle = $"{C(MenuService.Esc(pet.Name), Green)} · {MenuService.Esc(PetLogic.Description(pet))}";
            menu.Info(M(p, "journal.m.age", pet.HatchedUtc?.ToString("yyyy-MM-dd") ?? "-", PetLogic.Duration(pet.LifeSeconds)));
            menu.Info(M(p, "journal.m.care", s.RoundsTogether, s.Feeds, s.Pets));
            menu.Info(M(p, "journal.m.fun", s.Tricks, s.Emotes, s.Perches));
            menu.Info(M(p, "journal.m.eggs", s.EggsLaid, s.EggsGifted, s.EggsTraded));
            menu.Info(M(p, "journal.m.danger", s.Deaths, s.DeathsBurn, s.DeathsTaser, s.DeathsBomb));
            menu.Info(M(p, "journal.m.glory", s.Revenges, s.OwnerKillsWithPet));
            menu.Info(M(p, "journal.m.arena", s.ArenaWins, s.ArenaLosses, s.ArenaDraws));
        }
        else
        {
            menu.Info(C(M(p, pet.HasPet ? "journal.m.egg" : "journal.m.none"), Grey));
        }

        if (pet.PastPets.Count > 0) menu.Add(Arrow(M(p, "menu.journal.past", pet.PastPets.Count)), pl => Sub(pl, BuildPastPets, BuildJournal));
        menu.Add(M(p, "menu.info.chat"), pl => ShowJournal(pl));
        return menu;
    }

    private CoopMenu BuildPastPets(CCSPlayerController p)
    {
        var pet = Data(p)!;
        var menu = new CoopMenu(M(p, "menu.past.title"));
        foreach (var old in pet.PastPets.AsEnumerable().Reverse())
        {
            var shiny = Catalog.FindShiny(old.Shiny) is { } sv ? sv.Name + " " : "";
            menu.Info($"{C(MenuService.Esc(old.Name), Green)} · {MenuService.Esc(shiny + Catalog.BreedName(old.Breed))} · {M(p, "stage." + old.Stage.ToString().ToLowerInvariant())} · {M(p, "past.rounds", old.RoundsTogether)}");
        }

        return menu;
    }

    private CoopMenu BuildBoards(CCSPlayerController p)
    {
        var menu = new CoopMenu(M(p, "menu.boards.title"), M(p, "menu.boards.sub"));
        foreach (var b in Enum.GetValues<Board>())
        {
            var board = b;
            menu.Add(Arrow(M(p, "board." + b.ToString().ToLowerInvariant())), pl => Sub(pl, x => BuildBoard(x, board), BuildBoards));
        }

        return menu;
    }

    private CoopMenu BuildBoard(CCSPlayerController p, Board board)
    {
        var menu = new CoopMenu(M(p, "board." + board.ToString().ToLowerInvariant()));
        var top = PetLogic.Leaderboard(_store.All, board, 10);
        if (top.Count == 0) menu.Info(C(M(p, "board.empty"), Grey));
        var rank = 0;
        foreach (var pet in top)
        {
            rank++;
            var value = board == Board.Age ? PetLogic.Duration(pet.LifeSeconds) : ((int)PetLogic.BoardValue(pet, board)).ToString();
            var color = rank == 1 ? Gold : pet.SteamId == p.SteamID ? Green : "#FFFFFF";
            menu.Info(C($"{rank}. {MenuService.Esc(pet.Name)} ({MenuService.Esc(pet.OwnerName)}) · {value}", color));
        }

        return menu;
    }

    // ================================================================ settings and help

    private CoopMenu BuildSettings(CCSPlayerController p)
    {
        var pet = Data(p)!;
        string OnOff(bool on) => on ? C(M(p, "menu.on"), Green) : C(M(p, "menu.off"), Red);
        var menu = new CoopMenu(M(p, "menu.settings.title"), M(p, "menu.settings.sub"));
        menu.Add($"{M(p, "settings.others")}: {OnOff(pet.ShowOtherPets)}", pl => Toggle(pl, d => d.ShowOtherPets = !d.ShowOtherPets), keepOpen: true);
        menu.Add($"{M(p, "settings.tags")}: {OnOff(pet.ShowNameTags)}", pl => Toggle(pl, d => d.ShowNameTags = !d.ShowNameTags), keepOpen: true);
        if (pet.HasPet) menu.Add($"{M(p, "settings.bring")}: {OnOff(!pet.Hidden)}", pl => DoHide(pl, !pet.Hidden), keepOpen: true);
        return menu;
    }

    private void Toggle(CCSPlayerController p, Action<PetData> change)
    {
        var pet = Data(p);
        if (pet == null) return;
        change(pet);
        _store.MarkDirty();
        SyncViewSettings(p);
    }

    private CoopMenu BuildHelp(CCSPlayerController p)
    {
        var menu = new CoopMenu(M(p, "menu.help.title"), M(p, "menu.help.sub"));
        for (var i = 1; i <= 11; i++) menu.Info(M(p, "help.m" + i));
        return menu;
    }

    // ================================================================ admin

    private CoopMenu BuildAdmin(CCSPlayerController p)
    {
        var menu = new CoopMenu(C(M(p, "menu.admin"), Red), M(p, "admin.m.sub", ActivePets, MaxPets, _store.Count));
        menu.Add(Arrow(M(p, "admin.m.players")), pl => Sub(pl, BuildAdminPlayers, BuildAdmin));
        menu.Add($"{M(p, "admin.m.enabled")}: {(Config.Enabled ? C(M(p, "menu.on"), Green) : C(M(p, "menu.off"), Red))}", pl => TogglePlugin(pl), keepOpen: true);
        menu.Add($"{M(p, "admin.m.hatmode")}: {C(Config.Looks.HatMode, Gold)}", pl => CycleHatMode(pl), keepOpen: true);
        menu.Add($"{M(p, "admin.m.suppress")}: {(_suppressAll ? C(M(p, "menu.on"), Red) : C(M(p, "menu.off"), Green))}", _ => SuppressEveryone(!_suppressAll), keepOpen: true);
        menu.Add(M(p, "admin.m.save"), pl =>
        {
            SaveSafe(true);
            Tell(pl, "admin.saved", _store.Count);
        }, keepOpen: true);
        menu.Add(M(p, "admin.m.log"), pl => ShowLog(pl, 8), keepOpen: true);
        if (_store.Backend != null) menu.Info(C(M(p, "admin.m.storage", MenuService.Esc(_store.Backend.Name)), Grey));
        if (_needsMapChange) menu.Info(C(M(p, "admin.m.mapchange"), Red));
        return menu;
    }

    private CoopMenu BuildAdminPlayers(CCSPlayerController p)
    {
        var menu = new CoopMenu(M(p, "admin.m.players"));
        foreach (var t in Players().Where(t => !t.IsBot || Config.BotsGetPets).OrderBy(t => t.PlayerName))
        {
            var target = t;
            var pet = Data(t);
            var what = pet == null ? "-" : pet.IsHatched ? $"{MenuService.Esc(pet.Name)}, {M(p, "stage." + pet.Stage.ToString().ToLowerInvariant())}" : M(p, pet.HasPet ? "stage.egg" : "stage.none");
            menu.Add($"{MenuService.Esc(t.PlayerName)} {C("· " + what, Grey)}", pl => Sub(pl, x => BuildAdminPlayer(x, target), BuildAdminPlayers));
        }

        return menu;
    }

    private CoopMenu BuildAdminPlayer(CCSPlayerController p, CCSPlayerController target)
    {
        var menu = new CoopMenu(MenuService.Esc(target.PlayerName));
        if (!target.IsValid)
        {
            menu.Info(C(M(p, "admin.m.gone"), Grey));
            return menu;
        }

        var pet = Data(target);
        if (pet != null) menu.Subtitle = MenuService.Esc($"{PetLogic.StageName(pet.Stage)} · {PetLogic.Description(pet)} · bags {pet.FeedBags} · eggs {pet.BasketEggs}");
        foreach (var stage in new[] { PetStage.Egg, PetStage.Chick, PetStage.Pullet, PetStage.Hen })
        {
            var s = stage;
            menu.Add(M(p, "admin.m.give", M(p, "stage." + s.ToString().ToLowerInvariant())), pl => AdminGive(pl, target, s), keepOpen: true);
        }

        menu.Add(M(p, "admin.m.inspect"), pl => Inspect(pl, Data(target), target), keepOpen: true);
        menu.Add(M(p, "admin.m.bags"), pl => AdminBags(pl, target, 5), keepOpen: true);
        if (pet is { IsHatched: true } && Config.Moods.Enabled) menu.Add(M(p, "admin.m.cheer"), pl => AdminMood(pl, target, 100), keepOpen: true);
        if (pet is { PastPets.Count: > 0 })
            menu.Add(Arrow(M(p, "admin.m.restore", pet.PastPets.Count)), pl => Sub(pl, x => BuildAdminRestore(x, target), y => BuildAdminPlayer(y, target)));
        if (_store.Backups().Count > 0)
            menu.Add(Arrow(M(p, "admin.m.backup")), pl => Sub(pl, x => BuildAdminBackups(x, target), y => BuildAdminPlayer(y, target)));
        menu.Add(M(p, "admin.m.hats"), pl => AdminHats(pl, target, "all"), keepOpen: true);
        menu.Add(Arrow(M(p, "admin.m.breed")), pl => Sub(pl, x => BuildAdminBreed(x, target), y => BuildAdminPlayer(y, target)));
        menu.Add(Arrow(M(p, "admin.m.shiny")), pl => Sub(pl, x => BuildAdminShiny(x, target), y => BuildAdminPlayer(y, target)));
        menu.Add(C(M(p, "admin.m.reset"), Red), pl => AdminReset(pl, target), keepOpen: true);
        return menu;
    }

    private CoopMenu BuildAdminRestore(CCSPlayerController p, CCSPlayerController target)
    {
        var menu = new CoopMenu(M(p, "admin.m.restore.title"), MenuService.Esc(target.PlayerName));
        if (!target.IsValid || Data(target) is not { } pet) return menu;
        var n = 0;
        foreach (var old in pet.PastPets.AsEnumerable().Reverse())
        {
            var index = ++n;
            menu.Add($"{MenuService.Esc(old.Name)} {C($"· {Catalog.BreedName(old.Breed)} · {M(p, "stage." + old.Stage.ToString().ToLowerInvariant())} · {MenuService.Esc(old.Reason)}", Grey)}",
                pl => AdminRestore(pl, pet, target, index));
        }

        return menu;
    }

    private CoopMenu BuildAdminBackups(CCSPlayerController p, CCSPlayerController target)
    {
        var menu = new CoopMenu(M(p, "admin.m.backup"), M(p, "admin.m.backup.sub", MenuService.Esc(target.PlayerName)));
        if (!target.IsValid) return menu;
        var id = target.SteamID;
        foreach (var b in _store.Backups())
        {
            var which = b;
            var old = _store.FromBackup(id, b);
            var what = old == null ? M(p, "admin.m.backup.missing") : old.IsHatched ? $"{MenuService.Esc(old.Name)}, {M(p, "stage." + old.Stage.ToString().ToLowerInvariant())}" : M(p, old.HasPet ? "stage.egg" : "stage.none");
            menu.Add($"{(b == "bak" ? M(p, "admin.m.backup.last") : b)} {C("· " + what, Grey)}", pl => AdminBackup(pl, id, which), old != null);
        }

        return menu;
    }

    private CoopMenu BuildAdminBreed(CCSPlayerController p, CCSPlayerController target)
    {
        var menu = new CoopMenu(M(p, "admin.m.breed"), MenuService.Esc(target.PlayerName));
        foreach (var b in Enum.GetValues<Breed>())
        {
            var breed = b;
            menu.Add(Arrow(b.ToString()), pl => Sub(pl, x => BuildAdminCoats(x, target, breed), y => BuildAdminBreed(y, target)));
        }

        return menu;
    }

    private CoopMenu BuildAdminCoats(CCSPlayerController p, CCSPlayerController target, Breed breed)
    {
        var menu = new CoopMenu(breed.ToString(), MenuService.Esc(target.PlayerName));
        var names = Catalog.CoatNames(breed);
        for (var i = 0; i < names.Count; i++)
        {
            var coat = i;
            menu.Add(MenuService.Esc(names[i]), pl => AdminBreed(pl, target, breed, coat), keepOpen: true);
        }

        return menu;
    }

    private CoopMenu BuildAdminShiny(CCSPlayerController p, CCSPlayerController target)
    {
        var menu = new CoopMenu(M(p, "admin.m.shiny"), MenuService.Esc(target.PlayerName));
        menu.Add(M(p, "admin.m.shiny.off"), pl => AdminShiny(pl, target, null), keepOpen: true);
        foreach (var v in Catalog.ShinyVariants)
        {
            var id = v.Id;
            menu.Add(C(v.Name, Gold), pl => AdminShiny(pl, target, id), keepOpen: true);
        }

        return menu;
    }

    private void TogglePlugin(CCSPlayerController? caller)
    {
        Config.Enabled = !Config.Enabled;
        if (!Config.Enabled)
            foreach (var a in _actors.Values) Despawn(a, false);
        else
            foreach (var pl in Players()) TrySpawnPet(pl);
        Reply(caller, Config.Enabled ? "admin.toggle.on" : "admin.toggle.off");
    }

    private static readonly string[] HatModes = { "bonemerge", "attachment", "offset" };

    /// <summary>Switches how hats are attached and re-dresses every pet, to find what looks right on this server.</summary>
    private void CycleHatMode(CCSPlayerController? caller, string? mode = null)
    {
        var i = Array.IndexOf(HatModes, Config.Looks.HatMode);
        Config.Looks.HatMode = mode ?? HatModes[(i + 1) % HatModes.Length];
        foreach (var a in _actors.Values)
        {
            Remove(ref a.Hat);
            Remove(ref a.PerchHat);
            a.ShownHat = "?";
            if (Utilities.GetPlayerFromSlot(a.Slot) is { IsValid: true } owner && Data(owner) is { } pet)
            {
                if (a.PerchOut) RefreshPerchLooks(a, pet);
                else RefreshLooks(a, pet, owner);
            }
        }

        Reply(caller, "admin.hatmode", Config.Looks.HatMode);
    }
}
