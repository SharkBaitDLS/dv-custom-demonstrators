using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;
using CustomDemonstrators.Saves;

namespace CustomDemonstrators.World;

internal static class SlotTypes
{
    internal const string GarageIdPrefix = "CustomDemonstrators_garage_";
    internal const string CargoIdPrefix = "CustomDemonstrators_parts_";

    private const int SyntheticV1Base = 30000;

    private const string CargoMapIdsKey = "CustomDemonstrators_CargoIds";
    private const string CargoMapValuesKey = "CustomDemonstrators_CargoValues";

    private static Dictionary<string, int>? _cargoV1;

    private static readonly Dictionary<string, GarageType_v2> _garageCache = [];

    // The v1 numbers currently handed out to slot cargos. Kept as a set of its own rather than derived from
    // the cargo list because SyntheticCargoPatch asks on every Enum.IsDefined call in the game.
    private static readonly HashSet<int> _slotCargoValues = [];

    internal static bool IsSlotGarage(GarageType_v2? garage) =>
        garage != null && garage.id.StartsWith(GarageIdPrefix, StringComparison.Ordinal);

    internal static bool IsSlotCargo(CargoType_v2? cargo) =>
        cargo != null && cargo.id.StartsWith(CargoIdPrefix, StringComparison.Ordinal);

    internal static bool IsSlotCargoValue(int value) => _slotCargoValues.Contains(value);

    // The mapping belongs to the save, so it is re-read for whichever loads next.
    internal static void Reset()
    {
        _cargoV1 = null;
        _slotCargoValues.Clear();
    }

    internal static GarageType_v2 GetOrCreateGarage(
        string locoId, TrainCarLivery loco, TrainCarLivery? tender, GarageType_v2 template)
    {
        if (!_garageCache.TryGetValue(locoId, out var garage) || garage == null)
        {
            garage = ScriptableObject.CreateInstance<GarageType_v2>();
            garage.name = garage.id = GarageIdPrefix + locoId;
            // Unlocked garages are saved as id strings, so this number only has to be unique right now.
            garage.v1 = (Garage)FreeV1([
                .. Globals.G!.Types.garages.Select(g => (int)g.v1),
                .. _garageCache.Values.Where(g => g != null).Select(g => (int)g.v1),
            ]);
            _garageCache[locoId] = garage;
        }

        // Named after the loco rather than the template so unlock messages and the radio read sensibly.
        garage.localizationKey = loco.localizationKey;
        garage.summonPrice = template.summonPrice;
        garage.garageCarLiveries = tender != null ? [loco, tender] : [loco];
        FreeRoamField?.SetValue(garage, FreeRoamField.GetValue(template));
        return garage;
    }

    private static readonly FieldInfo? FreeRoamField =
        AccessTools.Field(typeof(GarageType_v2), "freeRoamAvailability");

    // The comms radio enumerates CarSpawner's own serialized array rather than the Globals garage list,
    // so a summonable garage has to be appended there too.
    internal static void AllowSummoning(GarageType_v2 garage)
    {
        var spawner = CarSpawner.Instance;
        if (spawner?.crewVehicleGarages == null) return;
        if (spawner.crewVehicleGarages.Contains(garage)) return;
        spawner.crewVehicleGarages = [.. spawner.crewVehicleGarages, garage];
    }

    internal static void RevokeSummoning(GarageType_v2 garage)
    {
        var spawner = CarSpawner.Instance;
        if (spawner?.crewVehicleGarages == null) return;
        spawner.crewVehicleGarages = [.. spawner.crewVehicleGarages.Where(g => g != garage)];
    }

    internal static CargoType_v2? CloneCargo(CargoType_v2? template, string locoId)
    {
        if (template == null) return null;
        var clone = UnityEngine.Object.Instantiate(template);
        clone.name = clone.id = CargoIdPrefix + locoId;
        clone.v1 = CargoV1(clone.id);
        _slotCargoValues.Add((int)clone.v1);
        Globals.G.Types.cargos.Add(clone);
        return clone;
    }

    internal static void ForgetCargo(CargoType_v2 cargo) => _slotCargoValues.Remove((int)cargo.v1);

    // The first value at or above the base that nobody has taken. Fine for anything whose number doesn't
    // have to survive a reload — garage unlocks are saved as id strings, so garages qualify.
    private static int FreeV1(HashSet<int> used)
    {
        int value = SyntheticV1Base;
        while (used.Contains(value)) value++;
        return value;
    }

    private static CargoType CargoV1(string cargoId)
    {
        _cargoV1 ??= LoadCargoMapping();

        var used = new HashSet<int>(Globals.G!.Types.cargos.Select(c => (int)c.v1));

        if (_cargoV1.TryGetValue(cargoId, out var saved))
        {
            if (!used.Contains(saved)) return (CargoType)saved;
            Main.Logger.Warning($"Parts cargo {cargoId} can't reuse its saved id {saved}, something else now "
                + "holds it. Any restoration parts already loaded onto a car will be lost.");
        }

        used.UnionWith(_cargoV1.Values);
        int value = FreeV1(used);
        _cargoV1[cargoId] = value;
        SaveCargoMapping();
        return (CargoType)value;
    }

    private static Dictionary<string, int> LoadCargoMapping()
    {
        var map = new Dictionary<string, int>();
        var data = SaveState.Data();
        var ids = data?.GetStringArray(CargoMapIdsKey);
        var values = data?.GetIntArray(CargoMapValuesKey);
        if (ids == null || values == null) return map;

        for (int i = 0; i < Math.Min(ids.Length, values.Length); i++)
        {
            map[ids[i]] = values[i];
        }
        return map;
    }

    private static void SaveCargoMapping()
    {
        var data = SaveState.Data();
        if (data == null || _cargoV1 == null) return;
        data.SetStringArray(CargoMapIdsKey, [.. _cargoV1.Keys]);
        data.SetIntArray(CargoMapValuesKey, [.. _cargoV1.Values]);
    }

    internal static IReadOnlyDictionary<string, int> CargoMapping() => _cargoV1 ?? LoadCargoMapping();
}
