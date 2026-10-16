using Color = System.Drawing.Color;
using ChickenCourier.Logic;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace ChickenCourier;

/// <summary>
/// Chicken Courier, by Gdaki: on bomb maps the C4 rides on a chicken instead of a player. Terrorists escort it (press E
/// to become its handler and it follows you) to a bombsite, where it lays the bomb for them to plant. Counter-Terrorists
/// shoot it to break its nerve (it panics and runs off) or press E to scare it. It never dies, so the bomb is never lost.
/// </summary>
[MinimumApiVersion(300)]
public sealed class CourierPlugin : BasePlugin, IPluginConfig<CourierConfig>
{
    public override string ModuleName => "Chicken Courier";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Gdaki";
    public override string ModuleDescription => "A standalone CS2 bomb mode inspired by ChickenCoop: a chicken carries the C4. Co-authored with Claude (Anthropic).";

    private const float TickSeconds = 0.2f;
    private const string C4Model = "weapons/models/c4/weapon_c4.vmdl";

    public CourierConfig Config { get; set; } = new();

    private readonly Lang _lang = new();
    private readonly List<Site> _sites = new();

    private CChicken? _chicken;
    private CBaseModelEntity? _bombProp;
    private CPointWorldText? _marker;
    private CourierState? _state;
    private int _handler = -1;
    private float _noHandlerSince;
    private float _lastProgressAt;
    private float _lastDistance;
    private float _nextCluck;
    private float _nextHud;
    private string _shownMarker = "";
    private readonly Dictionary<int, float> _scareReady = new();
    private int _round;

    public void OnConfigParsed(CourierConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        _lang.Load(Path.Combine(ModuleDirectory, "lang"), Config.Language);
        RegisterListener<Listeners.OnServerPrecacheResources>(m =>
        {
            m.AddResource(Config.Model);
            m.AddResource(C4Model);
            m.AddResource(Fx.Feathers);
            m.AddResource(Fx.Burst);
        });
        RegisterListener<Listeners.OnPlayerButtonsChanged>(OnButtons);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(OnDamage);
        RegisterListener<Listeners.OnMapStart>(_ => Clear());
        RegisterEventHandler<EventRoundPrestart>((_, _) =>
        {
            _round++;
            Clear();
            return HookResult.Continue;
        });
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventBombPlanted>((_, _) =>
        {
            RemoveCourier(true);
            return HookResult.Continue;
        });
        AddTimer(TickSeconds, Tick, TimerFlags.REPEAT);
        Logger.LogInformation("Chicken Courier {Version} by Gdaki loaded", ModuleVersion);
    }

    public override void Unload(bool hotReload) => RemoveCourier(true);

    private bool Active => _chicken is { IsValid: true } && _state != null;

    // ================================================================ round setup

    private HookResult OnRoundStart(EventRoundStart ev, GameEventInfo info)
    {
        var round = _round;
        // After everyone has spawned and been handed their weapons (the bomb included).
        AddTimer(1.0f, () =>
        {
            if (round == _round) SetupRound(null);
        }, TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    private bool ModeOn(out string? why)
    {
        why = null;
        if (!Config.Enabled) why = "off";
        else if (!CourierState.MapMatches(Server.MapName ?? "", Config.Maps)) why = "map";
        else if (Players().Count(p => OnTeam(p)) < Config.MinPlayers) why = "players";
        else if (Warmup()) why = "warmup";
        return why == null;
    }

    /// <param name="at">Spawn here (admin test) instead of next to the bomb carrier.</param>
    private void SetupRound(Vector? at)
    {
        RemoveCourier(true);
        if (at == null && !ModeOn(out _)) return;
        LoadSites();
        if (_sites.Count == 0) return;

        // Take the bomb off whoever got it, and spawn the courier next to them.
        Vector? spot = at;
        foreach (var p in Players().Where(p => p.Team == CsTeam.Terrorist))
        {
            var pawn = AlivePawn(p);
            if (pawn == null || !HasC4(pawn)) continue;
            spot ??= Behind(pawn, 40f);
            Utilities.RemoveItemByDesignerName(p, "weapon_c4");
        }

        foreach (var loose in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("weapon_c4").ToList())
            if (loose.IsValid && loose.OwnerEntity.Value == null) loose.Remove();

        if (spot == null)
        {
            var t = Players().FirstOrDefault(p => p.Team == CsTeam.Terrorist && AlivePawn(p) != null);
            if (t == null) return;
            spot = Behind(AlivePawn(t)!, 40f);
        }

        SpawnCourier(spot);
        TellTeam(CsTeam.Terrorist, "start.t");
        TellTeam(CsTeam.CounterTerrorist, "start.ct");
    }

    private static bool HasC4(CCSPlayerPawn pawn)
    {
        try
        {
            return pawn.WeaponServices?.MyWeapons.Any(w => w.Value is { IsValid: true } wpn && wpn.DesignerName == "weapon_c4") ?? false;
        }
        catch
        {
            return false;
        }
    }

    private void LoadSites()
    {
        _sites.Clear();
        foreach (var t in Utilities.FindAllEntitiesByDesignerName<CBombTarget>("func_bomb_target"))
        {
            try
            {
                var o = t.AbsOrigin ?? new Vector(0, 0, 0);
                var mins = t.Collision?.Mins;
                var maxs = t.Collision?.Maxs;
                var name = t.IsBombSiteB ? "B" : "A";
                if (mins != null && maxs != null)
                {
                    var min = new Point(o.X + mins.X, o.Y + mins.Y, o.Z + mins.Z);
                    var max = new Point(o.X + maxs.X, o.Y + maxs.Y, o.Z + maxs.Z);
                    var centre = new Point((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
                    _sites.Add(new Site(name, centre, min, max));
                }
            }
            catch
            {
                // fall back to the resource centres below
            }
        }

        if (_sites.Count > 0) return;
        var res = Utilities.FindAllEntitiesByDesignerName<CCSPlayerResource>("cs_player_manager").FirstOrDefault();
        if (res == null) return;
        if (res.BombsiteCenterA is { } a && (a.X != 0 || a.Y != 0)) _sites.Add(new Site("A", P(a)));
        if (res.BombsiteCenterB is { } b && (b.X != 0 || b.Y != 0)) _sites.Add(new Site("B", P(b)));
    }

    private void SpawnCourier(Vector at)
    {
        try
        {
            var c = Utilities.CreateEntityByName<CChicken>("chicken");
            if (c == null) return;
            c.Teleport(at, new QAngle(0, Random.Shared.Next(360), 0), new Vector(0, 0, 0));
            var kv = new CEntityKeyValues();
            kv.SetString("chicken_model", Config.Model);
            c.DispatchSpawn(kv);
            c.Health = 100000;
            c.MaxHealth = 100000;
            Scale(c, Config.Scale);
            NoBlock(c);
            _chicken = c;
            _state = new CourierState(Config);
            _handler = -1;
            _noHandlerSince = Server.CurrentTime;
            _lastProgressAt = Server.CurrentTime;
            _nextCluck = Server.CurrentTime + 2f;

            // The bomb strapped to its back, and a floating marker everyone can see.
            var bomb = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (bomb != null)
            {
                bomb.Spawnflags = 256;
                bomb.Teleport(new Vector(at.X, at.Y, at.Z + 17f * Config.Scale), new QAngle(0, c.AbsRotation?.Y ?? 0, 90), new Vector(0, 0, 0));
                var bkv = new CEntityKeyValues();
                bkv.SetString("model", C4Model);
                bkv.SetInt("solid", 0);
                bomb.DispatchSpawn(bkv);
                Scale(bomb, 0.75f);
                bomb.AcceptInput("SetParent", c, bomb, "!activator");
                _bombProp = bomb;
            }

            if (Config.Marker) _marker = SpawnMarker(c);
            Burst(at);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Chicken Courier could not spawn the courier");
            RemoveCourier(true);
        }
    }

    private CPointWorldText? SpawnMarker(CChicken c)
    {
        try
        {
            var o = c.AbsOrigin!;
            var t = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
            if (t == null) return null;
            t.MessageText = MarkerText();
            t.Enabled = true;
            t.FontSize = 48f;
            t.WorldUnitsPerPx = 0.14f;
            t.Fullbright = true;
            t.DrawBackground = false;
            t.Color = Color.FromArgb(255, 255, 140, 40);
            t.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
            t.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_BOTTOM;
            t.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_AROUND_UP;
            t.Teleport(new Vector(o.X, o.Y, o.Z + 40f * Config.Scale), new QAngle(0, 0, 90), new Vector(0, 0, 0));
            t.DispatchSpawn();
            t.AcceptInput("SetParent", c, t, "!activator");
            _shownMarker = t.MessageText;
            return t;
        }
        catch
        {
            return null;
        }
    }

    private string MarkerText()
    {
        if (_state == null) return "";
        var now = Server.CurrentTime;
        if (_state.Panicking(now)) return "💣 COURIER · PANIC!";
        if (_state.Laying) return $"💣 LAYING {CourierState.Bar(_state.LayFraction)}";
        return $"💣 COURIER {CourierState.Bar(_state.CourageFraction)}";
    }

    // ================================================================ every tick

    private void Tick()
    {
        if (!Active) return;
        var c = _chicken!;
        var st = _state!;
        var now = Server.CurrentTime;
        var at = c.AbsOrigin;
        if (at == null) return;

        // Handler: still valid? close enough?
        var handler = _handler >= 0 ? Utilities.GetPlayerFromSlot(_handler) : null;
        var hPawn = AlivePawn(handler);
        if (_handler >= 0 && (handler is not { IsValid: true } || handler.Team != CsTeam.Terrorist || hPawn == null
                              || Dist(hPawn.AbsOrigin!, at) > Config.LeashRange))
        {
            var name = handler?.PlayerName;
            _handler = -1;
            _noHandlerSince = now;
            hPawn = null;
            TellTeam(CsTeam.Terrorist, "handler.lost", name);
        }

        // Nobody leading it: after a while (or straight away with only bots) the nearest Terrorist takes over.
        if (_handler < 0)
        {
            var ts = Players().Where(p => p.Team == CsTeam.Terrorist && AlivePawn(p) != null).ToList();
            var onlyBots = ts.Count > 0 && ts.All(p => p.IsBot);
            if (onlyBots || (Config.AutoHandlerSeconds > 0 && now - _noHandlerSince >= Config.AutoHandlerSeconds))
            {
                var pick = CourierState.Nearest(P(at), ts.Select(p => (p.Slot, P(AlivePawn(p)!.AbsOrigin!))), Config.AutoRange);
                if (pick is { } slot) SetHandler(Utilities.GetPlayerFromSlot(slot)!, true);
            }

            handler = _handler >= 0 ? Utilities.GetPlayerFromSlot(_handler) : null;
            hPawn = AlivePawn(handler);
        }

        // Laying: inside a bombsite with the handler close by.
        var site = _sites.FirstOrDefault(s => s.Contains(P(at), Config.SiteRadius));
        var handlerClose = hPawn != null && Dist(hPawn.AbsOrigin!, at) <= Config.LayHandlerRange;
        foreach (var e in st.Tick(TickSeconds, now, site?.Name, handlerClose))
        {
            switch (e)
            {
                case CourierEvent.LayStarted:
                    TellTeam(CsTeam.Terrorist, "lay.start.t", site!.Name);
                    TellTeam(CsTeam.CounterTerrorist, "lay.start.ct", site!.Name);
                    c.EmitSound("c4.initiate");
                    break;
                case CourierEvent.LayInterrupted:
                    TellTeam(CsTeam.Terrorist, "lay.stopped");
                    break;
                case CourierEvent.Calmed:
                    TellTeam(CsTeam.Terrorist, "calm");
                    break;
                case CourierEvent.Laid:
                    LayBomb(site!.Name, handler, false);
                    return;
            }
        }

        // Movement: panicking = runs off on its own; laying = sits; otherwise follow the handler.
        if (st.Panicking(now))
        {
            SetLeader(c, null);
            c.DesiredActivity = EChickenActivity.Panic;
        }
        else if (st.Laying)
        {
            SetLeader(c, null);
            c.DesiredActivity = EChickenActivity.Squat;
        }
        else if (hPawn != null)
        {
            SetLeader(c, hPawn);
            KeepUp(c, hPawn, at, now);
        }
        else
        {
            SetLeader(c, null);
        }

        if (Config.ClucksEverySeconds > 0 && now >= _nextCluck)
        {
            _nextCluck = now + (float)Config.ClucksEverySeconds * (0.7f + Random.Shared.NextSingle() * 0.6f);
            c.EmitSound(st.Panicking(now) ? "Chicken.Panic" : "Chicken.Idle.Hen");
        }

        UpdateMarker();
        if (Config.CentreHud && st.Laying && handler is { IsValid: true, IsBot: false } && now >= _nextHud)
        {
            _nextHud = now + 0.5f;
            handler.PrintToCenterHtml(
                $"<font color='#FF8C28'><b>{_lang.P(handler, "hud.laying")}</b></font><br>" +
                $"<font color='#FFD24A'>{CourierState.Bar(st.LayFraction, 12)}</font> {(int)(st.LayFraction * 100)}%", 1);
        }
    }

    /// <summary>Stuck behind something while its handler walks off: teleport it back behind them.</summary>
    private void KeepUp(CChicken c, CCSPlayerPawn hPawn, Vector at, float now)
    {
        var d = Dist(hPawn.AbsOrigin!, at);
        if (d > 300f)
        {
            if (d < _lastDistance - 10f) _lastProgressAt = now;
            else if (now - _lastProgressAt > 6f)
            {
                var to = Behind(hPawn, 40f);
                c.Teleport(to, null, new Vector(0, 0, 0));
                Burst(to);
                _lastProgressAt = now;
            }
        }
        else
        {
            _lastProgressAt = now;
        }

        _lastDistance = d;
    }

    private void UpdateMarker()
    {
        if (_marker is not { IsValid: true }) return;
        var text = MarkerText();
        if (text == _shownMarker) return;
        _shownMarker = text;
        _marker.MessageText = text;
        Utilities.SetStateChanged(_marker, "CPointWorldText", "m_messageText");
    }

    // ================================================================ players: E presses

    private void OnButtons(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (!Active || (pressed & PlayerButtons.Use) == 0 || player is not { IsValid: true }) return;
        var pawn = AlivePawn(player);
        var at = _chicken!.AbsOrigin;
        if (pawn == null || at == null) return;
        var d = Dist(pawn.AbsOrigin!, at);
        var now = Server.CurrentTime;

        if (player.Team == CsTeam.Terrorist && d <= Config.HandlerRange)
        {
            if (_state!.Panicking(now))
            {
                player.PrintToCenter(_lang.P(player, "handler.panicking"));
                return;
            }

            if (_handler != player.Slot) SetHandler(player, false);
        }
        else if (player.Team == CsTeam.CounterTerrorist && Config.CtCanScare && d <= Config.ScareRange)
        {
            if (_scareReady.TryGetValue(player.Slot, out var ready) && now < ready) return;
            _scareReady[player.Slot] = now + (float)Config.ScareCooldownSeconds;
            if (!_state!.Scare(now)) return;
            Flee(pawn);
            Reward(player, Config.ScareReward);
            Tell(player, "scare.ct");
            TellTeam(CsTeam.Terrorist, "scare.t", player.PlayerName);
            if (Config.DropBombOnPanic) LayBomb(null, null, true);
        }
    }

    private void SetHandler(CCSPlayerController p, bool auto)
    {
        _handler = p.Slot;
        _lastProgressAt = Server.CurrentTime;
        foreach (var t in Players().Where(x => x.Team == CsTeam.Terrorist))
            Tell(t, t.Slot == p.Slot ? (auto ? "handler.you.auto" : "handler.you") : "handler.other", p.PlayerName);
        _chicken?.EmitSound("Chicken.Interact");
    }

    // ================================================================ damage: courage, panic

    private HookResult OnDamage(CBaseEntity entity, CTakeDamageInfo info)
    {
        if (!Active || entity.Index != _chicken!.Index) return HookResult.Continue;
        var attacker = AttackerOf(info);
        if (attacker is { Team: CsTeam.CounterTerrorist } && !_state!.Laid)
        {
            var now = Server.CurrentTime;
            _chicken.EmitSound("Chicken.Panic");
            if (_state.Hit(info.Damage, now))
            {
                if (AlivePawn(attacker) is { } pawn) Flee(pawn);
                Reward(attacker, Config.PanicReward);
                Tell(attacker, "panic.ct");
                TellTeam(CsTeam.Terrorist, "panic.t", attacker.PlayerName, (int)Config.PanicSeconds);
                if (_chicken.AbsOrigin is { } at) Burst(at);
                if (Config.DropBombOnPanic) LayBomb(null, null, true);
            }
        }

        return HookResult.Handled; // the courier never dies: the bomb is never lost
    }

    private void Flee(CCSPlayerPawn from)
    {
        if (!Active) return;
        SetLeader(_chicken!, null);
        try
        {
            Schema.SetSchemaValue(_chicken!.Handle, "CChicken", "m_fleeFrom", from.EntityHandle.Raw);
        }
        catch
        {
            // the panic activity still plays
        }

        _chicken!.DesiredActivity = EChickenActivity.Panic;
    }

    // ================================================================ laying the bomb

    /// <summary>The chicken lays (or, panicking with DropBombOnPanic, drops) the bomb: a normal C4 on the ground.</summary>
    private void LayBomb(string? site, CCSPlayerController? handler, bool dropped)
    {
        if (!Active) return;
        var c = _chicken!;
        var at = c.AbsOrigin!;
        try
        {
            var c4 = Utilities.CreateEntityByName<CBaseEntity>("weapon_c4");
            if (c4 != null)
            {
                c4.Teleport(new Vector(at.X, at.Y, at.Z + 8f), new QAngle(0, 0, 0), new Vector(0, 0, 0));
                c4.DispatchSpawn();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Chicken Courier could not drop the bomb");
        }

        c.EmitSound("c4.plant");
        Burst(at);
        if (dropped)
        {
            TellTeam(CsTeam.Terrorist, "dropped.t");
            TellTeam(CsTeam.CounterTerrorist, "dropped.ct");
        }
        else
        {
            if (handler is { IsValid: true }) Reward(handler, Config.LayReward);
            TellTeam(CsTeam.Terrorist, "laid.t", site);
            TellTeam(CsTeam.CounterTerrorist, "laid.ct", site);
        }

        // Its job is done: off it goes, flapping.
        RemoveCourier(false);
    }

    /// <summary>Clears the courier. With <paramref name="now"/> false the chicken runs off and vanishes a few seconds later.</summary>
    private void RemoveCourier(bool now)
    {
        Remove(_bombProp);
        Remove(_marker);
        _bombProp = null;
        _marker = null;
        var c = _chicken;
        _chicken = null;
        _state = null;
        _handler = -1;
        if (c is not { IsValid: true }) return;
        if (now)
        {
            c.Remove();
            return;
        }

        SetLeader(c, null);
        c.DesiredActivity = EChickenActivity.Panic;
        c.EmitSound("Chicken.FlapWings");
        AddTimer(6f, () =>
        {
            if (!c.IsValid) return;
            if (c.AbsOrigin is { } o) Burst(o);
            c.Remove();
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void Clear()
    {
        RemoveCourier(true);
        _scareReady.Clear();
    }

    // ================================================================ commands

    [ConsoleCommand("css_courier", "[on|off|status] - Chicken Courier mode")]
    public void OnCourierCommand(CCSPlayerController? caller, CommandInfo info)
    {
        var arg = info.ArgCount > 1 ? info.GetArg(1).ToLowerInvariant() : "status";
        if (arg is "on" or "off")
        {
            if (caller != null && !AdminManager.PlayerHasPermissions(caller, Config.AdminPermissions.ToArray()))
            {
                Reply(caller, "admin.denied");
                return;
            }

            Config.Enabled = arg == "on";
            if (!Config.Enabled) RemoveCourier(true);
            Reply(caller, Config.Enabled ? "admin.on" : "admin.off");
            return;
        }

        if (!ModeOn(out var why))
        {
            Reply(caller, "status.inactive." + why);
            return;
        }

        if (!Active)
        {
            Reply(caller, "status.none");
            return;
        }

        var handler = _handler >= 0 ? Utilities.GetPlayerFromSlot(_handler)?.PlayerName : null;
        Reply(caller, "status.active", (int)(_state!.CourageFraction * 100), handler ?? "-");
    }

    [ConsoleCommand("css_courier_spawn", "(testing) spawn the courier where you stand, taking the bomb off everyone")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSpawnCommand(CCSPlayerController? caller, CommandInfo info)
    {
        if (caller == null || !AdminManager.PlayerHasPermissions(caller, Config.AdminPermissions.ToArray()))
        {
            Reply(caller, "admin.denied");
            return;
        }

        if (AlivePawn(caller) is not { } pawn)
        {
            Reply(caller, "admin.alive");
            return;
        }

        SetupRound(Behind(pawn, -60f));
        Reply(caller, Active ? "admin.spawned" : "admin.no_sites");
    }

    // ================================================================ helpers

    private static IEnumerable<CCSPlayerController> Players() =>
        Utilities.GetPlayers().Where(p => p is { IsValid: true, IsHLTV: false, Connected: PlayerConnectedState.Connected });

    private static bool OnTeam(CCSPlayerController p) => p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist;

    private static CCSPlayerPawn? AlivePawn(CCSPlayerController? p)
    {
        if (p is not { IsValid: true, PawnIsAlive: true }) return null;
        var pawn = p.PlayerPawn.Value;
        return pawn is { IsValid: true, AbsOrigin: not null } && pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE ? pawn : null;
    }

    private static bool Warmup()
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

    private static CCSPlayerController? AttackerOf(CTakeDamageInfo info)
    {
        try
        {
            var ent = info.Attacker.Value;
            if (ent == null || !ent.IsValid) return null;
            if (ent.DesignerName != "player")
            {
                ent = ent.OwnerEntity.Value;
                if (ent == null || !ent.IsValid || ent.DesignerName != "player") return null;
            }

            var pawn = new CCSPlayerPawn(ent.Handle);
            return pawn.OriginalController.Value ?? pawn.Controller.Value?.As<CCSPlayerController>();
        }
        catch
        {
            return null;
        }
    }

    private static void SetLeader(CChicken c, CCSPlayerPawn? pawn)
    {
        var raw = pawn is { IsValid: true } ? pawn.EntityHandle.Raw : uint.MaxValue;
        if (Schema.GetSchemaValue<uint>(c.Handle, "CChicken", "m_leader") != raw) Schema.SetSchemaValue(c.Handle, "CChicken", "m_leader", raw);
    }

    private static void Reward(CCSPlayerController p, int money)
    {
        if (money <= 0 || p.InGameMoneyServices is not { } m) return;
        m.Account = Math.Min(16000, m.Account + money);
        Utilities.SetStateChanged(p, "CCSPlayerController", "m_pInGameMoneyServices");
    }

    private static void Scale(CBaseModelEntity e, float scale)
    {
        if (Math.Abs(scale - 1f) < 0.001f) return;
        try
        {
            if (e.CBodyComponent?.SceneNode is not { } node) return;
            node.Scale = scale;
            Utilities.SetStateChanged(e, "CBaseEntity", "m_CBodyComponent");
        }
        catch
        {
            // visual only
        }
    }

    private static void NoBlock(CBaseEntity e)
    {
        try
        {
            if (e.Collision is not { } col) return;
            col.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
            if (col.CollisionAttribute is { } attr) attr.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
            Utilities.SetStateChanged(e, "CCollisionProperty", "m_CollisionGroup");
        }
        catch
        {
            // best effort
        }
    }

    private static void Remove(CEntityInstance? e)
    {
        try
        {
            if (e is { IsValid: true }) e.Remove();
        }
        catch
        {
            // already gone
        }
    }

    private void Burst(Vector at)
    {
        try
        {
            var fx = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
            if (fx == null) return;
            fx.Teleport(at, new QAngle(0, 0, 0), new Vector(0, 0, 0));
            var kv = new CEntityKeyValues();
            kv.SetString("effect_name", Fx.Feathers);
            kv.SetBool("start_active", true);
            fx.DispatchSpawn(kv);
            fx.AcceptInput("Start");
            AddTimer(2.5f, () =>
            {
                if (fx.IsValid) fx.Remove();
            });
        }
        catch
        {
            // cosmetic
        }
    }

    private static Point P(Vector v) => new(v.X, v.Y, v.Z);

    private static float Dist(Vector a, Vector b) => P(a).DistanceTo(P(b));

    private static Vector Behind(CCSPlayerPawn pawn, float dist)
    {
        var o = pawn.AbsOrigin!;
        var yaw = (pawn.EyeAngles?.Y ?? 0f) * MathF.PI / 180f;
        return new Vector(o.X - MathF.Cos(yaw) * dist, o.Y - MathF.Sin(yaw) * dist, o.Z + 8f);
    }

    private string Prefix => Lang.Colorize(Config.ChatPrefix);

    private void Tell(CCSPlayerController? p, string key, params object?[] args)
    {
        if (p is not { IsValid: true } || p.IsBot) return;
        p.PrintToChat($" {Prefix} {_lang.T(p, key, args)}");
    }

    private void TellTeam(CsTeam team, string key, params object?[] args)
    {
        foreach (var p in Players().Where(p => p.Team == team)) Tell(p, key, args);
    }

    private void Reply(CCSPlayerController? p, string key, params object?[] args)
    {
        if (p == null) Server.PrintToConsole(Lang.StripTags(_lang.Raw("en", key, args)));
        else Tell(p, key, args);
    }

    private static class Fx
    {
        public const string Feathers = "particles/critters/chicken/chicken_gone_feathers.vpcf";
        public const string Burst = "particles/critters/chicken/chicken_impact_burst2.vpcf";
    }
}
