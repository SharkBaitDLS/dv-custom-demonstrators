using System;
using System.Reflection;
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

    // Rebuilds a delegate equal to one the game subscribed itself, so it can be unsubscribed by value.
    internal static T DelegateFor<T>(object target, string method) where T : Delegate =>
        (T)Delegate.CreateDelegate(typeof(T), target, AccessTools.Method(target.GetType(), method));
}
