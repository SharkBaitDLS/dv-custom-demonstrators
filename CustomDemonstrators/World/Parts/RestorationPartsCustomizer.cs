using System.Collections.Generic;
using DV.LocoRestoration;
using DV.ThingTypes;
using CustomDemonstrators.Saves;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World.Parts;

internal static class RestorationPartsCustomizer
{
    // Settings sentinel meaning "force the generic crate" even if the CCL mod provides a different prefab
    internal const string GenericCrateSentinel = "__cd_generic_crate__";

    internal static void ApplyCargo(LocoRestorationController controller, string slotId, TrainCarLivery? replacementLoco)
    {
        PartsCargoModels.CaptureStockModels();

        CargoPrefabs.Sanitize(controller.locoPartCargo);

        var source = ChooseCargo(controller, slotId, replacementLoco);

        CargoPrefabs.Sanitize(controller.locoPartCargo);

        Main.Logger.Log($"Restoration '{slotId}' is using parts cargo "
            + $"'{(controller.locoPartCargo != null ? controller.locoPartCargo.id : "<none>")}' ({source}).");

        // Whatever the choice turned out to be, the warehouse has to be willing to load it.
        PartsWarehouse.EnsureSupported(controller);
    }

    // Returns how the cargo was chosen, for the log line in ApplyCargo.
    private static string ChooseCargo(LocoRestorationController controller, string slotId, TrainCarLivery? replacementLoco)
    {
        // Snapshots exist to put a shared game cargo back the way we found it. A slot added by this mod
        // only ever rewrites a copy it owns, and gets a fresh one each load, so it has nothing to restore.
        if (!SlotTypes.IsSlotGarage(GameTypes.GarageOf(controller)))
            PartsCargoSnapshots.Ensure(slotId, controller.locoPartCargo);

        var choice = CargoChoice(slotId);

        if (!string.IsNullOrEmpty(choice) && choice != GenericCrateSentinel)
        {
            var picked = GameTypes.Cargo(choice);
            if (picked != null && SlotChoices.CanBeRestorationParts(picked))
            {
                controller.locoPartCargo = picked; // use the chosen cargo (and its model) as-is
                SyncRegisterNames(controller);
                return "explicit override";
            }

            // This should generally never happen since the settings GUI enforces correctness of the
            // overrides, but if the settings file is hand-edited or a mod author updates their CCL
            // car in a way that invalidates a previously valid configuration we should not brick the
            // entire quest chain. Log so user bug reports can be pointed to the actual root cause.
            Main.Logger.Warning($"Restoration parts cargo override '{choice}' for {slotId} is invalid "
                + "(cargo missing or not loadable on the parts flatcar); falling back to the car's own "
                + "parts, or the generic crate where its mod doesn't specify any.");
        }

        // Reverting back to vanilla quest state
        if (replacementLoco == null)
        {
            RevertCargo(controller, slotId);
            return "vanilla (no replacement loco)";
        }
        if (controller.locoPartCargo == null) return "none (slot has no parts cargo)";

        // Forcing the generic crate is the player saying they want the plain reskin, so it also opts out
        // of whatever the CCL author specified for the parts.
        bool useAuthored = choice != GenericCrateSentinel;

        bool authored = useAuthored && HasAuthoredParts(replacementLoco);

        // Past this point the cargo gets rewritten in place, so the slot has to be holding a cargo that is
        // ours to rewrite rather than whatever an explicit override last pointed it at.
        var original = PartsCargoSnapshots.Original(slotId);
        if (original != null)
            controller.locoPartCargo = original;

        // An added slot has no vanilla cargo to put back, and owns a copy made for exactly this.
        if (DemonstratorSlots.OwnCargoFor(controller) is CargoType_v2 own)
            controller.locoPartCargo = own;

        Customize(controller.locoPartCargo, replacementLoco, useAuthored);
        PartsCargoSnapshots.MarkCustomized(slotId);
        SyncRegisterNames(controller);
        return authored ? "authored by the car's mod"
            : choice == GenericCrateSentinel ? "generic crate (forced)"
            : "generic crate";
    }

    private static void Customize(CargoType_v2 partsCargo, TrainCarLivery loco, bool useAuthored)
    {
        PartsCargoNames.Apply(partsCargo, loco, useAuthored);
        PartsCargoModels.Apply(partsCargo, loco, useAuthored);

        if (CustomCarLoaderHelper.PartsMassFor(loco) is float mass && mass > 0f)
            partsCargo.massPerUnit = mass;
    }

    private static readonly SavedMap _bakedCargo =
        new("CustomDemonstrators_CargoSlots", "CustomDemonstrators_CargoChoices");

    internal static string? BakedCargoChoice(string slotId) => _bakedCargo.Get(slotId);

    internal static void MergeCargoChoicesFrom(SaveGameData other, ICollection<string> slotIds) =>
        _bakedCargo.MergeFrom(other, slotIds);

    private static string? CargoChoice(string slotId)
    {
        if (!SaveGuard.AllowDemonstratorChanges()) return _bakedCargo.Get(slotId);

        var choice = Main.Settings.GetPartsCargoId(slotId);
        _bakedCargo.Set(slotId, string.IsNullOrEmpty(choice) ? null : choice);
        return choice;
    }

    private static void SyncRegisterNames(LocoRestorationController controller)
    {
        var key = controller.locoPartCargo != null ? controller.locoPartCargo.localizationKeyFull : null;
        if (string.IsNullOrEmpty(key)) return;
        if (controller.orderPartsModule != null) controller.orderPartsModule.localizationKey = key!;
        if (controller.installPartsModule != null) controller.installPartsModule.localizationKey = key!;
    }

    private static void RevertCargo(LocoRestorationController controller, string slotId)
    {
        var original = PartsCargoSnapshots.Original(slotId);
        if (original == null) return;
        if (controller.locoPartCargo == original && !PartsCargoSnapshots.IsCustomized(slotId)) return;

        PartsCargoSnapshots.Revert(slotId);
        controller.locoPartCargo = original;
        SyncRegisterNames(controller);
    }

    // Only the save-derived choices. The snapshots and stock crates live for the whole run, see
    // PartsCargoSnapshots and PartsCargoModels.
    internal static void Reset()
    {
        _bakedCargo.Reset();
    }

    // Whether the CCL author configured anything about this loco's replacement parts,
    // used purely in the GUI to indicate to the users whether the default setting
    // comes from our mod or theirs.
    internal static bool HasAuthoredParts(TrainCarLivery? loco) =>
        PartsCargoNames.HasAuthored(loco) || PartsCargoModels.HasAuthored(loco);
}
