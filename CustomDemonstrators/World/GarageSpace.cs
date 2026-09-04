using UnityEngine;

namespace CustomDemonstrators.World;

// The safe spawnable dimensions of a locked garage
internal readonly struct GarageSpace(GarageSpace.DoorDirection direction, float toDoors, float toBack)
{
    internal enum DoorDirection
    {
        Forward = 1,
        Reverse = -1,
    }

    private const float DoorClearance = 0.5f;

    internal readonly DoorDirection Direction = direction;

    internal readonly float ToDoors = toDoors;

    internal readonly float ToBack = toBack;

    internal float Length => ToDoors + ToBack;

    internal static GarageSpace? Measure(GaragePadlockUnlocker unlocker, Vector3 anchor, Vector3 forward)
    {
        var blockers = unlocker.blockers;
        var padlock = unlocker.padlock;
        if (blockers == null || padlock == null) return null;

        float back = float.MaxValue, front = float.MinValue;
        foreach (var box in blockers.GetComponentsInChildren<BoxCollider>(includeInactive: true))
        {
            var shape = box.transform;
            var size = Vector3.Scale(box.size, shape.lossyScale);
            float reach = 0.5f * (
                Mathf.Abs(Vector3.Dot(shape.right, forward)) * size.x
                + Mathf.Abs(Vector3.Dot(shape.up, forward)) * size.y
                + Mathf.Abs(Vector3.Dot(shape.forward, forward)) * size.z);
            float middle = Vector3.Dot(shape.TransformPoint(box.center) - anchor, forward);

            back = Mathf.Min(back, middle - reach);
            front = Mathf.Max(front, middle + reach);
        }

        if (back >= 0f || front <= 0f)
        {
            Main.Logger.Warning($"Garage preview: the anchor for {unlocker.name} sits outside the "
                + "garage's teleport blockers, so its consist can't be measured against the building.");
            return null;
        }

        float doors = Vector3.Dot(padlock.transform.position - anchor, forward);
        DoorDirection direction = doors >= 0f ? DoorDirection.Forward : DoorDirection.Reverse;

        float toDoors = Mathf.Abs(doors) - DoorClearance;
        float toBack = direction == DoorDirection.Forward ? -back : front;
        if (toDoors <= 0f || toBack <= 0f) return null;

        return new GarageSpace(direction, toDoors, toBack);
    }

    internal static Bounds? Volume(GaragePadlockUnlocker unlocker)
    {
        var blockers = unlocker.blockers;
        if (blockers == null) return null;

        Bounds? volume = null;
        foreach (var box in blockers.GetComponentsInChildren<BoxCollider>(includeInactive: true))
        {
            var shape = box.transform;
            var size = Vector3.Scale(box.size, shape.lossyScale);
            var spread = 0.5f * (
                Abs(shape.right) * size.x + Abs(shape.up) * size.y + Abs(shape.forward) * size.z);
            var box3d = new Bounds(shape.TransformPoint(box.center), spread * 2f);

            if (volume is Bounds grown)
            {
                grown.Encapsulate(box3d);
                volume = grown;
            }
            else
            {
                volume = box3d;
            }
        }

        return volume;
    }

    private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
}
