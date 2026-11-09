using System.Collections.Generic;
using System.Linq;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using UnityEngine;
using CCL.Types;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World.Parts;

internal static class PartsCargoModels
{
    private static readonly Dictionary<PartsCargoModel, CargoType> _modelSources = new()
    {
        [PartsCargoModel.GenericBox] = CargoType.TrainPartsDM3,
        [PartsCargoModel.BoilerS060] = CargoType.TrainPartsS060,
        [PartsCargoModel.WheelsS282] = CargoType.TrainPartsS282A,
        [PartsCargoModel.EngineDE6] = CargoType.TrainPartsDE6,
    };

    private static Dictionary<CargoType, (TrainCarType_v2 CarType, GameObject[] Variants)[]>? _stockModels;

    internal static void CaptureStockModels()
    {
        if (_stockModels != null) return;
        _stockModels = _modelSources.Values.Distinct()
            .Select(model => (model, cargo: model.ToV2()))
            .Where(p => p.cargo != null && p.cargo.loadableCarTypes != null)
            .ToDictionary(p => p.model, p => p.cargo.loadableCarTypes
                .Where(li => li != null && li.carType != null)
                .Select(li => (li.carType, li.cargoPrefabVariants))
                .ToArray());
    }

    internal static bool HasAuthored(TrainCarLivery? loco) => CustomCarLoaderHelper.PartsModelFor(loco) switch
    {
        null or PartsCargoModel.GenericBox => false,
        PartsCargoModel.Custom => CustomCarLoaderHelper.HasPartsPrefab(loco),
        _ => true,
    };

    internal static void Apply(CargoType_v2 partsCargo, TrainCarLivery loco, bool useAuthored)
    {
        var model = useAuthored
            ? CustomCarLoaderHelper.PartsModelFor(loco) ?? PartsCargoModel.GenericBox
            : PartsCargoModel.GenericBox;

        if (model == PartsCargoModel.Custom && UseAuthored(partsCargo, loco)) return;

        UseStock(partsCargo,
            _modelSources.TryGetValue(model, out var source) ? source : CargoType.TrainPartsDM3);
    }

    // Loads the author's own crate prefabs, or falls back to the default crate if not provided
    private static bool UseAuthored(CargoType_v2 partsCargo, TrainCarLivery loco)
    {
        var (dm1u, flatbed) = CustomCarLoaderHelper.PartsPrefabsFor(loco);
        if (dm1u == null && flatbed == null)
        {
            return false;
        }
        if (partsCargo.loadableCarTypes == null) return false;

        // Default to the stock crate so a carrier the author shipped no prefab for keeps a model that fits it.
        UseStock(partsCargo, CargoType.TrainPartsDM3);

        foreach (var li in partsCargo.loadableCarTypes)
        {
            if (li.carType == null) continue;

            var prefab = li.carType.id switch
            {
                "LocoDM1U" => dm1u,
                "FlatbedShort" => flatbed,
                // If this is a modded flatcar, we don't try to guess at whether the DM1U or vanilla flatcar
                // prefabs from the loco mod will fit and let it keep the vanilla crate prefab it offers.
                _ => null
            };

            if (prefab != null) li.cargoPrefabVariants = [prefab];
        }
        CargoPrefabs.InvalidateCache(partsCargo);
        return true;
    }

    // Swap the parts crate model for one of the game's own, keeping the cargo's loadable car types
    private static void UseStock(CargoType_v2 partsCargo, CargoType model)
    {
        if (partsCargo.loadableCarTypes == null) return;
        if (_stockModels == null || !_stockModels.TryGetValue(model, out var stock) || stock.Length == 0) return;

        foreach (var li in partsCargo.loadableCarTypes)
        {
            var (CarType, Variants) = stock.FirstOrDefault(s => s.CarType == li.carType);
            li.cargoPrefabVariants = Variants ?? stock[0].Variants;
        }
        CargoPrefabs.InvalidateCache(partsCargo);
    }
}
