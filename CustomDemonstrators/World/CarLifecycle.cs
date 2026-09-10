using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.ServicePenalty;
using DV.Utils;
using HarmonyLib;

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

    // Rebuilds a delegate equal to one the game subscribed itself, so it can be unsubscribed by value.
    internal static T DelegateFor<T>(object target, string method) where T : Delegate =>
        (T)Delegate.CreateDelegate(typeof(T), target, AccessTools.Method(target.GetType(), method));
}
