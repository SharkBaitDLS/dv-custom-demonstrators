using System.Collections.Generic;
using DV.ThingTypes;
using DV.Utils;
using UnityEngine;

namespace CustomDemonstrators.World;

internal static class GarageStandIn
{
    private const float CarGap = 1f;

    internal static GameObject? Build(
        TrainCarLivery[] liveries, GarageCarSpawner spawner, GaragePadlockUnlocker unlocker,
        GarageType_v2 garage)
    {
        var root = new GameObject($"CustomDemonstrators_{garage.id}_StandIn");
        root.SetActive(false);

        var cars = new List<(GameObject Car, float Length, float Centre)>();
        foreach (var livery in liveries)
        {
            var car = CarModel.Build(livery, root.transform);
            if (car == null) continue;

            var bounds = CarModel.LocalBounds(car);
            cars.Add((car, bounds.size.z, bounds.center.z));
        }

        if (cars.Count == 0
            || !Pose(spawner, unlocker, CarModel.LocalBounds(cars[0].Car).extents, out var pose, out var facing))
        {
            Object.DestroyImmediate(root);
            return null;
        }

        Arrange(cars, GarageSpace.Measure(unlocker, pose, facing * Vector3.forward), garage);

        root.transform.SetPositionAndRotation(pose, facing);
        root.SetActive(true);
        return root;
    }

    private static void Arrange(
        List<(GameObject Car, float Length, float Centre)> cars, GarageSpace? space, GarageType_v2 garage)
    {
        float room = space?.Length ?? 0f;

        int fits = cars.Count;
        float total = 0f;
        for (int i = 0; i < cars.Count; i++)
        {
            float needed = total + cars[i].Length + (i > 0 ? CarGap : 0f);

            // The first car is shown whatever its size even if it clips out the back because it looks
            // cooler to have it there when you open the doors and most of the garages you aren't really 
            // looking at the back anyway. The DM1U garage is the only one where it might be noticeably ugly
            // but given that nobody has a CCL replacement for the DM1U most players will probably not override
            // that garage anyway.
            if (room > 0f && i > 0 && needed > room)
            {
                fits = i;
                break;
            }
            total = needed;
        }

        if (fits < cars.Count)
        {
            Main.Logger.Log($"Preview for {garage.id} shows {fits} of {cars.Count} cars; "
                + $"the rest would not fit in the {room:0.0} m the garage has.");
            for (int i = fits; i < cars.Count; i++) UnityEngine.Object.DestroyImmediate(cars[i].Car);
            cars.RemoveRange(fits, cars.Count - fits);
        }

        float cursor = total / 2f + Shift(total, space, garage);
        foreach (var (car, length, centre) in cars)
        {
            car.transform.localPosition = new Vector3(0f, 0f, cursor - length / 2f - centre);
            cursor -= length + CarGap;
        }
    }

    private static float Shift(float total, GarageSpace? space, GarageType_v2 garage)
    {
        if (space is not GarageSpace room) return 0f;

        float half = total / 2f;
        float shift;
        if (half > room.ToDoors)
        {
            shift = room.ToDoors - half;
            Main.Logger.Log($"Preview for {garage.id} is {half - room.ToDoors:0.0} m too long to sit "
                + "centred without blocking the doors, so it hangs out the back instead.");
        }
        else if (half > room.ToBack)
        {
            shift = Mathf.Min(half - room.ToBack, room.ToDoors - half);
        }
        else
        {
            return 0f;
        }

        return (int)room.Direction * shift;
    }

    // Try to let the game use its track placement algorithm, but clamp it since the game will try
    // to put it outside the garage if it doesn't fit and we want to make it clip in that case.
    private static bool Pose(
        GarageCarSpawner spawner, GaragePadlockUnlocker unlocker, Vector3 extents,
        out Vector3 position, out Quaternion rotation)
    {
        var anchor = spawner.locoSpawnPoint.transform.position;
        position = anchor;
        rotation = Quaternion.identity;

        var registry = SingletonBehaviour<RailTrackRegistryBase>.Instance;
        if (registry == null || registry.AllTracks == null || registry.AllTracks.Length == 0) return false;

        var closest = RailTrack.GetClosest(anchor, 0f, registry.AllTracks).point;
        if (closest == null) return false;

        rotation = Facing(closest.Value.forward, spawner);

        var garage = GarageSpace.Volume(unlocker);
        var onRails = (Vector3)closest.Value.position + WorldMover.currentMove;
        if (garage is not Bounds building || building.Contains(onRails)) position = onRails;

        if (garage is not Bounds inside) return true;

        var spot = CarSpawner.GetPointOnClosestAvailableTrackForCar(
            anchor, extents, registry.AllTracks, 1f, 1f, inside.extents.magnitude);
        if (!spot.HasValue) return true;

        var clear = (Vector3)spot.Value.point.position + WorldMover.currentMove;
        if (!inside.Contains(clear)) return true;

        position = clear;
        rotation = Facing(spot.Value.point.forward, spawner);
        return true;
    }

    private static Quaternion Facing(Vector3 forward, GarageCarSpawner spawner) =>
        Quaternion.LookRotation(spawner.flipSpawnLoco ? -forward : forward, Vector3.up);
}
