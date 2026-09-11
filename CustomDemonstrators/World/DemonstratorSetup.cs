using System.Collections.Generic;
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

    // A license lent to a tender outlives the world it was lent in, noted here so it can be removed when
    // reloading a save/switching sessions
    private static readonly HashSet<TrainCarLivery> _lentLicenses = [];

    private static void LendLicense(TrainCarLivery tender, GeneralLicenseType_v2? license)
    {
        if (license == null || tender.requiredLicense != null) return;
        tender.requiredLicense = license;
        _lentLicenses.Add(tender);
    }

    internal static void ReturnLentLicenses()
    {
        foreach (var tender in _lentLicenses)
        {
            tender?.requiredLicense = null;
        }
        _lentLicenses.Clear();
    }

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

    // What each slot's quest modules charged before we touched them, for the settings GUI to show as the
    // price a blank field will actually charge. Only a slot still on its vanilla loco ever needs this: any
    // CCL car answers from its own metadata, whose price fields always carry a value. The game's six don't
    // all charge the same, so there's no constant to use instead. Captured on the first pass over a slot,
    // since every later one may be reading back a price we ourselves wrote.
    private static readonly Dictionary<string, (float Order, float Install)> _originalPartsPrices = [];

    internal static (float Order, float Install)? OriginalPartsPrices(string slotId) =>
        _originalPartsPrices.TryGetValue(slotId, out var prices) ? prices : null;

    private static void SnapshotPartsPrices(LocoRestorationController controller, string slotId)
    {
        if (string.IsNullOrEmpty(slotId) || _originalPartsPrices.ContainsKey(slotId)) return;
        if (controller.orderPartsModule == null || controller.installPartsModule == null) return;

        _originalPartsPrices[slotId] = (controller.orderPartsModule.price, controller.installPartsModule.price);
    }

    internal static void ApplyTo(LocoRestorationController controller)
    {
        var loco = OriginalLoco(controller);
        var tender = OriginalTender(controller);

        string slotId = loco?.id ?? "";

        // Before the early return, so a slot the mod leaves alone still reports what it charges.
        SnapshotPartsPrices(controller, slotId);

        // Leave a save the mod never touched at its vanilla original
        if (!Resolve(loco, tender, slotId, out var replacementLoco, out var tenderId)) return;

        if (loco != null)
            controller.locoLivery = replacementLoco ?? loco; // revert to vanilla when the override is cleared

        // Update the quest board and poster image
        if (loco != null)
        {
            var posterId = SlotTypes.IsSlotGarage(controller.garageSpawner?.garageType)
                ? controller.locoLivery?.id
                : replacementLoco?.id;
            SlotBoard.ApplyVisuals(controller.GetComponent<LocoRestorationView>(), controller.locoLivery, posterId);
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
            LendLicense(tenderId, controller.locoLivery?.requiredLicense);

            controller.secondCarBlockerPrefab = ZoneBlockers.First(
                ZoneBlockers.PrefabFor(tenderId),
                ZoneBlockers.PrefabFor(tender),
                controller.secondCarBlockerPrefab,
                controller.locoBlockerPrefab);
        }

        // What the quest charges for the parts: the player's override first, then whatever the CCL author
        // priced this loco's parts at, and failing both the module keeps the vanilla demonstrator's price.
        // cargoLoco is the same "the CCL loco actually being restored, or null when this slot is vanilla"
        // the parts cargo was built from, so a reverted slot keeps the game's own prices.
        var orderPrice = Main.Settings.GetOrderPrice(slotId) ?? CustomCarLoaderHelper.PartsOrderPriceFor(cargoLoco);
        if (orderPrice.HasValue && controller.orderPartsModule != null)
            controller.orderPartsModule.price = orderPrice.Value;

        var installPrice = Main.Settings.GetInstallPrice(slotId)
            ?? CustomCarLoaderHelper.PartsInstallPriceFor(cargoLoco);
        if (installPrice.HasValue && controller.installPartsModule != null)
            controller.installPartsModule.price = installPrice.Value;
    }

    internal static bool SpawnMatchesSettings(LocoRestorationController controller)
    {
        var loco = OriginalLoco(controller);
        if (loco == null) return true;

        if (!Resolve(loco, OriginalTender(controller), loco.id, out var replacement, out var tender)) return true;

        return controller.locoLivery == (replacement ?? loco) && controller.secondCarLivery == tender;
    }

    internal static TrainCarLivery? OriginalLoco(LocoRestorationController controller) =>
        controller.garageSpawner?.garageType is GarageType_v2 g ? VanillaGarages.PrimaryLoco(g) : controller.locoLivery;

    internal static TrainCarLivery? OriginalTender(LocoRestorationController controller) =>
        controller.garageSpawner?.garageType is GarageType_v2 g ? VanillaGarages.OriginalTender(g) : null;
}
