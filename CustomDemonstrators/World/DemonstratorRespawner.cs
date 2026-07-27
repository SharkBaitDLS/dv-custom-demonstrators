using System.Collections;
using System.Reflection;
using DV;
using DV.LocoRestoration;
using DV.Utils;
using HarmonyLib;
using UnityEngine;

namespace CustomDemonstrators.World;

internal static class DemonstratorRespawner
{
    internal static void ReinitializeDemonstrator(LocoRestorationController controller)
    {
        bool spawnMatches = DemonstratorSetup.SpawnMatchesSettings(controller);

        DemonstratorSetup.ApplyTo(controller);

        if (spawnMatches) return;

        var oldLoco = Traverse.Create(controller).Field("loco").GetValue<TrainCar>();

        // Revoke ownership of the wreck "garage" which takes it out of the comms radio
        GarageUnlocks.Revoke(controller.garageSpawner?.garageType);
        SuppressPopups(controller);

        if (controller.State >= LocoRestorationController.RestorationState.S9_LocoServiced)
        {
            DemonstratorCars.DetachFinishedAndRestart(controller);
        }
        else
        {
            RespawnWreck(controller);
        }

        SingletonBehaviour<CoroutineManager>.Instance.Run(FinishRespawn(controller, oldLoco));
    }

    // Respawning can end up racing against the controller reinitializing and leave the map marker un-hidden
    // for the new wreck, poll until it's spawned and make sure it's actually hidden.
    private static IEnumerator FinishRespawn(LocoRestorationController controller, TrainCar? oldLoco)
    {
        var t = Traverse.Create(controller);
        try
        {
            TrainCar? loco = null;
            for (int i = 0; i < 300 && (loco == null || loco == oldLoco); i++)
            {
                SuppressPopups(controller);
                yield return null;
                loco = t.Field("loco").GetValue<TrainCar>();
            }
            // Respawn failed for whatever reason, don't hide the old locomotive
            if (loco == null || loco == oldLoco
                || controller.State >= LocoRestorationController.RestorationState.S9_LocoServiced)
            {
                Main.Logger.Warning($"Failed to spawn a new demonstrator wreck for {controller.name}");
                yield break;
            }

            HideOnMap(loco);
            HideOnMap(t.Field("secondCar").GetValue<TrainCar>());


            // Wait for the controller to settle before picking the wreck.
            float lastStep = Time.fixedTime;
            for (int steps = 0; steps < BlockerSettleSteps;)
            {
                if (controller.State != LocoRestorationController.RestorationState.S0_Initialized) yield break;
                SuppressPopups(controller);
                yield return null;
                if (Time.fixedTime <= lastStep) continue;
                lastStep = Time.fixedTime;
                steps++;
            }
            var settled = t.Field("loco").GetValue<TrainCar>();
            if (settled == null)
            {
                Main.Logger.Warning($"Demonstrator wreck for {controller.name} went missing while respawning");
                yield break;
            }

            RescueStrandedState(controller);
        }
        finally
        {
            RestorePopups(controller);
        }
    }

    // Physics steps, not rendered frames, so if a user pauses the game while interacting with UMM
    // we still wait until they unpause and the game logic resumes.
    private const int BlockerSettleSteps = 30;

    private static readonly FieldInfo? LoadingDoneField =
        AccessTools.Field(typeof(LocoRestorationController), "loadingDone");

    // The controller only shows its museum quest popups once loadingDone is set, and the game itself
    // clears the flag around LoadData to keep a state restore quiet. We borrow the same trick rather
    // than replaying all the popups at the player.
    private static void SuppressPopups(LocoRestorationController controller) =>
        LoadingDoneField?.SetValue(controller, false);

    private static void RestorePopups(LocoRestorationController controller) =>
        LoadingDoneField?.SetValue(controller, true);

    // Indeterminate respawn states can leave a wreck in S0 if it didn't detect the correct license
    // ownership upon spawn. We check ourselves and nudge it through if it got stuck.
    private static void RescueStrandedState(LocoRestorationController controller)
    {
        if (controller.State != LocoRestorationController.RestorationState.S0_Initialized) return;

        var manager = GarageUnlocks.Manager();
        if (manager == null) return;

        // Still legitimately locked behind the restoration license
        var restorationLicense = controller.requiredRestorationLicense;
        if (restorationLicense == null || !manager.IsGeneralLicenseAcquired(restorationLicense)) return;

        string id = controller.locoLivery?.id ?? controller.name;

        // Still legitimately locked behind the locomotive license
        var locoLicense = controller.locoLivery?.requiredLicense;
        if (locoLicense != null && !manager.IsGeneralLicenseAcquired(locoLicense))
        {
            AccessTools.Method(typeof(LocoRestorationController), "SetState")
                ?.Invoke(controller, [LocoRestorationController.RestorationState.S1_UnlockedRestorationLicense]);
            Main.Logger.Log($"{id} still needs its own license, advanced the restoration to S1.");
            return;
        }

        // Shit's fucked yo
        Main.Logger.Warning($"{id} holds every license it needs but stayed at S0, unblocking it directly so its restoration isn't stuck.");
        AccessTools.Method(typeof(LocoRestorationController), "OnBlockersRemoved", [typeof(bool)])
            ?.Invoke(controller, [true]);
    }

    private static void HideOnMap(TrainCar? car)
    {
        if (car == null) return;

        car.preventFastTravelDestination = true;
        if (car.FastTravelDestination != null)
        {
            car.FastTravelDestination.showOnMap = false;
            car.FastTravelDestination.RefreshMarkerVisibility();
        }
    }

    // For an unfinished restoration, deleting the wreck fires LocoRestorationController.OnUnexpectedDestroy,
    // which tears down the quest state and respawns it as a wreck via its Start() coroutine. Since we've
    // rewritten the controller metadata at this point, this will effectively cause our desired one to respawn.
    private static void RespawnWreck(LocoRestorationController controller)
    {
        var t = Traverse.Create(controller);
        var loco = t.Field("loco").GetValue<TrainCar>();
        if (loco == null) return;

        var secondCar = t.Field("secondCar").GetValue<TrainCar>();

        DemonstratorCars.ClearRegister(controller.orderPartsModule);
        DemonstratorCars.ClearRegister(controller.installPartsModule);

        DemonstratorCars.ReconcileSpawnPointUsage(ignoring: controller);

        CarLifecycle.DestroyStaleBlockers(loco);
        CarLifecycle.DestroyStaleBlockers(secondCar);

        Main.Logger.Log($"Destroying demonstrator {loco.name} [{loco.ID}] to force a respawn.");
        // Tearing down the tender cascades to the parent loco, but not visa versa,
        // so we attempt to delete that if it exists.
        if (secondCar != null)
        {
            secondCar.preventDelete = false;
            SingletonBehaviour<CarSpawner>.Instance.DeleteCar(secondCar);
        }
        else
        {
            loco.preventDelete = false;
            SingletonBehaviour<CarSpawner>.Instance.DeleteCar(loco);
        }
    }

    // A newly built slot spawns its wreck blocked and then sorts itself out: LocoZoneBlocker checks the
    // licenses the player already holds and unblocks on its own, taking the restoration to S2. This waits
    // for that to play out and then runs the same stranded-state check a respawned wreck gets, which stops
    // at S1 when the loco's own license is still missing and only forces the issue if it is genuinely stuck.
    internal static void SettleNewDemonstrator(LocoRestorationController controller) =>
        SingletonBehaviour<CoroutineManager>.Instance.Run(SettleNew(controller));

    // The wreck only appears once the world has finished loading its cars, which on a slow load is a while.
    private const int NewSlotSpawnTimeoutFrames = 3000;

    private static IEnumerator SettleNew(LocoRestorationController controller)
    {
        var t = Traverse.Create(controller);
        for (int i = 0; i < NewSlotSpawnTimeoutFrames; i++)
        {
            if (controller == null) yield break;
            if (t.Field("loco").GetValue<TrainCar>() != null) break;
            yield return null;
        }
        if (controller == null || t.Field("loco").GetValue<TrainCar>() == null)
        {
            Main.Logger.Warning($"{controller?.locoLivery?.id} never spawned a wreck for its new demonstrator slot.");
            yield break;
        }

        float lastStep = Time.fixedTime;
        for (int steps = 0; steps < BlockerSettleSteps;)
        {
            if (controller == null) yield break;
            // The blocker got there on its own; nothing to rescue.
            if (controller.State != LocoRestorationController.RestorationState.S0_Initialized) yield break;
            yield return null;
            if (Time.fixedTime <= lastStep) continue;
            lastStep = Time.fixedTime;
            steps++;
        }

        RescueStrandedState(controller);
    }
}
