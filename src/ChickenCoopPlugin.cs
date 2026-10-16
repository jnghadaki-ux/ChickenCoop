using ChickenCoop.Logic;
using ChickenCoop.Model;
using ChickenCoop.Runtime;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ChickenCoop;

/// <summary>
/// Chicken Coop: a pet chicken for every player, built from the stock CS2 pet chicken assets. Eggs hatch into chicks,
/// chicks grow into pullets (breed and coat revealed) and then hens, by playing on the server. Pets follow their owner
/// using the game's own chicken AI, can be fed, petted, named, dressed in the photo-booth hats, and can die.
/// </summary>
[MinimumApiVersion(300)]
public sealed partial class ChickenCoopPlugin : BasePlugin, IPluginConfig<CoopConfig>
{
    public override string ModuleName => "Chicken Coop";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Gdaki";
    public override string ModuleDescription => "Pet chickens for CS2: hatch, raise, feed, name and dress up your own chicken.";

    private const float GrowthTickSeconds = 5f;
    private const float FollowTickSeconds = 0.2f;

    public CoopConfig Config { get; set; } = new();

    private readonly Lang _lang = new();
    private PetStore _store = null!;
    private readonly Dictionary<int, PetActor> _actors = new();
    private readonly Dictionary<int, PetData> _botPets = new();
    private string _map = "";
    private bool _mapDisabled;
    private int _roundNumber;

    public void OnConfigParsed(CoopConfig config)
    {
        Config = config;
        Config.Combat.WhoCanHurtPets = (Config.Combat.WhoCanHurtPets ?? "enemies").Trim().ToLowerInvariant();
        Config.MaxActivePets = Math.Clamp(Config.MaxActivePets, 1, 64);
    }

    public override void Load(bool hotReload)
    {
        _lang.Load(Path.Combine(ModuleDirectory, "lang"), Config.Language);
        _store = new PetStore(Path.Combine(ModuleDirectory, "data", "pets.json"));
        _store.Load();

        RegisterListener<Listeners.OnServerPrecacheResources>(OnPrecache);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(OnEntityTakeDamagePre);

        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventOtherDeath>(OnOtherDeath);
        RegisterEventHandler<EventRoundPrestart>(OnRoundPrestart);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);

        AddTimer(GrowthTickSeconds, GrowthTick, TimerFlags.REPEAT);
        AddTimer(FollowTickSeconds, FollowTick, TimerFlags.REPEAT);
        AddTimer(60f, () => SaveSafe(), TimerFlags.REPEAT);

        _map = Server.MapName ?? "";
        _mapDisabled = PetLogic.MapDisabled(_map, Config.DisabledMaps);
        if (hotReload)
        {
            // Pets come back for everyone already alive.
            AddTimer(1f, () =>
            {
                foreach (var p in Players()) TrySpawnPet(p);
            });
        }

        Logger.LogInformation("Chicken Coop {Version} by Gdaki loaded: {Count} pets on file", ModuleVersion, _store.Count);
    }

    public override void Unload(bool hotReload)
    {
        foreach (var a in _actors.Values.ToList()) Despawn(a);
        _actors.Clear();
        SaveSafe(true);
    }

    // ================================================================ lifecycle

    private void OnPrecache(ResourceManifest manifest)
    {
        foreach (var m in Catalog.AllModels()) manifest.AddResource(m);
        foreach (var p in Particles.All) manifest.AddResource(p);
    }

    private void OnMapStart(string map)
    {
        _map = map;
        _mapDisabled = PetLogic.MapDisabled(map, Config.DisabledMaps);
        _actors.Clear();
        _botPets.Clear();
        _roundNumber = 0;
    }

    private void OnMapEnd()
    {
        _actors.Clear();
        SaveSafe(true);
    }

    private void OnClientDisconnect(int slot)
    {
        if (_actors.Remove(slot, out var a)) Despawn(a);
        _botPets.Remove(slot);
        SaveSafe();
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

    private bool HasPermission(CCSPlayerController p, List<string> flags) =>
        flags.Count == 0 || AdminManager.PlayerHasPermissions(p, flags.ToArray());

    private bool IsAdmin(CCSPlayerController? p) => p == null || HasPermission(p, Config.AdminPermissions);

    private bool MayOwn(CCSPlayerController p)
    {
        if (p.IsBot) return Config.BotsGetPets;
        return HasPermission(p, Config.OwnerPermissions) || (Config.AdminPermissions.Count > 0 && HasPermission(p, Config.AdminPermissions));
    }

    /// <summary>The pet record of a player: saved for humans, a throwaway one for bots (when BotsGetPets is on).</summary>
    private PetData? Data(CCSPlayerController p)
    {
        if (p.IsBot)
        {
            if (!Config.BotsGetPets) return null;
            if (!_botPets.TryGetValue(p.Slot, out var bot))
            {
                bot = new PetData { OwnerName = p.PlayerName };
                var seed = Random.Shared.Next();
                PetLogic.SetStage(bot, (PetStage)Random.Shared.Next(2, 5), Config, DateTime.UtcNow, seed);
                bot.Hat = Random.Shared.Next(3) == 0 ? Catalog.Hats[Random.Shared.Next(Catalog.Hats.Count)].Id : null;
                _botPets[p.Slot] = bot;
            }

            return bot;
        }

        return p.SteamID == 0 ? null : _store.Get(p.SteamID, p.PlayerName);
    }

    private PetActor Actor(CCSPlayerController p)
    {
        if (!_actors.TryGetValue(p.Slot, out var a) || a.SteamId != p.SteamID)
        {
            if (a != null) Despawn(a);
            a = new PetActor(p.Slot, p.SteamID);
            _actors[p.Slot] = a;
        }

        return a;
    }

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
        foreach (var p in Players())
        {
            if (p.IsBot || !OnTeam(p) || !MayOwn(p)) continue;
            var pet = Data(p);
            if (pet == null) continue;
            var hatsBefore = PetLogic.UnlockedIds(pet, Config);
            var events = PetLogic.Advance(pet, GrowthTickSeconds, Config, now, NewSeed(p));
            _store.MarkDirty();
            foreach (var e in events) OnGrowthEvent(p, pet, e);
            AnnounceNewHats(p, pet, hatsBefore);
            EggWiggle(p, pet);
        }
    }

    private static int NewSeed(CCSPlayerController p) => HashCode.Combine(p.SteamID, DateTime.UtcNow.Ticks, Random.Shared.Next());

    private void OnGrowthEvent(CCSPlayerController p, PetData pet, GrowthEvent e)
    {
        var a = Actor(p);
        switch (e)
        {
            case GrowthEvent.FoundEgg:
                Tell(p, "egg.found", PetLogic.Duration(PetLogic.StageSeconds(PetStage.Egg, Config)));
                PlayTo(p, Sounds.HatchTap);
                TrySpawnPet(p);
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

                break;

            case GrowthEvent.BecamePullet:
                TellAll("grow.pullet.all", p.PlayerName, pet.Name, PetLogic.Description(pet));
                Tell(p, "grow.pullet", pet.Name, PetLogic.Description(pet));
                Evolve(p, a, Sounds.EvolvePullet);
                break;

            case GrowthEvent.BecameHen:
                TellAll("grow.hen.all", p.PlayerName, pet.Name);
                Tell(p, "grow.hen", pet.Name);
                Evolve(p, a, Sounds.EvolveHen);
                break;

            case GrowthEvent.Hungry:
                Tell(p, "food.hungry", pet.Name, pet.FeedBags);
                PlayAt(a, Sounds.Pick(pet.Stage, Sounds.Hungry, Sounds.HungryChick));
                break;

            case GrowthEvent.Starving:
                Tell(p, "food.empty", pet.Name, pet.FeedBags);
                PlayAt(a, Sounds.Pick(pet.Stage, Sounds.Hungry, Sounds.HungryChick));
                break;

            case GrowthEvent.RetiredOldAge:
                Tell(p, "retire.age", pet.PastPets.LastOrDefault()?.Name ?? "");
                Despawn(a);
                break;

            case GrowthEvent.RetiredHunger:
                Tell(p, "retire.hunger", pet.PastPets.LastOrDefault()?.Name ?? "");
                Despawn(a);
                break;
        }
    }

    /// <summary>Swap the model in place for the next stage, with the stock evolve sound and a feather burst.</summary>
    private void Evolve(CCSPlayerController p, PetActor a, string sound)
    {
        PlayTo(p, sound);
        if (a.Chicken is { IsValid: true } c && c.AbsOrigin is { } at) Particles.Spawn(this, Particles.Feathers, Copy(at), 3f);
        RemoveChicken(a);
        TrySpawnPet(p);
    }

    private void AnnounceNewHats(CCSPlayerController p, PetData pet, HashSet<string> before)
    {
        if (Config.Looks.AllHatsUnlocked) return;
        foreach (var h in PetLogic.UnlockedHats(pet, Config).Where(h => !before.Contains(h.Id)))
            Tell(p, "hat.unlocked", h.Name);
    }

    /// <summary>Runs an action that can change stats, then announces hats it unlocked.</summary>
    private void WithHats(CCSPlayerController p, PetData pet, Action change)
    {
        var before = PetLogic.UnlockedIds(pet, Config);
        change();
        _store.MarkDirty();
        AnnounceNewHats(p, pet, before);
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
            if (pl is { IsValid: true }) TrySpawnPet(pl);
        }, TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam ev, GameEventInfo info)
    {
        var p = ev.Userid;
        if (p is { IsValid: true } && _actors.TryGetValue(p.Slot, out var a) && ev.Team is not 2 and not 3) Despawn(a);
        return HookResult.Continue;
    }

    private HookResult OnRoundPrestart(EventRoundPrestart ev, GameEventInfo info)
    {
        _roundNumber++;
        // The round restart removes every spawned entity; forget ours so nothing points at them.
        foreach (var a in _actors.Values)
        {
            Despawn(a);
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
            WithHats(p, pet, () => gained = PetLogic.RoundPlayed(pet, Config, ev.Winner == (int)p.Team));
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
            if (pet != null && pet.IsHatched)
            {
                if (aa.ChickenAlive) WithHats(attacker, pet, () => pet.Stats.OwnerKillsWithPet++);
                if (Config.Combat.Revenge && aa.DiedThisRound && aa.KillerSlot == victim.Slot)
                {
                    aa.KillerSlot = -1;
                    WithHats(attacker, pet, () => pet.Stats.Revenges++);
                    Tell(attacker, "revenge", pet.Name, victim.PlayerName);
                    PlayTo(attacker, Sounds.FlapLong);
                }
            }
        }

        // The owner died: the pet stops following and waits by them.
        if (victim is { IsValid: true } && _actors.TryGetValue(victim.Slot, out var va))
        {
            if (va.EggOut) RemoveEgg(va);
            if (va.Chicken is { IsValid: true } c)
            {
                SetLeader(c, null);
                c.DesiredActivity = EChickenActivity.Panic;
                var pet = Data(victim);
                if (pet != null) PlayAt(va, Sounds.Pick(pet.Stage, Sounds.Panic, Sounds.PanicChick));
                AddTimer(2.5f, () =>
                {
                    if (va.Chicken is { IsValid: true } ch) ch.DesiredActivity = EChickenActivity.Sleep;
                }, TimerFlags.STOP_ON_MAPCHANGE);
                if (Config.Behaviour.StayWithBodySeconds > 0)
                    AddTimer(Config.Behaviour.StayWithBodySeconds, () =>
                    {
                        if (AlivePawn(Utilities.GetPlayerFromSlot(va.Slot)) == null) Despawn(va);
                    }, TimerFlags.STOP_ON_MAPCHANGE);
            }
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

        RemoveExtras(a);
        a.Chicken = null;
        a.Shown = PetStage.None;
        a.DiedThisRound = true;
        a.KillerSlot = killer is { IsValid: true } && killer.Slot != a.Slot ? killer.Slot : -1;

        if (owner is not { IsValid: true } || pet == null) return HookResult.Continue;
        if (!owner.IsBot) WithHats(owner, pet, () => PetLogic.RecordDeath(pet, cause, alive));

        var how = _lang.Plain("death.cause." + cause.ToString().ToLowerInvariant());
        var by = killer is { IsValid: true } && killer.Slot != owner.Slot ? killer.PlayerName : null;
        Tell(owner, by != null ? "death.owner.by" : "death.owner", pet.Name, PetLogic.Duration(alive), how, by);
        if (by != null && killer!.Slot != owner.Slot) Tell(killer, "death.killer", owner.PlayerName, pet.Name);
        if (Config.Combat.AnnounceDeaths)
            foreach (var p in Players().Where(x => x.Slot != owner.Slot && (killer == null || x.Slot != killer.Slot)))
                Tell(p, "death.all", owner.PlayerName, pet.Name, how);

        if (Config.Combat.RespawnSeconds > 0)
        {
            var slot = a.Slot;
            var round = _roundNumber;
            AddTimer(Config.Combat.RespawnSeconds, () =>
            {
                if (round != _roundNumber) return;
                var pl = Utilities.GetPlayerFromSlot(slot);
                if (pl is { IsValid: true } && _actors.TryGetValue(slot, out var act))
                {
                    act.DiedThisRound = false;
                    TrySpawnPet(pl);
                    if (act.ChickenAlive) Tell(pl, "death.back", Data(pl)?.Name);
                }
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
            if (ent.DesignerName == "player")
            {
                var pawn = new CCSPlayerPawn(ent.Handle);
                return pawn.OriginalController.Value ?? pawn.Controller.Value?.As<CCSPlayerController>();
            }

            // Grenades, fire and the like: their owner threw them.
            var owner = ent.OwnerEntity.Value;
            if (owner is { IsValid: true } && owner.DesignerName == "player")
            {
                var pawn = new CCSPlayerPawn(owner.Handle);
                return pawn.OriginalController.Value ?? pawn.Controller.Value?.As<CCSPlayerController>();
            }
        }
        catch
        {
            // a dying entity can have half-torn handles
        }

        return null;
    }

    // ================================================================ chat

    private string Prefix => Lang.Colorize(Config.ChatPrefix);

    private void Tell(CCSPlayerController? p, string key, params object?[] args)
    {
        if (p is not { IsValid: true } || p.IsBot) return;
        p.PrintToChat($" {Prefix} {_lang.T(key, args)}");
    }

    private void TellAll(string key, params object?[] args)
    {
        foreach (var p in Players()) Tell(p, key, args);
    }

    private void Reply(CCSPlayerController? p, string key, params object?[] args)
    {
        if (p == null) Server.PrintToConsole(_lang.Plain(key, args));
        else Tell(p, key, args);
    }

    private static Vector Copy(Vector v) => new(v.X, v.Y, v.Z);
}
