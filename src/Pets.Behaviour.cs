using ChickenCoop.Logic;
using ChickenCoop.Model;
using ChickenCoop.Runtime;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace ChickenCoop;

public sealed partial class ChickenCoopPlugin
{
    /// <summary>
    /// Five times a second: keep each pet's leader set to its owner (the game's chicken AI does the walking, running and
    /// jumping), teleport it back when it is left far behind or stuck, and run the small extras: idle clucks, hunger,
    /// bird watch, shiny sparkle and rainbow colours, and laid-egg pickups.
    /// </summary>
    private void FollowTick()
    {
        var now = Server.CurrentTime;
        foreach (var a in _actors.Values)
        {
            if (a.TransmitDirty) a.RebuildIndices();
            if (a.LaidEggs.Count > 0) EggPickups(a);

            var owner = Utilities.GetPlayerFromSlot(a.Slot);
            var pet = owner is { IsValid: true } ? Data(owner) : null;
            if (pet == null) continue;
            ShinyTick(a, pet, now);

            if (a.Chicken is not { IsValid: true } c) continue;
            var pawn = AlivePawn(owner);
            if (pawn == null)
            {
                SetLeader(c, null);
                continue;
            }

            if (!pet.IsHatched || pet.Hidden || _suppressAll || _suppressed.Contains(a.Slot))
            {
                RemoveChicken(a);
                continue;
            }

            if (a.Staying || now < a.BusyUntil)
            {
                SetLeader(c, null);
                continue;
            }

            // Re-asserting the leader also undoes another player pressing E on the pet to lead it away.
            if (Config.Behaviour.OwnerOnlyFollow || LeaderRaw(c) == uint.MaxValue) SetLeader(c, pawn);

            var from = c.AbsOrigin;
            var to = pawn.AbsOrigin;
            if (from == null || to == null) continue;
            var dist = Distance(from, to);

            if (dist > Config.Behaviour.TeleportDistance)
            {
                TeleportToOwner(a, c, pawn, pet);
                continue;
            }

            // Stuck check: far enough away that it should be walking, but not getting any closer.
            if (dist > 200f)
            {
                if (dist < a.LastDistance - 12f) a.LastProgressAt = now;
                else if (now - a.LastProgressAt > Config.Behaviour.StuckSeconds)
                {
                    TeleportToOwner(a, c, pawn, pet);
                    continue;
                }
            }
            else
            {
                a.LastProgressAt = now;
            }

            a.LastDistance = dist;

            if (Config.Behaviour.IdleSoundSeconds > 0 && now >= a.NextIdleSound)
            {
                a.NextIdleSound = now + Config.Behaviour.IdleSoundSeconds * (0.6f + Random.Shared.NextSingle() * 0.8f);
                PlayAt(a, Random.Shared.Next(4) == 0 ? Sounds.Pick(pet.Stage, Sounds.Scratch, Sounds.ScratchChick) : Sounds.Idle(pet.Stage));
            }

            if (Config.Food.Enabled && pet.FoodSeconds <= Config.Food.HungryWarningMinutes * 60 && now >= a.NextHungrySound)
            {
                a.NextHungrySound = now + 40f + Random.Shared.NextSingle() * 20f;
                PlayAt(a, Sounds.Pick(pet.Stage, Sounds.Hungry, Sounds.HungryChick));
                c.DesiredActivity = EChickenActivity.LowOnFood;
            }

            if (Config.BirdWatch.Enabled && now >= a.NextBirdWatch) BirdWatch(a, c, owner!, pet, now);
        }
    }

    private void TeleportToOwner(PetActor a, CChicken c, CCSPlayerPawn pawn, PetData pet)
    {
        var at = BehindOf(pawn, 36f);
        c.Teleport(at, new QAngle(0, pawn.EyeAngles?.Y ?? 0f, 0), new Vector(0, 0, 0));
        a.LastProgressAt = Server.CurrentTime;
        a.LastDistance = 0;
        Fx(Particles.Feathers, at, 2f);
        PlayAt(a, Sounds.Pick(pet.Stage, Sounds.FlapShort, Sounds.FlapShortChick));
    }

    /// <summary>Shiny pets twinkle now and then; rainbow pets cycle their colour.</summary>
    private void ShinyTick(PetActor a, PetData pet, float now)
    {
        if (PetLogic.EffectiveStyle(pet) != Catalog.ShinyStyleId || Catalog.FindShiny(pet.Shiny) is not { } shiny) return;
        var body = a.Chicken is { IsValid: true } ? (CBaseModelEntity)a.Chicken : a.Perch is { IsValid: true } ? a.Perch : null;
        if (body == null) return;
        if (shiny.Rainbow)
        {
            a.RainbowHue = (a.RainbowHue + 9f) % 360f;
            ApplyStyle(body, pet, Catalog.ShinyStyleId, a.RainbowHue);
        }

        if (shiny.Particle != null && now >= a.NextShinyFx && body.AbsOrigin is { } at)
        {
            a.NextShinyFx = now + 4f + Random.Shared.NextSingle() * 3f;
            Fx(shiny.Particle, Up(at, 12), 2f);
        }
    }

    /// <summary>Bird watch (config): the pet panics and clucks when an enemy comes close.</summary>
    private void BirdWatch(PetActor a, CChicken c, CCSPlayerController owner, PetData pet, float now)
    {
        if (c.AbsOrigin is not { } at) return;
        var range = Config.BirdWatch.Range;
        var spotted = Players().Any(p => p.Team != owner.Team && OnTeam(p) && AlivePawn(p) is { AbsOrigin: { } o } && Distance(o, at) <= range);
        if (!spotted)
        {
            a.NextBirdWatch = now + 0.5f;
            return;
        }

        a.NextBirdWatch = now + Config.BirdWatch.CooldownSeconds;
        c.DesiredActivity = EChickenActivity.Panic;
        PlayAt(a, Sounds.Pick(pet.Stage, Sounds.Panic, Sounds.PanicChick));
        if (Config.BirdWatch.WarnOwner) owner.PrintToCenter(_lang.P(owner, "birdwatch.warn", pet.Name));
    }

    /// <summary>Laid eggs: picked up by walking over them (the owner, or anyone when Eggs.Pickup is "anyone").</summary>
    private void EggPickups(PetActor a)
    {
        a.LaidEggs.RemoveAll(e => !e.IsValid);
        if (a.LaidEggs.Count == 0) return;
        var owner = Utilities.GetPlayerFromSlot(a.Slot);
        foreach (var egg in a.LaidEggs.ToList())
        {
            if (egg.AbsOrigin is not { } at) continue;
            foreach (var p in Players())
            {
                if (p.IsBot || (Config.Eggs.Pickup != "anyone" && p.Slot != a.Slot)) continue;
                if (AlivePawn(p) is not { AbsOrigin: { } o } || Distance(o, at) > 52f) continue;
                var data = Data(p);
                if (data == null || !MayOwn(p)) continue;
                if (!PetLogic.CollectEgg(data, Config))
                {
                    p.PrintToCenter(_lang.P(p, "eggs.basket_full.short", Config.Eggs.MaxBasket));
                    continue;
                }

                _store.MarkDirty();
                egg.EmitSound(Sounds.HatchTap);
                egg.Remove();
                a.LaidEggs.Remove(egg);
                a.TransmitDirty = true;
                if (p.Slot == a.Slot) Tell(p, "eggs.picked", data.BasketEggs);
                else
                {
                    Tell(p, "eggs.stolen.thief", owner?.PlayerName, data.BasketEggs);
                    Tell(owner, "eggs.stolen.owner", p.PlayerName);
                }

                break;
            }
        }
    }

    /// <summary>Petting: an owner who looks at their pet up close and presses E (+use). Not while a menu is open.</summary>
    private void PettingTick()
    {
        if (_actors.Count == 0) return;
        foreach (var a in _actors.Values)
        {
            if (a.Chicken is not { IsValid: true } c) continue;
            var owner = Utilities.GetPlayerFromSlot(a.Slot);
            if (owner is not { IsValid: true } || owner.IsBot) continue;
            var down = (owner.Buttons & PlayerButtons.Use) != 0;
            var pressed = down && !a.UseWasDown;
            a.UseWasDown = down;
            if (!pressed || Server.CurrentTime < a.PetReadyAt || Menus.MenuService.HasMenu(owner)) continue;

            var pawn = AlivePawn(owner);
            if (pawn == null || !LookingAt(pawn, c, Config.Behaviour.PetDistance)) continue;
            a.PetReadyAt = Server.CurrentTime + Config.Behaviour.PetCooldownSeconds;
            Pet(owner, a, c);
        }
    }

    private static bool LookingAt(CCSPlayerPawn pawn, CBaseEntity target, float range)
    {
        var o = pawn.AbsOrigin;
        var t = target.AbsOrigin;
        var ang = pawn.EyeAngles;
        if (o == null || t == null || ang == null) return false;
        var eye = new Vector(o.X, o.Y, o.Z + (pawn.ViewOffset?.Z ?? 64f));
        var dx = t.X - eye.X;
        var dy = t.Y - eye.Y;
        var dz = t.Z + 10f - eye.Z;
        var len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len > range || len < 1f) return len < 1f;
        var pitch = ang.X * MathF.PI / 180f;
        var yaw = ang.Y * MathF.PI / 180f;
        var fx = MathF.Cos(pitch) * MathF.Cos(yaw);
        var fy = MathF.Cos(pitch) * MathF.Sin(yaw);
        var fz = -MathF.Sin(pitch);
        return (fx * dx + fy * dy + fz * dz) / len > 0.8f;
    }

    private void Pet(CCSPlayerController owner, PetActor a, CChicken c)
    {
        var pet = Data(owner);
        if (pet == null) return;
        WithUnlocks(owner, pet, () => pet.Stats.Pets++);
        PlayAt(a, Sounds.Pick(pet.Stage, Sounds.Interact, Sounds.InteractChick));
        c.DesiredActivity = EChickenActivity.Squat;
        owner.PrintToCenter(_lang.P(owner, "pet.petted", pet.Name));
    }

    // ================================================================ actions (commands and menus)

    internal void DoEmote(CCSPlayerController p, Emote emote)
    {
        var pet = Data(p);
        if (!RequireOut(p, pet, out var a, out var c, allowPerch: true)) return;
        if (!PetLogic.EmoteUnlocked(pet!, emote, Config))
        {
            Tell(p, "emote.locked", emote.Name, emote.HowToUnlock);
            return;
        }

        var now = Server.CurrentTime;
        if (now < a!.EmoteReadyAt)
        {
            Tell(p, "emote.wait");
            return;
        }

        a.EmoteReadyAt = now + Config.Behaviour.EmoteCooldownSeconds;
        WithUnlocks(p, pet!, () =>
        {
            if (emote.Id == "flap") pet!.Stats.Tricks++;
            else pet!.Stats.Emotes++;
        });

        var body = c != null ? (CBaseModelEntity)c : a.Perch!;
        if (emote.Particle != null && body.AbsOrigin is { } at) Fx(emote.Particle, Up(at, 8), Math.Max(2f, emote.Seconds));

        if (c != null)
        {
            a.BusyUntil = now + emote.Seconds;
            SetLeader(c, null);
            if (Enum.TryParse<EChickenActivity>(emote.Activity, true, out var act)) c.DesiredActivity = act;
            if (emote.Hop && c.AbsOrigin is { } o)
            {
                c.Teleport(new Vector(o.X, o.Y, o.Z + (pet!.Stage == PetStage.Chick ? 22f : 34f)), null, null);
                c.IsOnGround = false;
            }

            if (emote.Spin) Spin(c, emote.Seconds);
        }

        PlayAt(a, emote.Activity switch
        {
            "Panic" => Sounds.Pick(pet!.Stage, Sounds.Panic, Sounds.PanicChick),
            "Feed" => Sounds.Pick(pet!.Stage, Sounds.Peck, Sounds.PeckChick),
            "Sleep" or "Squat" => Sounds.Idle(pet!.Stage),
            _ => Sounds.Pick(pet!.Stage, Sounds.FlapShort, Sounds.FlapShortChick),
        });
    }

    /// <summary>Turns the pet on the spot a few times.</summary>
    private void Spin(CChicken c, float seconds)
    {
        var steps = (int)(seconds / 0.1f);
        var yaw = c.AbsRotation?.Y ?? 0f;
        for (var i = 1; i <= steps; i++)
        {
            var angle = yaw + i * 36f;
            AddTimer(0.1f * i, () =>
            {
                if (c.IsValid) c.Teleport(null, new QAngle(0, angle, 0), null);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
    }

    internal void DoStay(CCSPlayerController p, bool stay)
    {
        var pet = Data(p);
        if (!RequireOut(p, pet, out var a, out var c)) return;
        a!.Staying = stay;
        if (stay)
        {
            SetLeader(c!, null);
            c!.DesiredActivity = EChickenActivity.Squat;
            Tell(p, "stay.on", pet!.Name);
        }
        else
        {
            if (AlivePawn(p) is { } pawn) SetLeader(c!, pawn);
            c!.DesiredActivity = EChickenActivity.Walk;
            PlayAt(a, Sounds.Idle(pet!.Stage));
            Tell(p, "stay.off", pet.Name);
        }
    }

    internal void DoPerch(CCSPlayerController p, bool perch)
    {
        var pet = Data(p);
        if (pet == null) return;
        if (!pet.IsHatched)
        {
            Tell(p, "pet.egg_only");
            return;
        }

        if (perch && !CanPerch(pet))
        {
            Tell(p, Config.Perch.Enabled ? "perch.too_big" : "perch.off", pet.Name);
            return;
        }

        var a = Actor(p);
        var pawn = AlivePawn(p);
        if (pawn == null || (!a.ChickenAlive && !a.PerchOut))
        {
            Tell(p, pet.Hidden ? "pet.hidden" : "pet.not_out", pet.Name);
            return;
        }

        a.Perched = perch;
        if (perch)
        {
            WithUnlocks(p, pet, () => pet.Stats.Perches++);
            if (a.Chicken is { IsValid: true } c && c.AbsOrigin is { } at) Fx(Particles.Feathers, Up(at, 6), 2f);
            RemoveChicken(a);
            SpawnPerch(a, pawn, pet);
            Tell(p, "perch.on", pet.Name);
        }
        else
        {
            var at = a.Perch is { IsValid: true } pr && pr.AbsOrigin is { } po ? new Vector(po.X, po.Y, po.Z) : BehindOf(pawn, 36f);
            RemovePerch(a);
            SpawnChicken(a, p, pawn, pet, at);
            Tell(p, "perch.off_shoulder", pet.Name);
        }
    }

    internal void DoFeed(CCSPlayerController p)
    {
        var pet = Data(p);
        if (pet == null || p.IsBot) return;
        var a = Actor(p);
        var now = Server.CurrentTime;
        if (now < a.FeedReadyAt)
        {
            Tell(p, "feed.wait");
            return;
        }

        var result = FeedResult.NoPet;
        WithUnlocks(p, pet, () => result = PetLogic.Feed(pet, Config));
        switch (result)
        {
            case FeedResult.NoPet:
                Tell(p, "pet.none", PetLogic.Duration(PetLogic.SecondsLeft(pet, Config)));
                return;
            case FeedResult.IsEgg:
                Tell(p, "feed.egg");
                return;
            case FeedResult.NoBags:
                Tell(p, "feed.nobags", Config.Food.RoundsPerBag);
                return;
            case FeedResult.Full:
                Tell(p, "feed.full", pet.Name, PetLogic.Duration(pet.FoodSeconds));
                return;
        }

        a.FeedReadyAt = now + Config.Behaviour.FeedCooldownSeconds;
        Tell(p, "feed.done", pet.Name, PetLogic.Duration(pet.FoodSeconds), pet.FeedBags);
        if (a.Chicken is not { IsValid: true } c || c.AbsOrigin is not { } at)
        {
            PlayAt(a, Sounds.Feed);
            return;
        }

        // Scatter some feed in front of the pet; it stops to eat for a few seconds.
        var yaw = c.AbsRotation?.Y ?? 0f;
        var f = Forward(yaw);
        Remove(ref a.Feed);
        a.Feed = SpawnProp(Catalog.FeedModel, new Vector(at.X + f.X * 14f, at.Y + f.Y * 14f, at.Z + 1f), new QAngle(0, yaw, 0));
        a.TransmitDirty = true;
        a.BusyUntil = now + 4.5f;
        SetLeader(c, null);
        c.DesiredActivity = EChickenActivity.Feed;
        PlayAt(a, Sounds.FeedSeed);
        AddTimer(0.8f, () => PlayAt(a, pet.Stage == PetStage.Chick ? Sounds.PeckChick : Sounds.Feed), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(2.4f, () => PlayAt(a, pet.Stage == PetStage.Chick ? Sounds.PeckChick : Sounds.Peck), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(4.6f, () =>
        {
            Remove(ref a.Feed);
            a.TransmitDirty = true;
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    internal void DoHide(CCSPlayerController p, bool hide)
    {
        var pet = Data(p);
        if (pet == null) return;
        pet.Hidden = hide;
        _store.MarkDirty();
        if (hide)
        {
            if (_actors.TryGetValue(p.Slot, out var a)) Despawn(a, false);
            Tell(p, "hide.on");
        }
        else
        {
            Tell(p, "hide.off");
            TrySpawnPet(p);
        }
    }

    internal void DoGift(CCSPlayerController p, CCSPlayerController target)
    {
        var from = Data(p);
        var to = Data(target);
        if (from == null || to == null || target.IsBot) return;
        switch (PetLogic.GiftEgg(from, to, Config, DateTime.UtcNow, NewSeed(target)))
        {
            case GiftResult.Disabled:
                Tell(p, "gift.off");
                return;
            case GiftResult.NoEggs:
                Tell(p, "gift.noeggs");
                return;
            case GiftResult.Self:
                Tell(p, "gift.self");
                return;
            case GiftResult.TargetHasPet:
                Tell(p, "gift.haspet", target.PlayerName);
                return;
        }

        _store.MarkDirty();
        Tell(p, "gift.sent", target.PlayerName, from.BasketEggs);
        Tell(target, "gift.received", p.PlayerName, PetLogic.Duration(PetLogic.StageSeconds(PetStage.Egg, Config)));
        PlayTo(target, Sounds.HatchTap);
        TrySpawnPet(target);
        _api.RaiseStage(target, PetStage.Egg);
    }

    internal void DoTrade(CCSPlayerController p, int count)
    {
        var pet = Data(p);
        if (pet == null) return;
        if (Config.Eggs.BagsPerEgg <= 0)
        {
            Tell(p, "trade.off");
            return;
        }

        var traded = PetLogic.TradeEggs(pet, count, Config);
        if (traded == 0)
        {
            Tell(p, pet.BasketEggs == 0 ? "gift.noeggs" : "trade.full", Config.Food.MaxBags);
            return;
        }

        _store.MarkDirty();
        Tell(p, "trade.done", traded, traded * Config.Eggs.BagsPerEgg, pet.FeedBags, pet.BasketEggs);
    }

    private bool RequireOut(CCSPlayerController p, PetData? pet, out PetActor? actor, out CChicken? chicken, bool allowPerch = false)
    {
        actor = null;
        chicken = null;
        if (pet == null) return false;
        if (!pet.HasPet)
        {
            Tell(p, "pet.none", PetLogic.Duration(PetLogic.SecondsLeft(pet, Config)));
            return false;
        }

        if (!pet.IsHatched)
        {
            Tell(p, "pet.egg_only");
            return false;
        }

        if (!_actors.TryGetValue(p.Slot, out var a))
        {
            Tell(p, pet.Hidden ? "pet.hidden" : "pet.not_out", pet.Name);
            return false;
        }

        if (a.Chicken is { IsValid: true } c)
        {
            actor = a;
            chicken = c;
            return true;
        }

        if (allowPerch && a.PerchOut)
        {
            actor = a;
            return true;
        }

        Tell(p, a.PerchOut ? "perch.busy" : pet.Hidden ? "pet.hidden" : "pet.not_out", pet.Name);
        return false;
    }
}
