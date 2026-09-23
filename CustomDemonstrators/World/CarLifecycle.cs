using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.Utils;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace CustomDemonstrators.World;

// Small pieces of car teardown needed by both the demonstrator and the ordinary garage paths.
internal static class CarLifecycle
{
    private static readonly FieldInfo? BlockerTrainField =
        AccessTools.Field(typeof(LocoZoneBlocker), "train");

    // A blocked wreck's LocoZoneBlocker parents itself to the loco's interior transform, which isn't
    // loaded while the cab is blocked, so the blocker sits at the scene root rather than inside the car
    // hierarchy. Deleting the car therefore leaves the blocker alive and still subscribed to
    // LicenseManager.LicenseAcquired, and the next license purchase throws NREs from its dead references
    // in the middle of the event invocation, potentially leading to save corruption if any other mods
    // have active patching logic in that chain such as Career Rework.
    internal static void DestroyStaleBlockers(TrainCar? car)
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

    // Every car a garage or a demonstrator slot spawns is a uniqueCar, and DV stages a StagedOwnedCarDebt
    // for one of those the moment it is destroyed - even a pristine one, because the debt data is filtered
    // with returnEmptyDebtInsteadOfNull. It is deliberately unpayable: the game expects the car to come back,
    // since a deleted uniqueCar's state is stashed under its livery and the returning car of that livery
    // reclaims the old ID, which is what retires the staged entry. We delete these cars precisely so a
    // *different* livery can take their place, so nothing ever reclaims the ID and the entry sits in the
    // career manager forever at $0 with no way to clear it. Drop what this deletion staged.
    internal static void Delete(TrainCar car)
    {
        var owned = SingletonBehaviour<OwnedCarsStateController>.Instance;
        // Set difference rather than matching on the car's ID: deleting a tender cascades to its loco, so
        // one call can stage more than one debt, and only the ones this call created may be dropped.
        var before = owned != null
            ? new HashSet<StagedOwnedCarDebt>(owned.currentlyDestroyedOwnedCarStates)
            : null;

        SingletonBehaviour<CarSpawner>.Instance.DeleteCar(car);

        if (owned == null || before == null) return;

        var staged = owned.currentlyDestroyedOwnedCarStates.Where(d => !before.Contains(d)).ToList();
        if (staged.Count == 0) return;

        owned.currentlyDestroyedOwnedCarStates.RemoveAll(d => !before.Contains(d));
        owned.UpdateSortedList();

        Main.Logger.Log($"Dropped {staged.Count} owned-car fee(s) staged by removing this car, which nothing "
            + "would ever clear once its replacement takes a different ID: "
            + string.Join(", ", staged.Select(d => d.ID)));
    }

    private static readonly FieldInfo? DeletedUniqueField =
        AccessTools.Field(typeof(CarSpawner), "deletedUniqueCarLiveryToLastCarState");

    private static readonly HashSet<TrainCar> _forgetting = [];
    private static CarSpawner? _watching;

    // Deleting a uniqueCar stashes its whole state under its livery and reserves its loco ID,
    // so that it will respawn in the same state with the same ID. When we want to delete one
    // for real and not have it respawn, we clean out that state so that if it's re-added as
    // a demonstrator later it doesn't show back up in its prior (possibly restored) state.
    internal static void ForgetStateOnDelete(params TrainCar?[] cars)
    {
        _forgetting.RemoveWhere(car => car == null);
        foreach (var car in cars)
        {
            if (car != null) _forgetting.Add(car);
        }

        var spawner = SingletonBehaviour<CarSpawner>.Instance;
        if (_forgetting.Count == 0 || spawner == null || spawner == _watching) return;

        _watching?.CarAboutToBeDeleted -= OnCarAboutToBeDeleted;
        spawner.CarAboutToBeDeleted += OnCarAboutToBeDeleted;
        _watching = spawner;
    }

    private static void OnCarAboutToBeDeleted(TrainCar car)
    {
        if (_forgetting.Remove(car)) ForgetStashedState(car);
    }

    private static void ForgetStashedState(TrainCar car)
    {
        if (car.carLivery == null
            || DeletedUniqueField?.GetValue(SingletonBehaviour<CarSpawner>.Instance) is not IDictionary stash
            || !stash.Contains(car.carLivery))
        {
            return;
        }

        var stashedId = (stash[car.carLivery] as JObject)?["id"]?.ToString();
        stash.Remove(car.carLivery);
        if (!string.IsNullOrEmpty(stashedId)) SingletonBehaviour<IdGenerator>.Instance.UnReserveCarId(stashedId);

        Main.Logger.Log($"Dropped the stashed state of {car.carLivery.id} [{stashedId}].");
    }

    // Rebuilds a delegate equal to one the game subscribed itself, so it can be unsubscribed by value.
    internal static T DelegateFor<T>(object target, string method) where T : Delegate =>
        (T)Delegate.CreateDelegate(typeof(T), target, AccessTools.Method(target.GetType(), method));
}
