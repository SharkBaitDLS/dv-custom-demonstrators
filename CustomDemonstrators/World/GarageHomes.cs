using UnityEngine;
using CustomDemonstrators.Saves;

namespace CustomDemonstrators.World;

internal static class GarageHomes
{
    // Kept in the save rather than only in the settings for the same reason a demonstrator's is.
    // A load where they aren't the ones this save was baked from still has to be able to
    // put the garage back where it stood.
    private static readonly SavedMap _saved =
        new("CustomDemonstrators_GarageHomeIds", "CustomDemonstrators_GarageHomePlaces");

    internal static void Reset() => _saved.Reset();

    internal static void Release(string garageId) => _saved.Set(garageId, null);

    internal static (Vector3 Offset, float Yaw)? PlacementFor(string garageId) =>
        PlacedHomes.Decode(_saved.Get(garageId));

    // Same asymmetry as a demonstrator slot's
    internal static bool WouldErasePlacement(string garageId, Vector3? home) =>
        home == null && PlacementFor(garageId) != null;

    internal static void RecordPlacement(string garageId, (Vector3 Offset, float Yaw)? placement) =>
        _saved.Set(garageId, placement is (Vector3 offset, float yaw)
            ? PlacedHomes.Encode(offset, yaw)
            : null);

    // The frame a placement is stored in. Everything in the world hangs off the origin shift parent and
    // moves with it, so an offset in its space is the same spot on the map however far the player has walked.
    internal static Transform? Anchor() => WorldMover.OriginShiftParent;

    internal static (Vector3 Offset, float Yaw)? Placement(string garageId, string primaryId)
    {
        if (!SaveGuard.AllowGarageChanges()) return PlacementFor(garageId);

        var garage = Main.Settings.GetAdditionalGarage(primaryId);
        var placement = garage?.Home is Vector3 offset ? (offset, garage.HomeYaw) : ((Vector3, float)?)null;
        RecordPlacement(garageId, placement);
        return placement;
    }

    // The spawn point the garage's cars are placed at
    internal static void PlaceHome(GameObject home, (Vector3 Offset, float Yaw) placement)
    {
        home.transform.localPosition = placement.Offset;
        home.transform.localRotation = Quaternion.Euler(0f, placement.Yaw, 0f);
    }
}
