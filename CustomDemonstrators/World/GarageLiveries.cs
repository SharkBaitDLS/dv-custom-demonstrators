using System.Collections.Generic;
using System.Linq;
using DV;
using DV.ThingTypes;
using CustomDemonstrators.Saves;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

internal static class GarageLiveries
{
    // The garages whose liveries this call changed, for a caller that knows spawners are already standing.
    internal static List<GarageType_v2> Apply()
    {
        var changed = new List<GarageType_v2>();

        var types = Globals.G?.Types;
        if (types?.garages == null) return changed;

        // Nothing may reach the world until the save guard can actually answer
        if (SaveState.Data() == null) return changed;

        VanillaGarages.EnsureSnapshot(); // capture pristine originals before the first mutation

        var spokenFor = DemonstratorSlots.ClaimedLiveryIds();

        foreach (var garage in types.garages)
        {
            if (garage == null) continue;
            if (SlotTypes.IsAdded(garage)) continue;
            var desired = DesiredLiveries(garage, spokenFor);
            if (garage.garageCarLiveries == null || !garage.garageCarLiveries.SequenceEqual(desired))
            {
                garage.garageCarLiveries = desired;
                changed.Add(garage);
            }

            ApplySummonPrice(garage, desired.FirstOrDefault(), VanillaGarages.OriginalSummonPrice(garage));
        }

        if (changed.Count > 0) types.RecalculateCaches();
        return changed;
    }

    // Clean out the spawner registry so we don't corrupt state when loading a different save
    internal static void DropDeadRegistrations()
    {
        var dead = GarageCarSpawner.Spawners.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList();
        foreach (var livery in dead) GarageCarSpawner.Spawners.Remove(livery);

        if (dead.Count > 0)
        {
            Main.Logger.Log($"Cleared {dead.Count} summon registration(s) the previous world left behind: "
                + string.Join(", ", dead.Select(l => l.id)));
        }
    }

    internal static void RepointSpawners(IEnumerable<GarageType_v2> garages)
    {
        foreach (var garage in garages)
        {
            var spawner = GarageCarSpawner.Spawners.Values.FirstOrDefault(s => s != null && s.garageType == garage);
            if (spawner == null) continue;

            foreach (var stale in GarageCarSpawner.Spawners.Where(kv => kv.Value == spawner).Select(kv => kv.Key).ToList())
            {
                GarageCarSpawner.Spawners.Remove(stale);
            }

            var liveries = garage.garageCarLiveries ?? [];
            foreach (var livery in liveries)
            {
                if (livery != null) GarageCarSpawner.Spawners[livery] = spawner;
            }

            if (spawner.garageCars == null || spawner.garageCars.All(c => c == null))
            {
                spawner.garageCars = new TrainCar[liveries.Length];
            }

            Main.Logger.Log($"Garage {garage.id} settled on {liveries.Length} car(s) after its spawner had "
                + "already registered; the summon registry was pointed at the new consist.");
        }
    }

    // This is the only setting we live apply and keep out of the fingerprint
    internal static void ApplySummonPrice(string garageId, TrainCarLivery? primary, float fallback)
    {
        var garage = Globals.G?.Types?.garages?.FirstOrDefault(g => g != null && g.id == garageId);
        if (garage != null) ApplySummonPrice(garage, primary, fallback);
    }

    internal static void ApplySummonPrice(GarageType_v2 garage, float fallback) =>
        ApplySummonPrice(garage, garage.garageCarLiveries?.FirstOrDefault(), fallback);

    internal static void ApplySummonPrice(GarageType_v2 garage, TrainCarLivery? primary, float fallback) =>
        garage.summonPrice = Main.Settings.GetSummonPrice(garage.id) ?? DefaultSummonPrice(primary, fallback);

    private static TrainCarLivery? Unclaimed(TrainCarLivery? livery, HashSet<string> spokenFor) =>
        livery != null && spokenFor.Contains(livery.id) ? null : livery;

    internal static float DefaultSummonPrice(TrainCarLivery? primary, float fallback) =>
        CustomCarLoaderHelper.SummonPriceFor(primary) ?? fallback;

    internal static TrainCarLivery? PrimaryFor(GarageType_v2 garage) =>
        DesiredLiveries(garage, DemonstratorSlots.ClaimedLiveryIds()).FirstOrDefault();

    // The liveries a garage should spawn after overrides. A normal garage just does a simple replace.
    // A demonstrator garage is rebuilt from its primary loco plus its resolved tender if any.
    private static TrainCarLivery[] DesiredLiveries(GarageType_v2 garage, HashSet<string> spokenFor)
    {
        if (!VanillaGarages.IsDemonstrator(garage.v1))
        {
            if (SaveGuard.IsGarageBlocking && GarageOwnership.IsUnlockedAndOwned(garage))
            {
                return VanillaGarages.OriginalLiveries(garage);
            }

            var replaced = VanillaGarages.OriginalLiveries(garage)
                .Select(l => Unclaimed(Main.Settings.GetReplacement(l), spokenFor) ?? l);
            var extras = Main.Settings.GetExtraCars(garage.id)
                .Select(id => Unclaimed(DemonstratorSetup.GetLivery(id), spokenFor));
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
