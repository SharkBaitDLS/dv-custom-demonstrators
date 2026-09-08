using CCL.Importer;
using CCL.Importer.Types;
using DV.ThingTypes;

namespace CustomDemonstrators.Slots;

internal static class CustomCarLoaderHelper
{
    internal static bool IsCustomCar(TrainCarLivery livery) =>
        CarTypeInjector.IdToLiveryMap.ContainsKey(livery.id);

    internal static TrainCarLivery[] TrainsetFor(TrainCarLivery livery) =>
        CarManager.GetTrainsetForLivery(livery);

    internal static float? SummonPriceFor(TrainCarLivery? livery) =>
        livery is CCL_CarVariant variant ? variant.SummonPrice : null;
}
