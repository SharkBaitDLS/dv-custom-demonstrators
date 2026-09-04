using DV;
using DV.LocoRestoration;
using HarmonyLib;
using CustomDemonstrators.Saves;
using CustomDemonstrators.Slots;
using CustomDemonstrators.World;

namespace CustomDemonstrators;

// Every patch here is a timing hook, so they are listed in the order they fire.

// Re-check the save guard whenever a save file is loaded
[HarmonyPatch(typeof(WorldStreamingInit), "Awake")]
internal static class WorldStreamingInit_Awake_Patch
{
    private static void Prefix()
    {
        SaveGuard.Invalidate();
        GarageOwnership.ResetForNewSave();
        RestorationPartsCustomizer.Reset();
        PartsWarehouse.Reset();
        CommsRadioRefresher.Reset();
        SaveConfig.Reset();
        MuseumStalls.Reset();
        DemonstratorSlots.Reset();
        RestorationPopups.Reset();
    }
}

// Apply replacements before any consumer reads garage data. Unity's component init order between
// these is undefined, so we patch all 3 methods. GarageLiveries.Apply is idempotent so
// we'll still only do the actual work once from whichever patch gets invoked first.

[HarmonyPatch(typeof(GarageCarSpawner), "Awake")]
internal static class GarageCarSpawner_Awake_Patch
{
    private static void Prefix() => GarageLiveries.Apply();
}

[HarmonyPatch(typeof(CommsRadioCrewVehicle), "Awake")]
internal static class CommsRadioCrewVehicle_Awake_Patch
{
    private static void Prefix() => GarageLiveries.Apply();

    // Capture the instance now: it can't be FindObjectOfType'd on demand once the radio is holstered
    // (an inactive inventory item), which is exactly its state while we force a respawn from the menu.
    private static void Postfix(CommsRadioCrewVehicle __instance) => CommsRadioRefresher.Capture(__instance);
}

[HarmonyPatch(typeof(LocoRestorationController), "Awake")]
internal static class LocoRestorationController_Awake_Patch
{
    private static void Postfix(LocoRestorationController __instance)
    {
        GarageLiveries.Apply();
        DemonstratorSetup.ApplyTo(__instance);
    }
}

// A slot is built before the save is replayed into it, so this is where the state it came back as lands.
[HarmonyPatch(typeof(LocoRestorationController), nameof(LocoRestorationController.LoadData))]
internal static class LocoRestorationController_LoadData_Patch
{
    private static void Postfix(LocoRestorationController __instance) =>
        DemonstratorRespawner.SettleLoadedSlot(__instance);
}

// Build the slots this mod adds on top of the game's six. Both career entry points are patched because a
// new career and a loaded save reach the same window by different routes.

[HarmonyPatch(typeof(StartGameData_FromSaveGame), "DoLoad")]
internal static class StartGameData_FromSaveGame_DoLoad_Patch
{
    private static void Prefix() => DemonstratorSlots.BuildAll();
}

[HarmonyPatch(typeof(StartGameData_NewCareer), "DoLoad")]
internal static class StartGameData_NewCareer_DoLoad_Patch
{
    private static void Prefix() => DemonstratorSlots.BuildAll();
}

// Restoration steps announce themselves through a popup that only holds one message at a time, so
// slots sharing a license would clobber each other when the license is purchased. Instead, queue them.
[HarmonyPatch(typeof(LocoRestorationController), "SetState")]
internal static class LocoRestorationController_SetState_Patch
{
    private static void Prefix() => RestorationPopups.BeginCapture();

    private static void Finalizer() => RestorationPopups.EndCapture();
}

[HarmonyPatch(typeof(TutorialHelper), nameof(TutorialHelper.ShowTutorialFloatie))]
internal static class TutorialHelper_ShowTutorialPopup_Patch
{
    private static bool Prefix(string message, bool manualDismiss) =>
        RestorationPopups.ShouldShow(message, manualDismiss);
}

[HarmonyPatch(typeof(TutorialHelper), nameof(TutorialHelper.HideTutorialFloatie))]
internal static class TutorialHelper_HideTutorialPopup_Patch
{
    private static void Postfix() => RestorationPopups.OnDismissed();
}
