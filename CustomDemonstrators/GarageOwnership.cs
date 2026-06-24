using System.Collections.Generic;
using System.Linq;
using DV.ThingTypes;

namespace CustomDemonstrators;

// Detects ordinary garages the player has already unlocked and taken ownership of the car from, by
// reading the loaded save.
//
// Reads from save data rather than live objects because the decision happens at GarageCarSpawner.Awake
// after SaveGameManager.data is populated, but before cars are spawned.
internal static class GarageOwnership
{
    private static HashSet<string>? _unlockedGarageIds;
    private static HashSet<int>? _ownedCarTypes;

    internal static bool IsUnlockedAndOwned(GarageType_v2 garage)
    {
        if (!UnlockedGarageIds().Contains(garage.id)) return false;
        var types = OwnedCarTypes();
        if (types.Count == 0) return false;
        return GarageVehicles.OriginalLiveries(garage).Any(l => l != null && types.Contains((int)l.v1));
    }

    internal static void ResetForNewSave()
    {
        _unlockedGarageIds = null;
        _ownedCarTypes = null;
    }

    private static HashSet<string> UnlockedGarageIds()
    {
        if (_unlockedGarageIds != null) return _unlockedGarageIds;
        var ids = SaveState.Data()?.GetStringArray("Garages");
        return _unlockedGarageIds = ids != null ? [.. ids] : [];
    }

    private static HashSet<int> OwnedCarTypes()
    {
        if (_ownedCarTypes != null) return _ownedCarTypes;
        return _ownedCarTypes = ComputeOwnedCarTypes();
    }

    private static HashSet<int> ComputeOwnedCarTypes()
    {
        var result = new HashSet<int>();
        var data = SaveState.Data();
        if (data == null) return result;

        var hash = data.GetString("Last_Tracks_Hash");
        if (string.IsNullOrEmpty(hash)) return result;
        var carsObject = data.GetJObject("Cars#" + hash);
        if (carsObject == null) return result;

        foreach (var car in SaveGameData.LoadFromJson(carsObject).GetJObjectArray("carsData") ?? [])
        {
            var entry = SaveGameData.LoadFromJson(car);
            if (entry.GetBool("unique") != true) continue; // unique == player-owned
            var type = entry.GetInt("type");
            if (type.HasValue) result.Add(type.Value);
        }
        return result;
    }
}
