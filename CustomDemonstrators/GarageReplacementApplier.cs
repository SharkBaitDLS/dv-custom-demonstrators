using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

        if (!SaveGuard.AllowDemonstratorChanges()) return GarageVehicles.OriginalLiveries(garage);

        var primary = GarageVehicles.PrimaryLoco(garage);
        if (primary == null) return GarageVehicles.OriginalLiveries(garage);

        var desired = new List<TrainCarLivery> { Main.Settings.GetReplacement(primary) ?? primary };
        var tender = GarageReplacements.ResolveTender(primary.id, GarageVehicles.OriginalTender(garage));
        if (tender != null) desired.Add(tender);
        return [.. desired];
    }

    // Repoints a demonstrator's restoration controller at its replacement liveries, so the museum
    // restoration spawns and services the same car the garage now spawns, and applies the slot's
    // parts-cargo and price overrides.
    internal static void ApplyTo(LocoRestorationController controller)
    {
        if (!SaveGuard.AllowDemonstratorChanges()) return;

        var loco = OriginalLoco(controller);
        var tender = OriginalTender(controller);

        string slotId = loco?.id ?? "";

        var replacementLoco = loco != null ? Main.Settings.GetReplacement(loco) : null;
        if (loco != null)
            controller.locoLivery = replacementLoco ?? loco; // revert to vanilla when the override is cleared

        if (replacementLoco != null && controller.locoBlockerPrefab == null)
            controller.locoBlockerPrefab = OriginalBlocker(loco);

        RestorationPartsCustomizer.ApplyCargo(controller, slotId, replacementLoco);

        var tenderId = GarageReplacements.ResolveTender(slotId, tender);
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

        if (controller.State >= LocoRestorationController.RestorationState.S9_LocoServiced)
        {
            DetachFinishedLocoAndRespawn(controller);
        }
        else
        {
            RespawnWreck(controller);
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

        ClearRegister(controller.orderPartsModule);
        ClearRegister(controller.installPartsModule);

        foreach (var point in controller.spawnPoints)
        {
            point?.pointUsed = false;
        }

        Main.Logger.Log($"Destroying demonstrator {loco.name} [{loco.ID}] to force a respawn.");
        // Tearing down the tender cascades to the parent loco, but not visa versa,
        // so we attempt to delete that if it exists.
        var secondCar = t.Field("secondCar").GetValue<TrainCar>();
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

    private static GarageCarSpawner? SpawnerFor(GarageType_v2 garage)
    {
        foreach (var l in GarageVehicles.OriginalLiveries(garage))
        {
            if (l != null && GarageCarSpawner.Spawners.TryGetValue(l, out var spawner))
            {
                return spawner;
            }
        }
        return null;
    }

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
        foreach (var car in current)
        {
            if (car != null && !kept.Contains(car)) UnparentGarageCar(car, spawner);
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
        spawner.ForceCarsRespawn();
    }

    private static void UnparentGarageCar(TrainCar car, GarageCarSpawner spawner)
    {
        var home = car.GetComponent<HomeGarageReference>();
        if (home != null) UnityEngine.Object.Destroy(home);
        car.OnDestroyCar -= DelegateFor<Action<TrainCar>>(spawner, "OnGarageCarDeleted");
    }

    private static T DelegateFor<T>(object target, string method) where T : Delegate =>
        (T)Delegate.CreateDelegate(typeof(T), target, AccessTools.Method(target.GetType(), method));
}
