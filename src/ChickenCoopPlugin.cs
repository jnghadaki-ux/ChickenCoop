using ChickenCoop.Logic;
using ChickenCoop.Menus;
using ChickenCoop.Model;
using ChickenCoop.Runtime;
using ChickenCoop.Shared;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ChickenCoop;

/// <summary>
/// Chicken Coop, by Gdaki: a pet chicken for every player, built from the stock CS2 pet chicken assets. Eggs hatch into
/// chicks, chicks grow into pullets (breed, coat and shiny revealed) and then hens that lay eggs, by playing on the
/// server. Pets follow their owner using the game's own chicken AI, can be fed, petted, named, dressed up, perched on a
/// shoulder, made to emote, and can die.
/// </summary>
[MinimumApiVersion(300)]
public sealed partial class ChickenCoopPlugin : BasePlugin, IPluginConfig<CoopConfig>
{
    public override string ModuleName => "Chicken Coop";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Gdaki";
    public override string ModuleDescription => "Pet chickens for CS2: hatch, raise, feed, name and dress up your own chicken. Co-authored with Claude (Anthropic).";

    public static ChickenCoopPlugin? Instance { get; private set; }

    private const float GrowthTickSeconds = 5f;
    private const float FollowTickSeconds = 0.2f;
    private const int MaxSlots = 65;

    public CoopConfig Config { get; set; } = new();

    private readonly Lang _lang = new();
    private PetStore _store = null!;
    private CoopApi _api = null!;
    private readonly Dictionary<int, PetActor> _actors = new();
    private readonly Dictionary<int, PetData> _botPets = new();
    private readonly HashSet<int> _suppressed = new();
    private readonly HashSet<ulong> _seasonGreeted = new();
    private readonly HashSet<int> _mapNoticeTold = new();
    private bool _suppressAll;
    private bool _mapDisabled;
    private bool _needsMapChange;
    private int _roundNumber;
    private Season _season;

    public void OnConfigParsed(CoopConfig config)
    {
        Config = config;
        Config.Combat.WhoCanHurtPets = (Config.Combat.WhoCanHurtPets ?? "enemies").Trim().ToLowerInvariant();
        Config.Eggs.Pickup = (Config.Eggs.Pickup ?? "owner").Trim().ToLowerInvariant();
        Config.Looks.HatMode = (Config.Looks.HatMode ?? "bonemerge").Trim().ToLowerInvariant();
        Config.MaxActivePets = Math.Clamp(Config.MaxActivePets, 1, 64);
    }

    public override void Load(bool hotReload)
    {
        Instance = this;
        _lang.Load(Path.Combine(ModuleDirectory, "lang"), Config.Language);
        _store = new PetStore(Path.Combine(ModuleDirectory, "data", "pets.json"));
        _store.Load();
        _api = new CoopApi(this);
        Capabilities.RegisterPluginCapability(IChickenCoopApi.Capability, () => _api);

        RegisterListener<Listeners.OnServerPrecacheResources>(OnPrecache);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnPlayerButtonsChanged>(OnButtonsChanged);
        RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(OnEntityTakeDamagePre);

        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventOtherDeath>(OnOtherDeath);
        RegisterEventHandler<EventRoundPrestart>(OnRoundPrestart);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);

        AddCommandListener("say", ChatInput.OnSay, HookMode.Pre);
        AddCommandListener("say_team", ChatInput.OnSay, HookMode.Pre);

        AddTimer(GrowthTickSeconds, GrowthTick, TimerFlags.REPEAT);
        AddTimer(FollowTickSeconds, FollowTick, TimerFlags.REPEAT);
        AddTimer(60f, () => SaveSafe(), TimerFlags.REPEAT);

        var map = Server.MapName ?? "";
        _mapDisabled = PetLogic.MapDisabled(map, Config.DisabledMaps);
        _season = PetLogic.ActiveSeason(DateTime.Now, Config.Seasons);
        if (hotReload || map.Length > 0)
        {
            // Loaded while a map is running: the photo-booth hats and pet models were not precached for this map.
            _needsMapChange = true;
            Logger.LogWarning("Chicken Coop was loaded mid-map. Change map once so the pet models and hats are precached.");
            AddTimer(1f, () =>
            {
                foreach (var p in Players()) TrySpawnPet(p);
            });
        }

        Logger.LogInformation("Chicken Coop {Version} by Gdaki loaded: {Count} pets on file, languages: {Langs}", ModuleVersion,
            _store.Count, string.Join(", ", _lang.Languages));
    }

    public override void Unload(bool hotReload)
    {
        foreach (var a in _actors.Values.ToList()) Despawn(a, true);
        _actors.Clear();
        MenuService.Reset();
        ChatInput.Reset();
        SaveSafe(true);
        Instance = null;
    }

    // ================================================================ lifecycle

    private void OnPrecache(ResourceManifest manifest)
    {
        foreach (var m in Catalog.AllModels()) manifest.AddResource(m);
        foreach (var p in Particles.All) manifest.AddResource(p);
    }

    private void OnMapStart(string map)
    {
        _mapDisabled = PetLogic.MapDisabled(map, Config.DisabledMaps);
        _needsMapChange = false;
        _season = PetLogic.ActiveSeason(DateTime.Now, Config.Seasons);
        _actors.Clear();
        _botPets.Clear();
        _seasonGreeted.Clear();
        _mapNoticeTold.Clear();
        _roundNumber = 0;
        MenuService.Reset();
        ChatInput.Reset();
    }

    private void OnMapEnd()
    {
        _actors.Clear();
        SaveSafe(true);
    }

    private void OnClientDisconnect(int slot)
    {
        if (_actors.Remove(slot, out var a)) Despawn(a, true);
        _botPets.Remove(slot);
        _suppressed.Remove(slot);
        MenuService.ForgetSlot(slot);
        ChatInput.Forget(slot);
        SaveSafe();
    }

    private void OnTick()
    {
        MenuService.Tick();
        PettingTick();
    }

    private void OnButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (player is { IsValid: true, IsBot: false } && pressed != 0) MenuService.OnButtons(player, pressed);
    }

    private void SaveSafe(bool force = false)
    {
        try
        {
            _store.Save(force);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Chicken Coop could not save pets.json");
        }
    }

    // ================================================================ players and data

    private static IEnumerable<CCSPlayerController> Players() =>
        Utilities.GetPlayers().Where(p => p is { IsValid: true, IsHLTV: false, Connected: PlayerConnectedState.Connected });

    private static bool OnTeam(CCSPlayerController p) => p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist;

    private static CCSPlayerPawn? AlivePawn(CCSPlayerController? p)
    {
        if (p is not { IsValid: true, PawnIsAlive: true }) return null;
        var pawn = p.PlayerPawn.Value;
        return pawn is { IsValid: true } && pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE ? pawn : null;
    }

    private static bool HasFlags(CCSPlayerController p, List<string> flags) =>
        flags.Count == 0 || AdminManager.PlayerHasPermissions(p, flags.ToArray());

    internal bool IsAdmin(CCSPlayerController? p) => p == null || (Config.AdminPermissions.Count > 0 && HasFlags(p, Config.AdminPermissions));

    internal bool MayOwn(CCSPlayerController p)
    {
        if (p.IsBot) return Config.BotsGetPets;
        return HasFlags(p, Config.OwnerPermissions) || (Config.AdminPermissions.Count > 0 && HasFlags(p, Config.AdminPermissions));
    }

    /// <summary>The pet record of a player: saved for humans, a throwaway one for bots (when BotsGetPets is on).</summary>
    internal PetData? Data(CCSPlayerController p)
    {
        if (p.IsBot)
        {
            if (!Config.BotsGetPets) return null;
            if (!_botPets.TryGetValue(p.Slot, out var bot))
            {
                bot = new PetData { OwnerName = p.PlayerName };
                PetLogic.SetStage(bot, (PetStage)Random.Shared.Next(2, 5), Config, DateTime.UtcNow, Random.Shared.Next());
                if (Random.Shared.Next(4) == 0) bot.Shiny = Catalog.ShinyVariants[Random.Shared.Next(Catalog.ShinyVariants.Count)].Id;
                bot.Hat = Random.Shared.Next(3) == 0 ? Catalog.Hats[Random.Shared.Next(Catalog.Hats.Count - 1)].Id : null;
                bot.GiftedHats.AddRange(Catalog.Hats.Select(h => h.Id));
                _botPets[p.Slot] = bot;
            }

            return bot;
        }

        return p.SteamID == 0 ? null : _store.Get(p.SteamID, p.PlayerName);
    }

    internal PetActor Actor(CCSPlayerController p)
    {
        if (!_actors.TryGetValue(p.Slot, out var a) || a.SteamId != p.SteamID)
        {
            if (a != null) Despawn(a, true);
            a = new PetActor(p.Slot, p.SteamID);
            _actors[p.Slot] = a;
        }

        return a;
    }

    internal bool IsPetOut(CCSPlayerController p) => _actors.TryGetValue(p.Slot, out var a) && a.Out;

    internal void MarkDirty() => _store.MarkDirty();

    private bool Warmup()
    {
        try
        {
            return Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules?.WarmupPeriod ?? false;
        }
        catch
        {
            return false;
        }
    }

    // ================================================================ growth

    private void GrowthTick()
    {
        if (!Config.Enabled) return;
        var now = DateTime.UtcNow;
        _season = PetLogic.ActiveSeason(DateTime.Now, Config.Seasons);
        foreach (var p in Players())
        {
            if (p.IsBot || !OnTeam(p) || !MayOwn(p)) continue;
            var pet = Data(p);
            if (pet == null) continue;
            var before = PetLogic.UnlockedIds(pet, Config);
            var events = PetLogic.Advance(pet, GrowthTickSeconds, Config, now, NewSeed(p));
            _store.MarkDirty();
            foreach (var e in events) OnGrowthEvent(p, pet, e);
            if (_season != Season.None)
                foreach (var name in PetLogic.SeasonRewards(pet, _season)) Tell(p, "season.unlocked", name);
            AnnounceUnlocks(p, pet, before);
            EggWiggle(p, pet);
        }
    }

    internal static int NewSeed(CCSPlayerController p) => HashCode.Combine(p.SteamID, DateTime.UtcNow.Ticks, Random.Shared.Next());

    private void OnGrowthEvent(CCSPlayerController p, PetData pet, GrowthEvent e)
    {
        var a = Actor(p);
        switch (e)
        {
            case GrowthEvent.FoundEgg:
                Tell(p, "egg.found", PetLogic.Duration(PetLogic.StageSeconds(PetStage.Egg, Config)));
                PlayTo(p, Sounds.HatchTap);
                TrySpawnPet(p);
                _api.RaiseStage(p, PetStage.Egg);
                break;

            case GrowthEvent.Hatched:
                if (a.EggOut && AlivePawn(p) != null)
                {
                    HatchSequence(p, a, pet);
                }
                else
                {
                    Tell(p, "egg.hatched", pet.Name);
                    PlayTo(p, Sounds.EvolveChick);
                    TrySpawnPet(p);
                }

                _api.RaiseStage(p, PetStage.Chick);
                break;

            case GrowthEvent.BecamePullet:
                TellAll("grow.pullet.all", p.PlayerName, pet.Name, PetLogic.Description(pet));
                Evolve(p, a, Sounds.EvolvePullet);
                if (pet.IsShiny)
                {
                    if (Config.Shiny.Announce) TellAll("shiny.reveal.all", p.PlayerName, pet.Name, Catalog.FindShiny(pet.Shiny)!.Name);
                    else Tell(p, "shiny.reveal", pet.Name, Catalog.FindShiny(pet.Shiny)!.Name);
                    Celebrate(a, Particles.Confetti);
                }

                _api.RaiseStage(p, PetStage.Pullet);
                break;

            case GrowthEvent.BecameHen:
                TellAll("grow.hen.all", p.PlayerName, pet.Name);
                Evolve(p, a, Sounds.EvolveHen);
                _api.RaiseStage(p, PetStage.Hen);
                break;

            case GrowthEvent.Hungry:
                Tell(p, "food.hungry", pet.Name, pet.FeedBags);
                PlayAt(a, Sounds.Pick(pet.Stage, Sounds.Hungry, Sounds.HungryChick));
                break;

            case GrowthEvent.Starving:
                Tell(p, "food.empty", pet.Name, pet.FeedBags);
                PlayAt(a, Sounds.Pick(pet.Stage, Sounds.Hungry, Sounds.HungryChick));
                break;

            case GrowthEvent.LaidEgg:
                LayEgg(p, a, pet);
                break;

            case GrowthEvent.RetiredOldAge:
                Tell(p, "retire.age", pet.PastPets.LastOrDefault()?.Name ?? "");
                Despawn(a, true);
                _api.RaiseStage(p, PetStage.None);
                break;

            case GrowthEvent.RetiredHunger:
                Tell(p, "retire.hunger", pet.PastPets.LastOrDefault()?.Name ?? "");
                Despawn(a, true);
                _api.RaiseStage(p, PetStage.None);
                break;
        }
    }

    /// <summary>Swaps the model in place for the next stage, with the stock evolve sound and a celebration.</summary>
    private void Evolve(CCSPlayerController p, PetActor a, string sound)
    {
        PlayTo(p, sound);
        if (a.Chicken is { IsValid: true } c && c.AbsOrigin is { } at)
        {
            Fx(Particles.PhotoFeathers, Up(at, 8), 3f);
            Fx(Particles.Confetti, Up(at, 8), 3f);
        }

        RemoveChicken(a);
        RemovePerch(a);
        TrySpawnPet(p);
    }

    private void AnnounceUnlocks(CCSPlayerController p, PetData pet, HashSet<string> before)
    {
        foreach (var id in PetLogic.UnlockedIds(pet, Config).Where(id => !before.Contains(id)))
        {
            var parts = id.Split(':', 2);
            switch (parts[0])
            {
                case "hat" when !Config.Looks.AllHatsUnlocked:
                    Tell(p, "hat.unlocked", Catalog.FindHat(parts[1])?.Name);
                    break;
                case "emote" when !Config.Looks.AllHatsUnlocked:
                    Tell(p, "emote.unlocked", Catalog.FindEmote(parts[1])?.Name);
                    break;
            }
        }
    }

    /// <summary>Runs an action that can change stats, then announces what it unlocked.</summary>
    internal void WithUnlocks(CCSPlayerController p, PetData pet, Action change)
    {
        var before = PetLogic.UnlockedIds(pet, Config);
        change();
        _store.MarkDirty();
        AnnounceUnlocks(p, pet, before);
    }

    // ================================================================ round events

    private HookResult OnPlayerSpawn(EventPlayerSpawn ev, GameEventInfo info)
    {
        var p = ev.Userid;
        if (p is not { IsValid: true } || p.IsHLTV) return HookResult.Continue;
        var slot = p.Slot;
        AddTimer(0.6f, () =>
        {
            var pl = Utilities.GetPlayerFromSlot(slot);
            if (pl is not { IsValid: true }) return;
            SyncViewSettings(pl);
            TrySpawnPet(pl);
            GreetSeason(pl);
            if (_needsMapChange && !pl.IsBot && IsAdmin(pl) && _mapNoticeTold.Add(pl.Slot)) Tell(pl, "admin.needs_map_change");
        }, TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    private void GreetSeason(CCSPlayerController p)
    {
        if (_season == Season.None || p.IsBot || !MayOwn(p) || !_seasonGreeted.Add(p.SteamID)) return;
        Tell(p, _season == Season.Halloween ? "season.halloween" : "season.christmas");
    }

    private HookResult OnPlayerTeam(EventPlayerTeam ev, GameEventInfo info)
    {
        var p = ev.Userid;
        if (p is { IsValid: true } && _actors.TryGetValue(p.Slot, out var a) && ev.Team is not 2 and not 3) Despawn(a, false);
        return HookResult.Continue;
    }

    private HookResult OnRoundPrestart(EventRoundPrestart ev, GameEventInfo info)
    {
        _roundNumber++;
        // Laid eggs nobody picked up go to their owner's basket; then the round restart removes every spawned entity.
        foreach (var a in _actors.Values)
        {
            var leftover = a.LaidEggs.Count(e => e.IsValid);
            if (leftover > 0 && Utilities.GetPlayerFromSlot(a.Slot) is { IsValid: true } owner && Data(owner) is { } pet)
            {
                var added = 0;
                for (var i = 0; i < leftover; i++)
                    if (PetLogic.CollectEgg(pet, Config)) added++;
                if (added > 0)
                {
                    _store.MarkDirty();
                    Tell(owner, "eggs.round_collected", added, pet.BasketEggs);
                }
            }

            Despawn(a, true);
            a.DiedThisRound = false;
            a.KillerSlot = -1;
            a.Staying = false;
        }

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd ev, GameEventInfo info)
    {
        if (!Config.Enabled || Warmup()) return HookResult.Continue;
        foreach (var p in Players().Where(p => !p.IsBot && OnTeam(p) && MayOwn(p)))
        {
            var pet = Data(p);
            if (pet == null) continue;
            var gained = 0;
            WithUnlocks(p, pet, () => gained = PetLogic.RoundPlayed(pet, Config, ev.Winner == (int)p.Team));
            if (gained > 0 && pet.HasPet) Tell(p, "food.bag_earned", gained, pet.FeedBags);
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath ev, GameEventInfo info)
    {
        var victim = ev.Userid;
        var attacker = ev.Attacker;

        // Kills with your pet out (banana hat) and revenge on whoever killed your pet.
        if (attacker is { IsValid: true } && victim is { IsValid: true } && attacker.Slot != victim.Slot
            && _actors.TryGetValue(attacker.Slot, out var aa))
        {
            var pet = Data(attacker);
            if (pet is { IsHatched: true })
            {
                if (aa.ChickenAlive || aa.PerchOut) WithUnlocks(attacker, pet, () => pet.Stats.OwnerKillsWithPet++);
                if (Config.Combat.Revenge && aa.DiedThisRound && aa.KillerSlot == victim.Slot)
                {
                    aa.KillerSlot = -1;
                    WithUnlocks(attacker, pet, () => pet.Stats.Revenges++);
                    Tell(attacker, "revenge", pet.Name, victim.PlayerName);
                    PlayTo(attacker, Sounds.FlapLong);
                }
            }
        }

        if (victim is not { IsValid: true } || !_actors.TryGetValue(victim.Slot, out var va)) return HookResult.Continue;

        if (va.EggOut) RemoveEgg(va);
        var vpet = Data(victim);

        // A perched pet jumps down where its owner fell.
        if (va.PerchOut && vpet != null && va.Perch!.AbsOrigin is { } perchAt)
        {
            var at = new Vector(perchAt.X, perchAt.Y, perchAt.Z);
            RemovePerch(va);
            if (!va.DiedThisRound) SpawnChicken(va, victim, null, vpet, at);
        }

        // The owner died: the pet panics, then sits down by them.
        if (va.Chicken is { IsValid: true } c)
        {
            SetLeader(c, null);
            c.DesiredActivity = EChickenActivity.Panic;
            if (vpet != null) PlayAt(va, Sounds.Pick(vpet.Stage, Sounds.Panic, Sounds.PanicChick));
            AddTimer(2.5f, () =>
            {
                if (va.Chicken is { IsValid: true } ch) ch.DesiredActivity = EChickenActivity.Sleep;
            }, TimerFlags.STOP_ON_MAPCHANGE);
            if (Config.Behaviour.StayWithBodySeconds > 0)
                AddTimer(Config.Behaviour.StayWithBodySeconds, () =>
                {
                    if (AlivePawn(Utilities.GetPlayerFromSlot(va.Slot)) == null) Despawn(va, false);
                }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        return HookResult.Continue;
    }

    private HookResult OnOtherDeath(EventOtherDeath ev, GameEventInfo info)
    {
        if (!string.Equals(ev.Othertype, "chicken", StringComparison.OrdinalIgnoreCase)) return HookResult.Continue;
        var a = _actors.Values.FirstOrDefault(x => x.Chicken != null && x.Chicken.IsValid && x.Chicken.Index == (uint)ev.Otherid);
        if (a == null) return HookResult.Continue;

        var owner = Utilities.GetPlayerFromSlot(a.Slot);
        var pet = owner is { IsValid: true } ? Data(owner) : null;
        var killer = Utilities.GetPlayerFromUserid(ev.Attacker);
        var cause = PetLogic.CauseFromWeapon(ev.Weapon);
        var alive = Server.CurrentTime - a.SpawnedAt;

        if (_season == Season.Halloween && a.Chicken is { IsValid: true } dying && dying.AbsOrigin is { } at)
            Fx(Particles.ZombieGone, Up(at, 4), 3f);
        RemoveExtras(a);
        a.Chicken = null;
        a.Shown = PetStage.None;
        a.DiedThisRound = true;
        a.TransmitDirty = true;
        a.KillerSlot = killer is { IsValid: true } && killer.Slot != a.Slot ? killer.Slot : -1;

        if (owner is not { IsValid: true } || pet == null) return HookResult.Continue;
        if (!owner.IsBot) WithUnlocks(owner, pet, () => PetLogic.RecordDeath(pet, cause, alive));
        _api.RaiseKilled(owner, killer is { IsValid: true } ? killer : null);

        var causeKey = "death.cause." + cause.ToString().ToLowerInvariant();
        var by = killer is { IsValid: true } && killer.Slot != owner.Slot ? killer : null;
        if (by != null) Tell(owner, "death.owner.by", pet.Name, PetLogic.Duration(alive), _lang.P(owner, causeKey), by.PlayerName);
        else Tell(owner, "death.owner", pet.Name, PetLogic.Duration(alive), _lang.P(owner, causeKey));
        if (by != null) Tell(by, "death.killer", owner.PlayerName, pet.Name);
        if (Config.Combat.AnnounceDeaths)
            foreach (var p in Players().Where(x => x.Slot != owner.Slot && (by == null || x.Slot != by.Slot)))
                Tell(p, "death.all", owner.PlayerName, pet.Name, _lang.P(p, causeKey));

        if (Config.Combat.RespawnSeconds > 0)
        {
            var slot = a.Slot;
            var round = _roundNumber;
            AddTimer(Config.Combat.RespawnSeconds, () =>
            {
                if (round != _roundNumber) return;
                var pl = Utilities.GetPlayerFromSlot(slot);
                if (pl is not { IsValid: true } || !_actors.TryGetValue(slot, out var act)) return;
                act.DiedThisRound = false;
                TrySpawnPet(pl);
                if (act.Out) Tell(pl, "death.back", Data(pl)?.Name);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        return HookResult.Continue;
    }

    /// <summary>Damage rules: owners never hurt their own pet; WhoCanHurtPets decides everyone else.</summary>
    private HookResult OnEntityTakeDamagePre(CBaseEntity entity, CTakeDamageInfo info)
    {
        if (entity.DesignerName != "chicken") return HookResult.Continue;
        var a = _actors.Values.FirstOrDefault(x => x.Chicken != null && x.Chicken.IsValid && x.Chicken.Index == entity.Index);
        if (a == null) return HookResult.Continue;

        var mode = Config.Combat.WhoCanHurtPets;
        if (mode == "nobody") return HookResult.Handled;

        var attacker = AttackerOf(info);
        if (attacker == null) return mode == "everyone" ? HookResult.Continue : HookResult.Handled;
        if (attacker.Slot == a.Slot) return HookResult.Handled;
        if (mode == "enemies")
        {
            var owner = Utilities.GetPlayerFromSlot(a.Slot);
            if (owner is { IsValid: true } && owner.Team == attacker.Team) return HookResult.Handled;
        }

        return HookResult.Continue;
    }

    private static CCSPlayerController? AttackerOf(CTakeDamageInfo info)
    {
        try
        {
            var ent = info.Attacker.Value;
            if (ent == null || !ent.IsValid) return null;
            if (ent.DesignerName != "player")
            {
                // Grenades, fire and the like: their owner threw them.
                ent = ent.OwnerEntity.Value;
                if (ent == null || !ent.IsValid || ent.DesignerName != "player") return null;
            }

            var pawn = new CCSPlayerPawn(ent.Handle);
            return pawn.OriginalController.Value ?? pawn.Controller.Value?.As<CCSPlayerController>();
        }
        catch
        {
            return null; // a dying entity can have half-torn handles
        }
    }

    // ================================================================ chat

    private string Prefix => Lang.Colorize(Config.ChatPrefix);

    internal void Tell(CCSPlayerController? p, string key, params object?[] args)
    {
        if (p is not { IsValid: true } || p.IsBot) return;
        p.PrintToChat($" {Prefix} {_lang.T(p, key, args)}");
    }

    internal void TellAll(string key, params object?[] args)
    {
        foreach (var p in Players()) Tell(p, key, args);
    }

    private void Reply(CCSPlayerController? p, string key, params object?[] args)
    {
        if (p == null) Server.PrintToConsole(Lang.StripTags(_lang.Raw("en", key, args)));
        else Tell(p, key, args);
    }

    /// <summary>Plain text in the player's language, for menus (HTML-escaped).</summary>
    internal string M(CCSPlayerController p, string key, params object?[] args) => MenuService.Esc(_lang.P(p, key, args));

    private static Vector Up(Vector v, float z) => new(v.X, v.Y, v.Z + z);
}
