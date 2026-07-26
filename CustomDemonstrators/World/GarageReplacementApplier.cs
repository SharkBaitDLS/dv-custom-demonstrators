using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV;
using DV.Garages;
using DV.LocoRestoration;
using DV.Shops;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using UnityEngine;
using CustomDemonstrators.Saves;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

// Bakes the configured replacements into the live game data when a world loads / a save is created.
// Originals are snapshotted by GarageVehicles so we always recompute from a clean baseline.
internal static class GarageReplacementApplier
{
    // Idempotently rewrites every garage's liveries to its configured replacements.
    internal static void Apply()
    {
        var types = Globals.G?.Types;
        if (types?.garages == null) return;

        GarageVehicles.EnsureSnapshot(); // capture pristine originals before the first mutation

        bool changed = false;
        foreach (var garage in types.garages)
        {
            if (garage == null) continue;
            // Slots we built already spawn exactly what they were created for.
            if (DemonstratorSlotFactory.IsSlotGarage(garage)) continue;
            var desired = DesiredLiveries(garage);
            if (garage.garageCarLiveries == null || !garage.garageCarLiveries.SequenceEqual(desired))
            {
                garage.garageCarLiveries = desired;
                changed = true;
            }
        }

        if (changed) types.RecalculateCaches();
    }

    private static TrainCarLivery? GetLivery(string id) =>
        Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == id);

    private static GameObject? OriginalBlocker(TrainCarLivery? livery) =>
        livery?.prefab?.GetComponentInChildren<LocoZoneBlocker>(includeInactive: true)?.gameObject;

    // The liveries a garage should spawn after overrides. A normal garage just does a simple replace.
    // A demonstrator garage is rebuilt from its primary loco plus its resolved tender if any.
    private static TrainCarLivery[] DesiredLiveries(GarageType_v2 garage)
    {
        if (!GarageVehicles.IsDemonstrator(garage.v1))
        {
            if (SaveGuard.IsGarageBlocking && GarageOwnership.IsUnlockedAndOwned(garage))
            {
                return GarageVehicles.OriginalLiveries(garage);
            }

            var replaced = GarageVehicles.OriginalLiveries(garage).Select(l => Main.Settings.GetReplacement(l) ?? l);
            var extras = Main.Settings.GetExtraCars(garage.id).Select(GetLivery);
            return [.. replaced.Concat(extras).Where(l => l != null)!];
        }

        var primary = GarageVehicles.PrimaryLoco(garage);
        if (primary == null) return GarageVehicles.OriginalLiveries(garage);

        if (!TryResolveDemonstrator(primary, GarageVehicles.OriginalTender(garage), primary.id,
                out var replacementLoco, out var tenderLivery))
        {
            return GarageVehicles.OriginalLiveries(garage);
        }

        var desired = new List<TrainCarLivery> { replacementLoco ?? primary };
        if (tenderLivery != null) desired.Add(tenderLivery);
        return [.. desired];
    }

    // Resolves the effective replacement loco + tender for a demonstrator slot. Uses the current settings
    // when the guard allows changes, otherwise falls back to the state the save was already in, so
    // saves reload their existing demonstrators instead of reverting to vanilla settings on mismatch.
    // Only if the save has never been modified by this mod do we let vanilla rules apply.
    private static bool TryResolveDemonstrator(
        TrainCarLivery? loco, TrainCarLivery? originalTender, string slotId,
        out TrainCarLivery? replacementLoco, out TrainCarLivery? tenderLivery)
    {
        if (SaveGuard.AllowDemonstratorChanges())
        {
            replacementLoco = loco != null ? Main.Settings.GetReplacement(loco) : null;
            tenderLivery = GarageReplacements.ResolveTender(slotId, originalTender);
            return true;
        }

        if (loco != null && SaveConfig.Demonstrators is { } baked && baked.TryGetValue(loco.id, out var e))
        {
            replacementLoco = e.SpawnId == loco.id ? null : GetLivery(e.SpawnId);
            tenderLivery = e.TenderId != null ? GetLivery(e.TenderId) : null;
            return true;
        }

        replacementLoco = null;
        tenderLivery = null;
        return false;
    }

    // Repoints a demonstrator's restoration controller at its replacement liveries, so the museum
    // restoration spawns and services the same car the garage now spawns, and applies the slot's
    // parts-cargo and price overrides.
    internal static void ApplyTo(LocoRestorationController controller)
    {
        var loco = OriginalLoco(controller);
        var tender = OriginalTender(controller);

        string slotId = loco?.id ?? "";

        // Leave a save the mod never touched at its vanilla original
        if (!TryResolveDemonstrator(loco, tender, slotId, out var replacementLoco, out var tenderId)) return;

        if (loco != null)
            controller.locoLivery = replacementLoco ?? loco; // revert to vanilla when the override is cleared

        if (replacementLoco != null && controller.locoBlockerPrefab == null)
            controller.locoBlockerPrefab = OriginalBlocker(loco);

        // The controller only subscribes to Unblocked when a blocker exists on the spawned car or can be
        // instantiated from the prefab. With neither, a wreck reset to S0 can never advance on its own.
        if (controller.locoBlockerPrefab == null && OriginalBlocker(controller.locoLivery) == null)
            Main.Logger.Warning(
                $"{controller.locoLivery?.id} has no loco zone blocker available, its restoration can't unblock itself.");

        // A slot this mod added has no vanilla loco it stands in for, so its own loco is what the parts
        // cargo has to be named and modelled after.
        var cargoLoco = replacementLoco
            ?? (DemonstratorSlotFactory.IsSlotGarage(controller.garageSpawner?.garageType) ? controller.locoLivery : null);
        RestorationPartsCustomizer.ApplyCargo(controller, slotId, cargoLoco);

        controller.secondCarLivery = tenderId;

        if (tenderId != null)
        {
            // To get the tender to display the demonstrator message, it has to inherit the license of the
            // locomotive. Most CCL mod authors don't license the tender, just the loco. Patch that for them.
            var effectiveLoco = controller.locoLivery;
            if (tenderId.requiredLicense == null && effectiveLoco?.requiredLicense != null)
                tenderId.requiredLicense = effectiveLoco.requiredLicense;

            // Ensure a blocker prefab exists at all (the tender's own, else the loco's).
            if (controller.secondCarBlockerPrefab == null)
                controller.secondCarBlockerPrefab = OriginalBlocker(tender) ?? OriginalBlocker(loco) ?? controller.locoBlockerPrefab;
        }

        // Price overrides. < 0 / unset = default.
        var orderPrice = Main.Settings.GetOrderPrice(slotId);
        if (orderPrice.HasValue && controller.orderPartsModule != null)
            controller.orderPartsModule.price = orderPrice.Value;

        var installPrice = Main.Settings.GetInstallPrice(slotId);
        if (installPrice.HasValue && controller.installPartsModule != null)
            controller.installPartsModule.price = installPrice.Value;
    }

    internal static void ReinitializeDemonstrator(LocoRestorationController controller)
    {
        bool spawnMatches = SpawnMatchesSettings(controller);

        ApplyTo(controller);

        if (spawnMatches) return;

        var oldLoco = Traverse.Create(controller).Field("loco").GetValue<TrainCar>();

        // Revoke ownership of the wreck "garage" which takes it out of the comms radio
        GarageUnlocks.Revoke(controller.garageSpawner?.garageType);
        SuppressPopups(controller);

        if (controller.State >= LocoRestorationController.RestorationState.S9_LocoServiced)
        {
            DetachFinishedLocoAndRespawn(controller);
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

    private static bool SpawnMatchesSettings(LocoRestorationController controller)
    {
        var loco = OriginalLoco(controller);
        if (loco == null) return true;
        var desiredLoco = Main.Settings.GetReplacement(loco) ?? loco;
        var desiredTender = GarageReplacements.ResolveTender(loco.id, OriginalTender(controller));
        return controller.locoLivery == desiredLoco && controller.secondCarLivery == desiredTender;
    }

    private static TrainCarLivery? OriginalLoco(LocoRestorationController controller) =>
        controller.garageSpawner?.garageType is GarageType_v2 g ? GarageVehicles.PrimaryLoco(g) : controller.locoLivery;

    private static TrainCarLivery? OriginalTender(LocoRestorationController controller) =>
        controller.garageSpawner?.garageType is GarageType_v2 g ? GarageVehicles.OriginalTender(g) : null;

    // For an unfinished restoration, deleting the wreck fires LocoRestorationController.OnUnexpectedDestroy,
    // which tears down the quest state and respawns it as a wreck via its Start() coroutine. Since we've
    // rewritten the controller metadata at this point, this will effectively cause our desired one to respawn.
    private static void RespawnWreck(LocoRestorationController controller)
    {
        var t = Traverse.Create(controller);
        var loco = t.Field("loco").GetValue<TrainCar>();
        if (loco == null) return;

        var secondCar = t.Field("secondCar").GetValue<TrainCar>();

        ClearRegister(controller.orderPartsModule);
        ClearRegister(controller.installPartsModule);

        ReconcileSpawnPointUsage(ignoring: controller);

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

    // How close a restoration car has to be to an anchor for that anchor to count as still occupied. The
    // wreck spawns centred on its anchor, so anything roughly a car length away has been moved off it.
    private const float SpawnPointOccupiedRadius = 20f;

    // Recomputes which wreck anchors are taken, from where the restoration cars actually are.
    //
    // The anchors are a pool shared by every restoration controller — which is what lets slots beyond the
    // game's six find a spot — so a controller must never blanket-clear the array it holds: those entries
    // belong to the other slots too. Deriving occupancy from live cars is the only reading that stays
    // correct for anchors this controller never claimed, and it also releases anchors whose wreck the
    // player has since hauled away.
    internal static void ReconcileSpawnPointUsage(LocoRestorationController? ignoring = null)
    {
        var occupied = new HashSet<LocoRestorationSpawnPoint>();
        var points = new HashSet<LocoRestorationSpawnPoint>();

        foreach (var controller in LocoRestorationController.allLocoRestorationControllers)
        {
            if (controller == null || controller.spawnPoints == null) continue;
            foreach (var point in controller.spawnPoints)
            {
                if (point != null) points.Add(point);
            }
            if (controller == ignoring) continue;

            var t = Traverse.Create(controller);
            foreach (var car in new[] { t.Field("loco").GetValue<TrainCar>(), t.Field("secondCar").GetValue<TrainCar>() })
            {
                if (car == null) continue;
                foreach (var point in controller.spawnPoints)
                {
                    if (point == null) continue;
                    if ((point.transform.position - car.transform.position).sqrMagnitude
                        < SpawnPointOccupiedRadius * SpawnPointOccupiedRadius)
                    {
                        occupied.Add(point);
                    }
                }
            }
        }

        foreach (var point in points)
        {
            point.pointUsed = occupied.Contains(point);
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

    // Lets go of a demonstrator's cars without putting anything back in their place: a finished loco stays
    // in the world as an ordinary owned car, an unfinished wreck is deleted. Used when a slot this mod
    // added is removed outright, as opposed to being respawned as something else.
    internal static void RetireDemonstratorCars(LocoRestorationController controller, GarageCarSpawner? spawner)
    {
        var t = Traverse.Create(controller);
        var loco = t.Field("loco").GetValue<TrainCar>();
        var secondCar = t.Field("secondCar").GetValue<TrainCar>();

        ClearRegister(controller.orderPartsModule);
        ClearRegister(controller.installPartsModule);

        bool keep = loco != null && controller.State >= LocoRestorationController.RestorationState.S9_LocoServiced;
        if (keep)
        {
            // Stop it reacting to the paint job it may still be waiting on before we let the cars go.
            AccessTools.Method(typeof(LocoRestorationController), "SetupListenersForPaintJob", [typeof(bool)])
                .Invoke(controller, [false]);
        }

        foreach (var car in new[] { secondCar, loco })
        {
            if (car == null) continue;
            // Unparent first either way: it drops OnUnexpectedDestroy, which would otherwise respawn the
            // very wreck we are deleting.
            UnparentCar(car, controller, spawner);
            if (keep) continue;

            CarLifecycle.DestroyStaleBlockers(car);
            car.preventDelete = false;
            SingletonBehaviour<CarSpawner>.Instance.DeleteCar(car);
        }

        if (keep)
        {
            Main.Logger.Log($"Kept restored {loco?.name} as a player-owned car while removing its demonstrator slot.");
        }

        t.Field("loco").SetValue(null);
        t.Field("secondCar").SetValue(null);
    }

    private static void ClearRegister(GenericThingCashRegisterModule? module)
    {
        if (module == null) return;
        AccessTools.Method(module.GetType(), "SetUnitsToBuy", [typeof(float)])?.Invoke(module, [0f]);
    }

    private static void DetachFinishedLocoAndRespawn(LocoRestorationController controller)
    {
        var t = Traverse.Create(controller);
        var loco = t.Field("loco").GetValue<TrainCar>();
        var secondCar = t.Field("secondCar").GetValue<TrainCar>();
        var garage = controller.garageSpawner;
        Main.Logger.Log($"Detected restored demonstrator {loco.name} [{loco.ID}], preserving it before spawning its replacement.");

        // Stop the controller from reacting to a transition to S10/painted
        AccessTools.Method(typeof(LocoRestorationController), "SetupListenersForPaintJob", [typeof(bool)])
            .Invoke(controller, [false]);

        if (loco != null) UnparentCar(loco, controller, garage);
        if (secondCar != null) UnparentCar(secondCar, controller, garage);

        if (garage?.garageCars != null)
        {
            for (int i = 0; i < garage.garageCars.Length; i++)
            {
                garage.garageCars[i] = null;
            }
        }

        // Reset the controller to a pristine restoration and respawn
        t.Field("loco").SetValue(null);
        t.Field("secondCar").SetValue(null);
        t.Field("transportingCars").SetValue(null);
        ReconcileSpawnPointUsage(ignoring: controller);
        controller.StartCoroutine(
            (IEnumerator)AccessTools.Method(typeof(LocoRestorationController), "Start")
                    .Invoke(controller, null));
    }

    private static void UnparentCar(TrainCar car, LocoRestorationController controller, GarageCarSpawner? garage)
    {
        var home = car.GetComponent<HomeGarageReference>();
        if (home != null) UnityEngine.Object.Destroy(home);

        car.OnDestroyCar -= CarLifecycle.DelegateFor<Action<TrainCar>>(controller, "OnUnexpectedDestroy");
        if (garage != null)
        {
            car.OnDestroyCar -= CarLifecycle.DelegateFor<Action<TrainCar>>(garage, "OnGarageCarDeleted");
        }
    }
}
