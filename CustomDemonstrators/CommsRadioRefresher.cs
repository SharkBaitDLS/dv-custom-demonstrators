using System.Linq;
using DV;
using HarmonyLib;
using UnityEngine;

namespace CustomDemonstrators;

internal static class CommsRadioRefresher
{
    private static CommsRadioCrewVehicle? _radio;
    private static System.Reflection.MethodInfo? _update;

    internal static void Capture(CommsRadioCrewVehicle radio) => _radio = radio;

    internal static void Reset() => _radio = null;

    internal static void Refresh()
    {
        if (_radio == null)
        {
            _radio = Resources.FindObjectsOfTypeAll<CommsRadioCrewVehicle>()
                .FirstOrDefault(r => r.gameObject.scene.IsValid());
        }
        if (_radio == null) return;

        _update ??= AccessTools.Method(typeof(CommsRadioCrewVehicle), "UpdateAvailableVehicles");
        _update?.Invoke(_radio, null);
    }
}
