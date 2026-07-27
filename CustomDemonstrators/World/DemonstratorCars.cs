using System;
using System.Collections;
using System.Collections.Generic;
using DV.Garages;
using DV.LocoRestoration;
using DV.Shops;
using DV.Utils;
using HarmonyLib;

namespace CustomDemonstrators.World;

internal static class DemonstratorCars
{
    // Cleans up a demonstrator by either deleting an un-restored wreck, or orphaning a finished one
    // as a player-owned locomotive that is no longer associated with a garage.
    internal static void Retire(LocoRestorationController controller, GarageCarSpawner? spawner)
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

    internal static void ClearRegister(GenericThingCashRegisterModule? module)
    {
        if (module == null) return;
        AccessTools.Method(module.GetType(), "SetUnitsToBuy", [typeof(float)])?.Invoke(module, [0f]);
    }

    // Hands a finished loco over to the player and restarts the controller from scratch, so it respawns as
    // whatever the settings now say the slot should be.
    internal static void DetachFinishedAndRestart(LocoRestorationController controller)
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

    // How close a restoration car has to be to an anchor for that anchor to count as still occupied. The
    // wreck spawns centred on its anchor, so anything roughly a car length away has been moved off it.
    private const float SpawnPointOccupiedRadius = 20f;

    // Recomputes which wreck anchors are taken, from where the restoration cars actually are
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
}
