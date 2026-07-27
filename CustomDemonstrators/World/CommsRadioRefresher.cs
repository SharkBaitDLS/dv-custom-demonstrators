using System.Collections;
using System.Linq;
using System.Reflection;
using DV;
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

    private static string AvailableCount(CommsRadioCrewVehicle radio)
    {
        _available ??= AccessTools.Field(typeof(CommsRadioCrewVehicle), "availableVehiclesForSpawn");
        return _available?.GetValue(radio) is ICollection available
            ? $"{available.Count} vehicle(s)"
            : "unknown";
    }
}
