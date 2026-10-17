using ChickenCoop.Model;
using ChickenCoop.Runtime;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace ChickenCoop;

public sealed partial class ChickenCoopPlugin
{
    private readonly bool[] _hideOthers = new bool[MaxSlots];
    private readonly bool[] _hideTags = new bool[MaxSlots];
    /// <summary>Per viewer: entities left out for distance (Performance settings), rebuilt by <see cref="CullTick"/>.</summary>
    private readonly uint[][] _cull = Enumerable.Repeat(Array.Empty<uint>(), MaxSlots).ToArray();

    /// <summary>
    /// Five times a second: for each living player, leave out name tags beyond TagDrawDistance and, with MaxVisiblePets,
    /// every pet but the closest few. Dead players and spectators see everything (their camera is elsewhere).
    /// </summary>
    private void CullTick()
    {
        var perf = Config.Performance;
        var tagRange = perf.TagDrawDistance;
        var cap = perf.MaxVisiblePets;
        if (tagRange <= 0 && cap <= 0)
        {
            for (var i = 0; i < MaxSlots; i++) _cull[i] = Array.Empty<uint>();
            return;
        }

        // Where each pet is (chicken, perch or egg).
        var spots = new List<(PetActor A, Vector At)>();
        foreach (var a in _actors.Values)
        {
            var body = a.Chicken is { IsValid: true } c ? (CBaseEntity)c : a.Perch is { IsValid: true } pr ? pr : a.Egg is { IsValid: true } e ? e : null;
            if (body?.AbsOrigin is { } at) spots.Add((a, at));
        }

        for (var slot = 0; slot < MaxSlots; slot++) _cull[slot] = Array.Empty<uint>();
        if (spots.Count == 0) return;
        foreach (var viewer in Players())
        {
            if (viewer.IsBot || viewer.Slot >= MaxSlots || AlivePawn(viewer)?.AbsOrigin is not { } eye) continue;
            var others = spots.Where(s => s.A.Slot != viewer.Slot).Select(s => (s.A, D: Distance(s.At, eye))).OrderBy(s => s.D).ToList();
            var strip = new List<uint>();
            for (var i = 0; i < others.Count; i++)
            {
                var (a, d) = others[i];
                if (cap > 0 && i >= cap) strip.AddRange(a.AllIndices);
                else if (tagRange > 0 && d > tagRange) strip.AddRange(a.TagIndices);
            }

            _cull[viewer.Slot] = strip.ToArray();
        }
    }

    /// <summary>Copies a player's view settings into the arrays read every tick by <see cref="OnCheckTransmit"/>.</summary>
    internal void SyncViewSettings(CCSPlayerController p)
    {
        if (p.Slot >= MaxSlots) return;
        var data = p.IsBot ? null : Data(p);
        _hideOthers[p.Slot] = data is { ShowOtherPets: false };
        _hideTags[p.Slot] = data is { ShowNameTags: false };
    }

    /// <summary>
    /// Per-viewer visibility: players who turned other pets (or name tags) off do not receive those entities, and an
    /// owner does not see their own perched pet (it would sit in front of the camera). Uses cached entity indices,
    /// rebuilt by the follow tick whenever a pet's entities change.
    /// </summary>
    private void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_actors.Count == 0) return;
        var hidePerch = Config.Perch.HideFromOwner;
        foreach (var (info, viewer) in infoList)
        {
            if (viewer is not { IsValid: true }) continue;
            var slot = viewer.Slot;
            if (slot >= MaxSlots) continue;
            var hideOthers = _hideOthers[slot];
            var hideTags = _hideTags[slot];
            foreach (var i in _cull[slot]) info.TransmitEntities.Remove(i);
            if (!hideOthers && !hideTags && !hidePerch) continue;

            foreach (var a in _actors.Values)
            {
                if (a.Slot == slot)
                {
                    if (hidePerch)
                        foreach (var i in a.PerchIndices) info.TransmitEntities.Remove(i);
                    if (hideTags)
                        foreach (var i in a.TagIndices) info.TransmitEntities.Remove(i);
                    continue;
                }

                var list = hideOthers ? a.AllIndices : hideTags ? a.TagIndices : Array.Empty<uint>();
                foreach (var i in list) info.TransmitEntities.Remove(i);
            }
        }
    }
}
