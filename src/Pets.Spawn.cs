using ChickenCoop.Logic;
using ChickenCoop.Model;
using ChickenCoop.Runtime;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ChickenCoop;

public sealed partial class ChickenCoopPlugin
{
    private int ActivePets => _actors.Values.Count(a => a.Out);

    /// <summary>Puts the player's pet (or egg) in the world if it should be out and is not already.</summary>
    private void TrySpawnPet(CCSPlayerController p)
    {
        if (!Config.Enabled || _mapDisabled || !MayOwn(p) || !OnTeam(p)) return;
        if (Config.HideDuringWarmup && Warmup()) return;
        var pawn = AlivePawn(p);
        var pet = Data(p);
        if (pawn == null || pet == null || !pet.HasPet || pet.Hidden) return;

        var a = Actor(p);
        if (a.Hatching) return;
        if (!pet.IsHatched)
        {
            if (!a.EggOut && Config.Looks.ShowEggAtSpawn && CapacityFor(a, p)) SpawnEgg(a, pawn);
            return;
        }

        if (a.EggOut) RemoveEgg(a);
        if (a.DiedThisRound) return;
        if (a.ChickenAlive && a.Shown == pet.Stage)
        {
            RefreshLooks(a, pet, p);
            return;
        }

        if (a.ChickenAlive) RemoveChicken(a);
        if (!CapacityFor(a, p)) return;
        SpawnChicken(a, p, pawn, pet, BehindOf(pawn, 36f));
    }

    private bool CapacityFor(PetActor a, CCSPlayerController p)
    {
        if (a.Out || ActivePets < Config.MaxActivePets) return true;
        Tell(p, "pet.cap");
        return false;
    }

    private void SpawnChicken(PetActor a, CCSPlayerController owner, CCSPlayerPawn pawn, PetData pet, Vector at)
    {
        var model = Catalog.ModelFor(pet.Stage, pet.Breed);
        CChicken? chicken = null;
        try
        {
            chicken = Utilities.CreateEntityByName<CChicken>("chicken");
            if (chicken == null) return;
            var yaw = pawn.EyeAngles?.Y ?? 0f;
            chicken.Teleport(at, new QAngle(0, yaw, 0), new Vector(0, 0, 0));
            var kv = new CEntityKeyValues();
            kv.SetString("chicken_model", model);
            chicken.DispatchSpawn(kv);

            // Older servers may ignore chicken_model; set the model directly in that case.
            if (!string.Equals(ModelOf(chicken), model, StringComparison.OrdinalIgnoreCase)) chicken.SetModel(model);

            var hp = pet.Stage switch
            {
                PetStage.Chick => Config.Combat.ChickHealth,
                PetStage.Pullet => Config.Combat.PulletHealth,
                _ => Config.Combat.HenHealth,
            };
            chicken.MaxHealth = Math.Max(1, hp);
            chicken.Health = Math.Max(1, hp);
            chicken.SpawnDyingParticles = Config.Combat.DeathParticles;
            SetOwner(chicken, owner);
            SetLeader(chicken, pawn);
            ApplyScale(chicken, pet.Stage);
            ApplyCoat(chicken, pet);
            if (Config.Behaviour.NoBlock) NoBlock(chicken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Chicken Coop could not spawn a pet ({Model})", model);
            if (chicken is { IsValid: true }) chicken.Remove();
            return;
        }

        a.Chicken = chicken;
        a.Shown = pet.Stage;
        a.SpawnedAt = Server.CurrentTime;
        a.LastProgressAt = Server.CurrentTime;
        a.LastDistance = 0;
        a.Staying = false;
        a.NextIdleSound = Server.CurrentTime + 5f + Random.Shared.NextSingle() * Config.Behaviour.IdleSoundSeconds;
        a.ShownHat = null;
        a.ShownTag = null;
        RefreshLooks(a, pet, owner);
    }

    private static string ModelOf(CBaseModelEntity e)
    {
        try
        {
            return e.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName ?? "";
        }
        catch
        {
            return "";
        }
    }

    private void ApplyScale(CBaseModelEntity e, PetStage stage)
    {
        var scale = stage switch
        {
            PetStage.Chick => Config.Looks.ChickScale,
            PetStage.Pullet => Config.Looks.PulletScale,
            PetStage.Hen => Config.Looks.HenScale,
            _ => 1f,
        };
        SetScale(e, scale);
    }

    private static void SetScale(CBaseModelEntity e, float scale)
    {
        if (Math.Abs(scale - 1f) < 0.001f) return;
        try
        {
            var node = e.CBodyComponent?.SceneNode;
            if (node == null) return;
            node.Scale = Math.Clamp(scale, 0.1f, 4f);
            Utilities.SetStateChanged(e, "CBaseEntity", "m_CBodyComponent");
        }
        catch
        {
            // visual only
        }
    }

    /// <summary>Coat = the model's skin (material group) index, or a material group name from CoatOverrides.</summary>
    private void ApplyCoat(CBaseModelEntity e, PetData pet)
    {
        if (pet.Stage < PetStage.Pullet) return; // every chick is the same yellow
        var coat = Catalog.CoatOf(pet.Breed, pet.Coat);
        var value = coat.Skin.ToString();
        var useGroup = Config.Looks.CoatMode.Equals("MaterialGroup", StringComparison.OrdinalIgnoreCase);
        if (Config.Looks.CoatOverrides.TryGetValue($"{pet.Breed}/{coat.Name}", out var over) && !string.IsNullOrWhiteSpace(over))
        {
            value = over.Trim();
            useGroup = useGroup || !int.TryParse(value, out _);
        }

        try
        {
            e.AcceptInput(useGroup ? "SetMaterialGroup" : "Skin", null, null, value);
        }
        catch
        {
            // cosmetic
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
            Utilities.SetStateChanged(e, "CCollisionProperty", "m_collisionAttribute");
        }
        catch
        {
            // best effort
        }
    }

    private static void SetLeader(CChicken c, CCSPlayerPawn? pawn)
    {
        var raw = pawn is { IsValid: true } ? pawn.EntityHandle.Raw : uint.MaxValue;
        if (Schema.GetSchemaValue<uint>(c.Handle, "CChicken", "m_leader") == raw) return;
        Schema.SetSchemaValue(c.Handle, "CChicken", "m_leader", raw);
    }

    private static uint LeaderRaw(CChicken c) => Schema.GetSchemaValue<uint>(c.Handle, "CChicken", "m_leader");

    private static void SetOwner(CChicken c, CCSPlayerController owner)
    {
        try
        {
            Schema.SetSchemaValue(c.Handle, "CChicken", "m_owner", owner.EntityHandle.Raw);
        }
        catch
        {
            // field only exists on newer builds
        }
    }

    // ================================================================ hat and name tag

    /// <summary>Makes the hat and name tag match the pet data (adds, swaps or removes them).</summary>
    private void RefreshLooks(PetActor a, PetData pet, CCSPlayerController owner)
    {
        if (a.Chicken is not { IsValid: true } chicken) return;

        var hatId = pet.Hat != null && Catalog.FindHat(pet.Hat) is { } h && PetLogic.HatUnlocked(pet, h, Config) ? h.Id : null;
        if (hatId != a.ShownHat || (hatId != null && a.Hat is not { IsValid: true }))
        {
            Remove(ref a.Hat);
            a.ShownHat = hatId;
            if (hatId != null) a.Hat = SpawnHat(chicken, Catalog.FindHat(hatId)!, pet.Stage);
        }

        var text = Config.Looks.NameTags
            ? Config.Looks.NameTagFormat.Replace("{owner}", owner.PlayerName).Replace("{pet}", pet.Name ?? "")
            : null;
        if (text != a.ShownTag || (text != null && a.Tag is not { IsValid: true }))
        {
            Remove(ref a.Tag);
            a.ShownTag = text;
            if (!string.IsNullOrWhiteSpace(text)) a.Tag = SpawnTag(chicken, text, pet.Stage, hatId != null);
        }
    }

    private float HatHeight(PetStage stage) => stage switch
    {
        PetStage.Chick => Config.Looks.ChickHatHeight,
        PetStage.Pullet => Config.Looks.PulletHatHeight,
        _ => Config.Looks.HenHatHeight,
    };

    private CBaseModelEntity? SpawnHat(CChicken chicken, Hat hat, PetStage stage)
    {
        try
        {
            var origin = chicken.AbsOrigin;
            if (origin == null) return null;
            var yaw = chicken.AbsRotation?.Y ?? 0f;
            var fwd = Forward(yaw);
            var e = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (e == null) return null;
            e.Spawnflags = 256;
            var at = new Vector(origin.X + fwd.X * Config.Looks.HatForward, origin.Y + fwd.Y * Config.Looks.HatForward, origin.Z + HatHeight(stage));
            e.Teleport(at, new QAngle(0, yaw, 0), new Vector(0, 0, 0));
            var kv = new CEntityKeyValues();
            kv.SetString("model", hat.Model);
            kv.SetInt("solid", 0);
            e.DispatchSpawn(kv);
            SetScale(e, stage == PetStage.Chick ? Config.Looks.ChickHatScale : Config.Looks.AdultHatScale);
            e.AcceptInput("SetParent", chicken, e, "!activator");
            if (Config.Looks.HatAttachment.Length > 0) e.AcceptInput("SetParentAttachmentMaintainOffset", null, null, Config.Looks.HatAttachment);
            return e;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Chicken Coop could not put a hat on a pet ({Model})", hat.Model);
            return null;
        }
    }

    private CPointWorldText? SpawnTag(CChicken chicken, string text, PetStage stage, bool hasHat)
    {
        try
        {
            var origin = chicken.AbsOrigin;
            if (origin == null) return null;
            var t = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
            if (t == null) return null;
            t.MessageText = text;
            t.Enabled = true;
            t.FontSize = Math.Clamp(Config.Looks.NameTagFontSize, 8f, 200f);
            t.WorldUnitsPerPx = 0.12f;
            t.Fullbright = true;
            t.DrawBackground = false;
            t.Color = ParseColor(Config.Looks.NameTagColor);
            t.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
            t.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_BOTTOM;
            t.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_AROUND_UP;
            var height = HatHeight(stage) + (hasHat ? 14f : 6f);
            t.Teleport(new Vector(origin.X, origin.Y, origin.Z + height), new QAngle(0, 0, 90), new Vector(0, 0, 0));
            t.DispatchSpawn();
            t.AcceptInput("SetParent", chicken, t, "!activator");
            return t;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Chicken Coop could not add a name tag");
            return null;
        }
    }

    private static System.Drawing.Color ParseColor(string rgb)
    {
        var parts = rgb.Split(',', StringSplitOptions.TrimEntries);
        int C(int i) => parts.Length > i && int.TryParse(parts[i], out var v) ? Math.Clamp(v, 0, 255) : 255;
        return System.Drawing.Color.FromArgb(255, C(0), C(1), C(2));
    }

    // ================================================================ egg and hatching

    private void SpawnEgg(PetActor a, CCSPlayerPawn pawn)
    {
        try
        {
            var at = BesideOf(pawn, 44f);
            var yaw = Random.Shared.Next(360);
            a.Nest = SpawnProp(Catalog.NestModel, at, new QAngle(0, yaw, 0));
            a.Egg = SpawnProp(Catalog.EggModel, new Vector(at.X, at.Y, at.Z + 4f), new QAngle(0, yaw, 0));
            a.Shown = PetStage.Egg;
            a.NextEggWiggle = Server.CurrentTime + 3f;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Chicken Coop could not place an egg");
        }
    }

    private CBaseModelEntity? SpawnProp(string model, Vector at, QAngle angles, bool solid = false)
    {
        var e = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (e == null) return null;
        if (!solid) e.Spawnflags = 256;
        e.Teleport(at, angles, new Vector(0, 0, 0));
        var kv = new CEntityKeyValues();
        kv.SetString("model", model);
        if (!solid) kv.SetInt("solid", 0);
        e.DispatchSpawn(kv);
        return e;
    }

    /// <summary>Eggs close to hatching rock in their nest now and then, louder and more often near the end.</summary>
    private void EggWiggle(CCSPlayerController p, PetData pet)
    {
        if (pet.Stage != PetStage.Egg || !_actors.TryGetValue(p.Slot, out var a) || a.Egg is not { IsValid: true } egg || a.Hatching) return;
        if (Server.CurrentTime < a.NextEggWiggle) return;
        var progress = PetLogic.Progress(pet, Config);
        a.NextEggWiggle = Server.CurrentTime + (float)(progress > 0.8 ? 6 : progress > 0.5 ? 15 : 30) + Random.Shared.NextSingle() * 6f;
        if (progress < 0.25) return;
        Wiggle(egg, 3);
        egg.EmitSound(Sounds.HatchTap);
    }

    private void Wiggle(CBaseModelEntity egg, int times)
    {
        var baseYaw = egg.AbsRotation?.Y ?? 0f;
        for (var i = 0; i < times; i++)
        {
            var roll = i % 2 == 0 ? 9f : -9f;
            var step = i;
            AddTimer(0.12f * step, () =>
            {
                if (egg.IsValid) egg.Teleport(null, new QAngle(0, baseYaw, roll), null);
            });
        }

        AddTimer(0.12f * times, () =>
        {
            if (egg.IsValid) egg.Teleport(null, new QAngle(0, baseYaw, 0), null);
        });
    }

    /// <summary>Tap, tap, tap... crack: the egg in the nest hatches and the chick walks to its owner.</summary>
    private void HatchSequence(CCSPlayerController p, PetActor a, PetData pet)
    {
        if (a.Egg is not { IsValid: true } egg || egg.AbsOrigin is not { } eggAt) return;
        a.Hatching = true;
        var spot = Copy(eggAt);
        var slot = p.Slot;
        Tell(p, "egg.hatching");

        for (var i = 0; i < 3; i++)
        {
            AddTimer(0.9f * i, () =>
            {
                if (!egg.IsValid) return;
                Wiggle(egg, 4);
                egg.EmitSound(Sounds.HatchTap);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        AddTimer(2.8f, () =>
        {
            if (!egg.IsValid)
            {
                a.Hatching = false;
                return;
            }

            egg.EmitSound(Sounds.HatchTapFinal);
            egg.Remove();
            a.Egg = SpawnProp(Catalog.HatchingEggModel, new Vector(spot.X, spot.Y, spot.Z), new QAngle(0, Random.Shared.Next(360), 0));
            a.Egg?.EmitSound(Sounds.Hatch);
            Particles.Spawn(this, Particles.Dust, spot, 2f);
        }, TimerFlags.STOP_ON_MAPCHANGE);

        AddTimer(4.4f, () =>
        {
            if (!a.Hatching) return; // the round restarted mid-hatch; the next spawn brings the chick
            a.Hatching = false;
            RemoveEgg(a);
            var pl = Utilities.GetPlayerFromSlot(slot);
            var pawn = AlivePawn(pl);
            if (pl == null || pawn == null || a.ChickenAlive) return;
            Particles.Spawn(this, Particles.Feathers, spot, 3f);
            SpawnChicken(a, pl, pawn, pet, new Vector(spot.X, spot.Y, spot.Z + 6f));
            PlayTo(pl, Sounds.EvolveChick);
            Tell(pl, "egg.hatched", pet.Name);
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    // ================================================================ removal

    private void RemoveChicken(PetActor a)
    {
        RemoveExtras(a);
        if (a.Chicken is { IsValid: true }) a.Chicken.Remove();
        a.Chicken = null;
        a.Shown = PetStage.None;
    }

    private static void RemoveExtras(PetActor a)
    {
        Remove(ref a.Hat);
        Remove(ref a.Tag);
        Remove(ref a.Feed);
        a.ShownHat = null;
        a.ShownTag = null;
    }

    private static void RemoveEgg(PetActor a)
    {
        Remove(ref a.Egg);
        Remove(ref a.Nest);
    }

    private void Despawn(PetActor a)
    {
        RemoveChicken(a);
        RemoveEgg(a);
        a.Hatching = false;
    }

    private static void Remove<T>(ref T? e) where T : CEntityInstance
    {
        try
        {
            if (e is { IsValid: true }) e.Remove();
        }
        catch
        {
            // already gone
        }

        e = null;
    }

    // ================================================================ sound

    /// <summary>A sound from the pet itself (heard by players nearby).</summary>
    private static void PlayAt(PetActor a, string sound)
    {
        try
        {
            if (a.Chicken is { IsValid: true } c) c.EmitSound(sound);
        }
        catch
        {
            // unknown sound event on an old build
        }
    }

    /// <summary>A sound only the owner hears, from their own position.</summary>
    private static void PlayTo(CCSPlayerController p, string sound)
    {
        try
        {
            var pawn = p.PlayerPawn.Value;
            if (pawn is not { IsValid: true }) return;
            var filter = new RecipientFilter { p };
            pawn.EmitSound(sound, filter);
        }
        catch
        {
            // unknown sound event on an old build
        }
    }

    // ================================================================ geometry

    private static Vector Forward(float yawDegrees)
    {
        var r = yawDegrees * MathF.PI / 180f;
        return new Vector(MathF.Cos(r), MathF.Sin(r), 0);
    }

    private static Vector BehindOf(CCSPlayerPawn pawn, float dist)
    {
        var o = pawn.AbsOrigin!;
        var f = Forward(pawn.EyeAngles?.Y ?? 0f);
        return new Vector(o.X - f.X * dist, o.Y - f.Y * dist, o.Z + 8f);
    }

    private static Vector BesideOf(CCSPlayerPawn pawn, float dist)
    {
        var o = pawn.AbsOrigin!;
        var f = Forward((pawn.EyeAngles?.Y ?? 0f) - 90f);
        return new Vector(o.X + f.X * dist, o.Y + f.Y * dist, o.Z + 2f);
    }

    private static float Distance(Vector a, Vector b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
