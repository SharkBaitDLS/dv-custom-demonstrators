using CCL.Importer;
using DV.ThingTypes;

namespace CustomDemonstrators.Slots;

internal static class CustomCarLoaderHelper
{
    internal static bool IsCustomCar(TrainCarLivery livery) =>
        CarTypeInjector.IdToLiveryMap.ContainsKey(livery.id);

    internal static TrainCarLivery[] TrainsetFor(TrainCarLivery livery) =>
        CarManager.GetTrainsetForLivery(livery);
}
