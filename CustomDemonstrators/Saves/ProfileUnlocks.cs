using System;
using HarmonyLib;
using CustomDemonstrators.World;

namespace CustomDemonstrators.Saves;

// Keeps the garages this mod fabricates out of the player's cross-save profile data
[HarmonyPatch(typeof(UnlockablesManager), nameof(UnlockablesManager.UnlockGarage))]
internal static class ProfileUnlocks
{
    private static bool Prefix(string garageId, ref bool __result)
    {
        if (string.IsNullOrEmpty(garageId)
            || !garageId.StartsWith(SlotTypes.GarageIdPrefix, StringComparison.Ordinal))
        {
            return true;
        }

        __result = false; // tell the game there are no changes to persist
        return false;
    }
}
