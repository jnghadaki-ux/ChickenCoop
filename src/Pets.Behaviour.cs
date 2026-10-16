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
    /// jumping), and teleport it back when it is left far behind or stuck.
    /// </summary>
    private void FollowTick()
    {
        var now = Server.CurrentTime;
        foreach (var a in _actors.Values)
        {
            if (a.Chicken is not { IsValid: true } c) continue;
            var owner = Utilities.GetPlayerFromSlot(a.Slot);
            var pawn = AlivePawn(owner);
            if (owner == null || pawn == null)
            {
                SetLeader(c, null);
                continue;
            }

            var pet = Data(owner);
            if (pet == null || !pet.IsHatched || pet.Hidden)
            {
                RemoveChicken(a);
                continue;
            }

            if (a.Staying || now < a.FeedingUntil)
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
                PlayAt(a, Sounds.Idle(pet.Stage));
            }

            if (Config.Food.Enabled && pet.FoodSeconds <= Config.Food.HungryWarningMinutes * 60 && now >= a.NextHungrySound)
            {
                a.NextHungrySound = now + 40f + Random.Shared.NextSingle() * 20f;
                PlayAt(a, Sounds.Pick(pet.Stage, Sounds.Hungry, Sounds.HungryChick));
                c.DesiredActivity = EChickenActivity.LowOnFood;
            }
        }
    }

    private void TeleportToOwner(PetActor a, CChicken c, CCSPlayerPawn pawn, PetData pet)
    {
        var at = BehindOf(pawn, 36f);
        c.Teleport(at, new QAngle(0, pawn.EyeAngles?.Y ?? 0f, 0), new Vector(0, 0, 0));
        a.LastProgressAt = Server.CurrentTime;
        a.LastDistance = 0;
        Particles.Spawn(this, Particles.Feathers, Copy(at), 2f);
        PlayAt(a, Sounds.Pick(pet.Stage, Sounds.FlapShort, Sounds.FlapShortChick));
    }

    /// <summary>Petting: an owner who looks at their pet up close and presses E (+use).</summary>
    private void OnTick()
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
            if (!pressed || Server.CurrentTime < a.PetReadyAt) continue;

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
        var tgt = new Vector(t.X, t.Y, t.Z + 10f);
        var dx = tgt.X - eye.X;
        var dy = tgt.Y - eye.Y;
        var dz = tgt.Z - eye.Z;
        var len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len > range || len < 1f) return len < 1f;
        var pitch = ang.X * MathF.PI / 180f;
        var yaw = ang.Y * MathF.PI / 180f;
        var fx = MathF.Cos(pitch) * MathF.Cos(yaw);
        var fy = MathF.Cos(pitch) * MathF.Sin(yaw);
        var fz = -MathF.Sin(pitch);
        var dot = (fx * dx + fy * dy + fz * dz) / len;
        return dot > 0.8f;
    }

    private void Pet(CCSPlayerController owner, PetActor a, CChicken c)
    {
        var pet = Data(owner);
        if (pet == null) return;
        WithHats(owner, pet, () => pet.Stats.Pets++);
        PlayAt(a, Sounds.Pick(pet.Stage, Sounds.Interact, Sounds.InteractChick));
        c.DesiredActivity = EChickenActivity.Squat;
        owner.PrintToCenter(_lang.Plain("pet.petted", pet.Name));
    }

    // ================================================================ actions (commands and menu)

    private void DoTrick(CCSPlayerController p)
    {
        var pet = Data(p);
        if (!RequireOut(p, pet, out var a, out var c)) return;
        var now = Server.CurrentTime;
        if (now < a!.TrickReadyAt)
        {
            Tell(p, "trick.wait");
            return;
        }

        a.TrickReadyAt = now + Config.Behaviour.TrickCooldownSeconds;
        WithHats(p, pet!, () => pet!.Stats.Tricks++);
        c!.DesiredActivity = EChickenActivity.Trick;
        // A little hop: lift it off the ground and the chicken AI glides it back down, flapping.
        if (c.AbsOrigin is { } o)
        {
            c.Teleport(new Vector(o.X, o.Y, o.Z + (pet!.Stage == PetStage.Chick ? 22f : 34f)), null, null);
            c.IsOnGround = false;
        }
        PlayAt(a, Sounds.Pick(pet!.Stage, Sounds.FlapShort, Sounds.FlapShortChick));
        Tell(p, "trick.done", pet.Name, pet.Stats.Tricks);
    }

    private void DoStay(CCSPlayerController p, bool stay)
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
            var pawn = AlivePawn(p);
            if (pawn != null) SetLeader(c!, pawn);
            c!.DesiredActivity = EChickenActivity.Walk;
            PlayAt(a, Sounds.Idle(pet!.Stage));
            Tell(p, "stay.off", pet.Name);
        }
    }

    private void DoFeed(CCSPlayerController p)
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
        WithHats(p, pet, () => result = PetLogic.Feed(pet, Config));
        switch (result)
        {
            case FeedResult.NoPet:
                Tell(p, "pet.none", PetLogic.Duration(Math.Max(0, PetLogic.StageSeconds(PetStage.None, Config) - pet.SecondsTowardEgg)));
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
        if (a.Chicken is not { IsValid: true } c || c.AbsOrigin is not { } at) return;

        // Scatter some feed in front of the pet; it stops to eat for a few seconds.
        var yaw = c.AbsRotation?.Y ?? 0f;
        var f = Forward(yaw);
        Remove(ref a.Feed);
        a.Feed = SpawnProp(Catalog.FeedModel, new Vector(at.X + f.X * 14f, at.Y + f.Y * 14f, at.Z + 1f), new QAngle(0, yaw, 0));
        a.FeedingUntil = now + 4.5f;
        SetLeader(c, null);
        c.DesiredActivity = EChickenActivity.Feed;
        PlayAt(a, Sounds.FeedSeed);
        AddTimer(0.8f, () => PlayAt(a, pet.Stage == PetStage.Chick ? Sounds.PeckChick : Sounds.Feed), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(2.4f, () => PlayAt(a, pet.Stage == PetStage.Chick ? Sounds.PeckChick : Sounds.Peck), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(4.6f, () => Remove(ref a.Feed), TimerFlags.STOP_ON_MAPCHANGE);
    }

    private bool RequireOut(CCSPlayerController p, PetData? pet, out PetActor? actor, out CChicken? chicken)
    {
        actor = null;
        chicken = null;
        if (pet == null) return false;
        if (!pet.HasPet)
        {
            Tell(p, "pet.none", PetLogic.Duration(Math.Max(0, PetLogic.StageSeconds(PetStage.None, Config) - pet.SecondsTowardEgg)));
            return false;
        }

        if (!pet.IsHatched)
        {
            Tell(p, "pet.egg_only");
            return false;
        }

        if (!_actors.TryGetValue(p.Slot, out var a) || a.Chicken is not { IsValid: true } c)
        {
            Tell(p, pet.Hidden ? "pet.hidden" : "pet.not_out", pet.Name);
            return false;
        }

        actor = a;
        chicken = c;
        return true;
    }
}
