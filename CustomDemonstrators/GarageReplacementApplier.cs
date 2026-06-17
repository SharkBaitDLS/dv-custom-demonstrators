using System.Collections.Generic;
using System.Linq;
using DV;
using DV.LocoRestoration;
using DV.ThingTypes;
using UnityEngine;

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

    private static TrainCarLivery? GetLivery(string id) =>
        Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == id);

    private static GameObject? OriginalBlocker(TrainCarLivery? livery) =>
        livery?.prefab?.GetComponentInChildren<LocoZoneBlocker>(includeInactive: true)?.gameObject;

    // The liveries a garage should spawn after overrides. A normal garage just does a simple replace.
    // A demonstrator garage is rebuilt from its primary loco plus its resolved tender if any.
    private static TrainCarLivery[] DesiredLiveries(GarageType_v2 garage)
    {
        if (!GarageVehicles.IsDemonstrator(garage.v1))
        {
            var replaced = GarageVehicles.OriginalLiveries(garage).Select(l => Main.Settings.GetReplacement(l) ?? l);
            var extras = Main.Settings.GetExtraCars(garage.id).Select(GetLivery);
            return [.. replaced.Concat(extras).Where(l => l != null)!];
        }

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

        if (replacementLoco != null && controller.locoBlockerPrefab == null)
            controller.locoBlockerPrefab = OriginalBlocker(loco);

        RestorationPartsCustomizer.ApplyCargo(controller, slotId, replacementLoco);

        var tenderId = GarageReplacements.ResolveTender(slotId, tender);
        controller.secondCarLivery = tenderId;

        if (tenderId != null)
        {
            // To get the tender to display the demonstrator message, it has to inherit the license of the
            // locomotive. Most CCL mod authors don't license the tender, just the loco. Patch that for them.
            var effectiveLoco = controller.locoLivery;
            if (tenderId.requiredLicense == null && effectiveLoco?.requiredLicense != null)
                tenderId.requiredLicense = effectiveLoco.requiredLicense;

            // Ensure a blocker prefab exists at all (the tender's own, else the loco's).
            if (controller.secondCarBlockerPrefab == null)
                controller.secondCarBlockerPrefab = OriginalBlocker(tender) ?? OriginalBlocker(loco) ?? controller.locoBlockerPrefab;
        }

        // Price overrides. < 0 / unset = default.
        var orderPrice = Main.Settings.GetOrderPrice(slotId);
        if (orderPrice.HasValue && controller.orderPartsModule != null)
            controller.orderPartsModule.price = orderPrice.Value;

        var installPrice = Main.Settings.GetInstallPrice(slotId);
        if (installPrice.HasValue && controller.installPartsModule != null)
            controller.installPartsModule.price = installPrice.Value;
    }
}
