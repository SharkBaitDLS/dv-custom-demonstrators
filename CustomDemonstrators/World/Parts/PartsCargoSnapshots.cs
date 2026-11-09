using System.Collections.Generic;
using System.Linq;
using DV.ThingTypes;
using UnityEngine;

namespace CustomDemonstrators.World.Parts;

internal static class PartsCargoSnapshots
{
    private sealed class Snapshot(
        CargoType_v2 cargo, string? fullKey, string? shortKey, float mass, GameObject[][] variants)
    {
        public readonly CargoType_v2 Cargo = cargo;
        public readonly string? FullKey = fullKey;
        public readonly string? ShortKey = shortKey;
        public readonly float Mass = mass;
        public readonly GameObject[][] Variants = variants; // parallel to Cargo.loadableCarTypes
    }

    private static readonly Dictionary<string, Snapshot> _snapshots = [];

    // Slots whose cargo we have rewritten since snapshotting it. A rewrite doesn't always show up in the
    // name (a CCL author can change nothing but the crate model), so "is this dirty?" has to be tracked
    // rather than inferred from any one field. Same lifetime as the snapshots for the same reason.
    private static readonly HashSet<string> _customized = [];

    internal static void Ensure(string slotId, CargoType_v2? cargo)
    {
        if (string.IsNullOrEmpty(slotId) || cargo == null || _snapshots.ContainsKey(slotId)) return;
        var variants = cargo.loadableCarTypes?.Select(li => li.cargoPrefabVariants).ToArray() ?? [];
        _snapshots[slotId] = new Snapshot(
            cargo, cargo.localizationKeyFull, cargo.localizationKeyShort, cargo.massPerUnit, variants);
    }

    // The cargo the slot came with, or null for a slot never snapshotted.
    internal static CargoType_v2? Original(string slotId) =>
        _snapshots.TryGetValue(slotId, out var snap) ? snap.Cargo : null;

    internal static bool IsCustomized(string slotId) => _customized.Contains(slotId);

    internal static void MarkCustomized(string slotId) => _customized.Add(slotId);

    // Puts the slot's original cargo back the way we found it, if we had rewritten it.
    internal static void Revert(string slotId)
    {
        if (!_snapshots.TryGetValue(slotId, out var snap)) return;
        if (!_customized.Remove(slotId)) return;

        snap.Cargo.localizationKeyFull = snap.FullKey;
        snap.Cargo.localizationKeyShort = snap.ShortKey;
        snap.Cargo.massPerUnit = snap.Mass;

        var loadables = snap.Cargo.loadableCarTypes;
        if (loadables != null)
        {
            for (int i = 0; i < loadables.Length && i < snap.Variants.Length; i++)
            {
                loadables[i].cargoPrefabVariants = snap.Variants[i];
            }
        }
        CargoPrefabs.InvalidateCache(snap.Cargo);
    }
}
