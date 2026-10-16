using ChickenCoop.Logic;
using ChickenCoop.Menus;
using ChickenCoop.Model;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace ChickenCoop;

public sealed partial class ChickenCoopPlugin
{
    // ================================================================ player commands

    [ConsoleCommand("css_pet", "Your pet chicken. !pet opens the menu; !pet help lists the shortcuts")]
    [ConsoleCommand("css_chicken", "Alias of !pet")]
    [ConsoleCommand("css_coop", "Alias of !pet")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnPetCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true }) return;
        if (!Config.Enabled)
        {
            Tell(player, "plugin.off");
            return;
        }

        if (!MayOwn(player))
        {
            Tell(player, "pet.no_permission");
            return;
        }

        if (Data(player) == null)
        {
            Tell(player, "pet.no_steam");
            return;
        }

        SyncViewSettings(player);
        var sub = info.ArgCount > 1 ? info.GetArg(1).ToLowerInvariant() : "";
        var rest = info.ArgCount > 2 ? string.Join(' ', Enumerable.Range(2, info.ArgCount - 2).Select(info.GetArg)) : "";
        switch (sub)
        {
            case "":
            case "menu":
                OpenMain(player);
                break;
            case "info":
            case "status":
            case "stats":
                ShowStatus(player);
                break;
            case "feed":
            case "eat":
                DoFeed(player);
                break;
            case "trick":
            case "jump":
            case "flap":
                DoEmote(player, Catalog.FindEmote("flap")!);
                break;
            case "emote":
            case "emotes":
                if (rest.Length == 0) Sub(player, BuildEmotes, BuildMain);
                else if (Catalog.FindEmote(rest) is { } emote) DoEmote(player, emote);
                else Tell(player, "emote.unknown", rest);
                break;
            case "sit":
            case "stay":
                DoStay(player, true);
                break;
            case "come":
            case "follow":
            case "heel":
                DoStay(player, false);
                break;
            case "perch":
            case "shoulder":
                DoPerch(player, !Actor(player).Perched);
                break;
            case "name":
            case "rename":
                if (rest.Length == 0) AskRename(player);
                else DoRename(player, rest);
                break;
            case "hat":
            case "hats":
                if (rest.Length == 0) Sub(player, BuildHats, BuildMain);
                else DoHat(player, rest);
                break;
            case "look":
            case "looks":
            case "style":
                Sub(player, BuildLooks, BuildMain);
                break;
            case "eggs":
            case "basket":
                Sub(player, BuildBasket, BuildMain);
                break;
            case "gift":
                if (rest.Length == 0) Sub(player, BuildGift, BuildMain);
                else if (Target(player, rest) is { } target) DoGift(player, target);
                else Tell(player, "admin.no_target", rest);
                break;
            case "trade":
                DoTrade(player, int.TryParse(rest, out var n) ? Math.Max(1, n) : 1);
                break;
            case "hide":
                DoHide(player, true);
                break;
            case "show":
                DoHide(player, false);
                break;
            case "journal":
            case "book":
            case "history":
                Sub(player, BuildJournal, BuildMain);
                break;
            case "top":
            case "leaderboard":
            case "leaderboards":
                Sub(player, BuildBoards, BuildMain);
                break;
            case "settings":
            case "options":
                Sub(player, BuildSettings, BuildMain);
                break;
            default:
                ShowHelp(player);
                break;
        }
    }

    [ConsoleCommand("css_pets", "List the pets on the server")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnPetsCommand(CCSPlayerController? player, CommandInfo info)
    {
        var any = false;
        Reply(player, "list.header");
        foreach (var p in Players().Where(p => !p.IsBot || Config.BotsGetPets))
        {
            var pet = Data(p);
            if (pet is not { HasPet: true }) continue;
            any = true;
            var what = pet.IsHatched
                ? _lang.P(player, "list.line.pet", pet.Name, _lang.P(player, "stage." + pet.Stage.ToString().ToLowerInvariant()), PetLogic.Description(pet))
                : _lang.P(player, "list.line.egg", (int)(PetLogic.Progress(pet, Config) * 100));
            Reply(player, "list.line", p.PlayerName, what);
        }

        if (!any) Reply(player, "list.empty");
    }

    private void ShowHelp(CCSPlayerController p)
    {
        for (var i = 1; i <= 7; i++) Tell(p, "help." + i);
    }

    private void ShowStatus(CCSPlayerController p)
    {
        var pet = Data(p);
        if (pet == null) return;
        if (!pet.HasPet)
        {
            Tell(p, "pet.none", PetLogic.Duration(PetLogic.SecondsLeft(pet, Config)));
            return;
        }

        var progress = PetLogic.Progress(pet, Config);
        var left = PetLogic.SecondsLeft(pet, Config);
        if (!pet.IsHatched)
        {
            Tell(p, "status.egg", PetLogic.Bar(progress), (int)(progress * 100), PetLogic.Duration(left));
            return;
        }

        Tell(p, "status.title", pet.Name, _lang.P(p, "stage." + pet.Stage.ToString().ToLowerInvariant()), PetLogic.Description(pet));
        if (pet.Stage == PetStage.Hen && Config.Growth.HenRetireMinutes <= 0) Tell(p, "status.grown");
        else if (pet.Stage == PetStage.Hen) Tell(p, "status.retire", PetLogic.Duration(left));
        else
            Tell(p, "status.growth", PetLogic.Bar(progress), (int)(progress * 100), PetLogic.Duration(left),
                _lang.P(p, "stage." + (pet.Stage + 1).ToString().ToLowerInvariant()),
                Config.Food.Enabled && pet.FoodSeconds <= 0 ? _lang.T(p, "status.paused") : "");
        if (Config.Food.Enabled)
            Tell(p, "status.food", PetLogic.Bar(pet.FoodSeconds / PetLogic.MaxFoodSeconds(Config)), PetLogic.Duration(pet.FoodSeconds), pet.FeedBags,
                Config.Food.RoundsPerBag > 0 ? Config.Food.RoundsPerBag - pet.RoundsTowardFeedBag : 0);
        Tell(p, "status.extras", PetLogic.UnlockedHats(pet, Config).Count, Catalog.Hats.Count, pet.BasketEggs,
            Catalog.Emotes.Count(e => PetLogic.EmoteUnlocked(pet, e, Config)), Catalog.Emotes.Count);
    }

    private void ShowJournal(CCSPlayerController p)
    {
        var pet = Data(p);
        if (pet == null) return;
        Tell(p, "journal.title", p.PlayerName);
        if (pet.IsHatched)
        {
            var s = pet.Stats;
            Tell(p, "journal.current", pet.Name, PetLogic.Description(pet), pet.HatchedUtc?.ToString("yyyy-MM-dd") ?? "-", PetLogic.Duration(pet.LifeSeconds));
            Tell(p, "journal.stats1", s.RoundsTogether, s.Feeds, s.Pets, s.Tricks + s.Emotes);
            Tell(p, "journal.stats2", s.Deaths, s.DeathsBurn, s.DeathsTaser, s.DeathsBomb, s.Revenges, s.OwnerKillsWithPet);
            Tell(p, "journal.stats3", s.EggsLaid, s.EggsGifted, s.EggsTraded);
        }
        else if (pet.HasPet)
        {
            Tell(p, "journal.egg", pet.CreatedUtc.ToString("yyyy-MM-dd"));
        }

        if (pet.PastPets.Count == 0) return;
        Tell(p, "journal.past");
        foreach (var old in pet.PastPets.TakeLast(5).Reverse())
            Tell(p, "journal.past.line", old.Name, Catalog.BreedName(old.Breed), Catalog.CoatOf(old.Breed, old.Coat).Name,
                _lang.P(p, "stage." + old.Stage.ToString().ToLowerInvariant()), old.RoundsTogether, old.Reason);
    }

    internal void DoRename(CCSPlayerController p, string name)
    {
        var pet = Data(p);
        if (pet == null) return;
        switch (PetLogic.Rename(pet, name, Config, out var reason))
        {
            case RenameResult.NoPet:
                Tell(p, "pet.none", PetLogic.Duration(PetLogic.SecondsLeft(pet, Config)));
                break;
            case RenameResult.IsEgg:
                Tell(p, "name.egg");
                break;
            case RenameResult.NoRenamesLeft:
                Tell(p, "name.used");
                break;
            case RenameResult.Invalid:
                Tell(p, "name.bad." + reason, Config.Naming.MaxLength);
                break;
            case RenameResult.Renamed:
                _store.MarkDirty();
                Tell(p, "name.done", pet.Name);
                if (_actors.TryGetValue(p.Slot, out var a)) RefreshLooks(a, pet, p);
                break;
        }
    }

    internal void DoHat(CCSPlayerController p, string which)
    {
        var pet = Data(p);
        if (pet == null) return;
        if (!pet.IsHatched)
        {
            Tell(p, "pet.egg_only");
            return;
        }

        if (which.Equals("off", StringComparison.OrdinalIgnoreCase) || which.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            pet.Hat = null;
            Tell(p, "hat.off", pet.Name);
        }
        else if (which.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var h in Catalog.Hats)
                Tell(p, PetLogic.HatUnlocked(pet, h, Config) ? "hat.list.open" : "hat.list.locked", h.Id, h.Name, h.HowToUnlock);
            return;
        }
        else
        {
            var hat = Catalog.FindHat(which);
            if (hat == null)
            {
                Tell(p, "hat.unknown", which);
                return;
            }

            if (!PetLogic.HatUnlocked(pet, hat, Config))
            {
                Tell(p, "hat.locked", hat.Name, hat.HowToUnlock);
                return;
            }

            pet.Hat = hat.Id;
            Tell(p, "hat.on", pet.Name, hat.Name);
        }

        _store.MarkDirty();
        if (!_actors.TryGetValue(p.Slot, out var a)) return;
        if (a.PerchOut) RefreshPerchLooks(a, pet);
        else RefreshLooks(a, pet, p);
    }

    // ================================================================ admin commands

    private CCSPlayerController? Target(CCSPlayerController? caller, string pattern)
    {
        pattern = pattern.Trim();
        if (pattern.Length == 0) return null;
        if (pattern.Equals("@me", StringComparison.OrdinalIgnoreCase)) return caller;
        var all = Players().ToList();
        if (pattern.StartsWith('#') && int.TryParse(pattern[1..], out var uid)) return all.FirstOrDefault(p => p.UserId == uid);
        if (ulong.TryParse(pattern, out var sid) && sid > 76561197960265728UL) return all.FirstOrDefault(p => p.SteamID == sid);
        var exact = all.Where(p => p.PlayerName.Equals(pattern, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1) return exact[0];
        var partial = all.Where(p => p.PlayerName.Contains(pattern, StringComparison.OrdinalIgnoreCase)).ToList();
        return partial.Count == 1 ? partial[0] : null;
    }

    private bool AdminGate(CCSPlayerController? caller, CommandInfo info, int minArgs, string usageKey, out CCSPlayerController? target)
    {
        target = null;
        if (!IsAdmin(caller))
        {
            Reply(caller, "admin.denied");
            return false;
        }

        if (info.ArgCount < minArgs)
        {
            Reply(caller, usageKey);
            return false;
        }

        target = Target(caller, info.GetArg(1));
        if (target == null || (target.IsBot && !Config.BotsGetPets))
        {
            Reply(caller, "admin.no_target", info.GetArg(1));
            return false;
        }

        return true;
    }

    [ConsoleCommand("css_pet_admin", "Open the Chicken Coop admin menu")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnAdminMenu(CCSPlayerController? caller, CommandInfo info)
    {
        if (caller == null || !IsAdmin(caller))
        {
            Reply(caller, "admin.denied");
            return;
        }

        Sub(caller, BuildAdmin, BuildMain);
    }

    [ConsoleCommand("css_pet_give", "<target> <egg|chick|pullet|hen> - give a pet at a stage")]
    public void OnAdminGive(CCSPlayerController? caller, CommandInfo info)
    {
        if (!AdminGate(caller, info, 3, "admin.give.usage", out var t)) return;
        if (!Enum.TryParse<PetStage>(info.GetArg(2), true, out var stage) || stage == PetStage.None)
        {
            Reply(caller, "admin.give.usage");
            return;
        }

        AdminGive(caller, t!, stage);
    }

    private void AdminGive(CCSPlayerController? caller, CCSPlayerController t, PetStage stage)
    {
        if (!t.IsValid || Data(t) is not { } pet) return;
        if (_actors.TryGetValue(t.Slot, out var a)) Despawn(a, false);
        PetLogic.SetStage(pet, stage, Config, DateTime.UtcNow, NewSeed(t));
        _store.MarkDirty();
        Reply(caller, "admin.done", t.PlayerName);
        Tell(t, "admin.given", _lang.P(t, "stage." + stage.ToString().ToLowerInvariant()));
        TrySpawnPet(t);
        _api.RaiseStage(t, stage);
    }

    [ConsoleCommand("css_pet_reset", "<target> - remove a player's pet (it is kept in their journal)")]
    public void OnAdminReset(CCSPlayerController? caller, CommandInfo info)
    {
        if (AdminGate(caller, info, 2, "admin.reset.usage", out var t)) AdminReset(caller, t!);
    }

    private void AdminReset(CCSPlayerController? caller, CCSPlayerController t)
    {
        if (!t.IsValid || Data(t) is not { } pet) return;
        if (_actors.TryGetValue(t.Slot, out var a)) Despawn(a, true);
        PetLogic.Retire(pet, "reset by an admin", DateTime.UtcNow);
        _store.MarkDirty();
        Reply(caller, "admin.done", t.PlayerName);
    }

    [ConsoleCommand("css_pet_bags", "<target> <amount> - give (or take) feed bags")]
    public void OnAdminBags(CCSPlayerController? caller, CommandInfo info)
    {
        if (!AdminGate(caller, info, 3, "admin.bags.usage", out var t)) return;
        if (!int.TryParse(info.GetArg(2), out var n))
        {
            Reply(caller, "admin.bags.usage");
            return;
        }

        AdminBags(caller, t!, n);
    }

    private void AdminBags(CCSPlayerController? caller, CCSPlayerController t, int n)
    {
        if (!t.IsValid || Data(t) is not { } pet) return;
        pet.FeedBags = Math.Clamp(pet.FeedBags + n, 0, 999);
        _store.MarkDirty();
        Reply(caller, "admin.done", t.PlayerName);
        Tell(t, "admin.bags.given", n, pet.FeedBags);
    }

    [ConsoleCommand("css_pet_eggs", "<target> <amount> - put eggs in a player's basket")]
    public void OnAdminEggs(CCSPlayerController? caller, CommandInfo info)
    {
        if (!AdminGate(caller, info, 3, "admin.eggs.usage", out var t)) return;
        if (!int.TryParse(info.GetArg(2), out var n) || n <= 0)
        {
            Reply(caller, "admin.eggs.usage");
            return;
        }

        _api.GiveBasketEggs(t!, n);
        Reply(caller, "admin.done", t!.PlayerName);
    }

    [ConsoleCommand("css_pet_givehat", "<target> <hat id|all> - unlock a hat for a player")]
    public void OnAdminHat(CCSPlayerController? caller, CommandInfo info)
    {
        if (AdminGate(caller, info, 3, "admin.hat.usage", out var t)) AdminHats(caller, t!, info.GetArg(2));
    }

    private void AdminHats(CCSPlayerController? caller, CCSPlayerController t, string which)
    {
        if (!t.IsValid || Data(t) is not { } pet) return;
        var hats = which.Equals("all", StringComparison.OrdinalIgnoreCase) ? Catalog.Hats.ToList()
            : Catalog.FindHat(which) is { } one ? new List<Hat> { one } : new();
        if (hats.Count == 0)
        {
            Reply(caller, "hat.unknown", which);
            return;
        }

        foreach (var h in hats.Where(h => !pet.GiftedHats.Contains(h.Id))) pet.GiftedHats.Add(h.Id);
        _store.MarkDirty();
        Reply(caller, "admin.done", t.PlayerName);
        if (hats.Count == 1) Tell(t, "hat.unlocked", hats[0].Name);
        else Tell(t, "admin.hats.all");
    }

    [ConsoleCommand("css_pet_breed", "<target> <catalana|polish|silkie> [coat number] - change breed and coat")]
    public void OnAdminBreed(CCSPlayerController? caller, CommandInfo info)
    {
        if (!AdminGate(caller, info, 3, "admin.breed.usage", out var t)) return;
        if (!Enum.TryParse<Breed>(info.GetArg(2), true, out var breed))
        {
            Reply(caller, "admin.breed.usage");
            return;
        }

        var count = Catalog.CoatNames(breed).Count;
        var coat = info.ArgCount > 3 && int.TryParse(info.GetArg(3), out var c) ? Math.Clamp(c - 1, 0, count - 1) : Random.Shared.Next(count);
        AdminBreed(caller, t!, breed, coat);
    }

    private void AdminBreed(CCSPlayerController? caller, CCSPlayerController t, Breed breed, int coat)
    {
        if (!t.IsValid || Data(t) is not { } pet) return;
        pet.Breed = breed;
        pet.Coat = coat;
        _store.MarkDirty();
        Respawn(t);
        Reply(caller, "admin.breed.done", t.PlayerName, PetLogic.Description(pet));
    }

    [ConsoleCommand("css_pet_shiny", "<target> <golden|rainbow|shadow|celestial|off> - make a pet shiny (or not)")]
    public void OnAdminShiny(CCSPlayerController? caller, CommandInfo info)
    {
        if (!AdminGate(caller, info, 3, "admin.shiny.usage", out var t)) return;
        var arg = info.GetArg(2);
        if (!arg.Equals("off", StringComparison.OrdinalIgnoreCase) && Catalog.FindShiny(arg) == null)
        {
            Reply(caller, "admin.shiny.usage");
            return;
        }

        AdminShiny(caller, t!, arg.Equals("off", StringComparison.OrdinalIgnoreCase) ? null : arg);
    }

    private void AdminShiny(CCSPlayerController? caller, CCSPlayerController t, string? variant)
    {
        if (!t.IsValid || Data(t) is not { } pet) return;
        pet.Shiny = Catalog.FindShiny(variant)?.Id;
        _store.MarkDirty();
        Respawn(t);
        Reply(caller, "admin.done", t.PlayerName);
    }

    /// <summary>Rebuilds a player's pet entities (after an admin change of breed, coat or shine).</summary>
    private void Respawn(CCSPlayerController t)
    {
        if (_actors.TryGetValue(t.Slot, out var a))
        {
            RemoveChicken(a);
            RemovePerch(a);
        }

        TrySpawnPet(t);
    }

    [ConsoleCommand("css_pet_hatmode", "[bonemerge|attachment|offset] - how hats are attached (cycles when left out)")]
    public void OnAdminHatMode(CCSPlayerController? caller, CommandInfo info)
    {
        if (!IsAdmin(caller))
        {
            Reply(caller, "admin.denied");
            return;
        }

        var mode = info.ArgCount > 1 ? info.GetArg(1).ToLowerInvariant() : null;
        if (mode != null && Array.IndexOf(HatModes, mode) < 0)
        {
            Reply(caller, "admin.hatmode.usage");
            return;
        }

        CycleHatMode(caller, mode);
    }

    [ConsoleCommand("css_pet_skin", "<index|group> - (testing) show a skin on your own pet, to map coats")]
    [CommandHelper(minArgs: 1, usage: "<index|group name>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnAdminSkin(CCSPlayerController? caller, CommandInfo info)
    {
        if (caller == null || !IsAdmin(caller))
        {
            Reply(caller, "admin.denied");
            return;
        }

        if (!_actors.TryGetValue(caller.Slot, out var a) || a.Chicken is not { IsValid: true } c)
        {
            Tell(caller, "pet.not_out", Data(caller)?.Name);
            return;
        }

        var value = info.GetArg(1);
        c.AcceptInput(int.TryParse(value, out _) ? "Skin" : "SetMaterialGroup", null, null, value);
        Tell(caller, "admin.skin", value);
    }

    [ConsoleCommand("css_pet_toggle", "Turn pets on or off for the whole server (until the map changes or the config reloads)")]
    public void OnAdminToggle(CCSPlayerController? caller, CommandInfo info)
    {
        if (!IsAdmin(caller))
        {
            Reply(caller, "admin.denied");
            return;
        }

        TogglePlugin(caller);
    }

    [ConsoleCommand("css_pet_save", "Write pets.json now")]
    public void OnAdminSave(CCSPlayerController? caller, CommandInfo info)
    {
        if (!IsAdmin(caller))
        {
            Reply(caller, "admin.denied");
            return;
        }

        SaveSafe(true);
        Reply(caller, "admin.saved", _store.Count);
    }
}
