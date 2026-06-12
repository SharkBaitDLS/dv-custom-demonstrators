using System.Collections.Generic;
using System.Linq;
using DV;
using DV.ThingTypes;

namespace CustomDemonstrators;

// Enumerates the rolling stock spawned by the game's garages, grouped per garage and split into
// demonstrators vs. ordinary garage/shed stock.
internal static class GarageVehicles
{
    // Demonstrator spawns are modelled as "garages"
    internal static readonly HashSet<Garage> DemonstratorGarages =
    [
        Garage.DE2_Relic, Garage.DE6_Relic, Garage.DH4_Relic,
        Garage.DM3_Relic, Garage.S282_Relic, Garage.S060_Relic,
    ];

    internal static bool IsDemonstrator(Garage garage) => DemonstratorGarages.Contains(garage);

    private static List<(GarageType_v2 garage, bool isDemonstrator, List<TrainCarLivery> liveries)>? _groups;

    // Rebuild while empty so we don't latch an empty list if asked before the type registry loads.
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

        foreach (var garage in garages)
        {
            if (garage == null || garage.v1 == Garage.NotSet || garage.garageCarLiveries == null) continue;
            var liveries = garage.garageCarLiveries.Where(l => l != null).ToList();
            if (liveries.Count == 0) continue;
            result.Add((garage, IsDemonstrator(garage.v1), liveries));
        }

        // Demonstrators first, then ordinary garages
        return [.. result.OrderBy(g => g.Item2 ? 0 : 1).ThenBy(g => (int)g.Item1.v1)];
    }
}
