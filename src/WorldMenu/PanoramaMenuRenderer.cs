using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Utils;

namespace Swbs.Common;

/// <summary>
/// Draws menus on the HUD through CS2's custom_hud_layout entity: one entity loads the layout from the Workshop pack
/// (Workshop/panorama in this folder, compiled into the pack), and every player gets their own text and classes on it.
/// <list type="bullet">
/// <item>Only what changed is sent (every call is a networked update of the layout entity).</item>
/// <item>The entity is kept as a handle and created again when the map or a round restart removed it.</item>
/// <item>A newly opened menu shows <see cref="WorldMenuSettings.OpenDelay"/> later: a HUD update in the same tick as the
/// chat command that opened it can stall the client's connection.</item>
/// <item>The HUD works for living, dead and spectating players alike.</item>
/// </list>
/// Depends on CounterStrikeSharp only, so any plugin can use it. Players without the Workshop pack see nothing, so the
/// server owner turns it on (<see cref="WorldMenuSettings.Panorama"/>) once the pack has the layout.
/// </summary>
public static class PanoramaMenuRenderer
{
    private const int Slots = 65;

    private sealed class Sent
    {
        public readonly Dictionary<string, string> Vars = new();
        public readonly Dictionary<string, bool> Classes = new();
        public readonly Dictionary<string, string?> Groups = new();
    }

    private static uint _handle;
    private static CCSCustomHudLayout? _fresh;
    private static float _createdAt;
    private static float _nextErrorLog;
    private static readonly Sent?[] Cache = new Sent?[Slots];
    private static readonly bool[] Open = new bool[Slots];
    private static readonly float[] CreateAt = new float[Slots];
    private static readonly float[] FailedUntil = new float[Slots];

    /// <summary>The layout the entity loads (from the Workshop pack).</summary>
    public static string LayoutPath { get; set; } = "panorama/layout/custom_game/swbs_menu.xml";

    /// <summary>Set by the host so failures reach the server log (rate-limited).</summary>
    public static Action<Exception>? LogError { get; set; }

    /// <summary>The last failure, for diagnostics (!menutest).</summary>
    public static string? LastError { get; private set; }

    public static bool HasLayout => Layout() != null;

    public static bool IsOpen(int slot) => slot is >= 0 and < Slots && Open[slot];

    private static CCSCustomHudLayout? Layout()
    {
        if (_handle == 0) return null;
        try
        {
            var e = new CHandle<CCSCustomHudLayout>(_handle).Value;
            if (e is { IsValid: true })
            {
                _fresh = null;
                return e;
            }
        }
        catch
        {
            // fall through
        }

        // If the new entity does not resolve by handle yet, keep using the created object for a moment.
        return _fresh != null && Server.CurrentTime - _createdAt < 1f ? _fresh : null;
    }

    /// <summary>The layout entity, created when missing. A new one starts blank for everybody.</summary>
    public static CCSCustomHudLayout? Ensure()
    {
        if (Layout() is { } layout) return layout;
        _handle = 0;
        _fresh = null;
        Array.Clear(Cache);
        Array.Clear(Open);
        var e = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
        if (e == null || e.Handle == IntPtr.Zero)
        {
            LastError = "custom_hud_layout could not be created";
            return null;
        }

        e.StrLayout = LayoutPath;
        e.DispatchSpawn();
        _handle = e.EntityHandle.Raw;
        _fresh = e;
        _createdAt = Server.CurrentTime;
        return e;
    }

    /// <summary>Show or update the menu. False: the classic menu should be drawn instead.</summary>
    public static bool Show(CCSPlayerController c, WorldMenuView view, PanoramaMenuModel.Options options, float openDelay)
    {
        if (!c.IsValid || c.IsBot || c.Slot is < 0 or >= Slots) return false;
        var slot = c.Slot;
        var now = Server.CurrentTime;
        if (now < FailedUntil[slot]) return false;
        try
        {
            var layout = Ensure();
            if (layout == null)
            {
                Fail(slot);
                return false;
            }

            if (!Open[slot])
            {
                // A new menu waits a moment after it opened (it may come from a chat command this tick).
                if (CreateAt[slot] == 0f) CreateAt[slot] = now + Math.Max(0f, openDelay);
                if (now < CreateAt[slot]) return true;
            }

            Apply(layout, c, PanoramaMenuModel.Build(view, options));
            SetClass(layout, c, Cache[slot]!, PanoramaMenuModel.Root, "is-open", true);
            Open[slot] = true;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            Report(ex);
            Fail(slot);
            return false;
        }
    }

    private static void Apply(CCSCustomHudLayout layout, CCSPlayerController c, PanoramaMenuModel.State state)
    {
        var sent = Cache[c.Slot] ??= new Sent();
        foreach (var (panel, name, value) in state.Vars)
        {
            var key = panel + "|" + name;
            if (sent.Vars.TryGetValue(key, out var old) && old == value) continue;
            layout.SetDialogVariableStringForPlayer(c, panel, name, value);
            sent.Vars[key] = value;
        }

        foreach (var (panel, group, active) in state.Groups)
        {
            var key = panel + "|" + group;
            if (sent.Groups.TryGetValue(key, out var old))
            {
                if (old == active) continue;
                if (old != null) SetClass(layout, c, sent, panel, old, false);
            }
            else
            {
                // Nothing known about this group yet (new layout, new player in the slot): clear the whole group.
                foreach (var member in PanoramaMenuModel.GroupMembers(group))
                    if (member != active) SetClass(layout, c, sent, panel, member, false);
            }

            if (active != null) SetClass(layout, c, sent, panel, active, true);
            sent.Groups[key] = active;
        }

        foreach (var (panel, cls, on) in state.Classes) SetClass(layout, c, sent, panel, cls, on);
    }

    private static void SetClass(CCSCustomHudLayout layout, CCSPlayerController c, Sent sent, string panel, string cls, bool on)
    {
        var key = panel + "|" + cls;
        if (sent.Classes.TryGetValue(key, out var old) && old == on) return;
        layout.SetHasClassForPlayer(c, panel, cls, on);
        sent.Classes[key] = on;
    }

    public static void Hide(int slot)
    {
        if (slot is < 0 or >= Slots) return;
        CreateAt[slot] = 0f;
        if (!Open[slot]) return;
        Open[slot] = false;
        try
        {
            if (Layout() is { } layout && Utilities.GetPlayerFromSlot(slot) is { IsValid: true } c)
                SetClass(layout, c, Cache[slot] ??= new Sent(), PanoramaMenuModel.Root, "is-open", false);
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    /// <summary>A player joined this slot: whatever the last one left on the layout is closed and forgotten.</summary>
    public static void Reset(CCSPlayerController c)
    {
        if (!c.IsValid || c.Slot is < 0 or >= Slots) return;
        Cache[c.Slot] = null;
        Open[c.Slot] = false;
        CreateAt[c.Slot] = 0f;
        FailedUntil[c.Slot] = 0f;
        try
        {
            if (Layout() is { } layout) layout.SetHasClassForPlayer(c, PanoramaMenuModel.Root, "is-open", false);
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    /// <summary>Disconnect: forget the slot.</summary>
    public static void Clear(int slot)
    {
        if (slot is < 0 or >= Slots) return;
        Cache[slot] = null;
        Open[slot] = false;
        CreateAt[slot] = 0f;
        FailedUntil[slot] = 0f;
    }

    /// <summary>Map change: the entity is gone already, only forget it.</summary>
    public static void Forget()
    {
        _handle = 0;
        _fresh = null;
        Array.Clear(Cache);
        Array.Clear(Open);
        Array.Clear(CreateAt);
        Array.Clear(FailedUntil);
    }

    private static void Fail(int slot)
    {
        Hide(slot);
        FailedUntil[slot] = Server.CurrentTime + 5f;
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
}
