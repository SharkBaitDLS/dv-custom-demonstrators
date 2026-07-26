using System.Collections;
using System.Linq;
using System.Reflection;
using DV;
using DV.Utils;
using HarmonyLib;
using UnityEngine;

namespace CustomDemonstrators.World;

internal static class CommsRadioRefresher
{
    private static CommsRadioCrewVehicle? _radio;
    private static MethodInfo? _update;
    private static FieldInfo? _available;
    private static bool _queued;

    internal static void Capture(CommsRadioCrewVehicle radio) => _radio = radio;

    internal static void Reset()
    {
        _radio = null;
        _queued = false;
    }

    internal static void Refresh()
    {
        if (_queued) return;

        _queued = true;
        SingletonBehaviour<CoroutineManager>.Instance.Run(RefreshAtEndOfFrame());
    }

    // A single frame is enough to land after every Awake in a scene load: Unity runs all of them while the
    // scene loads, and a coroutine yielding null doesn't resume until the first Update afterwards.
    private static IEnumerator RefreshAtEndOfFrame()
    {
        yield return null;
        _queued = false;

        // Can't be found on demand once the radio is holstered (an inactive inventory item), which is
        // exactly its state during a forced respawn, so the Awake patch hands us the instance instead.
        var radio = _radio;
        if (radio == null)
        {
            radio = _radio = Resources.FindObjectsOfTypeAll<CommsRadioCrewVehicle>()
                .FirstOrDefault(r => r.gameObject.scene.IsValid());
        }
        if (radio == null) yield break;

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
