using System.Collections.Generic;
using System.Linq;
using DV;
using DV.ThingTypes;
using CustomDemonstrators.World;

namespace CustomDemonstrators.Slots;

// Enumerates the rolling stock spawned by the game's garages, grouped per garage and split into
// demonstrators vs. ordinary garage stock.
internal static class GarageVehicles
{
    // Demonstrator spawns are modelled as "garages"
    internal static readonly HashSet<Garage> DemonstratorGarages =
    [
        Garage.DE2_Relic, Garage.DE6_Relic, Garage.DH4_Relic,
        Garage.DM3_Relic, Garage.S282_Relic, Garage.S060_Relic,
    ];

    internal static bool IsDemonstrator(Garage garage) => DemonstratorGarages.Contains(garage);

    // The liveries backing the game's own demonstrator slots, used to tell them apart from the slots this
    // mod adds when reading a save's baked configuration back.
    internal static HashSet<string> VanillaDemonstratorIds()
    {
        var ids = new HashSet<string>();
        foreach (var (_, isDemonstrator, liveries) in Groups)
        {
            if (!isDemonstrator) continue;
            if (liveries.FirstOrDefault() is TrainCarLivery primary) ids.Add(primary.id);
        }
        return ids;
    }

    // Pristine copy of each garage's liveries, captured before GarageReplacementApplier rewrites the
    // live game data.
    private static readonly Dictionary<GarageType_v2, TrainCarLivery[]> _originals = [];

    private static List<(GarageType_v2 garage, bool isDemonstrator, List<TrainCarLivery> liveries)>? _groups;

    internal static void EnsureSnapshot()
    {
        var garages = Globals.G?.Types?.garages;
        if (garages == null) return;
        foreach (var garage in garages)
        {
            if (garage?.garageCarLiveries == null || _originals.ContainsKey(garage)) continue;
            _originals[garage] = (TrainCarLivery[])garage.garageCarLiveries.Clone();
        }
    }

    internal static TrainCarLivery[] OriginalLiveries(GarageType_v2 garage)
    {
        EnsureSnapshot();
        return _originals.TryGetValue(garage, out var o) ? o : garage.garageCarLiveries ?? [];
    }

    internal static TrainCarLivery? PrimaryLoco(GarageType_v2 garage)
    {
        var liveries = OriginalLiveries(garage);
        return liveries.FirstOrDefault(l => l != null && CarTypes.IsLocomotive(l))
            ?? liveries.FirstOrDefault(l => l != null);
    }

    internal static TrainCarLivery? OriginalTender(GarageType_v2 garage) =>
        OriginalLiveries(garage).FirstOrDefault(l => l != null && CarTypes.IsTender(l));

    internal static IReadOnlyList<(GarageType_v2 garage, bool isDemonstrator, List<TrainCarLivery> liveries)> Groups
    {
        get
        {
            if (_groups == null || _groups.Count == 0) _groups = Build();
            return _groups;
        }
    }

    private static List<(GarageType_v2, bool, List<TrainCarLivery>)> Build()
    {
        var result = new List<(GarageType_v2, bool, List<TrainCarLivery>)>();
        var garages = Globals.G?.Types?.garages;
        if (garages == null) return result;
        EnsureSnapshot();

        foreach (var garage in garages)
        {
            if (garage == null || garage.v1 == Garage.NotSet) continue;
            // Slots this mod creates are configured from their own settings list, not as rows built from
            // game data, so they never take part in the replacement grouping.
            if (DemonstratorSlotFactory.IsSlotGarage(garage)) continue;
            bool demonstrator = IsDemonstrator(garage.v1);

            List<TrainCarLivery> liveries;
            if (demonstrator)
            {
                var primary = PrimaryLoco(garage);
                if (primary == null) continue;
                liveries = [primary];
            }
            else
            {
                liveries = OriginalLiveries(garage).Where(l => l != null).ToList();
                if (liveries.Count == 0) continue;
            }
            result.Add((garage, demonstrator, liveries));
        }

        // Demonstrators first, then actual garages
        return [.. result.OrderBy(g => g.Item2 ? 0 : 1).ThenBy(g => (int)g.Item1.v1)];
    }
}
