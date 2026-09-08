using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;

namespace CustomDemonstrators.World;

// Used when the force respawn logic runs to ensure the radio retains the correct state
internal static class CommsRadioRefresher
{
    private static CommsRadioCrewVehicle? _radio;
    private static MethodInfo? _update;
    private static FieldInfo? _available;

    internal static void Capture(CommsRadioCrewVehicle radio) => _radio = radio;

    internal static void Reset() => _radio = null;

    internal static void Refresh()
    {
        // Can't be found on demand once the radio is holstered (an inactive inventory item), which is
        // exactly its state during a forced respawn, so the Awake patch hands us the instance instead.
        var radio = _radio;
        if (radio == null)
        {
            radio = _radio = Resources.FindObjectsOfTypeAll<CommsRadioCrewVehicle>()
                .FirstOrDefault(r => r.gameObject.scene.IsValid());
        }
        if (radio == null) return;

        _update ??= AccessTools.Method(typeof(CommsRadioCrewVehicle), "UpdateAvailableVehicles");
        _update?.Invoke(radio, null);

        Main.Logger.Log($"Refreshed the comms radio's work train list: {AvailableCount(radio)} available.");
    }

    private static string AvailableCount(CommsRadioCrewVehicle radio) =>
        Available(radio) is List<TrainCarLivery> available ? $"{available.Count} vehicle(s)" : "unknown";

    private static List<TrainCarLivery>? Available(CommsRadioCrewVehicle radio)
    {
        _available ??= AccessTools.Field(typeof(CommsRadioCrewVehicle), "availableVehiclesForSpawn");
        return _available?.GetValue(radio) as List<TrainCarLivery>;
    }

    // Duplicates occur as soon as a garage of ours claims a livery another mod also offers without a garage.
    // The radio resolves a pick's garage from the livery itself, so both already summon the same loco and
    // the duplicate is just noise.
    internal static void Dedupe(CommsRadioCrewVehicle radio)
    {
        if (Available(radio) is not List<TrainCarLivery> available) return;

        var seen = new HashSet<TrainCarLivery>();
        int dropped = available.RemoveAll(livery => !seen.Add(livery));
        if (dropped == 0) return;

        ClampSelection(radio, available.Count);
        Main.Logger.Log($"Dropped {dropped} work train(s) the comms radio was offering twice, which a "
            + "garage and something spawning the same car without one would both have summoned.");
    }

    private static FieldInfo? _selected;

    private static void ClampSelection(CommsRadioCrewVehicle radio, int count)
    {
        if (count == 0) return;

        _selected ??= AccessTools.Field(typeof(CommsRadioCrewVehicle), "selectedVehicleIndex");
        if (_selected?.GetValue(radio) is not int index || (index >= 0 && index < count)) return;

        _selected.SetValue(radio, 0);
    }
}

[HarmonyPatch(typeof(CommsRadioCrewVehicle), "UpdateAvailableVehicles")]
internal static class CommsRadioCrewVehicle_UpdateAvailableVehicles_Patch
{
    private static void Postfix(CommsRadioCrewVehicle __instance) => CommsRadioRefresher.Dedupe(__instance);
}
