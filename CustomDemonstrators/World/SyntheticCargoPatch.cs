using System;
using DV.ThingTypes;
using HarmonyLib;

namespace CustomDemonstrators.World;

// Follows in the footsteps of CCL/Custom Cargo for adding a new cargo identifier
[HarmonyPatch(typeof(Enum), nameof(Enum.IsDefined))]
internal static class SyntheticCargoPatch
{
    // Runs for every Enum.IsDefined call in the game, so it has to reject cheaply and must never call
    // Enum.IsDefined itself.
    private static bool Prefix(Type enumType, object value, ref bool __result)
    {
        if (enumType != typeof(CargoType)) return true;

        int number;
        if (value is int i) number = i;
        else if (value is CargoType cargo) number = (int)cargo;
        else return true;

        if (!SlotTypes.IsSlotCargoValue(number)) return true;

        __result = true;
        return false;
    }
}
