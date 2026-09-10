using System.Linq;
using CCL.Importer;
using CCL.Importer.Types;
using CCL.Types;
using DV.ThingTypes;
using UnityEngine;

namespace CustomDemonstrators.Slots;

// The single place this mod reads Custom Car Loader's per-livery metadata. CCL exposes everything a
// demonstrator needs on the livery itself, so an author who fills those fields in gets the whole quest
// configured for them and the settings GUI only has to offer overrides on top.
//
// Every accessor here returns null for a car CCL didn't contribute. The value-typed fields all carry CCL's own
// defaults (an author who ignores them still gets sensible numbers), so a non-null answer means "This was a CCL
// locomotive", not necessarily "the CCL mod author explicitly chose this value".
internal static class CustomCarLoaderHelper
{
    internal static bool IsCustomCar(TrainCarLivery livery) =>
        CarTypeInjector.IdToLiveryMap.ContainsKey(livery.id);

    internal static TrainCarLivery[] TrainsetFor(TrainCarLivery livery) =>
        CarManager.GetTrainsetForLivery(livery);

    private static CCL_CarVariant? Variant(TrainCarLivery? livery) => livery as CCL_CarVariant;

    internal static float? SummonPriceFor(TrainCarLivery? livery) => Variant(livery)?.SummonPrice;

    internal static float? PartsOrderPriceFor(TrainCarLivery? livery) =>
        Variant(livery)?.DemonstratorPartsOrderCost;

    internal static float? PartsInstallPriceFor(TrainCarLivery? livery) =>
        Variant(livery)?.DemonstratorPartsInstallationCost;

    internal static float? PartsMassFor(TrainCarLivery? livery) => Variant(livery)?.PartsCargoMass;

    internal static PartsCargoModel? PartsModelFor(TrainCarLivery? livery) => Variant(livery)?.PartsModel;

    // The author's own crate models, only meaningful when PartsModelFor said Custom.
    internal static (GameObject? Dm1u, GameObject? Flatbed) PartsPrefabsFor(TrainCarLivery? livery) =>
        Variant(livery) is CCL_CarVariant v ? (v.PartsCargoPrefabDM1U, v.PartsCargoPrefabFlatbed) : (null, null);

    internal static bool HasPartsPrefab(TrainCarLivery? livery)
    {
        var (dm1u, flatbed) = PartsPrefabsFor(livery);
        return dm1u != null || flatbed != null;
    }

    internal static Texture2D? PosterFor(TrainCarLivery? livery) => Variant(livery)?.DemonstratorPoster;

    // The localization keys CCL registers the parts name under, or null when not set by the mod author
    internal static string? PartsNameKeyFor(TrainCarLivery? livery) =>
        Variant(livery) is CCL_CarVariant v && HasText(v.DemonstratorPartName) ? v.DemoPartsNameTranslationKey : null;

    internal static string? PartsShortNameKeyFor(TrainCarLivery? livery) =>
        Variant(livery) is CCL_CarVariant v && HasText(v.DemonstratorPartNameShort)
            ? v.DemoPartsNameShortTranslationKey
            : null;

    // Mirrors CCL's own test for whether a translation was worth registering.
    private static bool HasText(DVLangHelper.Data.TranslationData? data) =>
        data?.Items != null && data.Items.Any(i => !string.IsNullOrEmpty(i.Value));
}
