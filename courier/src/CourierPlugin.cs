using Color = System.Drawing.Color;
using ChickenCourier.Logic;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
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
/// On deathmatch servers it runs Delivery mode instead: every so often a courier appears with a sack of feed, and whoever
/// walks it to the drop zone scores. Pauses and timeouts freeze it, so it sits quietly on PUG servers.
/// </summary>
[MinimumApiVersion(300)]
public sealed class CourierPlugin : BasePlugin, IPluginConfig<CourierConfig>
{
    public override string ModuleName => "Chicken Courier";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Gdaki";
    public override string ModuleDescription => "A standalone CS2 mode inspired by ChickenCoop: a chicken carries the C4 (or, in deathmatch, a delivery). Co-authored with Claude (Anthropic).";

    private const float TickSeconds = 0.2f;
    private const string C4Model = "weapons/models/c4/weapon_c4.vmdl";

    public CourierConfig Config { get; set; } = new();

    private readonly Lang _lang = new();
    private readonly List<Site> _sites = new();

    private CChicken? _chicken;
    private CBaseModelEntity? _cargo;
    private CPointWorldText? _marker;
    private CourierMode _mode;
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

    // Delivery mode
    private Site? _dropZone;
    private CPointWorldText? _zoneMarker;
    private float _deadline;
    private float _nextDelivery; // 0 = none scheduled
    private float _nextZoneFx;

    // Pauses, timeouts and freeze time
    private float? _heldSince;
    private CCSGameRulesProxy? _rules;

    public void OnConfigParsed(CourierConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        _lang.Load(Path.Combine(ModuleDirectory, "lang"), Config.Language);
        RegisterListener<Listeners.OnServerPrecacheResources>(m =>
        {
            m.AddResource(Config.Model);
            m.AddResource(C4Model);
            m.AddResource(Config.CargoModel);
            m.AddResource(Fx.Feathers);
            m.AddResource(Fx.Burst);
        });
        RegisterListener<Listeners.OnPlayerButtonsChanged>(OnButtons);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(OnDamage);
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            Clear();
            _rules = null;
        });
        RegisterEventHandler<EventRoundPrestart>((_, _) =>
        {
            _round++;
            Clear();
            return HookResult.Continue;
        });
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventBombPlanted>((_, _) =>
        {
            if (_mode == CourierMode.Bomb) RemoveCourier(true);
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
        if (ResolvedMode() == CourierMode.Delivery)
        {
            // Deathmatch: no courier at round start, the first one turns up a little later.
            _nextDelivery = Server.CurrentTime + (float)Math.Min(15, Config.DeliveryEverySeconds);
            return HookResult.Continue;
        }

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

    /// <summary>Bomb or Delivery, from the config's Mode and (for "auto") the server's game_type / game_mode.</summary>
    private CourierMode ResolvedMode()
    {
        int Cvar(string name)
        {
            try
            {
                return ConVar.Find(name)?.GetPrimitiveValue<int>() ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        return CourierState.ResolveMode(Config.Mode, Cvar("game_type"), Cvar("game_mode"));
    }

    /// <param name="at">Spawn here (admin test) instead of next to the bomb carrier.</param>
    private void SetupRound(Vector? at)
    {
        RemoveCourier(true);
        if (at == null && !ModeOn(out _)) return;
        LoadSites();
        if (_sites.Count == 0) return;

        // Take the bomb off whoever got it, and spawn the courier next to them.
        var carriers = Players().Where(p => p.Team == CsTeam.Terrorist && AlivePawn(p) is { } pawn && HasC4(pawn)).ToList();
        if (at == null && Config.RequireC4 && carriers.Count == 0) return; // knife round, retakes, a pug plugin's bomb-less round

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

        _mode = CourierMode.Bomb;
        SpawnCourier(spot, C4Model, 0.75f);
        if (!Active) return;
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

    /// <summary>Spawns the chicken with <paramref name="cargoModel"/> on its back. Set <see cref="_mode"/> first.</summary>
    private void SpawnCourier(Vector at, string cargoModel, float cargoScale)
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
            _state = new CourierState(Config, _mode == CourierMode.Delivery ? Config.DeliverySeconds : null);
            _handler = -1;
            _noHandlerSince = Server.CurrentTime;
            _lastProgressAt = Server.CurrentTime;
            _nextCluck = Server.CurrentTime + 2f;

            // The bomb (or the delivery) strapped to its back, and a floating marker everyone can see.
            var cargo = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (cargo != null)
            {
                cargo.Spawnflags = 256;
                var roll = cargoModel == C4Model ? 90 : 0;
                cargo.Teleport(new Vector(at.X, at.Y, at.Z + 17f * Config.Scale), new QAngle(0, c.AbsRotation?.Y ?? 0, roll), new Vector(0, 0, 0));
                var bkv = new CEntityKeyValues();
                bkv.SetString("model", cargoModel);
                bkv.SetInt("solid", 0);
                cargo.DispatchSpawn(bkv);
                Scale(cargo, cargoScale);
                cargo.AcceptInput("SetParent", c, cargo, "!activator");
                _cargo = cargo;
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
        var t = WorldText(c.AbsOrigin!, 40f * Config.Scale, MarkerText(), 48f, Color.FromArgb(255, 255, 140, 40));
        if (t == null) return null;
        t.AcceptInput("SetParent", c, t, "!activator");
        _shownMarker = t.MessageText;
        return t;
    }

    private static CPointWorldText? WorldText(Vector o, float up, string text, float size, Color color)
    {
        try
        {
            var t = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
            if (t == null) return null;
            t.MessageText = text;
            t.Enabled = true;
            t.FontSize = size;
            t.WorldUnitsPerPx = 0.14f;
            t.Fullbright = true;
            t.DrawBackground = false;
            t.Color = color;
            t.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
            t.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_BOTTOM;
            t.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_AROUND_UP;
            t.Teleport(new Vector(o.X, o.Y, o.Z + up), new QAngle(0, 0, 90), new Vector(0, 0, 0));
            t.DispatchSpawn();
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
        var icon = _mode == CourierMode.Delivery ? "📦" : "💣";
        if (_heldSince != null && Paused()) return $"{icon} COURIER · PAUSED";
        if (_state.Panicking(now)) return $"{icon} COURIER · PANIC!";
        if (_state.Laying) return $"{icon} {(_mode == CourierMode.Delivery ? "DELIVERING" : "LAYING")} {CourierState.Bar(_state.LayFraction)}";
        return $"{icon} COURIER {CourierState.Bar(_state.CourageFraction)}";
    }

    // ================================================================ delivery mode (deathmatch)

    private void TrySpawnDelivery()
    {
        var now = Server.CurrentTime;
        if (!ModeOn(out var why))
        {
            // Off or the wrong map: wait for the next round. Warmup or too few players: check again shortly.
            _nextDelivery = why is "players" or "warmup" ? now + 10f : 0;
            return;
        }

        if (ResolvedMode() != CourierMode.Delivery)
        {
            _nextDelivery = 0;
            return;
        }

        SpawnDelivery(null);
    }

    /// <param name="at">Spawn here (admin test) instead of at a random spawn point.</param>
    private void SpawnDelivery(Vector? at)
    {
        RemoveCourier(true);
        var now = Server.CurrentTime;
        _nextDelivery = now + 30f; // retry if anything below fails
        var points = SpawnPoints();
        if (at == null && points.Count == 0) return;
        var from = at != null ? P(at) : points[Random.Shared.Next(points.Count)];
        if (CourierState.PickDropZone(points, from, Config.DropZoneMinDistance, Random.Shared) is not { } zone) return;

        _mode = CourierMode.Delivery;
        SpawnCourier(at ?? new Vector(from.X, from.Y, from.Z + 8f), Config.CargoModel, 1f);
        if (!Active) return;
        _nextDelivery = 0;
        _dropZone = new Site("drop", zone);
        _deadline = now + (float)Config.DeliveryTimeLimit;
        _nextZoneFx = now;
        _zoneMarker = WorldText(new Vector(zone.X, zone.Y, zone.Z), 72f, "📦 DROP ZONE", 72f, Color.FromArgb(255, 90, 230, 90));
        TellAll("delivery.spawn", Config.DeliveryScore);
    }

    /// <summary>Deathmatch spawns (or the team spawns on maps without them): where couriers appear and drop zones go.</summary>
    private static List<Point> SpawnPoints()
    {
        foreach (var names in new[] { new[] { "info_deathmatch_spawn" }, new[] { "info_player_terrorist", "info_player_counterterrorist" } })
        {
            var list = names.SelectMany(n => Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(n))
                .Where(e => e.IsValid && e.AbsOrigin != null)
                .Select(e => P(e.AbsOrigin!))
                .ToList();
            if (list.Count >= 2) return list;
        }

        return new List<Point>();
    }

    /// <summary>Delivered: points on the scoreboard, a heal, armour and money for whoever walked it in.</summary>
    private void Deliver(CCSPlayerController? handler)
    {
        if (!Active) return;
        var at = _chicken!.AbsOrigin!;
        _chicken.EmitSound("Chicken.FlapWings");
        Burst(at);
        if (handler is { IsValid: true })
        {
            if (Config.DeliveryScore != 0)
            {
                handler.Score += Config.DeliveryScore;
                Utilities.SetStateChanged(handler, "CCSPlayerController", "m_iScore");
            }

            if (AlivePawn(handler) is { } pawn)
            {
                if (Config.DeliveryHeal && pawn.Health < 100)
                {
                    pawn.Health = 100;
                    Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
                }

                if (Config.DeliveryArmor && pawn.ArmorValue < 100)
                {
                    pawn.ArmorValue = 100;
                    Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
                }
            }

            Reward(handler, Config.DeliveryMoney);
            TellAll("delivery.done", handler.PlayerName, Config.DeliveryScore);
        }

        RemoveCourier(false);
    }

    // ================================================================ every tick

    private void Tick()
    {
        var now = Server.CurrentTime;

        // Pauses, timeouts, freeze time: the courier sits still and every clock stops.
        if (Held())
        {
            _heldSince ??= now;
            if (Active)
            {
                SetLeader(_chicken!, null);
                _chicken!.DesiredActivity = EChickenActivity.Idle;
                UpdateMarker();
            }

            return;
        }

        if (_heldSince is { } since)
        {
            _heldSince = null;
            Resume(now - since);
        }

        if (!Active)
        {
            if (_nextDelivery > 0 && now >= _nextDelivery) TrySpawnDelivery();
            return;
        }

        var c = _chicken!;
        var st = _state!;
        var at = c.AbsOrigin;
        if (at == null) return;
        var delivery = _mode == CourierMode.Delivery;

        if (delivery && now >= _deadline)
        {
            TellAll("delivery.timeout");
            RemoveCourier(false);
            return;
        }

        // Handler: still valid? close enough?
        var handler = _handler >= 0 ? Utilities.GetPlayerFromSlot(_handler) : null;
        var hPawn = AlivePawn(handler);
        if (_handler >= 0 && (handler is not { IsValid: true } || (!delivery && handler.Team != CsTeam.Terrorist) || hPawn == null
                              || Dist(hPawn.AbsOrigin!, at) > Config.LeashRange))
        {
            var name = handler?.PlayerName;
            _handler = -1;
            _noHandlerSince = now;
            handler = null;
            hPawn = null;
            if (delivery) TellAll("delivery.dropped", name);
            else TellTeam(CsTeam.Terrorist, "handler.lost", name);
        }

        // Bomb mode, nobody leading it: after a while (or straight away with only bots) the nearest Terrorist takes over.
        // Delivery mode has no auto handler: it's a race.
        if (_handler < 0 && !delivery)
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

        // Laying (delivering): inside a bombsite (the drop zone) with the handler close by.
        var site = delivery
            ? (_dropZone is { } zone && zone.Contains(P(at), Config.DropZoneRadius) ? zone : null)
            : _sites.FirstOrDefault(s => s.Contains(P(at), Config.SiteRadius));
        var handlerClose = hPawn != null && Dist(hPawn.AbsOrigin!, at) <= Config.LayHandlerRange;
        foreach (var e in st.Tick(TickSeconds, now, site?.Name, handlerClose))
        {
            switch (e)
            {
                case CourierEvent.LayStarted when delivery:
                    TellAll("delivery.delivering", handler?.PlayerName);
                    c.EmitSound("Chicken.Interact");
                    break;
                case CourierEvent.LayStarted:
                    TellTeam(CsTeam.Terrorist, "lay.start.t", site!.Name);
                    TellTeam(CsTeam.CounterTerrorist, "lay.start.ct", site!.Name);
                    c.EmitSound("c4.initiate");
                    break;
                case CourierEvent.LayInterrupted when !delivery:
                    TellTeam(CsTeam.Terrorist, "lay.stopped");
                    break;
                case CourierEvent.Calmed when !delivery:
                    TellTeam(CsTeam.Terrorist, "calm");
                    break;
                case CourierEvent.Laid when delivery:
                    Deliver(handler);
                    return;
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

        if (delivery && _dropZone is { } dz && now >= _nextZoneFx)
        {
            _nextZoneFx = now + 3f;
            Burst(new Vector(dz.Centre.X, dz.Centre.Y, dz.Centre.Z + 16f));
        }

        UpdateMarker();
        if (Config.CentreHud && handler is { IsValid: true, IsBot: false } && now >= _nextHud)
        {
            if (st.Laying)
            {
                _nextHud = now + 0.5f;
                handler.PrintToCenterHtml(
                    $"<font color='#FF8C28'><b>{_lang.P(handler, delivery ? "hud.delivering" : "hud.laying")}</b></font><br>" +
                    $"<font color='#FFD24A'>{CourierState.Bar(st.LayFraction, 12)}</font> {(int)(st.LayFraction * 100)}%", 1);
            }
            else if (delivery && _dropZone is { } z)
            {
                // Which way to go: distance to the drop zone and the time left.
                _nextHud = now + 0.5f;
                var metres = (int)(P(at).DistanceTo(z.Centre) * 0.0254f);
                handler.PrintToCenterHtml(
                    $"<font color='#5AE65A'><b>{_lang.P(handler, "hud.dropzone", metres, (int)Math.Max(0, _deadline - now))}</b></font>", 1);
            }
        }
    }

    /// <summary>After a pause: push every clock on by its length so nothing ran out while the game stood still.</summary>
    private void Resume(float seconds)
    {
        _state?.Shift(seconds);
        _noHandlerSince += seconds;
        _lastProgressAt += seconds;
        if (_deadline > 0) _deadline += seconds;
        if (_nextDelivery > 0) _nextDelivery += seconds;
        foreach (var slot in _scareReady.Keys.ToList()) _scareReady[slot] += seconds;
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
        if (_heldSince != null) return;

        if (_mode == CourierMode.Delivery)
        {
            // Anyone can grab it; with StealWithE, even out from under its handler.
            if (d > Config.HandlerRange || _handler == player.Slot) return;
            if (_state!.Panicking(now))
            {
                player.PrintToCenter(_lang.P(player, "handler.panicking"));
                return;
            }

            if (_handler >= 0 && !Config.StealWithE) return;
            _handler = player.Slot;
            _lastProgressAt = now;
            foreach (var p in Players()) Tell(p, p.Slot == player.Slot ? "delivery.you" : "delivery.handler", player.PlayerName);
            _chicken.EmitSound("Chicken.Interact");
            return;
        }

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
        if (_heldSince != null) return HookResult.Handled;
        var attacker = AttackerOf(info);
        if (_mode == CourierMode.Delivery)
        {
            DeliveryHit(attacker, info.Damage);
            return HookResult.Handled;
        }

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

    /// <summary>Delivery mode: anyone but the handler (and, outside free-for-all, their team) can spook it loose.</summary>
    private void DeliveryHit(CCSPlayerController? attacker, float damage)
    {
        if (attacker is not { IsValid: true } || attacker.Slot == _handler || _state!.Laid) return;
        var handler = _handler >= 0 ? Utilities.GetPlayerFromSlot(_handler) : null;
        if (handler is { IsValid: true } && handler.Team == attacker.Team && !FreeForAll()) return;

        var now = Server.CurrentTime;
        _chicken!.EmitSound("Chicken.Panic");
        if (!_state.Hit(damage, now)) return;
        if (AlivePawn(attacker) is { } pawn) Flee(pawn);
        _handler = -1;
        _noHandlerSince = now;
        Reward(attacker, Config.PanicReward);
        TellAll("delivery.spooked", attacker.PlayerName);
        if (_chicken.AbsOrigin is { } at) Burst(at);
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
        Remove(_cargo);
        Remove(_marker);
        Remove(_zoneMarker);
        _cargo = null;
        _marker = null;
        _zoneMarker = null;
        _dropZone = null;
        _deadline = 0;
        var c = _chicken;
        _chicken = null;
        _state = null;
        _handler = -1;
        if (c is not { IsValid: true }) return;
        if (_mode == CourierMode.Delivery) _nextDelivery = Server.CurrentTime + (float)Config.DeliveryEverySeconds;
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
        _nextDelivery = 0;
        _heldSince = null;
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
            if (!Config.Enabled) Clear();
            else if (!Active && ResolvedMode() == CourierMode.Delivery) _nextDelivery = Server.CurrentTime + 10f;
            Reply(caller, Config.Enabled ? "admin.on" : "admin.off");
            return;
        }

        if (!ModeOn(out var why))
        {
            Reply(caller, "status.inactive." + why);
            return;
        }

        var mode = Active ? _mode : ResolvedMode();
        Reply(caller, mode == CourierMode.Delivery ? "status.mode.delivery" : "status.mode.bomb");
        if (_heldSince != null) Reply(caller, "status.paused");
        if (!Active)
        {
            if (mode == CourierMode.Delivery && _nextDelivery > 0)
                Reply(caller, "status.next", (int)Math.Max(0, _nextDelivery - Server.CurrentTime));
            else Reply(caller, "status.none");
            return;
        }

        var handler = _handler >= 0 ? Utilities.GetPlayerFromSlot(_handler)?.PlayerName : null;
        Reply(caller, "status.active", (int)(_state!.CourageFraction * 100), handler ?? "-");
    }

    [ConsoleCommand("css_courier_spawn", "(testing) spawn a courier where you stand (bomb mode takes the bomb off everyone)")]
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

        if (ResolvedMode() == CourierMode.Delivery)
        {
            SpawnDelivery(Behind(pawn, -60f));
            Reply(caller, Active ? "admin.spawned.delivery" : "admin.no_spawns");
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

    private CCSGameRules? Rules()
    {
        try
        {
            if (_rules is not { IsValid: true })
                _rules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
            return _rules?.GameRules;
        }
        catch
        {
            return null;
        }
    }

    private bool Warmup() => Rules()?.WarmupPeriod ?? false;

    /// <summary>The courier stands still: freeze time always, pauses and timeouts with PauseAware.</summary>
    private bool Held() => (Rules()?.FreezePeriod ?? false) || Paused();

    /// <summary>A tactical or technical pause (PUG servers), when PauseAware is on.</summary>
    private bool Paused()
    {
        var r = Rules();
        return Config.PauseAware && r != null
            && (r.MatchWaitingForResume || r.TechnicalTimeOut || r.TerroristTimeOutActive || r.CTTimeOutActive);
    }

    private static bool FreeForAll()
    {
        try
        {
            return ConVar.Find("mp_teammates_are_enemies")?.GetPrimitiveValue<bool>() ?? false;
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

    private void TellAll(string key, params object?[] args)
    {
        foreach (var p in Players()) Tell(p, key, args);
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
