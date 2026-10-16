using ChickenCoop.Model;
using CounterStrikeSharp.API.Core;

namespace ChickenCoop;

public sealed partial class ChickenCoopPlugin
{
    private readonly bool[] _hideOthers = new bool[MaxSlots];
    private readonly bool[] _hideTags = new bool[MaxSlots];

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
