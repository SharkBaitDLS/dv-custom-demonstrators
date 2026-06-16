using System.Collections.Generic;
using System.Linq;
using DV;
using DV.ThingTypes;

namespace CustomDemonstrators;

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
