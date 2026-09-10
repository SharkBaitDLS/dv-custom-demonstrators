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

    internal static Placement? PlacementFor(string garageId) =>
        PlacedHomes.Decode(_saved.Get(garageId));

    // Same asymmetry as a demonstrator slot's
    internal static bool WouldErasePlacement(string garageId, Placement? home) =>
        home == null && PlacementFor(garageId) != null;

    // Whether the settings put a garage somewhere other than where the save has it standing.
    internal static bool PlacementMoved(string garageId, Placement? home)
    {
        if (home is not Placement placement) return false;
        if (PlacementFor(garageId) is not Placement saved) return true;
        return saved.Offset != placement.Offset || !Mathf.Approximately(saved.Yaw, placement.Yaw);
    }

    internal static void RecordPlacement(string garageId, Placement? placement) =>
        _saved.Set(garageId, placement is Placement p ? PlacedHomes.Encode(p) : null);

    // The frame a placement is stored in. Everything in the world hangs off the origin shift parent and
    // moves with it, so an offset in its space is the same spot on the map however far the player has walked.
    internal static Transform? Anchor() => WorldMover.OriginShiftParent;

    internal static Placement? Placement(string garageId, string primaryId)
    {
        if (!SaveGuard.AllowGarageChanges()) return PlacementFor(garageId);

        var placement = Main.Settings.GetAdditionalGarage(primaryId)?.Home;
        RecordPlacement(garageId, placement);
        return placement;
    }

    // The spawn point the garage's cars are placed at
    internal static void PlaceHome(GameObject home, Placement placement)
    {
        home.transform.localPosition = placement.Offset;
        home.transform.localRotation = Quaternion.Euler(0f, placement.Yaw, 0f);
    }
}
