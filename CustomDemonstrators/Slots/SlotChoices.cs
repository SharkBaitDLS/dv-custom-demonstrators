using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using CustomDemonstrators.World;

namespace CustomDemonstrators.Slots;

internal enum SlotKind
{
    Garage,
    Demonstrator,
    UtilityFlatcar
}

// Enforces the game's rule that exactly one livery can be spawned in the entire demonstrator/garage
// pool. When the player picks a replacement that another slot already spawns, the two slots swap.
internal static class SlotChoices
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

    internal static string CurrentSpawnId(TrainCarLivery slot) => CurrentSpawnId(slot.id);

    internal static string CurrentSpawnId(string slotId) =>
        Main.Settings.LiveryReplacements.TryGetValue(slotId, out var r) && !string.IsNullOrEmpty(r)
            ? r
            : slotId;

    internal static SlotKind KindFor(GarageType_v2 garage, bool isDemonstrator) =>
        isDemonstrator ? SlotKind.Demonstrator
        : garage.v1 == Garage.Museum_FlatbedShort ? SlotKind.UtilityFlatcar
        : SlotKind.Garage;

    private static IEnumerable<(TrainCarLivery livery, SlotKind kind)> AllSlots() =>
        VanillaGarages.Groups.SelectMany(g => g.liveries.Select(l => (l, KindFor(g.garage, g.isDemonstrator))));

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

        // Same for a car serving one of the slots this mod adds: those are chosen directly rather than
        // being a replacement for something, so there's nothing to trade back.
        if (CurrentSpawnId(slot) != candidate.id && AdditionalSlotIds().Contains(candidate.id))
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

    private static void InferTender(TrainCarLivery slot)
    {
        if (!IsPrimaryReplaced(slot.id)) return;

        var loco = GetLivery(CurrentSpawnId(slot.id));
        var tender = loco != null ? AutoTender(loco) : null;

        // Never auto-claim a livery something else already spawns
        if (tender != null && !AllSpawnedIds().Contains(tender.id))
            Main.Settings.SetTenderId(slot.id, tender.id);
    }

    // Both sides of a swap are cleared before either is inferred, so a swap goes through properly
    private static void InferTendersForSwap(params TrainCarLivery?[] slots)
    {
        foreach (var slot in slots)
            if (slot != null && IsDemonstratorSlot(slot)) Main.Settings.SetTenderId(slot.id, null);
        foreach (var slot in slots)
            if (slot != null && IsDemonstratorSlot(slot)) InferTender(slot);
    }

    private static bool IsDemonstratorSlot(TrainCarLivery slot) =>
        AllSlots().Any(s => s.livery.id == slot.id && s.kind == SlotKind.Demonstrator);

    // Adds a demonstrator slot of this mod's own, pre-selecting its loco's tender the same way.
    internal static void AddAdditionalSlot(string locoId)
    {
        Main.Settings.AddAdditionalSlot(locoId);

        var loco = GetLivery(locoId);
        var tender = loco != null ? AutoTender(loco) : null;
        if (tender != null && !AllSpawnedIds().Contains(tender.id))
            Main.Settings.SetTenderId(locoId, tender.id);
    }

    // CCL as the primary source of truth, anything without a configured trainset falls back to the game's
    // <livery>A + <livery>B convention.
    internal static TrainCarLivery? AutoTender(TrainCarLivery loco)
    {
        if (!CarTypes.IsLocomotive(loco)) return null;

        foreach (var member in CustomCarLoaderHelper.TrainsetFor(loco))
            if (member != null && CarTypes.IsTender(member)) return member;

        return ConventionalTender(loco);
    }

    private static TrainCarLivery? ConventionalTender(TrainCarLivery loco)
    {
        if (!loco.id.EndsWith("A", StringComparison.Ordinal)) return null;
        var tender = GetLivery(loco.id.Substring(0, loco.id.Length - 1) + "B");
        return tender != null && CarTypes.IsTender(tender) ? tender : null;
    }

    private static bool IsPrimaryReplaced(string slotId) =>
        Main.Settings.LiveryReplacements.TryGetValue(slotId, out var r)
        && !string.IsNullOrEmpty(r) && r != slotId;

    internal static bool CanSelectTender(string slotId, TrainCarLivery? originalTender, TrainCarLivery candidate)
    {
        if (!IsValidTender(candidate)) return false;
        var current = ResolveTender(slotId, originalTender);
        if (current != null && current.id == candidate.id) return true;
        return !AllSpawnedIds().Contains(candidate.id);
    }

    // Track the full set of configured spawns to ensure enforcement of exactly-one spawn in the combined
    // demonstrator/garage pool.
    internal static IEnumerable<string> AllSpawnedIds()
    {
        foreach (var (garage, isDemonstrator, liveries) in VanillaGarages.Groups)
        {
            if (isDemonstrator)
            {
                var primary = liveries.FirstOrDefault();
                if (primary == null) continue;
                yield return CurrentSpawnId(primary);
                var second = ResolveTender(primary.id, VanillaGarages.OriginalTender(garage));
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

        foreach (var id in AdditionalSlotIds())
            yield return id;
    }

    // The locos (and tenders) claimed by the demonstrator slots this mod adds.
    internal static HashSet<string> AdditionalSlotIds()
    {
        var ids = new HashSet<string>();
        foreach (var slot in Main.Settings.AdditionalSlots)
        {
            if (string.IsNullOrEmpty(slot.LocoId)) continue;
            ids.Add(slot.LocoId);
            var tender = Main.Settings.GetTenderId(slot.LocoId);
            if (!string.IsNullOrEmpty(tender)) ids.Add(tender!);
        }
        return ids;
    }

    // Whether `candidate` can back a brand new demonstrator slot: it has to satisfy the same rules as a
    // demonstrator replacement and not already be spawned anywhere in the pool.
    internal static bool CanBeAdditionalSlot(TrainCarLivery candidate) =>
        IsValidDemonstrator(candidate) && !AllSpawnedIds().Contains(candidate.id);


    // Liveries currently configured as a garage's extra consist cars.
    private static HashSet<string> ExtraCarIds()
    {
        var ids = new HashSet<string>();
        foreach (var (garage, isDemonstrator, _) in VanillaGarages.Groups)
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
        foreach (var (garage, isDemonstrator, liveries) in VanillaGarages.Groups)
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
        foreach (var (garage, isDemonstrator, liveries) in VanillaGarages.Groups)
        {
            if (!isDemonstrator) continue;
            var primary = liveries.FirstOrDefault();
            if (primary == null) continue;
            var second = ResolveTender(primary.id, VanillaGarages.OriginalTender(garage));
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
        foreach (var (garage, isDemonstrator, liveries) in VanillaGarages.Groups)
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
    internal static void PruneInvalidCargoOverrides()
    {
        foreach (var (_, isDemonstrator, liveries) in VanillaGarages.Groups)
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
        if (string.IsNullOrEmpty(newSpawnId))
        {
            if (GarageIdForSlot(slot) is string garageId)
                Main.Settings.ClearExtraCars(garageId);
            Main.Settings.ClearDemonstratorOverride(slot.id);
        }

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

        InferTendersForSwap(slot, livery);

        RestorationPartsCustomizer.RevertSlotCargo(slot.id);
        if (livery != null)
        {
            RestorationPartsCustomizer.RevertSlotCargo(livery.id);
        }

        PruneInvalidCargoOverrides();
    }

    private static void SetSpawn(TrainCarLivery slot, string spawnId) => SetSpawn(slot.id, spawnId);

    internal static void SetSpawn(string slotId, string spawnId)
    {
        if (spawnId == slotId)
            Main.Settings.LiveryReplacements.Remove(slotId);
        else
            Main.Settings.LiveryReplacements[slotId] = spawnId;
    }
}
