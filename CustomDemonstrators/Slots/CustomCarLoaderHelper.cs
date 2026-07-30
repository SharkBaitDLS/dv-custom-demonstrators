using CCL.Importer;
using CCL.Importer.Types;
using DV.ThingTypes;

namespace CustomDemonstrators.Slots;

internal static class CustomCarLoaderHelper
{
    internal static bool IsCustomCar(TrainCarLivery livery) =>
        CarTypeInjector.IdToLiveryMap.ContainsKey(livery.id);

    internal static float? PartsOrderCostFor(TrainCarLivery livery) =>
        livery is CCL_CarVariant { DemonstratorPartsOrderCost: > 0f } v ? v.DemonstratorPartsOrderCost : null;

    internal static float? PartsInstallCostFor(TrainCarLivery livery) =>
        livery is CCL_CarVariant { DemonstratorPartsInstallationCost: > 0f } v ? v.DemonstratorPartsInstallationCost : null;
}
