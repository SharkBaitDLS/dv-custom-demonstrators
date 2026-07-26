using DV;
using DV.LocoRestoration;
using HarmonyLib;
using CustomDemonstrators.Saves;
using CustomDemonstrators.World;

namespace CustomDemonstrators;

// Every patch here is a timing hook, so they are listed in the order they fire. The sequence is what the
// mod's correctness rests on, and none of it is obvious from the patched methods themselves:
//
//  1. WorldStreamingInit.Awake     — before any scene is loaded. Per-world state is dropped here.
//  2. game content scene loads     — Awake runs on the museum's restoration controllers, the garage
//                                    spawners and the comms radio, in an order Unity does not define.
//                                    Garage data is rewritten from settings here, before anything reads it.
//  3. AStartGameData.DoLoad        — everything from step 2 exists, but no saved state has been restored
//                                    yet. Slots this mod adds are built in this gap so that the restoration
//                                    data loaded moments later, in the body of the same method, finds them.
//  4. (inside DoLoad)              — the game matches saved restorations to controllers by SaveID, then
//                                    sets carsAndJobsLoadingFinished. Every controller's Start coroutine is
//                                    waiting on that flag, which is what stops a new wreck being spawned on
//                                    top of one just restored from the save.

// ---- 1. world teardown ------------------------------------------------------------------------------

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
        SaveConfig.Reset();
        DemonstratorSlotFactory.Reset();
    }
}

// ---- 2. game content scene ---------------------------------------------------------------------------

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

// ---- 3. before saved state is restored ---------------------------------------------------------------

// Build the slots this mod adds on top of the game's six. Both career entry points are patched because a
// new career and a loaded save reach the same window by different routes.

[HarmonyPatch(typeof(StartGameData_FromSaveGame), "DoLoad")]
internal static class StartGameData_FromSaveGame_DoLoad_Patch
{
    private static void Prefix() => DemonstratorSlotFactory.BuildAll();
}

[HarmonyPatch(typeof(StartGameData_NewCareer), "DoLoad")]
internal static class StartGameData_NewCareer_DoLoad_Patch
{
    private static void Prefix() => DemonstratorSlotFactory.BuildAll();
}
