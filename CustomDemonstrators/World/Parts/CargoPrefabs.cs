using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.ThingTypes;
using HarmonyLib;

namespace CustomDemonstrators.World.Parts;

internal static class CargoPrefabs
{
    private static readonly FieldInfo? _prefabCacheField =
        AccessTools.Field(typeof(CargoType_v2), "_trainCargoToCargoPrefabs");

    // Force a rebulid of TrainCargoToCargoPrefabs for this cargo
    internal static void InvalidateCache(CargoType_v2 cargo) => _prefabCacheField?.SetValue(cargo, null);

    // Repair any cargoes that duplicate their loadable car types
    internal static void Sanitize(CargoType_v2? cargo)
    {
        var loadables = cargo != null ? cargo.loadableCarTypes : null;
        if (cargo == null || loadables == null || loadables.Length == 0) return;

        var seen = new HashSet<TrainCarType_v2>();
        var kept = new List<CargoType_v2.LoadableInfo>(loadables.Length);
        var duplicates = new List<string>();
        int blanks = 0;

        foreach (var li in loadables)
        {
            if (li == null || li.carType == null) { blanks++; continue; }
            if (!seen.Add(li.carType)) { duplicates.Add(li.carType.id); continue; }
            kept.Add(li);
        }

        if (duplicates.Count == 0 && blanks == 0) return;

        cargo.loadableCarTypes = [.. kept];
        InvalidateCache(cargo);

        if (duplicates.Count > 0)
            Main.Logger.Warning($"Cargo '{cargo.id}' listed {string.Join(", ", duplicates.Distinct())} more "
                + "than once among its loadable car types; dropped the duplicate(s). This is a data error in "
                + "whichever mod contributed that car type, and would otherwise crash loading that cargo.");
        if (blanks > 0)
            Main.Logger.Warning($"Cargo '{cargo.id}' had {blanks} loadable car type entries with no car type; "
                + "dropped them. Same failure mode as a duplicate entry.");
    }
}
