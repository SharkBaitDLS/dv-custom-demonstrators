using System.Linq;
using DV.ThingTypes;
using UnityEngine;

namespace CustomDemonstrators.World;

// Picking the LocoZoneBlocker a restoration should build its wreck's blocked state from.
internal static class ZoneBlockers
{
    internal static GameObject? PrefabFor(TrainCarLivery? livery) =>
        livery != null && livery.prefab != null
            ? livery.prefab.GetComponentInChildren<LocoZoneBlocker>(includeInactive: true)?.gameObject
            : null;

    internal static GameObject? First(params GameObject?[] candidates) =>
        candidates.FirstOrDefault(c => c != null);
}
