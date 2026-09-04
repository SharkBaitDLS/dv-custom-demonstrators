using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV.Garages;
using DV.ThingTypes;
using DV.Utils;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

internal static class WorkTrainGarages
{
    internal static void ReinitializeAll()
    {
        foreach (var (garage, isDemonstrator, _) in VanillaGarages.Groups)
        {
            if (isDemonstrator) continue;
            if (SpawnerFor(garage) is not GarageCarSpawner spawner) continue;

            Reconcile(spawner);
            GaragePreviews.Refresh(spawner);
        }
    }

    private static GarageCarSpawner? SpawnerFor(GarageType_v2 garage) =>
        GarageCarSpawner.Spawners.Values.FirstOrDefault(s => s.garageType == garage);

    private static void Reconcile(GarageCarSpawner spawner)
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

        if (!GarageUnlocks.IsSpawningAllowed(spawner))
        {
            Main.Logger.Log($"Garage {spawner.garageType.id} is still locked, so its new consist stays "
                + "unspawned until the player opens it.");
            return;
        }

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
        car.OnDestroyCar -= CarLifecycle.DelegateFor<Action<TrainCar>>(spawner, "OnGarageCarDeleted");
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
        car.OnDestroyCar -= CarLifecycle.DelegateFor<Action<TrainCar>>(spawner, "OnGarageCarDeleted");
        var home = car.GetComponent<HomeGarageReference>();
        if (home != null) UnityEngine.Object.Destroy(home);
        CarLifecycle.DestroyStaleBlockers(car);
        SingletonBehaviour<CarSpawner>.Instance.DeleteCar(car);
    }
}
