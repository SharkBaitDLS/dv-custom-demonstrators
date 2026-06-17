using System.Collections.Generic;
using System.Linq;
using DV;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;

namespace CustomDemonstrators;

internal enum SlotKind
{
    Garage,
    Demonstrator,
    UtilityFlatcar
}

// Enforces the game's rule that exactly one livery can be spawned in the entire demonstrator/garage
// pool. When the player picks a replacement that another slot already spawns, the two slots swap.
internal static class GarageReplacements
{
    // The restoration parts cargos (one per demonstrator). A replacement for the utility flatcar must
    // be able to carry all of them, since the single flatcar hauls parts for whichever demonstrator.
    // We also have inverse enforcement for custom restoration parts that they must be carriable by this
    // (or any custom selected) flatcar, so those two rules should combine to ensure that any combination
    // of custom flatcars or custom loco parts are UI-enforced to be usable.
    private static readonly CargoType[] PartsCargoTypes =
    [
        CargoType.TrainPartsDE2, CargoType.TrainPartsDE6, CargoType.TrainPartsDH4,
        CargoType.TrainPartsDM3, CargoType.TrainPartsS060, CargoType.TrainPartsS282A,
    ];

    internal static string CurrentSpawnId(TrainCarLivery slot) =>
        Main.Settings.LiveryReplacements.TryGetValue(slot.id, out var r) && !string.IsNullOrEmpty(r)
            ? r
            : slot.id;

    internal static SlotKind KindFor(GarageType_v2 garage, bool isDemonstrator) =>
        isDemonstrator ? SlotKind.Demonstrator
        : garage.v1 == Garage.Museum_FlatbedShort ? SlotKind.UtilityFlatcar
        : SlotKind.Garage;

    private static IEnumerable<(TrainCarLivery livery, SlotKind kind)> AllSlots() =>
        GarageVehicles.Groups.SelectMany(g => g.liveries.Select(l => (l, KindFor(g.garage, g.isDemonstrator))));

    private static TrainCarLivery? GetLivery(string id) =>
        Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == id);

    private static (TrainCarLivery livery, SlotKind kind) ColliderFor(TrainCarLivery slot, string targetId) =>
        AllSlots().FirstOrDefault(s => s.livery.id != slot.id && CurrentSpawnId(s.livery) == targetId);

    internal static bool IsClaimedByOther(TrainCarLivery slot, string candidateId) =>
        ColliderFor(slot, candidateId).livery != null;

    // Whether offering `candidate` as `slot`'s spawn yields a valid configuration. Used to filter the
    // picker so the player is never shown a choice whose swap would corrupt a slot.
    internal static bool CanSelect(TrainCarLivery slot, SlotKind slotKind, TrainCarLivery candidate)
    {
        if (!IsValidFor(slotKind, candidate))
            return false;

        // A car already serving as a demonstrator's tender can't also be a garage/loco spawn (the
        // game allows a livery in only one garage), and tenders aren't swappable, so disallow it.
        if (CurrentSpawnId(slot) != candidate.id && TenderIds().Contains(candidate.id))
            return false;

        // Likewise a car already added as another garage's extra consist car has no swap partner.
        if (CurrentSpawnId(slot) != candidate.id && ExtraCarIds().Contains(candidate.id))
            return false;

        // Selecting a candidate another slot already spawns trades our current spawn to that slot.
        // Don't allow a swap that would push an invalid car onto a more restricted slot.
        var (livery, colliderKind) = ColliderFor(slot, candidate.id);
        if (livery != null && colliderKind != SlotKind.Garage)
        {
            var vacated = GetLivery(CurrentSpawnId(slot));
            if (vacated == null || !IsValidFor(colliderKind, vacated))
                return false;
        }
        return true;
    }

    private static bool IsValidFor(SlotKind kind, TrainCarLivery livery) => kind switch
    {
        SlotKind.Demonstrator => IsValidDemonstrator(livery),
        SlotKind.UtilityFlatcar => CanCarryRestorationParts(livery),
        _ => true,
    };

    // Demonstrator replacements are restricted to Custom Car Loader locos that are license-gated.
    // The museum questline only fires if the locomotive is gated by a license so this is a hard requirement
    // to make the questline work correctly. Non-licensed CCL locos can still be put in garages.
    // 
    // Swapping around the vanilla demonstrators would only serve to move them around in the roundhouse
    // which is (imo) pretty much useless and not worth the UI clutter to add them into the selections.
    internal static bool IsValidDemonstrator(TrainCarLivery livery) =>
        CustomCarLoaderHelper.IsCustomCar(livery)
        && CarTypes.IsLocomotive(livery) && livery.requiredLicense != null;

    // The tender isn't license-gated by the restoration, it shares its blocker rules with its
    // locomotive.
    internal static bool IsValidTender(TrainCarLivery livery) =>
        CustomCarLoaderHelper.IsCustomCar(livery) && CarTypes.IsTender(livery);

    internal static TrainCarLivery? ResolveTender(string slotId, TrainCarLivery? originalTender)
    {
        var id = Main.Settings.GetTenderId(slotId);
        if (!string.IsNullOrEmpty(id)) return GetLivery(id!);
        return IsPrimaryReplaced(slotId) ? null : originalTender;
    }

    private static bool IsPrimaryReplaced(string slotId) =>
        Main.Settings.LiveryReplacements.TryGetValue(slotId, out var r)
        && !string.IsNullOrEmpty(r) && r != slotId;

    internal static bool CanSelectTender(TrainCarLivery primary, TrainCarLivery? originalTender, TrainCarLivery candidate)
    {
        if (!IsValidTender(candidate)) return false;
        var current = ResolveTender(primary.id, originalTender);
        if (current != null && current.id == candidate.id) return true;
        return !AllSpawnedIds().Contains(candidate.id);
    }

    // Track the full set of configured spawns to ensure enforcement of exactly-one spawn in the combined
    // demonstrator/garage pool.
    internal static IEnumerable<string> AllSpawnedIds()
    {
        foreach (var (garage, isDemonstrator, liveries) in GarageVehicles.Groups)
        {
            if (isDemonstrator)
            {
                var primary = liveries.FirstOrDefault();
                if (primary == null) continue;
                yield return CurrentSpawnId(primary);
                var second = ResolveTender(primary.id, GarageVehicles.OriginalTender(garage));
                if (second != null) yield return second.id;
            }
            else
            {
                foreach (var livery in liveries)
                    yield return CurrentSpawnId(livery);
                foreach (var extra in Main.Settings.GetExtraCars(garage.id))
                    yield return extra;
            }
        }
    }

    // Liveries currently configured as a garage's extra consist cars.
    private static HashSet<string> ExtraCarIds()
    {
        var ids = new HashSet<string>();
        foreach (var (garage, isDemonstrator, _) in GarageVehicles.Groups)
        {
            if (isDemonstrator) continue;
            foreach (var extra in Main.Settings.GetExtraCars(garage.id))
                ids.Add(extra);
        }
        return ids;
    }

    // The id of the garage a slot livery belongs to, if any.
    private static string? GarageIdForSlot(TrainCarLivery slot)
    {
        foreach (var (garage, isDemonstrator, liveries) in GarageVehicles.Groups)
        {
            if (isDemonstrator) continue;
            if (liveries.Any(l => l != null && l.id == slot.id)) return garage.id;
        }
        return null;
    }

    internal static bool CanAddExtraCar(TrainCarLivery candidate) =>
        !AllSpawnedIds().Contains(candidate.id);

    // The resolved tender ids across all demonstrators, so the normal-garage picker can avoid
    // handing out a livery that's already serving as a tender.
    private static HashSet<string> TenderIds()
    {
        var ids = new HashSet<string>();
        foreach (var (garage, isDemonstrator, liveries) in GarageVehicles.Groups)
        {
            if (!isDemonstrator) continue;
            var primary = liveries.FirstOrDefault();
            if (primary == null) continue;
            var second = ResolveTender(primary.id, GarageVehicles.OriginalTender(garage));
            if (second != null) ids.Add(second.id);
        }
        return ids;
    }

    internal static bool CanCarryRestorationParts(TrainCarLivery livery)
    {
        var carType = livery.parentType;
        if (carType == null) return false;
        foreach (var t in PartsCargoTypes)
        {
            var cargo = t.ToV2();
            if (cargo != null && !cargo.IsLoadableOnCarType(carType))
                return false;
        }
        return true;
    }

    private static TrainCarLivery? RestorationFlatcar()
    {
        foreach (var (garage, isDemonstrator, liveries) in GarageVehicles.Groups)
        {
            if (isDemonstrator || garage.v1 != Garage.Museum_FlatbedShort) continue;
            var slot = liveries.FirstOrDefault();
            return slot == null ? null : GetLivery(CurrentSpawnId(slot)) ?? slot;
        }
        return null;
    }

    internal static bool CanBeRestorationParts(CargoType_v2 cargo)
    {
        var carType = RestorationFlatcar()?.parentType;
        return carType == null || cargo.IsLoadableOnCarType(carType);
    }

    // After the flatcar changes, drop any explicit parts-cargo overrides the new flatcar can't carry.
    private static void PruneInvalidCargoOverrides()
    {
        foreach (var (_, isDemonstrator, liveries) in GarageVehicles.Groups)
        {
            if (!isDemonstrator) continue;
            var primary = liveries.FirstOrDefault();
            if (primary == null) continue;

            var choice = Main.Settings.GetPartsCargoId(primary.id);
            // auto-detect and the generic crate are always loadable since we enforce that flatcar selections
            // can at minimum carry all vanilla parts cargo
            if (string.IsNullOrEmpty(choice) || choice == RestorationPartsCustomizer.GenericCrateSentinel)
                continue;

            var cargo = RestorationPartsCustomizer.FindCargo(choice!);
            if (cargo == null || !CanBeRestorationParts(cargo))
                Main.Settings.SetPartsCargoId(primary.id, null);
        }
    }

    // Applies a selection, swapping with any colliding slot to keep every spawn unique.
    internal static void Select(TrainCarLivery slot, string? newSpawnId)
    {
        // Clearing the main car reverts the garage to vanilla, so its extra consist cars (which only make
        // sense alongside a customized consist) go with it.
        if (string.IsNullOrEmpty(newSpawnId) && GarageIdForSlot(slot) is string garageId)
            Main.Settings.ClearExtraCars(garageId);

        string targetId = string.IsNullOrEmpty(newSpawnId) ? slot.id : newSpawnId!;
        string vacatedId = CurrentSpawnId(slot);
        if (targetId == vacatedId)
        {
            SetSpawn(slot, targetId);
            return;
        }

        var (livery, _) = ColliderFor(slot, targetId);
        SetSpawn(slot, targetId);
        if (livery != null)
            SetSpawn(livery, vacatedId);

        PruneInvalidCargoOverrides();
    }

    private static void SetSpawn(TrainCarLivery slot, string spawnId)
    {
        if (spawnId == slot.id)
            Main.Settings.LiveryReplacements.Remove(slot.id);
        else
            Main.Settings.LiveryReplacements[slot.id] = spawnId;
    }
}
