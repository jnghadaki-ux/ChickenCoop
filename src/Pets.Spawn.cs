using System.Drawing;
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
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace ChickenCoop;

public sealed partial class ChickenCoopPlugin
{
    private const uint EfBoneMerge = 0x001;
    private const uint EfBoneMergeFastCull = 0x080;

    private int ActivePets => _actors.Values.Count(a => a.Out);

    /// <summary>Puts the player's pet (or egg) in the world if it should be out and is not already.</summary>
    internal void TrySpawnPet(CCSPlayerController p)
    {
        if (!Config.Enabled || _mapDisabled || _suppressAll || _suppressed.Contains(p.Slot) || !MayOwn(p) || !OnTeam(p)) return;
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

        if (a.Perched && CanPerch(pet))
        {
            if (a.ChickenAlive) RemoveChicken(a);
            if (!a.PerchOut || a.Shown != pet.Stage) SpawnPerch(a, pawn, pet);
            else RefreshPerchLooks(a, pet);
            return;
        }

        a.Perched = false;
        if (a.PerchOut) RemovePerch(a);
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

    internal bool CanPerch(PetData pet) => Config.Perch.Enabled && PetLogic.StageListed(Config.Perch.Stages, pet.Stage);

    private void SpawnChicken(PetActor a, CCSPlayerController owner, CCSPlayerPawn? pawn, PetData pet, Vector at)
    {
        var model = Catalog.ModelFor(pet.Stage, pet.Breed);
        CChicken? chicken = null;
        try
        {
            chicken = Utilities.CreateEntityByName<CChicken>("chicken");
            if (chicken == null) return;
            var yaw = pawn?.EyeAngles?.Y ?? Random.Shared.Next(360);
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
            if (pawn != null) SetLeader(chicken, pawn);
            SetScale(chicken, StageScale(pet.Stage));
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
        a.NextIdleSound = Server.CurrentTime + 5f + Random.Shared.NextSingle() * Math.Max(1f, Config.Behaviour.IdleSoundSeconds);
        a.NextBirdWatch = Server.CurrentTime + 3f;
        a.ShownHat = null;
        a.ShownTag = null;
        a.ShownStyle = "?";
        a.TransmitDirty = true;

        SeasonPuff(at);
        RefreshLooks(a, pet, owner);
    }

    private void SeasonPuff(Vector at)
    {
        switch (_season)
        {
            case Season.Halloween:
                Fx(Particles.Rise, at, 3f);
                break;
            case Season.Christmas:
                Fx(Particles.Snow, Up(at, 6), 3f);
                break;
        }
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

    private float StageScale(PetStage stage) => stage switch
    {
        PetStage.Chick => Config.Looks.ChickScale,
        PetStage.Pullet => Config.Looks.PulletScale,
        PetStage.Hen => Config.Looks.HenScale,
        _ => 1f,
    };

    private static void SetScale(CBaseModelEntity e, float scale)
    {
        if (Math.Abs(scale - 1f) < 0.001f) return;
        try
        {
            var node = e.CBodyComponent?.SceneNode;
            if (node == null) return;
            node.Scale = Math.Clamp(scale, 0.05f, 4f);
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

    internal static void SetLeader(CChicken c, CCSPlayerPawn? pawn)
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
            // Also makes CS2 draw its own "<owner>'s Pet Chicken" label over the pet.
            Schema.SetSchemaValue(c.Handle, "CChicken", "m_owner", owner.EntityHandle.Raw);
        }
        catch
        {
            // field only exists on newer builds
        }
    }

    // ================================================================ looks: style, hat, name tag

    /// <summary>Makes the colour, hat and name tag match the pet data (adds, swaps or removes them).</summary>
    internal void RefreshLooks(PetActor a, PetData pet, CCSPlayerController owner)
    {
        if (a.Chicken is not { IsValid: true } chicken) return;

        var style = PetLogic.EffectiveStyle(pet);
        if (style != a.ShownStyle)
        {
            a.ShownStyle = style;
            ApplyStyle(chicken, pet, style, 0f);
        }

        var hatId = WornHat(pet);
        if (hatId != a.ShownHat || (hatId != null && a.Hat is not { IsValid: true }))
        {
            Remove(ref a.Hat);
            a.ShownHat = hatId;
            if (hatId != null) a.Hat = SpawnHat(chicken, Catalog.FindHat(hatId)!, pet.Stage, StageScale(pet.Stage));
            a.TransmitDirty = true;
        }

        var text = Config.Looks.NameTags
            ? Config.Looks.NameTagFormat.Replace("{owner}", owner.PlayerName).Replace("{pet}", pet.Name ?? "")
            : null;
        if (pet.IsShiny && text != null) text = "✦ " + text + " ✦";
        if (text != a.ShownTag || (text != null && a.Tag is not { IsValid: true }))
        {
            Remove(ref a.Tag);
            a.ShownTag = text;
            if (!string.IsNullOrWhiteSpace(text)) a.Tag = SpawnTag(chicken, text, pet, hatId != null);
            a.TransmitDirty = true;
        }
    }

    private string? WornHat(PetData pet) =>
        pet.Hat != null && Catalog.FindHat(pet.Hat) is { } h && PetLogic.HatUnlocked(pet, h, Config) ? h.Id : null;

    /// <summary>Colours the pet: shiny tint, seasonal style, or natural. Rainbow pets are recoloured by the follow tick.</summary>
    private static void ApplyStyle(CBaseModelEntity e, PetData pet, string? style, float hue)
    {
        var color = Color.White;
        var mode = RenderMode_t.kRenderNormal;
        if (style == Catalog.ShinyStyleId && Catalog.FindShiny(pet.Shiny) is { } shiny)
        {
            color = shiny.Rainbow ? Hue(hue) : Color.FromArgb(255, shiny.R, shiny.G, shiny.B);
        }
        else if (Catalog.FindStyle(style) is { } st)
        {
            color = Color.FromArgb(st.Alpha, st.R, st.G, st.B);
            if (st.Alpha < 255) mode = RenderMode_t.kRenderTransAlpha;
        }

        try
        {
            e.RenderMode = mode;
            e.Render = color;
            Utilities.SetStateChanged(e, "CBaseModelEntity", "m_nRenderMode");
            Utilities.SetStateChanged(e, "CBaseModelEntity", "m_clrRender");
        }
        catch
        {
            // cosmetic
        }
    }

    private static Color Hue(float degrees)
    {
        var h = (degrees % 360 + 360) % 360 / 60f;
        var x = 1 - Math.Abs(h % 2 - 1);
        var (r, g, b) = h switch
        {
            < 1 => (1f, x, 0f),
            < 2 => (x, 1f, 0f),
            < 3 => (0f, 1f, x),
            < 4 => (0f, x, 1f),
            < 5 => (x, 0f, 1f),
            _ => (1f, 0f, x),
        };
        // Pastel: keep the pet readable.
        return Color.FromArgb(255, (int)(140 + 115 * r), (int)(140 + 115 * g), (int)(140 + 115 * b));
    }

    private float OffsetHatHeight(PetStage stage) => stage switch
    {
        PetStage.Chick => Config.Looks.ChickHatHeight,
        PetStage.Pullet => Config.Looks.PulletHatHeight,
        _ => Config.Looks.HenHatHeight,
    };

    /// <summary>
    /// Puts a hat on a pet (chicken or perched model). Photo-booth hats are made for the chicken: by default they are
    /// bone-merged onto its skeleton so they sit on the head and move with it at every size. Other props (the snowman
    /// hat) always use the offset placement.
    /// </summary>
    private CBaseModelEntity? SpawnHat(CBaseModelEntity pet, Hat hat, PetStage stage, float petScale)
    {
        try
        {
            var origin = pet.AbsOrigin;
            if (origin == null) return null;
            var yaw = pet.AbsRotation?.Y ?? 0f;
            var e = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (e == null) return null;
            e.Spawnflags = 256;
            var mode = hat.Model.Contains("/silly_hats/") ? Config.Looks.HatMode : "offset";

            Vector at;
            if (mode == "offset")
            {
                var f = Forward(yaw);
                var height = OffsetHatHeight(stage) * (petScale / StageScale(stage)) + hat.Lift;
                at = new Vector(origin.X + f.X * Config.Looks.HatForward * petScale, origin.Y + f.Y * Config.Looks.HatForward * petScale, origin.Z + height);
            }
            else
            {
                at = new Vector(origin.X, origin.Y, origin.Z);
            }

            e.Teleport(at, new QAngle(0, yaw, 0), new Vector(0, 0, 0));
            var kv = new CEntityKeyValues();
            kv.SetString("model", hat.Model);
            kv.SetInt("solid", 0);
            e.DispatchSpawn(kv);
            e.AcceptInput("SetParent", pet, e, "!activator");

            switch (mode)
            {
                case "bonemerge":
                    SetScale(e, petScale * Config.Looks.HatScale);
                    e.Effects |= EfBoneMerge | EfBoneMergeFastCull;
                    Utilities.SetStateChanged(e, "CBaseEntity", "m_fEffects");
                    break;
                case "attachment":
                    SetScale(e, petScale * Config.Looks.HatScale);
                    e.AcceptInput("SetParentAttachment", null, null, Config.Looks.HatAttachment);
                    break;
                default:
                    SetScale(e, petScale * Config.Looks.HatScale * hat.Scale);
                    if (Config.Looks.HatAttachment.Length > 0 && hat.Model.Contains("/silly_hats/"))
                        e.AcceptInput("SetParentAttachmentMaintainOffset", null, null, Config.Looks.HatAttachment);
                    break;
            }

            return e;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Chicken Coop could not put a hat on a pet ({Model})", hat.Model);
            return null;
        }
    }

    private CPointWorldText? SpawnTag(CBaseModelEntity pet, string text, PetData data, bool hasHat)
    {
        try
        {
            var origin = pet.AbsOrigin;
            if (origin == null) return null;
            var t = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
            if (t == null) return null;
            t.MessageText = text;
            t.Enabled = true;
            t.FontSize = Math.Clamp(Config.Looks.NameTagFontSize, 8f, 200f);
            t.WorldUnitsPerPx = 0.12f;
            t.Fullbright = true;
            t.DrawBackground = false;
            t.Color = TagColor(data);
            t.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
            t.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_BOTTOM;
            t.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_AROUND_UP;
            var height = OffsetHatHeight(data.Stage) + (hasHat ? 14f : 6f);
            t.Teleport(Up(origin, height), new QAngle(0, 0, 90), new Vector(0, 0, 0));
            t.DispatchSpawn();
            t.AcceptInput("SetParent", pet, t, "!activator");
            return t;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Chicken Coop could not add a name tag");
            return null;
        }
    }

    private Color TagColor(PetData pet)
    {
        if (pet.IsShiny) return Color.FromArgb(255, 255, 215, 90);
        return _season switch
        {
            Season.Halloween => Color.FromArgb(255, 255, 150, 40),
            Season.Christmas => Color.FromArgb(255, 190, 230, 255),
            _ => ParseColor(Config.Looks.NameTagColor),
        };
    }

    private static Color ParseColor(string rgb)
    {
        var parts = rgb.Split(',', StringSplitOptions.TrimEntries);
        int C(int i) => parts.Length > i && int.TryParse(parts[i], out var v) ? Math.Clamp(v, 0, 255) : 255;
        return Color.FromArgb(255, C(0), C(1), C(2));
    }

    // ================================================================ shoulder perch

    private void SpawnPerch(PetActor a, CCSPlayerPawn pawn, PetData pet)
    {
        RemovePerch(a);
        try
        {
            var o = pawn.AbsOrigin!;
            var yaw = pawn.AbsRotation?.Y ?? pawn.EyeAngles?.Y ?? 0f;
            var f = Forward(yaw);
            var r = Forward(yaw - 90f);
            var cfg = Config.Perch;
            var at = new Vector(o.X + f.X * cfg.Forward + r.X * cfg.Right, o.Y + f.Y * cfg.Forward + r.Y * cfg.Right, o.Z + cfg.Up);
            var model = Catalog.ModelFor(pet.Stage, pet.Breed);
            var e = SpawnProp(model, at, new QAngle(0, yaw, 0));
            if (e == null) return;
            var scale = StageScale(pet.Stage) * cfg.Scale;
            SetScale(e, scale);
            ApplyCoat(e, pet);
            ApplyStyle(e, pet, PetLogic.EffectiveStyle(pet), a.RainbowHue);
            e.AcceptInput("SetParent", pawn, e, "!activator");
            if (cfg.Attachment.Length > 0) e.AcceptInput("SetParentAttachmentMaintainOffset", null, null, cfg.Attachment);
            a.Perch = e;
            a.Shown = pet.Stage;
            a.ShownStyle = PetLogic.EffectiveStyle(pet);
            a.SpawnedAt = Server.CurrentTime;
            RefreshPerchLooks(a, pet);
            PlayAt(a, Sounds.Pick(pet.Stage, Sounds.FlapShort, Sounds.FlapShortChick));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Chicken Coop could not perch a pet");
        }

        a.TransmitDirty = true;
    }

    private void RefreshPerchLooks(PetActor a, PetData pet)
    {
        if (a.Perch is not { IsValid: true } perch) return;
        var hatId = WornHat(pet);
        if (hatId == a.ShownHat && (hatId == null || a.PerchHat is { IsValid: true })) return;
        Remove(ref a.PerchHat);
        a.ShownHat = hatId;
        if (hatId != null) a.PerchHat = SpawnHat(perch, Catalog.FindHat(hatId)!, pet.Stage, StageScale(pet.Stage) * Config.Perch.Scale);
        a.TransmitDirty = true;
    }

    // ================================================================ egg and hatching

    private void SpawnEgg(PetActor a, CCSPlayerPawn pawn)
    {
        try
        {
            var at = BesideOf(pawn, 44f);
            var yaw = Random.Shared.Next(360);
            a.Nest = SpawnProp(Catalog.NestModel, at, new QAngle(0, yaw, 0));
            a.Egg = SpawnProp(Catalog.EggModel, Up(at, 4f), new QAngle(0, yaw, 0));
            a.Shown = PetStage.Egg;
            a.NextEggWiggle = Server.CurrentTime + 3f;
            a.TransmitDirty = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Chicken Coop could not place an egg");
        }
    }

    private CBaseModelEntity? SpawnProp(string model, Vector at, QAngle angles)
    {
        var e = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (e == null) return null;
        e.Spawnflags = 256;
        e.Teleport(at, angles, new Vector(0, 0, 0));
        var kv = new CEntityKeyValues();
        kv.SetString("model", model);
        kv.SetInt("solid", 0);
        e.DispatchSpawn(kv);
        return e;
    }

    /// <summary>Eggs close to hatching rock in their nest now and then, more often near the end.</summary>
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
            AddTimer(0.12f * i, () =>
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
        var spot = Up(eggAt, 0);
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
            a.Egg = SpawnProp(Catalog.HatchingEggModel, spot, new QAngle(0, Random.Shared.Next(360), 0));
            a.Egg?.EmitSound(Sounds.Hatch);
            a.TransmitDirty = true;
            Fx(Particles.Dust, spot, 2f);
        }, TimerFlags.STOP_ON_MAPCHANGE);

        AddTimer(4.4f, () =>
        {
            if (!a.Hatching) return; // the round restarted mid-hatch; the next spawn brings the chick
            a.Hatching = false;
            RemoveEgg(a);
            var pl = Utilities.GetPlayerFromSlot(slot);
            var pawn = AlivePawn(pl);
            if (pl == null || pawn == null || a.ChickenAlive) return;
            Fx(Particles.Feathers, spot, 3f);
            Fx(Particles.Confetti, Up(spot, 6), 3f);
            SpawnChicken(a, pl, pawn, pet, Up(spot, 6f));
            PlayTo(pl, Sounds.EvolveChick);
            Tell(pl, "egg.hatched", pet.Name);
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    /// <summary>A hen lays an egg: it drops under her to be picked up (or goes straight to the basket if she is not out).</summary>
    private void LayEgg(CCSPlayerController p, PetActor a, PetData pet)
    {
        if (a.Chicken is { IsValid: true } c && c.AbsOrigin is { } at)
        {
            var egg = SpawnProp(Catalog.EggModel, Up(at, 2f), new QAngle(0, Random.Shared.Next(360), 0));
            if (egg != null)
            {
                a.LaidEggs.Add(egg);
                a.TransmitDirty = true;
                egg.EmitSound(Sounds.HatchTap);
                c.DesiredActivity = EChickenActivity.Squat;
                Tell(p, Config.Eggs.Pickup == "anyone" ? "eggs.laid.anyone" : "eggs.laid", pet.Name);
                return;
            }
        }

        if (PetLogic.CollectEgg(pet, Config)) Tell(p, "eggs.laid.basket", pet.Name, pet.BasketEggs);
        else Tell(p, "eggs.basket_full", Config.Eggs.MaxBasket);
        _store.MarkDirty();
    }

    // ================================================================ removal

    private void RemoveChicken(PetActor a)
    {
        RemoveExtras(a);
        if (a.Chicken is { IsValid: true }) a.Chicken.Remove();
        a.Chicken = null;
        a.Shown = PetStage.None;
        a.TransmitDirty = true;
    }

    private static void RemoveExtras(PetActor a)
    {
        Remove(ref a.Hat);
        Remove(ref a.Tag);
        Remove(ref a.Feed);
        a.ShownHat = null;
        a.ShownTag = null;
        a.ShownStyle = "?";
        a.TransmitDirty = true;
    }

    private static void RemovePerch(PetActor a)
    {
        Remove(ref a.PerchHat);
        Remove(ref a.Perch);
        a.ShownHat = null;
        a.TransmitDirty = true;
    }

    private static void RemoveEgg(PetActor a)
    {
        Remove(ref a.Egg);
        Remove(ref a.Nest);
        a.TransmitDirty = true;
    }

    /// <summary>Removes everything the pet has in the world. Laid eggs stay unless <paramref name="eggsToo"/>.</summary>
    internal void Despawn(PetActor a, bool eggsToo)
    {
        RemoveChicken(a);
        RemovePerch(a);
        RemoveEgg(a);
        if (eggsToo)
        {
            foreach (var e in a.LaidEggs.Where(e => e.IsValid)) e.Remove();
            a.LaidEggs.Clear();
        }
        else
        {
            a.LaidEggs.RemoveAll(e => !e.IsValid);
        }

        a.Hatching = false;
        a.TransmitDirty = true;
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

    // ================================================================ sound and effects

    /// <summary>A sound from the pet itself (heard by players nearby).</summary>
    private static void PlayAt(PetActor a, string sound)
    {
        try
        {
            if (a.Chicken is { IsValid: true } c) c.EmitSound(sound);
            else if (a.Perch is { IsValid: true } p) p.EmitSound(sound);
        }
        catch
        {
            // unknown sound event on an old build
        }
    }

    /// <summary>A sound only the owner hears.</summary>
    private static void PlayTo(CCSPlayerController p, string sound)
    {
        try
        {
            var pawn = p.PlayerPawn.Value;
            if (pawn is not { IsValid: true }) return;
            pawn.EmitSound(sound, new RecipientFilter { p });
        }
        catch
        {
            // unknown sound event on an old build
        }
    }

    /// <summary>A one-off stock particle effect at a point, removed after <paramref name="seconds"/>.</summary>
    internal void Fx(string effect, Vector at, float seconds)
    {
        try
        {
            var fx = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
            if (fx == null) return;
            fx.Teleport(at, new QAngle(0, 0, 0), new Vector(0, 0, 0));
            var kv = new CEntityKeyValues();
            kv.SetString("effect_name", effect);
            kv.SetBool("start_active", true);
            fx.DispatchSpawn(kv);
            fx.AcceptInput("Start");
            AddTimer(seconds, () =>
            {
                if (fx.IsValid) fx.Remove();
            });
        }
        catch
        {
            // purely cosmetic
        }
    }

    private void Celebrate(PetActor a, string effect)
    {
        var e = a.Chicken is { IsValid: true } ? (CBaseEntity)a.Chicken : a.Perch;
        if (e is { IsValid: true } && e.AbsOrigin is { } at) Fx(effect, Up(at, 10), 3f);
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
