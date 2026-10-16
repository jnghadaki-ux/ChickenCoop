using ChickenCoop.Logic;
using ChickenCoop.Model;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;

namespace ChickenCoop;

public sealed partial class ChickenCoopPlugin
{
    // ================================================================ player commands

    [ConsoleCommand("css_pet", "Your pet chicken: !pet [feed|trick|sit|come|name|hat|hide|show|info|journal|help]")]
    [ConsoleCommand("css_chicken", "Alias of !pet")]
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

        var sub = info.ArgCount > 1 ? info.GetArg(1).ToLowerInvariant() : "";
        var rest = info.ArgCount > 2 ? string.Join(' ', Enumerable.Range(2, info.ArgCount - 2).Select(info.GetArg)) : "";
        switch (sub)
        {
            case "":
            case "menu":
                OpenMainMenu(player);
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
                DoTrick(player);
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
            case "name":
            case "rename":
                DoRename(player, rest);
                break;
            case "hat":
            case "hats":
                if (rest.Length == 0) OpenHatMenu(player);
                else DoHat(player, rest);
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
                ShowJournal(player);
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
            if (pet == null || !pet.HasPet) continue;
            any = true;
            var what = pet.IsHatched
                ? _lang.Plain("list.line.pet", pet.Name, PetLogic.StageName(pet.Stage), pet.ShowsBreed ? PetLogic.Description(pet) : "?")
                : _lang.Plain("list.line.egg", (int)(PetLogic.Progress(pet, Config) * 100));
            Reply(player, "list.line", p.PlayerName, what);
        }

        if (!any) Reply(player, "list.empty");
    }

    private void ShowHelp(CCSPlayerController p)
    {
        foreach (var key in new[] { "help.1", "help.2", "help.3", "help.4", "help.5", "help.6" }) Tell(p, key);
    }

    private void ShowStatus(CCSPlayerController p)
    {
        var pet = Data(p);
        if (pet == null) return;
        if (!pet.HasPet)
        {
            Tell(p, "pet.none", PetLogic.Duration(Math.Max(0, PetLogic.StageSeconds(PetStage.None, Config) - pet.SecondsTowardEgg)));
            return;
        }

        var progress = PetLogic.Progress(pet, Config);
        var left = Math.Max(0, PetLogic.StageSeconds(pet.Stage, Config) - pet.StageSeconds);
        if (!pet.IsHatched)
        {
            Tell(p, "status.egg", PetLogic.Bar(progress), (int)(progress * 100), PetLogic.Duration(left));
            return;
        }

        Tell(p, "status.title", pet.Name, PetLogic.StageName(pet.Stage), PetLogic.Description(pet));
        if (pet.Stage == PetStage.Hen && Config.Growth.HenRetireMinutes <= 0) Tell(p, "status.grown");
        else if (pet.Stage == PetStage.Hen) Tell(p, "status.retire", PetLogic.Duration(left));
        else Tell(p, "status.growth", PetLogic.Bar(progress), (int)(progress * 100), PetLogic.Duration(left),
            PetLogic.StageName(pet.Stage + 1), Config.Food.Enabled && pet.FoodSeconds <= 0 ? _lang.T("status.paused") : "");
        if (Config.Food.Enabled)
        {
            var max = Math.Max(Config.Food.MaxFoodMinutes, Config.Food.MinutesPerBag) * 60;
            Tell(p, "status.food", PetLogic.Bar(pet.FoodSeconds / max), PetLogic.Duration(pet.FoodSeconds), pet.FeedBags,
                Config.Food.RoundsPerBag > 0 ? Config.Food.RoundsPerBag - pet.RoundsTowardFeedBag : 0);
        }

        var hats = PetLogic.UnlockedHats(pet, Config);
        Tell(p, "status.hats", hats.Count, Catalog.Hats.Count, pet.Hat != null ? Catalog.FindHat(pet.Hat)?.Name ?? "-" : "-");
    }

    private void ShowJournal(CCSPlayerController p)
    {
        var pet = Data(p);
        if (pet == null) return;
        Tell(p, "journal.title", p.PlayerName);
        if (pet.IsHatched)
        {
            var s = pet.Stats;
            Tell(p, "journal.current", pet.Name, PetLogic.Description(pet), pet.HatchedUtc?.ToString("yyyy-MM-dd") ?? "-",
                PetLogic.Duration(pet.LifeSeconds));
            Tell(p, "journal.stats1", s.RoundsTogether, s.Feeds, s.Pets, s.Tricks);
            Tell(p, "journal.stats2", s.Deaths, s.DeathsBurn, s.DeathsTaser, s.DeathsBomb, s.Revenges, s.OwnerKillsWithPet);
        }
        else if (pet.HasPet)
        {
            Tell(p, "journal.egg", pet.CreatedUtc.ToString("yyyy-MM-dd"));
        }

        if (pet.PastPets.Count == 0) return;
        Tell(p, "journal.past");
        foreach (var old in pet.PastPets.TakeLast(5).Reverse())
            Tell(p, "journal.past.line", old.Name, Catalog.BreedName(old.Breed), Catalog.CoatOf(old.Breed, old.Coat).Name,
                PetLogic.StageName(old.Stage), old.RoundsTogether, old.Reason);
    }

    private void DoRename(CCSPlayerController p, string name)
    {
        var pet = Data(p);
        if (pet == null) return;
        if (name.Trim().Length == 0)
        {
            Tell(p, "name.usage", Config.Naming.MaxLength);
            return;
        }

        switch (PetLogic.Rename(pet, name, Config, out var reason))
        {
            case RenameResult.NoPet:
                Tell(p, "pet.none", PetLogic.Duration(Math.Max(0, PetLogic.StageSeconds(PetStage.None, Config) - pet.SecondsTowardEgg)));
                break;
            case RenameResult.IsEgg:
                Tell(p, "name.egg");
                break;
            case RenameResult.NoRenamesLeft:
                Tell(p, "name.used", PetLogic.StageName(pet.Stage + (pet.Stage < PetStage.Hen ? 1 : 0)));
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

    private void DoHat(CCSPlayerController p, string which)
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
        if (_actors.TryGetValue(p.Slot, out var a)) RefreshLooks(a, pet, p);
    }

    private void DoHide(CCSPlayerController p, bool hide)
    {
        var pet = Data(p);
        if (pet == null) return;
        pet.Hidden = hide;
        _store.MarkDirty();
        if (hide)
        {
            if (_actors.TryGetValue(p.Slot, out var a)) Despawn(a);
            Tell(p, "hide.on");
        }
        else
        {
            Tell(p, "hide.off");
            TrySpawnPet(p);
        }
    }

    // ================================================================ menus

    private void OpenMenu(CCSPlayerController p, BaseMenu menu)
    {
        if (menu is CenterHtmlMenu html) MenuManager.OpenCenterHtmlMenu(this, p, html);
        else MenuManager.OpenChatMenu(p, (ChatMenu)menu);
    }

    private BaseMenu NewMenu(string title) => Config.UseCenterMenu ? new CenterHtmlMenu(title, this) : new ChatMenu(title);

    private void OpenMainMenu(CCSPlayerController p)
    {
        var pet = Data(p);
        if (pet == null) return;
        var title = !pet.HasPet ? _lang.Plain("menu.title.none")
            : !pet.IsHatched ? _lang.Plain("menu.title.egg", (int)(PetLogic.Progress(pet, Config) * 100))
            : _lang.Plain("menu.title", pet.Name, PetLogic.StageName(pet.Stage));
        var menu = NewMenu(title);
        menu.AddMenuOption(_lang.Plain("menu.status"), (pl, _) => ShowStatus(pl));
        var hatched = pet.IsHatched;
        menu.AddMenuOption(_lang.Plain("menu.feed", pet.FeedBags), (pl, _) => DoFeed(pl), !hatched);
        menu.AddMenuOption(_lang.Plain("menu.trick"), (pl, _) => DoTrick(pl), !hatched);
        var staying = _actors.TryGetValue(p.Slot, out var a) && a.Staying;
        menu.AddMenuOption(_lang.Plain(staying ? "menu.come" : "menu.sit"), (pl, _) => DoStay(pl, !staying), !hatched);
        menu.AddMenuOption(_lang.Plain("menu.hats"), (pl, _) => OpenHatMenu(pl), !hatched);
        menu.AddMenuOption(_lang.Plain("menu.rename"), (pl, _) => Tell(pl, "name.usage", Config.Naming.MaxLength), !hatched);
        menu.AddMenuOption(_lang.Plain("menu.journal"), (pl, _) => ShowJournal(pl));
        menu.AddMenuOption(_lang.Plain(pet.Hidden ? "menu.show" : "menu.hide"), (pl, _) => DoHide(pl, !pet.Hidden), !pet.HasPet);
        menu.PostSelectAction = PostSelectAction.Close;
        OpenMenu(p, menu);
    }

    private void OpenHatMenu(CCSPlayerController p)
    {
        var pet = Data(p);
        if (pet == null) return;
        if (!pet.IsHatched)
        {
            Tell(p, "pet.egg_only");
            return;
        }

        var menu = NewMenu(_lang.Plain("menu.hats.title", pet.Name));
        menu.AddMenuOption(_lang.Plain("menu.hats.none"), (pl, _) => DoHat(pl, "off"), pet.Hat == null);
        foreach (var h in Catalog.Hats)
        {
            var open = PetLogic.HatUnlocked(pet, h, Config);
            var label = open ? (pet.Hat == h.Id ? _lang.Plain("menu.hats.worn", h.Name) : h.Name) : _lang.Plain("menu.hats.locked", h.Name);
            var id = h.Id;
            menu.AddMenuOption(label, (pl, _) =>
            {
                if (open) DoHat(pl, id);
                else Tell(pl, "hat.locked", h.Name, h.HowToUnlock);
            }, pet.Hat == h.Id);
        }

        menu.PostSelectAction = PostSelectAction.Close;
        OpenMenu(p, menu);
    }

    // ================================================================ admin commands

    private CCSPlayerController? Target(CCSPlayerController? caller, string pattern)
    {
        pattern = pattern.Trim();
        if (pattern.Length == 0) return null;
        if (pattern.Equals("@me", StringComparison.OrdinalIgnoreCase)) return caller;
        var all = Players().ToList();
        if (pattern.StartsWith('#') && int.TryParse(pattern[1..], out var uid))
            return all.FirstOrDefault(p => p.UserId == uid);
        if (ulong.TryParse(pattern, out var sid) && sid > 76561197960265728UL)
            return all.FirstOrDefault(p => p.SteamID == sid);
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
        if (target == null || target.IsBot && !Config.BotsGetPets)
        {
            Reply(caller, "admin.no_target", info.GetArg(1));
            return false;
        }

        return true;
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

        var pet = Data(t!)!;
        if (_actors.TryGetValue(t!.Slot, out var a)) Despawn(a);
        PetLogic.SetStage(pet, stage, Config, DateTime.UtcNow, NewSeed(t));
        _store.MarkDirty();
        Reply(caller, "admin.done", t.PlayerName);
        Tell(t, "admin.given", PetLogic.StageName(stage));
        TrySpawnPet(t);
    }

    [ConsoleCommand("css_pet_reset", "<target> - remove a player's pet (it is kept in their journal)")]
    public void OnAdminReset(CCSPlayerController? caller, CommandInfo info)
    {
        if (!AdminGate(caller, info, 2, "admin.reset.usage", out var t)) return;
        var pet = Data(t!)!;
        if (_actors.TryGetValue(t!.Slot, out var a)) Despawn(a);
        PetLogic.Retire(pet, "reset by an admin", DateTime.UtcNow);
        _store.MarkDirty();
        Reply(caller, "admin.done", t.PlayerName);
    }

    [ConsoleCommand("css_pet_bags", "<target> <amount> - give feed bags")]
    public void OnAdminBags(CCSPlayerController? caller, CommandInfo info)
    {
        if (!AdminGate(caller, info, 3, "admin.bags.usage", out var t)) return;
        if (!int.TryParse(info.GetArg(2), out var n))
        {
            Reply(caller, "admin.bags.usage");
            return;
        }

        var pet = Data(t!)!;
        pet.FeedBags = Math.Clamp(pet.FeedBags + n, 0, 999);
        _store.MarkDirty();
        Reply(caller, "admin.done", t!.PlayerName);
        Tell(t, "admin.bags.given", n, pet.FeedBags);
    }

    [ConsoleCommand("css_pet_givehat", "<target> <hat id|all> - unlock a hat for a player")]
    public void OnAdminHat(CCSPlayerController? caller, CommandInfo info)
    {
        if (!AdminGate(caller, info, 3, "admin.hat.usage", out var t)) return;
        var pet = Data(t!)!;
        var arg = info.GetArg(2);
        var hats = arg.Equals("all", StringComparison.OrdinalIgnoreCase) ? Catalog.Hats.ToList() : Catalog.FindHat(arg) is { } one ? new List<Hat> { one } : new();
        if (hats.Count == 0)
        {
            Reply(caller, "hat.unknown", arg);
            return;
        }

        foreach (var h in hats.Where(h => !pet.GiftedHats.Contains(h.Id))) pet.GiftedHats.Add(h.Id);
        _store.MarkDirty();
        Reply(caller, "admin.done", t!.PlayerName);
        foreach (var h in hats) Tell(t, "hat.unlocked", h.Name);
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

        var pet = Data(t!)!;
        pet.Breed = breed;
        var count = Catalog.CoatNames(breed).Count;
        pet.Coat = info.ArgCount > 3 && int.TryParse(info.GetArg(3), out var c) ? Math.Clamp(c - 1, 0, count - 1) : Random.Shared.Next(count);
        _store.MarkDirty();
        if (_actors.TryGetValue(t!.Slot, out var a)) RemoveChicken(a);
        TrySpawnPet(t);
        Reply(caller, "admin.breed.done", t.PlayerName, PetLogic.Description(pet));
    }

    [ConsoleCommand("css_pet_skin", "<index> - (testing) show skin <index> on your own pet, to map coats")]
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

        Config.Enabled = !Config.Enabled;
        if (!Config.Enabled)
            foreach (var a in _actors.Values) Despawn(a);
        else
            foreach (var p in Players()) TrySpawnPet(p);
        Reply(caller, Config.Enabled ? "admin.toggle.on" : "admin.toggle.off");
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
