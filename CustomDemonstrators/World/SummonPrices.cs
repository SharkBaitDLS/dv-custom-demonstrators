using System.Reflection;
using CCL.Importer.Types;
using DV;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;

namespace CustomDemonstrators.World;

// Override CCL's work train price patching when a loco is assigned to a garage.
[HarmonyPatch(typeof(CommsRadioCrewVehicle), "SummonPrice", MethodType.Getter)]
internal static class CommsRadioCrewVehicle_SummonPrice_Patch
{
    // Ahead of CCL, whose prefix skips the getter outright.
    [HarmonyPriority(Priority.First)]
    [HarmonyBefore("cc.foxden.customcarloader")]
    private static bool Prefix(CommsRadioCrewVehicle __instance, ref float __result)
    {
        if (GarageFor(__instance) is not GarageType_v2 garage) return true;

        __result = Mathf.Min(garage.summonPrice, Globals.G.GameParams.WorkTrainSummonMaxPrice);
        return false;
    }

    private static FieldInfo? _selectedCar;
    private static FieldInfo? _livery;
    private static FieldInfo? _spawner;

    // The garage the selected car belongs to, and only where CCL would otherwise price it for us.
    private static GarageType_v2? GarageFor(CommsRadioCrewVehicle radio)
    {
        _selectedCar ??= AccessTools.Field(typeof(CommsRadioCrewVehicle), "selectedCar");
        if (_selectedCar?.GetValue(radio) is not object selected) return null;

        _livery ??= AccessTools.Field(selected.GetType(), "livery");
        if (_livery?.GetValue(selected) is not CCL_CarVariant) return null;

        _spawner ??= AccessTools.Field(selected.GetType(), "garageSpawner");
        if (_spawner?.GetValue(selected) is not GarageCarSpawner spawner || spawner == null) return null;

        return spawner.garageType;
    }
}
