using CCL.Importer;
using DV.ThingTypes;

namespace CustomDemonstrators;

internal static class CustomCarLoaderHelper
{
    internal static bool IsCustomCar(TrainCarLivery livery) =>
        CarTypeInjector.IdToLiveryMap.ContainsKey(livery.id);
}
