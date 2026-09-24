using System.Collections.Generic;
using System.Linq;
using DV;
using DV.LocoRestoration;
using DV.ThingTypes;

namespace CustomDemonstrators.World;

// Unity-safe lookups for things read all over the mod. Globals, its object model and the types in it are Unity
// objects, and ?. on those skips Unity's check for one that has been destroyed.
internal static class GameTypes
{
    internal static DVObjectModel? Current
    {
        get
        {
            var globals = Globals.G;
            return globals != null && globals.Types != null ? globals.Types : null;
        }
    }

    internal static List<GarageType_v2>? Garages
    {
        get
        {
            var types = Current;
            return types != null ? types.garages : null;
        }
    }

    internal static List<CargoType_v2>? Cargos
    {
        get
        {
            var types = Current;
            return types != null ? types.cargos : null;
        }
    }

    internal static TrainCarLivery? Livery(string? id)
    {
        var types = Current;
        return id != null && types != null ? types.Liveries.FirstOrDefault(l => l.id == id) : null;
    }

    internal static GarageType_v2? GarageOf(LocoRestorationController controller) =>
        controller.garageSpawner != null ? controller.garageSpawner.garageType : null;

    internal static string? Id(TrainCarLivery? livery) => livery != null ? livery.id : null;
}
