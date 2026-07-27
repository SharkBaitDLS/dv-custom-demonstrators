using System.Collections.Generic;
using System.Linq;
using DV;
using DV.ThingTypes;
using CustomDemonstrators.Saves;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

internal static class GarageLiveries
{
    internal static void Apply()
    {
        var types = Globals.G?.Types;
        if (types?.garages == null) return;

        VanillaGarages.EnsureSnapshot(); // capture pristine originals before the first mutation

        bool changed = false;
        foreach (var garage in types.garages)
        {
            if (garage == null) continue;
            // Custom demonstrator garages are already set up correctly
            if (SlotTypes.IsSlotGarage(garage)) continue;
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
        if (!VanillaGarages.IsDemonstrator(garage.v1))
        {
            if (SaveGuard.IsGarageBlocking && GarageOwnership.IsUnlockedAndOwned(garage))
            {
                return VanillaGarages.OriginalLiveries(garage);
            }

            var replaced = VanillaGarages.OriginalLiveries(garage).Select(l => Main.Settings.GetReplacement(l) ?? l);
            var extras = Main.Settings.GetExtraCars(garage.id).Select(DemonstratorSetup.GetLivery);
            return [.. replaced.Concat(extras).Where(l => l != null)!];
        }

        var primary = VanillaGarages.PrimaryLoco(garage);
        if (primary == null) return VanillaGarages.OriginalLiveries(garage);

        if (!DemonstratorSetup.Resolve(primary, VanillaGarages.OriginalTender(garage), primary.id,
                out var replacementLoco, out var tenderLivery))
        {
            return VanillaGarages.OriginalLiveries(garage);
        }

        var desired = new List<TrainCarLivery> { replacementLoco ?? primary };
        if (tenderLivery != null) desired.Add(tenderLivery);
        return [.. desired];
    }
}
