using System.Collections.Generic;
using System.Reflection;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;

namespace CustomDemonstrators.World;

// Manages a demonstrator slot's garage unlock
internal static class GarageUnlocks
{
    private static readonly FieldInfo? UnlockedGarages =
        AccessTools.Field(typeof(LicenseManager), "unlockedGarages");

    private static readonly FieldInfo? UnsavedChanges =
        AccessTools.Field(typeof(LicenseManager), "unsavedChanges");

    internal static void Revoke(GarageType_v2? garage, string? reason = null)
    {
        if (garage == null) return;

        var manager = Manager();
        if (manager == null) return;

        if (UnlockedGarages?.GetValue(manager) is not HashSet<GarageType_v2> unlocked) return;
        if (!unlocked.Remove(garage)) return;

        UnsavedChanges?.SetValue(manager, true);
        Main.Logger.Log($"Revoked the garage unlock for {garage.id}, "
            + (reason ?? "its restoration has to be completed again."));
    }

    private static readonly FieldInfo? SpawnAllowed =
        AccessTools.Field(typeof(GarageCarSpawner), "spawnAllowed");

    internal static void StopSpawning(GarageCarSpawner? spawner)
    {
        if (spawner == null) return;
        SpawnAllowed?.SetValue(spawner, false);
    }

    internal static bool IsSpawningAllowed(GarageCarSpawner? spawner) =>
        spawner != null && SpawnAllowed?.GetValue(spawner) is true;

    internal static LicenseManager? Manager() =>
        typeof(SingletonBehaviour<LicenseManager>)
            .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)?
            .GetValue(null) as LicenseManager;
}
