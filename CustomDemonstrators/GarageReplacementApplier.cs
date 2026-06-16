using System.Collections.Generic;
using System.Linq;
using DV;
using DV.LocoRestoration;
using DV.ThingTypes;

namespace CustomDemonstrators;

// Bakes the configured replacements into the live game data when a world loads / a save is created.
// Originals are snapshotted by GarageVehicles so we always recompute from a clean baseline.
internal static class GarageReplacementApplier
{
    // Idempotently rewrites every garage's liveries to its configured replacements.
    internal static void Apply()
    {
        var types = Globals.G?.Types;
        if (types?.garages == null) return;

        GarageVehicles.EnsureSnapshot(); // capture pristine originals before the first mutation

        bool changed = false;
        foreach (var garage in types.garages)
        {
            if (garage == null) continue;
            var desired = DesiredLiveries(garage);
            if (garage.garageCarLiveries == null || !garage.garageCarLiveries.SequenceEqual(desired))
            {
                garage.garageCarLiveries = desired;
                changed = true;
            }
        }

        if (changed) types.RecalculateCaches();
    }

    // The liveries a garage should spawn after overrides. A normal garage just does a simple replace.
    // A demonstrator garage is rebuilt from its primary loco plus its resolved tender if any.
    private static TrainCarLivery[] DesiredLiveries(GarageType_v2 garage)
    {
        if (!GarageVehicles.IsDemonstrator(garage.v1))
            return [.. GarageVehicles.OriginalLiveries(garage).Select(l => Main.Settings.GetReplacement(l) ?? l)];

        var primary = GarageVehicles.PrimaryLoco(garage);
        if (primary == null) return GarageVehicles.OriginalLiveries(garage);

        var desired = new List<TrainCarLivery> { Main.Settings.GetReplacement(primary) ?? primary };
        var tender = GarageReplacements.ResolveTender(primary.id, GarageVehicles.OriginalTender(garage));
        if (tender != null) desired.Add(tender);
        return [.. desired];
    }

    // Repoints a demonstrator's restoration controller at its replacement liveries, so the museum
    // restoration spawns and services the same car the garage now spawns, and applies the slot's
    // parts-cargo and price overrides.
    internal static void ApplyTo(LocoRestorationController controller)
    {
        var loco = controller.locoLivery;
        var tender = controller.secondCarLivery;

        // The slot identity is the ORIGINAL demonstrator livery id so that we have a consistent
        // primary key for each slot in the roundhouse.
        string slotId = loco?.id ?? "";

        var replacementLoco = loco != null ? Main.Settings.GetReplacement(loco) : null;
        if (replacementLoco != null)
            controller.locoLivery = replacementLoco;

        RestorationPartsCustomizer.ApplyCargo(controller, slotId, replacementLoco);

        var tenderId = GarageReplacements.ResolveTender(slotId, tender);
        controller.secondCarLivery = tenderId;

        // The prefabs for the loco blockers might not quite line up with the wrecks since
        // they're built to the original demonstrator shapes but that's a small bit of jank
        // that only matters until the player clears the licenses to be able to rerail the
        // wreck. I'm not sure it's worth figuring out how to dynamically generate a blocker
        // that precisely masks the given selected locomotive and/or tender.
        if (tenderId != null && controller.secondCarBlockerPrefab == null)
            controller.secondCarBlockerPrefab = controller.locoBlockerPrefab;

        // Price overrides. < 0 / unset = default.
        var orderPrice = Main.Settings.GetOrderPrice(slotId);
        if (orderPrice.HasValue && controller.orderPartsModule != null)
            controller.orderPartsModule.price = orderPrice.Value;

        var installPrice = Main.Settings.GetInstallPrice(slotId);
        if (installPrice.HasValue && controller.installPartsModule != null)
            controller.installPartsModule.price = installPrice.Value;
    }
}
