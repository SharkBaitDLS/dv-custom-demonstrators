using DV;
using DV.LocoRestoration;
using HarmonyLib;

namespace CustomDemonstrators;

// Re-check the save guard whenever a save file is loaded
[HarmonyPatch(typeof(WorldStreamingInit), "Awake")]
internal static class WorldStreamingInit_Awake_Patch
{
    private static void Prefix()
    {
        SaveGuard.Invalidate();
        GarageOwnership.ResetForNewSave();
        RestorationPartsCustomizer.Reset();
        CommsRadioRefresher.Reset();
    }
}

// Apply replacements before any consumer reads garage data. Unity's component init order between
// these is undefined, so we patch all 3 methods. GarageReplacementApplier.Apply is idempotent so
// we'll still only do the actual work once from whichever patch gets invoked first.

[HarmonyPatch(typeof(GarageCarSpawner), "Awake")]
internal static class GarageCarSpawner_Awake_Patch
{
    private static void Prefix() => GarageReplacementApplier.Apply();

    private static void Postfix() => CommsRadioRefresher.Refresh();
}

[HarmonyPatch(typeof(CommsRadioCrewVehicle), "Awake")]
internal static class CommsRadioCrewVehicle_Awake_Patch
{
    private static void Prefix() => GarageReplacementApplier.Apply();

    // Capture the instance now: it can't be FindObjectOfType'd on demand once the radio is holstered
    // (an inactive inventory item), which is exactly its state while we force a respawn from the menu.
    private static void Postfix(CommsRadioCrewVehicle __instance) => CommsRadioRefresher.Capture(__instance);
}

[HarmonyPatch(typeof(LocoRestorationController), "Awake")]
internal static class LocoRestorationController_Awake_Patch
{
    private static void Postfix(LocoRestorationController __instance)
    {
        GarageReplacementApplier.Apply();
        GarageReplacementApplier.ApplyTo(__instance);
    }
}
