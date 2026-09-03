using System.Linq;
using DV;
using DV.LocoRestoration;
using DV.ThingTypes;
using CustomDemonstrators.Saves;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

internal static class DemonstratorSetup
{
    internal static TrainCarLivery? GetLivery(string id) =>
        Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == id);

    internal static bool Resolve(
        TrainCarLivery? loco, TrainCarLivery? originalTender, string slotId,
        out TrainCarLivery? replacementLoco, out TrainCarLivery? tenderLivery)
    {
        if (SaveGuard.AllowDemonstratorChanges())
        {
            replacementLoco = loco != null ? Main.Settings.GetReplacement(loco) : null;
            tenderLivery = SlotChoices.ResolveTender(slotId, originalTender);
            return true;
        }

        if (loco != null && SaveConfig.Demonstrators is { } baked && baked.TryGetValue(loco.id, out var e))
        {
            replacementLoco = e.SpawnId == loco.id ? null : GetLivery(e.SpawnId);
            tenderLivery = e.TenderId != null ? GetLivery(e.TenderId) : null;
            return true;
        }

        replacementLoco = null;
        tenderLivery = null;
        return false;
    }

    internal static void ApplyTo(LocoRestorationController controller)
    {
        var loco = OriginalLoco(controller);
        var tender = OriginalTender(controller);

        string slotId = loco?.id ?? "";

        // Leave a save the mod never touched at its vanilla original
        if (!Resolve(loco, tender, slotId, out var replacementLoco, out var tenderId)) return;

        if (loco != null)
            controller.locoLivery = replacementLoco ?? loco; // revert to vanilla when the override is cleared

        // Update the quest board and poster image
        if (loco != null)
        {
            var panel = controller.GetComponent<LocoRestorationView>();
            SlotBoard.Rename(panel, controller.locoLivery);
            SlotBoard.FitName(panel);

            if (replacementLoco != null)
            {
                SlotPoster.Apply(controller.gameObject, replacementLoco.id);
            }
            else
            {
                SlotPoster.Restore(controller.gameObject);
            }
        }

        controller.locoBlockerPrefab = ZoneBlockers.First(
            ZoneBlockers.PrefabFor(controller.locoLivery),
            ZoneBlockers.PrefabFor(loco),
            controller.locoBlockerPrefab);

        // The controller only subscribes to Unblocked when a blocker exists on the spawned car or can be
        // instantiated from the prefab. With neither, a wreck reset to S0 can never advance on its own.
        if (controller.locoBlockerPrefab == null)
            Main.Logger.Warning(
                $"{controller.locoLivery?.id} has no loco zone blocker available, its restoration can't unblock itself.");

        // A slot this mod added has no vanilla loco it stands in for, so its own loco is what the parts
        // cargo has to be named and modelled after.
        var cargoLoco = replacementLoco
            ?? (SlotTypes.IsSlotGarage(controller.garageSpawner?.garageType) ? controller.locoLivery : null);
        RestorationPartsCustomizer.ApplyCargo(controller, slotId, cargoLoco);

        controller.secondCarLivery = tenderId;

        if (tenderId != null)
        {
            // To get the tender to display the demonstrator message, it has to inherit the license of the
            // locomotive. Most CCL mod authors don't license the tender, just the loco. Patch that for them.
            var effectiveLoco = controller.locoLivery;
            if (tenderId.requiredLicense == null && effectiveLoco?.requiredLicense != null)
                tenderId.requiredLicense = effectiveLoco.requiredLicense;

            controller.secondCarBlockerPrefab = ZoneBlockers.First(
                ZoneBlockers.PrefabFor(tenderId),
                ZoneBlockers.PrefabFor(tender),
                controller.secondCarBlockerPrefab,
                controller.locoBlockerPrefab);
        }

        // Price overrides. < 0 / unset = default.
        var orderPrice = Main.Settings.GetOrderPrice(slotId);
        if (orderPrice.HasValue && controller.orderPartsModule != null)
            controller.orderPartsModule.price = orderPrice.Value;

        var installPrice = Main.Settings.GetInstallPrice(slotId);
        if (installPrice.HasValue && controller.installPartsModule != null)
            controller.installPartsModule.price = installPrice.Value;
    }

    internal static bool SpawnMatchesSettings(LocoRestorationController controller)
    {
        var loco = OriginalLoco(controller);
        if (loco == null) return true;
        var desiredLoco = Main.Settings.GetReplacement(loco) ?? loco;
        var desiredTender = SlotChoices.ResolveTender(loco.id, OriginalTender(controller));
        return controller.locoLivery == desiredLoco && controller.secondCarLivery == desiredTender;
    }

    internal static TrainCarLivery? OriginalLoco(LocoRestorationController controller) =>
        controller.garageSpawner?.garageType is GarageType_v2 g ? VanillaGarages.PrimaryLoco(g) : controller.locoLivery;

    internal static TrainCarLivery? OriginalTender(LocoRestorationController controller) =>
        controller.garageSpawner?.garageType is GarageType_v2 g ? VanillaGarages.OriginalTender(g) : null;
}
