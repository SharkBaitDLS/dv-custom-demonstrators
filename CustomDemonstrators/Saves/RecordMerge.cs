using System.Collections.Generic;
using System.Linq;
using CustomDemonstrators.Slots;
using CustomDemonstrators.World;
using Newtonsoft.Json.Linq;

namespace CustomDemonstrators.Saves;

internal static class RecordMerge
{
    // Bring part of one save's demonstrator record into another.
    //
    // The placements and parts cargo choices are keyed by the same slot ids and follow the same split, so the
    // whole demonstrator half of the fingerprint stays internally consistent.
    //
    // A demonstrator named in saveIds goes back into the slot it came from, displacing whatever occupies
    // that slot. One named in asNewSlots is given a slot of its own instead if possible.
    //
    // Returns the slots it rewrote, which the caller is expected to put cars into itself, or null if there
    // was nothing to merge.
    internal static HashSet<string>? Demonstrators(
        JObject theirs, IEnumerable<string> saveIds, IEnumerable<string> asNewSlots)
    {
        var data = SaveState.Data();
        if (data == null) return null;

        var other = SaveGameData.LoadFromJson(theirs);
        var theirEntries = SaveConfig.DemonstratorsIn(other);
        if (theirEntries == null) return null;

        // Enforce that a livery can only occupy one slot
        var added = new HashSet<string>(asNewSlots);
        var wanted = new HashSet<string>(saveIds);
        wanted.ExceptWith(added);

        var inPlace = theirEntries.Where(entry => wanted.Contains(entry.Value.SpawnId))
            .Select(entry => entry.Key)
            .ToList();

        // A slot of its own is named after the locomotive standing in it, so the save id is the slot id.
        var ownSlots = theirEntries.Where(entry => added.Contains(entry.Value.SpawnId))
            .Select(entry => (SlotId: entry.Value.SpawnId, entry.Value.TenderId))
            .ToList();

        if (inPlace.Count == 0 && ownSlots.Count == 0) return null;

        var merged = SaveConfig.DemonstratorsIn(data) ?? [];

        foreach (var slotId in inPlace) merged[slotId] = theirEntries[slotId];
        foreach (var (slotId, tenderId) in ownSlots) merged[slotId] = (slotId, tenderId);

        SaveConfig.WriteDemonstrators(data, merged);

        var touched = inPlace.Concat(ownSlots.Select(slot => slot.SlotId)).ToList();
        MuseumStalls.MergeFrom(other, touched);
        RestorationPartsCustomizer.MergeCargoChoicesFrom(other, touched);

        Main.Logger.Log($"Merged {touched.Count} demonstrator(s) from another save's record into this one: "
            + string.Join(", ", touched)
            + (ownSlots.Count > 0 ? $" ({ownSlots.Count} into slot(s) of their own)" : "")
            + ". Every other slot kept what this save already said about it.");
        return [.. touched];
    }

    // What each of these demonstrators would displace by going back into the slot it came from
    internal static Dictionary<string, string> SlotOccupants(JObject theirs, IEnumerable<string> saveIds)
    {
        var result = new Dictionary<string, string>();

        var other = SaveGameData.LoadFromJson(theirs);
        var theirEntries = SaveConfig.DemonstratorsIn(other);
        if (theirEntries == null) return result;

        var wanted = new HashSet<string>(saveIds);
        var mine = SaveConfig.Demonstrators;

        foreach (var entry in theirEntries.Where(entry => wanted.Contains(entry.Value.SpawnId)))
        {
            var occupant = mine != null && mine.TryGetValue(entry.Key, out var here) ? here.SpawnId : entry.Key;
            if (occupant != entry.Value.SpawnId) result[entry.Value.SpawnId] = occupant;
        }

        return result;
    }

    // Why the requested demonstrators can't be given a slot of its own, or null if they can.
    internal static Dictionary<string, string?> NewSlotEligibility(JObject theirs, IEnumerable<string> saveIds)
    {
        var result = new Dictionary<string, string?>();

        var other = SaveGameData.LoadFromJson(theirs);
        var theirEntries = SaveConfig.DemonstratorsIn(other);
        var free = FreeStalls();

        foreach (var saveId in saveIds)
        {
            result[saveId] = SlotUnavailableReason(saveId, theirEntries, free);
            if (result[saveId] == null) free--;
        }

        return result;
    }

    private static string? SlotUnavailableReason(string saveId,
        Dictionary<string, (string SpawnId, string? TenderId)>? theirEntries, int free)
    {
        var vanilla = VanillaGarages.VanillaDemonstratorIds();
        if (vanilla.Contains(saveId)) return "cannot occupy a vanilla demonstrator slot";

        if (SaveConfig.Demonstrators?.ContainsKey(saveId) == true)
            return "this save already has a slot of its own for it";

        if (free <= 0) return "the museum has no stall left for this slot";

        var tenderId = theirEntries?.Values.FirstOrDefault(e => e.SpawnId == saveId).TenderId;
        return DemonstratorSlots.SlotUnavailableReason(
            saveId, tenderId != null ? DemonstratorSetup.GetLivery(tenderId) : null);
    }

    private static int FreeStalls()
    {
        var baked = SaveConfig.Demonstrators;
        if (baked == null) return MuseumStalls.All.Count;

        var vanilla = VanillaGarages.VanillaDemonstratorIds();
        return MuseumStalls.All.Count - baked.Keys.Count(id => !vanilla.Contains(id));
    }
}
