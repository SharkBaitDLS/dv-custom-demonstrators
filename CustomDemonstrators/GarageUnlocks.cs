using System.Collections.Generic;
using System.Reflection;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;

namespace CustomDemonstrators;

// Manages a demonstrator slot's garage unlock
internal static class GarageUnlocks
{
    private static readonly FieldInfo? UnlockedGarages =
        AccessTools.Field(typeof(LicenseManager), "unlockedGarages");

    private static readonly FieldInfo? UnsavedChanges =
        AccessTools.Field(typeof(LicenseManager), "unsavedChanges");

    internal static void Revoke(GarageType_v2? garage)
    {
        if (garage == null) return;

        var manager = Manager();
        if (manager == null) return;

        if (UnlockedGarages?.GetValue(manager) is not HashSet<GarageType_v2> unlocked) return;
        if (!unlocked.Remove(garage)) return;

        UnsavedChanges?.SetValue(manager, true);
        Main.Logger.Log($"Revoked the garage unlock for {garage.id}, its restoration has to be completed again.");
    }

    internal static LicenseManager? Manager() =>
        typeof(SingletonBehaviour<LicenseManager>)
            .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)?
            .GetValue(null) as LicenseManager;
}
