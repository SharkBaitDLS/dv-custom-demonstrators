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

namespace CustomDemonstrators;

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

        RestorationPartsCustomizer.ApplyCargo(controller, slotId, replacementLoco);

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

        foreach (var point in controller.spawnPoints)
        {
            point?.pointUsed = false;
        }

        DestroyStaleBlockers(loco);
        DestroyStaleBlockers(secondCar);

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

    private static void ClearRegister(GenericThingCashRegisterModule? module)
    {
        if (module == null) return;
        AccessTools.Method(module.GetType(), "SetUnitsToBuy", [typeof(float)])?.Invoke(module, [0f]);
    }

    private static readonly FieldInfo? BlockerTrainField =
        AccessTools.Field(typeof(LocoZoneBlocker), "train");

    // A blocked wreck's LocoZoneBlocker parents itself to the loco's interior transform, which isn't
    // loaded while the cab is blocked, so the blocker sits at the scene root rather than inside the car
    // hierarchy. Deleting the car therefore leaves the blocker alive and still subscribed to
    // LicenseManager.LicenseAcquired, and the next license purchase throws NREs from its dead references
    // in the middle of the event invocation, potentially leading to save corruption if any other mods
    // have active patching logic in that chain such as Career Rework.
    private static void DestroyStaleBlockers(TrainCar? car)
    {
        if (car == null) return;

        foreach (var blocker in UnityEngine.Object.FindObjectsOfType<LocoZoneBlocker>())
        {
            if (BlockerTrainField?.GetValue(blocker) as TrainCar != car) continue;

            Main.Logger.Log($"Destroying the zone blocker for {car.name} [{car.ID}] along with the car.");
            if (blocker.blockerObjectsParent != null)
                UnityEngine.Object.Destroy(blocker.blockerObjectsParent);
            UnityEngine.Object.Destroy(blocker.gameObject);
        }
    }

    // For a completed restoration we keep the finished loco as a normal player-owned car (just no longer
    // considered a demonstrator), then reset the controller and trigger Start() manually.
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
        foreach (var point in controller.spawnPoints)
        {
            point?.pointUsed = false;
        }
        controller.StartCoroutine(
            (IEnumerator)AccessTools.Method(typeof(LocoRestorationController), "Start")
                    .Invoke(controller, null));
    }

    private static void UnparentCar(TrainCar car, LocoRestorationController controller, GarageCarSpawner? garage)
    {
        var home = car.GetComponent<HomeGarageReference>();
        if (home != null) UnityEngine.Object.Destroy(home);

        car.OnDestroyCar -= DelegateFor<Action<TrainCar>>(controller, "OnUnexpectedDestroy");
        if (garage != null)
        {
            car.OnDestroyCar -= DelegateFor<Action<TrainCar>>(garage, "OnGarageCarDeleted");
        }
    }

    internal static void ReinitializeGarages()
    {
        foreach (var (garage, isDemonstrator, _) in GarageVehicles.Groups)
        {
            if (isDemonstrator) continue;
            if (SpawnerFor(garage) is GarageCarSpawner spawner) ReconcileGarage(spawner);
        }
    }

    private static GarageCarSpawner? SpawnerFor(GarageType_v2 garage) =>
        GarageCarSpawner.Spawners.Values.FirstOrDefault(s => s.garageType == garage);

    private static void ReconcileGarage(GarageCarSpawner spawner)
    {
        var desired = spawner.GarageCarLiveries;
        if (desired == null) return;

        // Already registered to the configured consist, nothing to do
        if (desired.All(l => l != null && GarageCarSpawner.Spawners.TryGetValue(l, out var s) && s == spawner))
        {
            return;
        }

        var current = spawner.garageCars ?? [];

        // Keep spawned cars whose livery is still wanted (placed at their new slot); free the rest.
        var rebuilt = new TrainCar[desired.Length];
        var kept = new HashSet<TrainCar>();
        for (int i = 0; i < desired.Length; i++)
        {
            var match = current.FirstOrDefault(c => c != null && c.carLivery == desired[i] && !kept.Contains(c));
            if (match != null)
            {
                rebuilt[i] = match;
                kept.Add(match);
            }
        }
        var deletedBlockers = new List<TrainCar>();
        foreach (var car in current)
        {
            if (car == null || kept.Contains(car)) continue;

            // A car still parked on its spawn point would block the replacement from spawning.
            // Generally speaking if someone has unlocked a garage they've probably either moved what's
            // within, or if they left it untouched they are likely fine with its replacement removing it.
            if (IsParkedAtHome(car, spawner))
            {
                DeleteGarageCar(car, spawner);
                deletedBlockers.Add(car);
            }
            else
            {
                UnparentGarageCar(car, spawner);
            }
        }

        // Re-point the static livery->spawner registry from the stale liveries to the desired ones.
        foreach (var key in GarageCarSpawner.Spawners.Where(kv => kv.Value == spawner).Select(kv => kv.Key).ToList())
        {
            GarageCarSpawner.Spawners.Remove(key);
        }
        foreach (var l in desired)
        {
            if (l != null) GarageCarSpawner.Spawners[l] = spawner;
        }

        spawner.garageCars = rebuilt;

        // A deleted car's collider lingers until Object.Destroy runs at the end of the frame, so wait until
        // the blockers are actually gone before respawning.
        if (deletedBlockers.Count > 0)
        {
            SingletonBehaviour<CoroutineManager>.Instance.Run(RespawnAfterBlockersGone(spawner, deletedBlockers));
        }
        else
        {
            spawner.ForceCarsRespawn();
        }
    }

    private static IEnumerator RespawnAfterBlockersGone(GarageCarSpawner spawner, List<TrainCar> blockers)
    {
        for (int i = 0; i < 10 && blockers.Any(c => c != null); i++)
        {
            yield return null;
        }
        if (spawner == null) yield break;
        var spawned = spawner.ForceCarsRespawn();
        Main.Logger.Log($"Garage respawn after clearing blockers spawned {spawned?.Count ?? 0} car(s).");
    }

    private static void UnparentGarageCar(TrainCar car, GarageCarSpawner spawner)
    {
        var home = car.GetComponent<HomeGarageReference>();
        if (home != null) UnityEngine.Object.Destroy(home);
        car.OnDestroyCar -= DelegateFor<Action<TrainCar>>(spawner, "OnGarageCarDeleted");
    }

    private const float HomeSpawnRadius = 75f;

    private static bool IsParkedAtHome(TrainCar car, GarageCarSpawner spawner)
    {
        if (spawner.locoSpawnPoint == null) return false;
        var offset = car.transform.position - spawner.locoSpawnPoint.transform.position;
        return offset.sqrMagnitude < HomeSpawnRadius * HomeSpawnRadius;
    }

    private static void DeleteGarageCar(TrainCar car, GarageCarSpawner spawner)
    {
        Main.Logger.Log($"Deleting garage car {car.name} [{car.ID}] that was blocking its replacement from spawning.");
        car.OnDestroyCar -= DelegateFor<Action<TrainCar>>(spawner, "OnGarageCarDeleted");
        var home = car.GetComponent<HomeGarageReference>();
        if (home != null) UnityEngine.Object.Destroy(home);
        DestroyStaleBlockers(car);
        SingletonBehaviour<CarSpawner>.Instance.DeleteCar(car);
    }

    private static T DelegateFor<T>(object target, string method) where T : Delegate =>
        (T)Delegate.CreateDelegate(typeof(T), target, AccessTools.Method(target.GetType(), method));
}
