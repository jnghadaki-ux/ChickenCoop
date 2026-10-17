using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace Swbs.Common;

/// <summary>
/// Draws <see cref="WorldMenuPanel"/>s as point_worldtext entities a few units in front of a player's eyes. Every layer
/// is one entity (one colour, same line count, top-aligned), so the columns line up exactly.
/// <list type="bullet">
/// <item>The panel is moved in front of the view every tick (<see cref="Tick"/>). It is not parented to the player:
/// CS2 does not draw the local player's own pawn - or anything parented to it - in first person, and the viewmodel is no
/// longer a server entity that text could follow.</item>
/// <item>Entities are kept as handles and looked up every time, never as stored objects: the round restart removes them,
/// and touching a removed entity crashes the server.</item>
/// <item>Only the owner receives their panel (<see cref="FilterTransmit"/>).</item>
/// </list>
/// Depends on CounterStrikeSharp only, so any plugin can use it.
/// </summary>
public static class WorldMenuRenderer
{
    private sealed class Layer
    {
        public uint Handle;
        public uint Index;

        /// <summary>The entity as created: used only until the entity system lists it (the first frame or so).</summary>
        public CPointWorldText? Fresh;
        public float CreatedAt;
        public required WorldMenuLayer Spec;
        public float X, Y;
        public int ShownAlpha;
    }

    private sealed class Panel
    {
        public readonly Dictionary<string, Layer> Layers = new();
        public string Shape = "";
        public float OpenedAt;
        public bool FadeDone;
        public uint[] Indices = Array.Empty<uint>();
        public bool Parented;
        public float Pitch, EyeZ;
    }

    private const int Slots = 65;
    private static readonly Panel?[] Panels = new Panel?[Slots];
    private static int _open;
    private static float _nextErrorLog;

    /// <summary>Set by the host so draw failures reach the server log (rate-limited).</summary>
    public static Action<Exception>? LogError { get; set; }

    /// <summary>The last failure, for diagnostics (!menutest).</summary>
    public static string? LastError { get; private set; }

    /// <summary>Entities in a player's open panel (0 = none), for diagnostics.</summary>
    public static int LayerCount(int slot) => slot is >= 0 and < Slots && Panels[slot] is { } p ? p.Layers.Values.Count(l => Ent(l) != null) : 0;

    public static bool IsOpen(int slot) => slot is >= 0 and < Slots && Panels[slot] != null;

    private static CPointWorldText? Ent(Layer l)
    {
        if (l.Handle == 0) return null;
        try
        {
            var e = new CHandle<CPointWorldText>(l.Handle).Value;
            if (e is { IsValid: true })
            {
                l.Fresh = null;
                return e;
            }
        }
        catch
        {
            // fall through
        }

        // A just-created entity is not listed yet (CounterStrikeSharp calls it invalid until it is): keep using it for a
        // moment. Never longer - a stored entity object goes stale when the round restart removes it.
        return l.Fresh != null && Server.CurrentTime - l.CreatedAt < 1f ? l.Fresh : null;
    }

    private static CCSPlayerPawn? LivePawn(CCSPlayerController? c)
    {
        if (c is not { IsValid: true }) return null;
        var pawn = c.PlayerPawn.Value;
        return pawn is { IsValid: true } && pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE && pawn.AbsOrigin != null ? pawn : null;
    }

    /// <summary>
    /// Show or update the panel. False when it cannot be drawn (dead, no pawn, entity limit): the caller falls back to
    /// its centre-screen menu.
    /// </summary>
    public static bool Show(CCSPlayerController c, WorldMenuPanel panel, WorldMenuSettings s, string position, float sizeScale)
    {
        if (!c.IsValid || c.IsBot || c.Slot is < 0 or >= Slots) return false;
        var pawn = LivePawn(c);
        if (pawn == null) return false;
        try
        {
            var fov = c.DesiredFOV == 0 ? 90f : c.DesiredFOV;
            var fovScale = MathF.Tan(fov / 2f * MathF.PI / 180f);
            var fontSize = Math.Clamp(s.FontSize * sizeScale * fovScale, 8f, 120f);
            var shape = $"{position}|{fontSize:0.00}|{panel.Width:0.0}|{panel.Lines}";

            var p = Panels[c.Slot];
            // A different size or shape, or entities removed by the round restart: build it again.
            if (p != null && (p.Shape != shape || p.Layers.Values.Any(l => Ent(l) == null)))
            {
                var fadeDone = p.FadeDone;
                var openedAt = p.OpenedAt;
                Destroy(c.Slot);
                p = new Panel { OpenedAt = openedAt, FadeDone = fadeDone };
                Panels[c.Slot] = p;
                _open++;
            }

            if (p == null)
            {
                p = new Panel { OpenedAt = Server.CurrentTime, FadeDone = !s.Animations || s.FadeInSeconds <= 0 };
                Panels[c.Slot] = p;
                _open++;
            }

            p.Shape = shape;
            var parent = s.Follow.Equals("parent", StringComparison.OrdinalIgnoreCase);
            if (p.Layers.Count == 0)
            {
                p.Parented = parent;
                p.Pitch = Math.Clamp(pawn.EyeAngles.X, -s.MaxPitch, s.MaxPitch);
                p.EyeZ = pawn.ViewOffset.Z;
            }

            var em = fontSize * s.WorldUnitsPerPx;
            var left = position switch
            {
                "center" => -panel.Width * em / 2f,
                "right" => -s.LeftEdge * fovScale - panel.Width * em,
                _ => s.LeftEdge * fovScale
            };
            var top = s.TopEdge * fovScale;
            var fade = p.FadeDone ? 1f : Math.Clamp((Server.CurrentTime - p.OpenedAt) / Math.Max(0.01f, s.FadeInSeconds), 0.05f, 1f);

            var wanted = new HashSet<string>();
            foreach (var spec in panel.Layers)
            {
                wanted.Add(spec.Key);
                var alpha = (int)(spec.Alpha * fade);
                var x = left + spec.X * em;
                if (p.Layers.TryGetValue(spec.Key, out var have) && Math.Abs(have.X - x) < 0.001f && have.Spec.Right == spec.Right && Ent(have) is { } e)
                {
                    Update(e, have, spec, alpha);
                    continue;
                }

                if (have != null) Kill(have);
                var ent = Create(spec, s, fontSize, alpha);
                if (ent == null)
                {
                    LastError = "point_worldtext could not be created";
                    Destroy(c.Slot);
                    return false;
                }

                var layer = new Layer { Handle = ent.EntityHandle.Raw, Index = ent.Index, Fresh = ent, CreatedAt = Server.CurrentTime, Spec = spec, X = x, Y = top, ShownAlpha = alpha };
                p.Layers[spec.Key] = layer;
                Place(ent, c, pawn, layer, s, p);
            }

            foreach (var key in p.Layers.Keys.Where(k => !wanted.Contains(k)).ToList())
            {
                Kill(p.Layers[key]);
                p.Layers.Remove(key);
            }

            p.Indices = p.Layers.Values.Select(l => l.Index).ToArray();
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            Report(ex);
            Destroy(c.Slot);
            return false;
        }
    }

    private static void Report(Exception ex)
    {
        LastError = ex.GetType().Name + ": " + ex.Message;
        var now = Server.CurrentTime;
        if (LogError == null || now < _nextErrorLog) return;
        _nextErrorLog = now + 30f;
        try { LogError(ex); }
        catch { /* logging must never break the menu */ }
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

    /// <summary>Every tick: keep each panel in front of its owner's eyes, and fade new panels in.</summary>
    public static void Tick()
    {
        if (_open == 0) return;
        var now = Server.CurrentTime;
        var settings = WorldMenuSettingsHolder.Current;
        for (var slot = 0; slot < Slots; slot++)
        {
            var p = Panels[slot];
            if (p == null) continue;
            var c = Utilities.GetPlayerFromSlot(slot);
            var pawn = LivePawn(c);
            if (pawn == null)
            {
                Destroy(slot);
                continue;
            }

            try
            {
                var t = p.FadeDone || settings.FadeInSeconds <= 0 ? 1f : Math.Clamp((now - p.OpenedAt) / settings.FadeInSeconds, 0.05f, 1f);
                // Parented panels move with the player by themselves; they are only re-aimed when the view tilts or crouches.
                var pitch = Math.Clamp(pawn.EyeAngles.X, -settings.MaxPitch, settings.MaxPitch);
                var move = !p.Parented || Math.Abs(pitch - p.Pitch) > settings.PitchStep || Math.Abs(pawn.ViewOffset.Z - p.EyeZ) > 6f;
                if (p.Parented && move)
                {
                    p.Pitch = pitch;
                    p.EyeZ = pawn.ViewOffset.Z;
                }

                foreach (var l in p.Layers.Values)
                {
                    if (Ent(l) is not { } e) continue;
                    if (!p.FadeDone) Update(e, l, l.Spec, (int)(l.Spec.Alpha * t));
                    if (move) Place(e, c!, pawn, l, settings, p);
                }

                if (t >= 1f) p.FadeDone = true;
            }
            catch (Exception ex)
            {
                Report(ex);
                Destroy(slot);
            }
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

    private static void Update(CPointWorldText e, Layer l, WorldMenuLayer spec, int alpha)
    {
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

    private static CPointWorldText? Create(WorldMenuLayer spec, WorldMenuSettings s, float fontSize, int alpha)
    {
        // No IsValid check here: a just-created entity reads as invalid until it has spawned.
        var ent = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (ent == null || ent.Handle == IntPtr.Zero) return null;
        ent.MessageText = spec.Text;
        ent.Enabled = true;
        ent.FontSize = fontSize;
        ent.FontName = s.FontName;
        ent.Fullbright = true;
        ent.WorldUnitsPerPx = s.WorldUnitsPerPx;
        ent.Color = WorldMenuTheme.Parse(spec.Color, alpha);
        ent.JustifyHorizontal = spec.Right
            ? PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_RIGHT
            : PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_LEFT;
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
        return ent;
    }

    /// <summary>Put a layer at its (x, y) on the panel plane in front of the eyes, facing the player.</summary>
    private static void Place(CPointWorldText ent, CCSPlayerController c, CCSPlayerPawn pawn, Layer l, WorldMenuSettings s, Panel p)
    {
        var o = pawn.AbsOrigin!;
        var angles = pawn.EyeAngles;
        var pitch = p.Parented ? p.Pitch : Math.Clamp(angles.X, -s.MaxPitch, s.MaxPitch);
        var view = new QAngle(pitch, angles.Y, 0);
        Vector forward = new(), right = new(), up = new();
        NativeAPI.AngleVectors(view.Handle, forward.Handle, right.Handle, up.Handle);
        var v = pawn.AbsVelocity;
        // What the player sees of a moved entity is about one latency old: aim where they will be by then.
        var lead = !p.Parented && s.MoveLead ? Math.Clamp(c.Ping / 1000f + 0.03f, 0f, 0.25f) : 0f;
        var d = s.Distance;
        var pos = new Vector(
            o.X + v.X * lead + forward.X * d + right.X * l.X + up.X * l.Y,
            o.Y + v.Y * lead + forward.Y * d + right.Y * l.X + up.Y * l.Y,
            o.Z + v.Z * lead + pawn.ViewOffset.Z + forward.Z * d + right.Z * l.X + up.Z * l.Y);
        var face = new QAngle(0, angles.Y + 270f, 90f - pitch);
        if (p.Parented)
        {
            ent.AcceptInput("ClearParent");
            ent.Teleport(pos, face, new Vector(0, 0, 0));
            ent.AcceptInput("SetParent", pawn, ent, "!activator");
        }
        else ent.Teleport(pos, face, new Vector(v.X, v.Y, v.Z));
    }

    private static void Destroy(int slot)
    {
        if (slot is < 0 or >= Slots || Panels[slot] is not { } p) return;
        foreach (var l in p.Layers.Values) Kill(l);
        Panels[slot] = null;
        _open = Math.Max(0, _open - 1);
    }

    private static void Kill(Layer l)
    {
        try { Ent(l)?.Remove(); }
        catch { /* already gone */ }
        l.Handle = 0;
    }
}

/// <summary>The settings the renderer's per-tick work uses (set by the host plugin when its config loads).</summary>
public static class WorldMenuSettingsHolder
{
    public static WorldMenuSettings Current { get; set; } = new();
}
