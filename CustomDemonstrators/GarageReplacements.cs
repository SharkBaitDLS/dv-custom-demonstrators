using System.Collections.Generic;
using System.Linq;
using DV;
using DV.ThingTypes;

namespace CustomDemonstrators;

// Enforces the game's rule that exactly one livery can be spawned in the entire demonstrator/garage
// pool. When the player picks a replacement that another slot already spawns, the two slots swap.
internal static class GarageReplacements
{
    // The livery id a slot currently spawns: its configured replacement, else its own default.
    internal static string CurrentSpawnId(TrainCarLivery slot) =>
        Main.Settings.LiveryReplacements.TryGetValue(slot.id, out var r) && !string.IsNullOrEmpty(r)
            ? r
            : slot.id;

    private static IEnumerable<(TrainCarLivery livery, bool isDemonstrator)> AllSlots() =>
        GarageVehicles.Groups.SelectMany(g => g.liveries.Select(l => (l, g.isDemonstrator)));

    private static TrainCarLivery? GetLivery(string id) =>
        Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == id);

    // The other slot (if any) currently spawning targetId
    private static (TrainCarLivery livery, bool isDemonstrator) ColliderFor(TrainCarLivery slot, string targetId) =>
        AllSlots().FirstOrDefault(s => s.livery.id != slot.id && CurrentSpawnId(s.livery) == targetId);

    // True if another garage slot currently spawns candidateId
    internal static bool IsClaimedByOther(TrainCarLivery slot, string candidateId) =>
        ColliderFor(slot, candidateId).livery != null;

    // Whether offering `candidate` as `slot`'s spawn yields a valid configuration. Used to filter the
    // picker so the player is never shown a choice whose swap would corrupt a slot.
    internal static bool CanSelect(TrainCarLivery slot, bool slotIsDemonstrator, TrainCarLivery candidate)
    {
        // A demonstrator can only spawn a locomotive (or its tender).
        if (slotIsDemonstrator && !CarTypes.IsAnyLocomotiveOrTender(candidate))
            return false;

        // Selecting a candidate another slot already spawns hands our current spawn to that slot.
        // Don't allow a swap that would push a non-locomotive onto a demonstrator.
        var (livery, isDemonstrator) = ColliderFor(slot, candidate.id);
        if (livery != null && isDemonstrator)
        {
            var vacated = GetLivery(CurrentSpawnId(slot));
            if (vacated == null || !CarTypes.IsAnyLocomotiveOrTender(vacated))
                return false;
        }
        return true;
    }

    // Applies a selection, swapping with any colliding slot to keep every spawn unique.
    internal static void Select(TrainCarLivery slot, string? newSpawnId)
    {
        string targetId = string.IsNullOrEmpty(newSpawnId) ? slot.id : newSpawnId!;
        string vacatedId = CurrentSpawnId(slot);
        if (targetId == vacatedId)
        {
            SetSpawn(slot, targetId);
            return;
        }

        var collider = ColliderFor(slot, targetId);
        SetSpawn(slot, targetId);
        if (collider.livery != null)
            SetSpawn(collider.livery, vacatedId);
    }

    private static void SetSpawn(TrainCarLivery slot, string spawnId)
    {
        if (spawnId == slot.id)
            Main.Settings.LiveryReplacements.Remove(slot.id);
        else
            Main.Settings.LiveryReplacements[slot.id] = spawnId;
    }
}
