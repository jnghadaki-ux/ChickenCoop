using ChickenCoop.Logic;
using ChickenCoop.Menus;
using ChickenCoop.Model;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ChickenCoop;

/// <summary>
/// Coop Arena: two players' pets fight a turn-based duel in a small ring in front of the challenger. Both owners pick a
/// move each turn from a menu (Peck, Guard, Flap, Special) while the real chickens act it out with the stock pet
/// effects. Optional feed-bag wagers, and spectators can bet on the winner.
/// </summary>
public sealed partial class ChickenCoopPlugin
{
    private enum DuelPhase { Betting, Choosing, Resolving, Over }

    private sealed class Challenge
    {
        public int From;
        public int To;
        public int Wager;
        public float Expires;
    }

    private sealed class Duel
    {
        public int SlotA;
        public int SlotB;
        public ulong IdA;
        public ulong IdB;
        public required Fighter A;
        public required Fighter B;
        public CChicken? ChickenA;
        public CChicken? ChickenB;
        public CPointWorldText? TagA;
        public CPointWorldText? TagB;
        public Vector SpotA = new(0, 0, 0);
        public Vector SpotB = new(0, 0, 0);
        public float YawA;
        public float YawB;
        public int Wager;
        public int Turn;
        public DuelPhase Phase;
        public float PhaseEnds;
        public ArenaMove? MoveA;
        public ArenaMove? MoveB;
        public readonly Dictionary<ulong, (int Side, int Stake)> Bets = new();
        public readonly Random Rng = new();
    }

    private readonly List<Challenge> _challenges = new();
    private readonly List<Duel> _duels = new();
    private readonly HashSet<uint> _arenaChickens = new();
    private readonly Dictionary<ulong, float> _duelCooldown = new();

    private Duel? DuelOf(int slot) => _duels.FirstOrDefault(d => d.Phase != DuelPhase.Over && (d.SlotA == slot || d.SlotB == slot));

    private bool InDuel(CCSPlayerController p) => DuelOf(p.Slot) != null;

    private PetStage ArenaMinStage =>
        Enum.TryParse<PetStage>(Config.Arena.MinStage, true, out var s) && s >= PetStage.Chick ? s : PetStage.Chick;

    // ================================================================ challenges

    /// <summary>Why this player can't duel right now (a message key), or null.</summary>
    private string? DuelBlocker(CCSPlayerController p, bool self)
    {
        var pet = Data(p);
        if (!Config.Arena.Enabled) return "arena.off";
        if (pet == null || !pet.IsHatched || pet.Stage < ArenaMinStage) return self ? "arena.need_pet" : "arena.they_need_pet";
        if (InDuel(p)) return self ? "arena.busy" : "arena.they_busy";
        if (_duelCooldown.TryGetValue(p.SteamID, out var until) && Server.CurrentTime < until) return self ? "arena.cooldown" : "arena.they_cooldown";
        switch ((Config.Arena.When ?? "anytime").ToLowerInvariant())
        {
            case "dead" when AlivePawn(p) != null: return "arena.when_dead";
            case "warmup" when !Warmup(): return "arena.when_warmup";
        }

        return null;
    }

    internal void OpenArena(CCSPlayerController p) => Sub(p, BuildArena, BuildMain);

    private CoopMenu BuildArena(CCSPlayerController p)
    {
        var pet = Data(p)!;
        var s = pet.Stats;
        var menu = new CoopMenu(M(p, "arena.title"), M(p, "arena.sub", s.ArenaWins, s.ArenaLosses, s.ArenaDraws));
        var incoming = _challenges.FirstOrDefault(c => c.To == p.Slot && c.Expires > Server.CurrentTime);
        if (incoming != null && Utilities.GetPlayerFromSlot(incoming.From) is { IsValid: true } from)
        {
            menu.Add(C(M(p, "arena.m.accept", MenuService.Esc(from.PlayerName)), Green), pl => AcceptChallenge(pl));
            menu.Add(C(M(p, "arena.m.decline"), Red), pl => DeclineChallenge(pl));
        }

        if (pet.IsHatched)
        {
            var f = ArenaLogic.FromPet(pet);
            menu.Info(M(p, "arena.m.stats", MenuService.Esc(f.Name), f.MaxHp, f.Attack, Catalog.FindEmote(f.SpecialFx)?.Name ?? f.SpecialFx));
        }

        menu.Add(Arrow(M(p, "arena.m.challenge")), pl => Sub(pl, BuildChallengeList, BuildArena), DuelBlocker(p, true) == null);
        var live = _duels.Where(d => d.Phase == DuelPhase.Betting).ToList();
        if (Config.Arena.Betting && live.Count > 0) menu.Add(Arrow(M(p, "arena.m.bet")), pl => Sub(pl, BuildBetDuels, BuildArena));
        menu.Info(C(M(p, "arena.m.rules1"), Grey));
        menu.Info(C(M(p, "arena.m.rules2"), Grey));
        return menu;
    }

    private CoopMenu BuildChallengeList(CCSPlayerController p)
    {
        var menu = new CoopMenu(M(p, "arena.m.challenge"), M(p, "arena.m.pick"));
        var any = false;
        foreach (var t in Players().Where(t => t.Slot != p.Slot && (!t.IsBot || Config.BotsGetPets)).OrderBy(t => t.PlayerName))
        {
            if (Data(t) is not { IsHatched: true } tp || tp.Stage < ArenaMinStage) continue;
            any = true;
            var target = t;
            var blocked = DuelBlocker(t, false) != null;
            menu.Add($"{MenuService.Esc(t.PlayerName)} {C("· " + MenuService.Esc(tp.Name) + ", " + M(p, "stage." + tp.Stage.ToString().ToLowerInvariant()), Grey)}",
                pl => Sub(pl, x => BuildWager(x, target), BuildChallengeList), !blocked);
        }

        if (!any) menu.Info(C(M(p, "arena.m.nobody"), Grey));
        return menu;
    }

    private CoopMenu BuildWager(CCSPlayerController p, CCSPlayerController target)
    {
        var pet = Data(p)!;
        var menu = new CoopMenu(M(p, "arena.m.wager"), M(p, "arena.m.wager.sub", MenuService.Esc(target.PlayerName), pet.FeedBags));
        menu.Add(M(p, "arena.m.wager.none"), pl => SendChallenge(pl, target, 0));
        for (var w = 1; w <= Math.Max(0, Config.Arena.MaxWager); w++)
        {
            var wager = w;
            menu.Add(M(p, "arena.m.wager.bags", w), pl => SendChallenge(pl, target, wager), pet.FeedBags >= w);
        }

        return menu;
    }

    private void SendChallenge(CCSPlayerController p, CCSPlayerController target, int wager)
    {
        if (!target.IsValid) return;
        if (DuelBlocker(p, true) is { } mine)
        {
            Tell(p, mine);
            return;
        }

        if (DuelBlocker(target, false) is { } theirs)
        {
            Tell(p, theirs, target.PlayerName);
            return;
        }

        _challenges.RemoveAll(c => c.From == p.Slot || c.To == target.Slot);
        _challenges.Add(new Challenge { From = p.Slot, To = target.Slot, Wager = wager, Expires = Server.CurrentTime + Config.Arena.RequestSeconds });
        var myPet = Data(p)!;
        Tell(p, "arena.sent", target.PlayerName, Config.Arena.RequestSeconds);
        if (target.IsBot)
        {
            AddTimer(1.5f, () =>
            {
                if (target.IsValid) AcceptChallenge(target);
            }, TimerFlags.STOP_ON_MAPCHANGE);
            return;
        }

        Tell(target, wager > 0 ? "arena.received.wager" : "arena.received", p.PlayerName, myPet.Name, wager);
        PlayTo(target, Sounds.Popup);
        BuildChallengePrompt(target, p, wager).Open(target);
    }

    private CoopMenu BuildChallengePrompt(CCSPlayerController target, CCSPlayerController from, int wager)
    {
        var their = Data(from)!;
        var menu = new CoopMenu(M(target, "arena.title"),
            M(target, wager > 0 ? "arena.prompt.wager" : "arena.prompt", MenuService.Esc(from.PlayerName), MenuService.Esc(their.Name), wager));
        menu.Add(C(M(target, "arena.m.accept.short"), Green), pl => AcceptChallenge(pl));
        menu.Add(C(M(target, "arena.m.decline"), Red), pl => DeclineChallenge(pl));
        return menu;
    }

    internal void AcceptChallenge(CCSPlayerController p)
    {
        var c = _challenges.FirstOrDefault(x => x.To == p.Slot && x.Expires > Server.CurrentTime);
        if (c == null)
        {
            Tell(p, "arena.no_challenge");
            return;
        }

        _challenges.Remove(c);
        var from = Utilities.GetPlayerFromSlot(c.From);
        if (from is not { IsValid: true })
        {
            Tell(p, "arena.gone");
            return;
        }

        foreach (var (who, self) in new[] { (from, true), (p, true) })
        {
            if (DuelBlocker(who, self) is { } why)
            {
                Tell(from, why, who.PlayerName);
                Tell(p, why, who.PlayerName);
                return;
            }
        }

        var pa = Data(from)!;
        var pb = Data(p)!;
        if (pa.FeedBags < c.Wager || pb.FeedBags < c.Wager)
        {
            Tell(from, "arena.wager_short", c.Wager);
            Tell(p, "arena.wager_short", c.Wager);
            return;
        }

        StartDuel(from, p, c.Wager);
    }

    internal void DeclineChallenge(CCSPlayerController p)
    {
        var c = _challenges.FirstOrDefault(x => x.To == p.Slot);
        if (c == null) return;
        _challenges.Remove(c);
        Tell(p, "arena.declined.you");
        Tell(Utilities.GetPlayerFromSlot(c.From), "arena.declined", p.PlayerName);
    }

    // ================================================================ the duel

    private void StartDuel(CCSPlayerController a, CCSPlayerController b, int wager)
    {
        var pa = Data(a)!;
        var pb = Data(b)!;
        // The ring: in front of the challenger, the two fighters facing each other.
        var anchor = a.PlayerPawn.Value;
        var origin = anchor?.AbsOrigin;
        if (anchor == null || origin == null)
        {
            Tell(a, "arena.no_room");
            return;
        }

        var yaw = anchor.EyeAngles?.Y ?? 0f;
        var f = Forward(yaw);
        var r = Forward(yaw - 90f);
        var half = Math.Clamp(Config.Arena.RingSize, 40f, 300f) / 2f;
        var centre = new Vector(origin.X + f.X * 110f, origin.Y + f.Y * 110f, origin.Z + 4f);

        var duel = new Duel
        {
            SlotA = a.Slot,
            SlotB = b.Slot,
            IdA = a.SteamID,
            IdB = b.SteamID,
            A = ArenaLogic.FromPet(pa),
            B = ArenaLogic.FromPet(pb),
            Wager = wager,
            SpotA = new Vector(centre.X - r.X * half, centre.Y - r.Y * half, centre.Z),
            SpotB = new Vector(centre.X + r.X * half, centre.Y + r.Y * half, centre.Z),
            YawA = yaw - 90f,
            YawB = yaw + 90f,
        };

        if (wager > 0)
        {
            pa.FeedBags -= wager;
            pb.FeedBags -= wager;
            _store.MarkDirty();
        }

        // The owners' following pets step aside while their arena selves fight.
        Suppress(a, true);
        Suppress(b, true);
        duel.ChickenA = SpawnFighter(pa, duel.SpotA, duel.YawA);
        duel.ChickenB = SpawnFighter(pb, duel.SpotB, duel.YawB);
        duel.TagA = FighterTag(duel.ChickenA, duel.A);
        duel.TagB = FighterTag(duel.ChickenB, duel.B);
        Fx(Particles.Confetti, Up(centre, 10), 3f);
        _duels.Add(duel);

        var betting = Config.Arena.Betting && Config.Arena.BetSeconds > 0;
        duel.Phase = betting ? DuelPhase.Betting : DuelPhase.Choosing;
        duel.PhaseEnds = Server.CurrentTime + (betting ? Config.Arena.BetSeconds : 1f);
        if (Config.Arena.Announce)
            TellAll(betting ? "arena.start.bet" : "arena.start", a.PlayerName, duel.A.Name, b.PlayerName, duel.B.Name, Config.Arena.BetSeconds);
        else
            foreach (var x in new[] { a, b }) Tell(x, "arena.start", a.PlayerName, duel.A.Name, b.PlayerName, duel.B.Name, 0);
    }

    private CChicken? SpawnFighter(PetData pet, Vector at, float yaw)
    {
        try
        {
            var model = Catalog.ModelFor(pet.Stage, pet.Breed);
            var c = Utilities.CreateEntityByName<CChicken>("chicken");
            if (c == null) return null;
            c.Teleport(at, new QAngle(0, yaw, 0), new Vector(0, 0, 0));
            var kv = new CEntityKeyValues();
            kv.SetString("chicken_model", model);
            c.DispatchSpawn(kv);
            if (!string.Equals(ModelOf(c), model, StringComparison.OrdinalIgnoreCase)) c.SetModel(model);
            c.MaxHealth = 10000;
            c.Health = 10000;
            SetScale(c, StageScale(pet.Stage));
            ApplyCoat(c, pet);
            ApplyStyle(c, pet, PetLogic.EffectiveStyle(pet), Random.Shared.Next(360));
            NoBlock(c);
            _arenaChickens.Add(c.Index);
            if (WornHat(pet) is { } hatId) SpawnHat(c, Catalog.FindHat(hatId)!, pet.Stage, StageScale(pet.Stage));
            Fx(Particles.Feathers, at, 2f);
            return c;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Chicken Coop could not spawn an arena fighter");
            return null;
        }
    }

    private CPointWorldText? FighterTag(CChicken? c, Fighter f)
    {
        if (c is not { IsValid: true }) return null;
        try
        {
            var origin = c.AbsOrigin!;
            var t = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
            if (t == null) return null;
            t.MessageText = TagText(f);
            t.Enabled = true;
            t.FontSize = 44f;
            t.WorldUnitsPerPx = 0.12f;
            t.Fullbright = true;
            t.DrawBackground = false;
            t.Color = System.Drawing.Color.FromArgb(255, 255, 210, 74);
            t.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
            t.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_BOTTOM;
            t.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_AROUND_UP;
            t.Teleport(Up(origin, 34f), new QAngle(0, 0, 90), new Vector(0, 0, 0));
            t.DispatchSpawn();
            t.AcceptInput("SetParent", c, t, "!activator");
            return t;
        }
        catch
        {
            return null;
        }
    }

    private static string TagText(Fighter f) => $"{f.Name}  {f.Hp}/{f.MaxHp}" + (f.Charge >= ArenaLogic.ChargeNeeded ? "  ★" : "");

    private static void SetTag(CPointWorldText? t, Fighter f)
    {
        if (t is not { IsValid: true }) return;
        t.MessageText = TagText(f);
        Utilities.SetStateChanged(t, "CPointWorldText", "m_messageText");
    }

    /// <summary>Four times a second: expire challenges, run the betting countdown, turn timers and endings.</summary>
    private void ArenaTick()
    {
        var now = Server.CurrentTime;
        foreach (var c in _challenges.Where(c => c.Expires <= now).ToList())
        {
            _challenges.Remove(c);
            Tell(Utilities.GetPlayerFromSlot(c.From), "arena.expired");
        }

        foreach (var d in _duels.ToList())
        {
            var a = Utilities.GetPlayerFromSlot(d.SlotA);
            var b = Utilities.GetPlayerFromSlot(d.SlotB);
            if (a is not { IsValid: true } || b is not { IsValid: true } || a.SteamID != d.IdA || b.SteamID != d.IdB)
            {
                EndDuel(d, a is { IsValid: true } && a.SteamID == d.IdA ? 1 : b is { IsValid: true } && b.SteamID == d.IdB ? 2 : 0, "arena.left");
                continue;
            }

            // Keep the fighters in the ring (the chicken AI likes to wander).
            Hold(d.ChickenA, d.SpotA, d.YawA, d.Phase != DuelPhase.Resolving);
            Hold(d.ChickenB, d.SpotB, d.YawB, d.Phase != DuelPhase.Resolving);

            switch (d.Phase)
            {
                case DuelPhase.Betting when now >= d.PhaseEnds:
                case DuelPhase.Resolving when now >= d.PhaseEnds:
                    NextTurn(d, a, b);
                    break;
                case DuelPhase.Choosing:
                    if (b.IsBot && d.MoveB == null) d.MoveB = ArenaLogic.BotMove(d.B, d.Rng);
                    if (a.IsBot && d.MoveA == null) d.MoveA = ArenaLogic.BotMove(d.A, d.Rng);
                    if ((d.MoveA != null && d.MoveB != null) || now >= d.PhaseEnds) PlayTurn(d, a, b);
                    break;
            }
        }
    }

    private static void Hold(CChicken? c, Vector spot, float yaw, bool snap)
    {
        if (c is not { IsValid: true } || c.AbsOrigin is not { } o) return;
        SetLeader(c, null);
        var dx = o.X - spot.X;
        var dy = o.Y - spot.Y;
        if (snap && dx * dx + dy * dy > 12f * 12f) c.Teleport(spot, new QAngle(0, yaw, 0), new Vector(0, 0, 0));
    }

    private void NextTurn(Duel d, CCSPlayerController a, CCSPlayerController b)
    {
        var winner = ArenaLogic.Winner(d.A, d.B, d.Turn >= Config.Arena.MaxTurns);
        if (winner >= 0)
        {
            EndDuel(d, winner, null);
            return;
        }

        d.Turn++;
        d.MoveA = null;
        d.MoveB = null;
        d.Phase = DuelPhase.Choosing;
        d.PhaseEnds = Server.CurrentTime + Math.Max(4, Config.Arena.TurnSeconds);
        foreach (var (p, isA) in new[] { (a, true), (b, false) })
            if (!p.IsBot) BuildMoveMenu(p, d, isA).Open(p);
    }

    private CoopMenu BuildMoveMenu(CCSPlayerController p, Duel d, bool isA)
    {
        var me = isA ? d.A : d.B;
        var foe = isA ? d.B : d.A;
        var bar = $"{C(MenuService.Esc(me.Name), Green)} {MenuService.Bar((double)me.Hp / me.MaxHp, Green, 8)} {me.Hp}" +
                  $"  vs  {C(MenuService.Esc(foe.Name), Red)} {MenuService.Bar((double)foe.Hp / foe.MaxHp, Red, 8)} {foe.Hp}";
        var menu = new CoopMenu(M(p, "arena.turn", d.Turn, Config.Arena.MaxTurns), bar) { ExitButton = false };
        void Pick(ArenaMove m) => menu.Add(M(p, "arena.move." + m.ToString().ToLowerInvariant()), pl => ChooseMove(pl, d, isA, m),
            m != ArenaMove.Special || me.Charge >= ArenaLogic.ChargeNeeded);
        Pick(ArenaMove.Peck);
        Pick(ArenaMove.Guard);
        Pick(ArenaMove.Flap);
        Pick(ArenaMove.Special);
        menu.Info(C(M(p, "arena.charge", me.Charge, ArenaLogic.ChargeNeeded, Catalog.FindEmote(me.SpecialFx)?.Name ?? ""), Gold));
        menu.Add(C(M(p, "arena.forfeit"), Red), pl => EndDuel(d, isA ? 2 : 1, "arena.forfeited"));
        return menu;
    }

    private void ChooseMove(CCSPlayerController p, Duel d, bool isA, ArenaMove m)
    {
        if (d.Phase != DuelPhase.Choosing) return;
        if (isA) d.MoveA = m;
        else d.MoveB = m;
        Tell(p, "arena.picked", _lang.P(p, "arena.move." + m.ToString().ToLowerInvariant()));
    }

    private void PlayTurn(Duel d, CCSPlayerController a, CCSPlayerController b)
    {
        var ma = d.MoveA ?? ArenaMove.Guard;
        var mb = d.MoveB ?? ArenaMove.Guard;
        var res = ArenaLogic.Resolve(d.A, ma, d.B, mb, d.Rng);
        d.Phase = DuelPhase.Resolving;
        d.PhaseEnds = Server.CurrentTime + 2.6f;
        MenuService.Close(a);
        MenuService.Close(b);

        Act(d.ChickenA, d.ChickenB, res.MoveA, d.A, res.DamageToB, res.BDodged);
        AddTimer(0.9f, () => Act(d.ChickenB, d.ChickenA, res.MoveB, d.B, res.DamageToA, res.ADodged), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(1.8f, () =>
        {
            SetTag(d.TagA, d.A);
            SetTag(d.TagB, d.B);
        }, TimerFlags.STOP_ON_MAPCHANGE);

        foreach (var p in Watchers(d))
        {
            Tell(p, "arena.log", d.A.Name, _lang.P(p, "arena.move." + res.MoveA.ToString().ToLowerInvariant()), d.B.Name,
                _lang.P(p, "arena.move." + res.MoveB.ToString().ToLowerInvariant()));
            Tell(p, "arena.log.hp", d.A.Name, res.DamageToA, d.A.Hp, d.B.Name, res.DamageToB, d.B.Hp,
                res.ADodged || res.BDodged ? _lang.P(p, "arena.dodge") : "");
        }
    }

    /// <summary>One fighter acts out its move against the other.</summary>
    private void Act(CChicken? me, CChicken? foe, ArenaMove move, Fighter f, int damage, bool dodged)
    {
        if (me is not { IsValid: true } || me.AbsOrigin is not { } at) return;
        var foeAt = foe is { IsValid: true } && foe.AbsOrigin is { } fo ? fo : at;
        var toward = new Vector(at.X + (foeAt.X - at.X) * 0.35f, at.Y + (foeAt.Y - at.Y) * 0.35f, at.Z + 6f);
        switch (move)
        {
            case ArenaMove.Peck:
                me.DesiredActivity = EChickenActivity.Feed;
                me.Teleport(toward, null, null);
                me.EmitSound(Sounds.Peck);
                break;
            case ArenaMove.Guard:
                me.DesiredActivity = EChickenActivity.Squat;
                me.EmitSound(Sounds.Scratch);
                break;
            case ArenaMove.Flap:
                me.DesiredActivity = EChickenActivity.Trick;
                me.Teleport(Up(at, 30f), null, null);
                me.IsOnGround = false;
                me.EmitSound(Sounds.FlapShort);
                break;
            case ArenaMove.Special:
                me.DesiredActivity = EChickenActivity.Trick;
                me.Teleport(Up(toward, 20f), null, null);
                me.IsOnGround = false;
                me.EmitSound(Sounds.FlapLong);
                if (Catalog.FindEmote(f.SpecialFx)?.Particle is { } fx) Fx(fx, Up(foeAt, 10f), 2.5f);
                break;
        }

        if (foe is not { IsValid: true }) return;
        if (dodged)
        {
            Fx(Particles.PhotoFeathers, Up(foeAt, 12f), 1.5f);
            return;
        }

        if (damage > 0)
        {
            Fx(Particles.Feathers, Up(foeAt, 8f), 1.5f);
            foe.DesiredActivity = EChickenActivity.Panic;
            foe.EmitSound(Sounds.Panic);
        }
    }

    /// <summary>Duellists, bettors, and (when announcing) everyone nearby: whoever gets the turn log.</summary>
    private IEnumerable<CCSPlayerController> Watchers(Duel d)
    {
        var ids = new HashSet<ulong>(d.Bets.Keys) { d.IdA, d.IdB };
        return Players().Where(p => !p.IsBot && ids.Contains(p.SteamID));
    }

    /// <param name="winner">0 draw, 1 = A, 2 = B.</param>
    private void EndDuel(Duel d, int winner, string? reasonKey)
    {
        if (d.Phase == DuelPhase.Over) return;
        d.Phase = DuelPhase.Over;
        _duels.Remove(d);
        var a = Utilities.GetPlayerFromSlot(d.SlotA);
        var b = Utilities.GetPlayerFromSlot(d.SlotB);
        if (a is { IsValid: true }) MenuService.Close(a);
        if (b is { IsValid: true }) MenuService.Close(b);

        var pa = a is { IsValid: true } && a.SteamID == d.IdA ? Data(a) : _store.TryGet(d.IdA, out var x) ? x : null;
        var pb = b is { IsValid: true } && b.SteamID == d.IdB ? Data(b) : _store.TryGet(d.IdB, out var y) ? y : null;

        // Wager pot: winner takes both stakes, a draw refunds.
        if (d.Wager > 0)
        {
            if (winner == 1 && pa != null) pa.FeedBags += d.Wager * 2;
            else if (winner == 2 && pb != null) pb.FeedBags += d.Wager * 2;
            else
            {
                if (pa != null) pa.FeedBags += d.Wager;
                if (pb != null) pb.FeedBags += d.Wager;
            }
        }

        if (pa != null && pb != null)
        {
            switch (winner)
            {
                case 1:
                    pa.Stats.ArenaWins++;
                    pb.Stats.ArenaLosses++;
                    break;
                case 2:
                    pb.Stats.ArenaWins++;
                    pa.Stats.ArenaLosses++;
                    break;
                default:
                    pa.Stats.ArenaDraws++;
                    pb.Stats.ArenaDraws++;
                    break;
            }
        }

        var payouts = ArenaLogic.Payouts(d.Bets, winner);
        foreach (var (id, bet) in d.Bets)
        {
            var payout = payouts.GetValueOrDefault(id);
            if (payout > 0 && _store.TryGet(id, out var bettor)) bettor.FeedBags += payout;
            var betKey = payout > bet.Stake ? "arena.bet.won" : payout == bet.Stake ? "arena.bet.refund" : "arena.bet.lost";
            Tell(Utilities.GetPlayerFromSteamId(id), betKey, payout, bet.Stake);
        }

        _store.MarkDirty();

        // Show the result in the ring, then clear it.
        var winChicken = winner == 1 ? d.ChickenA : winner == 2 ? d.ChickenB : null;
        var loseChicken = winner == 1 ? d.ChickenB : winner == 2 ? d.ChickenA : null;
        if (winChicken is { IsValid: true } w && w.AbsOrigin is { } wat)
        {
            Fx(Particles.Confetti, Up(wat, 10f), 3f);
            w.DesiredActivity = EChickenActivity.Trick;
            w.EmitSound(Sounds.FlapLong);
        }

        if (loseChicken is { IsValid: true } l) l.DesiredActivity = EChickenActivity.Sleep;
        var leftovers = new CBaseEntity?[] { d.ChickenA, d.ChickenB, d.TagA, d.TagB };
        AddTimer(4f, () =>
        {
            foreach (var e in leftovers)
            {
                if (e is not { IsValid: true }) continue;
                if (e.AbsOrigin is { } o && e is CChicken) Fx(Particles.Feathers, o, 2f);
                _arenaChickens.Remove(e.Index);
                e.Remove();
            }
        }, TimerFlags.STOP_ON_MAPCHANGE);

        var cooldown = Server.CurrentTime + Config.Arena.CooldownSeconds;
        _duelCooldown[d.IdA] = cooldown;
        _duelCooldown[d.IdB] = cooldown;
        if (a is { IsValid: true }) Suppress(a, false);
        if (b is { IsValid: true }) Suppress(b, false);

        var nameA = a is { IsValid: true } ? a.PlayerName : pa?.OwnerName ?? "?";
        var nameB = b is { IsValid: true } ? b.PlayerName : pb?.OwnerName ?? "?";
        var key = winner == 0 ? "arena.result.draw" : "arena.result";
        var (wn, wp, ln, lp) = winner == 2 ? (nameB, d.B.Name, nameA, d.A.Name) : (nameA, d.A.Name, nameB, d.B.Name);
        var targets = Config.Arena.Announce ? Players().ToList() : Watchers(d).ToList();
        foreach (var p in targets)
        {
            if (reasonKey != null) Tell(p, reasonKey, winner == 2 ? nameA : nameB);
            Tell(p, key, wn, wp, ln, lp, d.Wager > 0 ? _lang.P(p, "arena.result.wager", d.Wager * 2) : "");
        }
    }

    /// <summary>Round restart / map change: duels can't survive the entity cleanup, so they end as draws.</summary>
    private void CancelDuels()
    {
        foreach (var d in _duels.ToList()) EndDuel(d, 0, "arena.cancelled");
        _challenges.Clear();
    }

    // ================================================================ betting

    private CoopMenu BuildBetDuels(CCSPlayerController p)
    {
        var menu = new CoopMenu(M(p, "arena.m.bet"), M(p, "arena.m.bet.sub", Data(p)?.FeedBags ?? 0));
        foreach (var d in _duels.Where(d => d.Phase == DuelPhase.Betting))
        {
            var duel = d;
            var mine = d.SlotA == p.Slot || d.SlotB == p.Slot;
            menu.Add($"{MenuService.Esc(d.A.Name)} vs {MenuService.Esc(d.B.Name)} {C(PetLogic.Duration(Math.Max(0, d.PhaseEnds - Server.CurrentTime)), Grey)}",
                pl => Sub(pl, x => BuildBetSide(x, duel), BuildBetDuels), !mine && !d.Bets.ContainsKey(p.SteamID));
        }

        return menu;
    }

    private CoopMenu BuildBetSide(CCSPlayerController p, Duel d)
    {
        var menu = new CoopMenu(M(p, "arena.m.bet"), M(p, "arena.m.bet.pick"));
        var bags = Data(p)?.FeedBags ?? 0;
        foreach (var (side, f) in new[] { (1, d.A), (2, d.B) })
            for (var stake = 1; stake <= Math.Max(1, Config.Arena.MaxBet); stake++)
            {
                var s = stake;
                var sd = side;
                menu.Add(M(p, "arena.m.bet.on", MenuService.Esc(f.Name), stake), pl => PlaceBet(pl, d, sd, s), bags >= stake);
            }

        return menu;
    }

    private void PlaceBet(CCSPlayerController p, Duel d, int side, int stake)
    {
        var pet = Data(p);
        if (pet == null || d.Phase != DuelPhase.Betting || d.SlotA == p.Slot || d.SlotB == p.Slot || d.Bets.ContainsKey(p.SteamID)) return;
        if (pet.FeedBags < stake)
        {
            Tell(p, "feed.nobags", Config.Food.RoundsPerBag);
            return;
        }

        pet.FeedBags -= stake;
        d.Bets[p.SteamID] = (side, stake);
        _store.MarkDirty();
        Tell(p, "arena.bet.placed", stake, side == 1 ? d.A.Name : d.B.Name);
    }
}
