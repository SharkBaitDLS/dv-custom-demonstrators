using System.Collections.Generic;
using System.Linq;
using DV;
using DV.ThingTypes;
using CustomDemonstrators.World;

namespace CustomDemonstrators.Slots;

// Enumerates the rolling stock spawned by the game's garages, grouped per garage and split into
// demonstrators vs. ordinary garage stock.
internal static class VanillaGarages
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

    // Pristine copy of each garage's liveries, captured before GarageLiveries rewrites the
    // live game data.
    private static readonly Dictionary<GarageType_v2, TrainCarLivery[]> _originals = [];

    // The summon price each garage shipped with, so clearing an override puts the game's own back.
    private static readonly Dictionary<GarageType_v2, float> _originalPrices = [];

    private static List<(GarageType_v2 garage, bool isDemonstrator, List<TrainCarLivery> liveries)>? _groups;

    internal static void EnsureSnapshot()
    {
        var garages = GameTypes.Garages;
        if (garages == null) return;
        foreach (var garage in garages)
        {
            if (garage == null || garage.garageCarLiveries == null || _originals.ContainsKey(garage)) continue;
            _originals[garage] = (TrainCarLivery[])garage.garageCarLiveries.Clone();
            _originalPrices[garage] = garage.summonPrice;
        }
    }

    internal static TrainCarLivery[] OriginalLiveries(GarageType_v2 garage)
    {
        EnsureSnapshot();
        return _originals.TryGetValue(garage, out var o) ? o : garage.garageCarLiveries ?? [];
    }

    internal static float OriginalSummonPrice(GarageType_v2 garage)
    {
        EnsureSnapshot();
        return _originalPrices.TryGetValue(garage, out var price) ? price : garage.summonPrice;
    }

    // What a garage of this mod's own charges before any override: whatever the game's first garage of the
    // same kind charges. Read from the game's data rather than from whichever garage or controller happened
    // to serve as the build template, so the menu and the built garage can't disagree.
    internal static float DefaultSummonPrice() => FirstSummonPrice(demonstrator: false);

    internal static float DefaultDemonstratorSummonPrice() => FirstSummonPrice(demonstrator: true);

    private static float FirstSummonPrice(bool demonstrator)
    {
        foreach (var (garage, isDemonstrator, _) in Groups)
        {
            if (isDemonstrator == demonstrator) return OriginalSummonPrice(garage);
        }
        return 0f;
    }

    internal static TrainCarLivery? PrimaryLoco(GarageType_v2 garage)
    {
        var liveries = OriginalLiveries(garage);
        var loco = liveries.FirstOrDefault(l => l != null && CarTypes.IsLocomotive(l));
        return loco != null ? loco : liveries.FirstOrDefault(l => l != null);
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
        var garages = GameTypes.Garages;
        if (garages == null) return result;
        EnsureSnapshot();

        foreach (var garage in garages)
        {
            if (garage == null || garage.v1 == Garage.NotSet) continue;
            // Garages this mod creates are configured from their own settings list, not as rows built from
            // game data, so they never take part in the replacement grouping.
            if (SlotTypes.IsAdded(garage)) continue;
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
