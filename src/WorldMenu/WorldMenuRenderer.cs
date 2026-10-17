using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace Swbs.Common;

/// <summary>
/// Draws <see cref="WorldMenuPanel"/>s as point_worldtext entities a few units in front of a player's eyes. The panel
/// is parented to the player's pawn, so it moves and turns with them; when they look up or down (or crouch) it is
/// re-anchored to the new view height. Every layer is one entity (one colour, same line count, top-aligned), so the
/// columns line up exactly. Only the owner receives their panel (<see cref="FilterTransmit"/>). Depends on
/// CounterStrikeSharp only, so any plugin can use it. (Older CS2 builds let screen menus parent to a second viewmodel;
/// the viewmodel is no longer a server entity.)
/// </summary>
public static class WorldMenuRenderer
{
    private sealed class Layer
    {
        public required CPointWorldText Entity;
        public required WorldMenuLayer Spec;
        public int ShownAlpha;
    }

    private sealed class Panel
    {
        public readonly Dictionary<string, Layer> Layers = new();
        public uint Pawn;
        public float Pitch, EyeZ, Yaw, PawnYaw;
        public string Shape = "";
        public float OpenedAt;
        public bool FadeDone;
        public uint[] Indices = Array.Empty<uint>();
    }

    private const int Slots = 65;
    private static readonly Panel?[] Panels = new Panel?[Slots];
    private static int _open;

    public static bool IsOpen(int slot) => slot is >= 0 and < Slots && Panels[slot] != null;

    /// <summary>
    /// Show or update the panel. False when it cannot be drawn (dead, no pawn, entity limit): the caller falls back to
    /// its centre-screen menu.
    /// </summary>
    public static bool Show(CCSPlayerController c, WorldMenuPanel panel, WorldMenuSettings s, string position, float sizeScale)
    {
        if (!c.IsValid || c.IsBot || c.Slot is < 0 or >= Slots) return false;
        var pawn = c.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || pawn.AbsOrigin == null) return false;
        try
        {

            var fov = c.DesiredFOV == 0 ? 90f : c.DesiredFOV;
            var fovScale = MathF.Tan(fov / 2f * MathF.PI / 180f);
            var fontSize = Math.Clamp(s.FontSize * sizeScale * fovScale, 8f, 120f);
            var shape = $"{position}|{fontSize:0.00}|{panel.Width:0.0}|{panel.Lines}";

            var p = Panels[c.Slot];
            if (p != null && (p.Pawn != pawn.EntityHandle.Raw || p.Layers.Values.Any(l => !l.Entity.IsValid) || p.Shape != shape))
            {
                Destroy(c.Slot, keepFade: p.Shape.Length > 0 && p.Pawn == pawn.EntityHandle.Raw);
                p = Panels[c.Slot];
            }

            if (p == null)
            {
                p = new Panel { OpenedAt = Server.CurrentTime, FadeDone = !s.Animations || s.FadeInSeconds <= 0 };
                Panels[c.Slot] = p;
                _open++;
            }

            var pitch = Math.Clamp(pawn.EyeAngles.X, -s.MaxPitch, s.MaxPitch);
            var eyeZ = pawn.ViewOffset.Z;
            var yaw = pawn.EyeAngles.Y;
            var pawnYaw = pawn.AbsRotation?.Y ?? yaw;
            // Where the panel faces now: its anchor yaw turned with the pawn (if the pawn turns with the view, never off).
            var facing = p.Yaw + AngleDiff(pawnYaw, p.PawnYaw);
            var reanchor = p.Layers.Count > 0 && (Math.Abs(pitch - p.Pitch) > s.PitchStep || Math.Abs(eyeZ - p.EyeZ) > 6f
                || Math.Abs(AngleDiff(yaw, facing)) > 8f);
            if (p.Layers.Count == 0 || reanchor)
            {
                p.Pitch = pitch;
                p.EyeZ = eyeZ;
                p.Yaw = yaw;
                p.PawnYaw = pawnYaw;
            }

            p.Pawn = pawn.EntityHandle.Raw;
            p.Shape = shape;
            var em = fontSize * s.WorldUnitsPerPx;
            var left = position switch
            {
                "center" => -panel.Width * em / 2f,
                "right" => -s.LeftEdge * fovScale - panel.Width * em,
                _ => s.LeftEdge * fovScale
            };
            var top = s.TopEdge * fovScale;
            var fade = p.FadeDone ? 1f : Math.Clamp((Server.CurrentTime - p.OpenedAt) / s.FadeInSeconds, 0.05f, 1f);

            var wanted = new HashSet<string>();
            foreach (var spec in panel.Layers)
            {
                wanted.Add(spec.Key);
                var alpha = (int)(spec.Alpha * fade);
                if (p.Layers.TryGetValue(spec.Key, out var have) && Math.Abs(have.Spec.X - spec.X) < 0.01f)
                {
                    Update(have, spec, alpha);
                    if (reanchor) Place(have.Entity, pawn, p.Pitch, s, left + spec.X * em, top);
                    continue;
                }

                if (have != null) Kill(have.Entity);
                var ent = Create(pawn, p.Pitch, spec, s, fontSize, left + spec.X * em, top, alpha);
                if (ent == null)
                {
                    Destroy(c.Slot);
                    return false;
                }

                p.Layers[spec.Key] = new Layer { Entity = ent, Spec = spec, ShownAlpha = alpha };
            }

            foreach (var key in p.Layers.Keys.Where(k => !wanted.Contains(k)).ToList())
            {
                Kill(p.Layers[key].Entity);
                p.Layers.Remove(key);
            }

            p.Indices = p.Layers.Values.Select(l => l.Entity.Index).ToArray();
            return true;
        }
        catch
        {
            Destroy(c.Slot);
            return false;
        }
    }

    public static void Hide(int slot) => Destroy(slot);

    /// <summary>a - b in degrees, wrapped to (-180, 180].</summary>
    public static float AngleDiff(float a, float b)
    {
        var d = (a - b) % 360f;
        if (d > 180f) d -= 360f;
        if (d <= -180f) d += 360f;
        return d;
    }

    public static void HideAll()
    {
        for (var i = 0; i < Slots; i++) Destroy(i);
    }

    /// <summary>Map change: the entities are gone already, only forget them.</summary>
    public static void Forget()
    {
        for (var i = 0; i < Slots; i++) Panels[i] = null;
        _open = 0;
    }

    /// <summary>Fade-in steps for panels that just opened (call every tick or two).</summary>
    public static void Tick()
    {
        if (_open == 0) return;
        var now = Server.CurrentTime;
        foreach (var p in Panels)
        {
            if (p == null || p.FadeDone) continue;
            var settings = WorldMenuSettingsHolder.Current;
            var t = settings.FadeInSeconds <= 0 ? 1f : Math.Clamp((now - p.OpenedAt) / settings.FadeInSeconds, 0.05f, 1f);
            foreach (var l in p.Layers.Values) Update(l, l.Spec, (int)(l.Spec.Alpha * t));
            if (t >= 1f) p.FadeDone = true;
        }
    }

    /// <summary>CheckTransmit listener: a player receives only their own panel.</summary>
    public static void FilterTransmit(CCheckTransmitInfoList infoList)
    {
        if (_open == 0) return;
        foreach (var (info, viewer) in infoList)
        {
            var slot = viewer is { IsValid: true } ? viewer.Slot : -1;
            for (var i = 0; i < Slots; i++)
            {
                if (i == slot || Panels[i] is not { } p) continue;
                foreach (var index in p.Indices) info.TransmitEntities.Remove(index);
            }
        }
    }

    // ------------------------------------------------------------------

    private static void Update(Layer l, WorldMenuLayer spec, int alpha)
    {
        var e = l.Entity;
        if (!e.IsValid) return;
        if (l.Spec.Text != spec.Text)
        {
            e.MessageText = spec.Text;
            Utilities.SetStateChanged(e, "CPointWorldText", "m_messageText");
        }

        if (l.Spec.Color != spec.Color || l.ShownAlpha != alpha)
        {
            e.Color = WorldMenuTheme.Parse(spec.Color, alpha);
            Utilities.SetStateChanged(e, "CPointWorldText", "m_Color");
            l.ShownAlpha = alpha;
        }

        l.Spec = spec;
    }

    private static CPointWorldText? Create(CCSPlayerPawn pawn, float pitch, WorldMenuLayer spec, WorldMenuSettings s,
        float fontSize, float x, float y, int alpha)
    {
        var ent = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (ent == null || !ent.IsValid) return null;
        ent.MessageText = spec.Text;
        ent.Enabled = true;
        ent.FontSize = fontSize;
        ent.FontName = s.FontName;
        ent.Fullbright = true;
        ent.WorldUnitsPerPx = s.WorldUnitsPerPx;
        ent.Color = WorldMenuTheme.Parse(spec.Color, alpha);
        ent.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_LEFT;
        ent.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_TOP;
        ent.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;
        ent.RenderMode = RenderMode_t.kRenderNormal;
        ent.DepthOffset = spec.Depth;
        if (spec.Background)
        {
            ent.DrawBackground = true;
            ent.BackgroundBorderWidth = s.PanelBorder;
            ent.BackgroundBorderHeight = s.PanelBorder;
            ent.BackgroundWorldToUV = 0.05f;
        }

        ent.DispatchSpawn();
        Place(ent, pawn, pitch, s, x, y, parented: false);
        return ent;
    }

    /// <summary>Put a layer at (x, y) on the panel plane in front of the eyes (view yaw, anchored pitch) and parent it to the pawn.</summary>
    private static void Place(CPointWorldText ent, CCSPlayerPawn pawn, float pitch, WorldMenuSettings s, float x, float y, bool parented = true)
    {
        if (parented) ent.AcceptInput("ClearParent");
        var angles = new QAngle(pitch, pawn.EyeAngles.Y, 0);
        Vector forward = new(), right = new(), up = new();
        NativeAPI.AngleVectors(angles.Handle, forward.Handle, right.Handle, up.Handle);
        var o = pawn.AbsOrigin!;
        var eye = new Vector(o.X, o.Y, o.Z + pawn.ViewOffset.Z);
        var d = s.Distance;
        var pos = new Vector(
            eye.X + forward.X * d + right.X * x + up.X * y,
            eye.Y + forward.Y * d + right.Y * x + up.Y * y,
            eye.Z + forward.Z * d + right.Z * x + up.Z * y);
        ent.Teleport(pos, new QAngle(0, angles.Y + 270f, 90f - pitch), new Vector(0, 0, 0));
        ent.AcceptInput("SetParent", pawn, null, "!activator");
    }

    private static void Destroy(int slot, bool keepFade = false)
    {
        if (slot is < 0 or >= Slots || Panels[slot] is not { } p) return;
        foreach (var l in p.Layers.Values) Kill(l.Entity);
        Panels[slot] = null;
        _open = Math.Max(0, _open - 1);
        if (keepFade)
        {
            // Rebuilt (new shape): no second fade-in.
            Panels[slot] = new Panel { OpenedAt = p.OpenedAt, FadeDone = true };
            _open++;
        }
    }

    private static void Kill(CPointWorldText e)
    {
        try { if (e.IsValid) e.Remove(); }
        catch { /* already gone */ }
    }
}

/// <summary>The settings the renderer's fade uses (set by the host plugin when its config loads).</summary>
public static class WorldMenuSettingsHolder
{
    public static WorldMenuSettings Current { get; set; } = new();
}
