using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.Saves;

// Owns the on-disk format of the demonstrator save fingerprint and can parse it into the mod's config
// so that we can safely adopt an existing save file's configuration when the mod's settings have diverged
// from it.
internal static class SaveConfig
{
    private const char EntrySeparator = ';';
    private const char SpawnSeparator = '>';
    private const char TenderSeparator = '+';
    private const string NoTender = "-";

    private const char GarageSeparator = '=';
    private const char GarageSpawnSeparator = ',';
    private const char ExtraCarSeparator = '+';

    private static Dictionary<string, (string SpawnId, string? TenderId)>? _demo;
    private static bool _parsed;

    private static Dictionary<string, (List<string> SpawnIds, List<string> Extras)>? _garages;
    private static bool _parsedGarages;

    // Original-primary-loco id -> the loco + tender the save was written with. Null on a save the mod never
    // touched.
    internal static Dictionary<string, (string SpawnId, string? TenderId)>? Demonstrators
    {
        get
        {
            if (_parsed) return _demo;
            if (SaveState.Data() == null) return null;
            _parsed = true;
            _demo = DemonstratorsIn(SaveState.Data()!);
            return _demo;
        }
    }

    // Garage id -> the stock (in the garage's own livery order) plus the extra consist cars the save was
    // written with. Null on a save the mod never touched.
    internal static Dictionary<string, (List<string> SpawnIds, List<string> Extras)>? Garages
    {
        get
        {
            if (_parsedGarages) return _garages;
            if (SaveState.Data() == null) return null;
            _parsedGarages = true;
            _garages = ParseGarages(SaveGuard.StoredGarageFingerprint);
            return _garages;
        }
    }

    internal static void Reset()
    {
        _demo = null;
        _parsed = false;
        _garages = null;
        _parsedGarages = false;
    }

    // The demonstrator record held by any save, this one or another. Null on a save the mod never touched.
    internal static Dictionary<string, (string SpawnId, string? TenderId)>? DemonstratorsIn(SaveGameData data) =>
        ParseDemonstrators(data.GetString(SaveGuard.DemonstratorFingerprintKey));

    // Replaces the loaded save's demonstrator record, returning false if it already said exactly this.
    internal static bool WriteDemonstrators(SaveGameData data,
        IReadOnlyDictionary<string, (string SpawnId, string? TenderId)> entries)
    {
        var serialized = SerializeDemonstrators(entries);
        if (data.GetString(SaveGuard.DemonstratorFingerprintKey) == serialized) return false;

        data.SetString(SaveGuard.DemonstratorFingerprintKey, serialized);
        _demo = null;
        _parsed = false;
        return true;
    }

    internal static string SerializeDemonstrators(IReadOnlyDictionary<string, (string SpawnId, string? TenderId)> entries)
    {
        var vanilla = VanillaGarages.Demonstrators.Select((d, i) => (d.Primary.id, i))
            .ToDictionary(d => d.id, d => d.i, StringComparer.Ordinal);

        var sb = new StringBuilder();
        foreach (var entry in entries
            .OrderBy(e => vanilla.TryGetValue(e.Key, out var i) ? i : int.MaxValue)
            .ThenBy(e => e.Key, StringComparer.Ordinal))
        {
            sb.Append(entry.Key).Append(SpawnSeparator)
              .Append(entry.Value.SpawnId).Append(TenderSeparator)
              .Append(entry.Value.TenderId ?? NoTender).Append(EntrySeparator);
        }
        return sb.ToString();
    }

    private static Dictionary<string, (string SpawnId, string? TenderId)>? ParseDemonstrators(string? fingerprint)
    {
        if (string.IsNullOrEmpty(fingerprint)) return null;

        var map = new Dictionary<string, (string SpawnId, string? TenderId)>();
        foreach (var entry in fingerprint!.Split(EntrySeparator))
        {
            if (entry.Length == 0) continue;
            int spawn = entry.IndexOf(SpawnSeparator);
            if (spawn <= 0) continue;
            int tender = entry.IndexOf(TenderSeparator, spawn + 1);
            if (tender < 0) continue;

            var primaryId = entry.Substring(0, spawn);
            var spawnId = entry.Substring(spawn + 1, tender - spawn - 1);
            var tenderPart = entry.Substring(tender + 1);
            if (spawnId.Length == 0) continue;

            map[primaryId] = (spawnId, tenderPart == NoTender ? null : tenderPart);
        }
        return map.Count > 0 ? map : null;
    }

    internal static string SerializeGarages(IEnumerable<(string GarageId, IEnumerable<string> SpawnIds, IEnumerable<string> Extras)> entries)
    {
        var sb = new StringBuilder();
        foreach (var (garageId, spawnIds, extras) in entries)
        {
            sb.Append(garageId).Append(GarageSeparator);
            foreach (var spawnId in spawnIds)
                sb.Append(spawnId).Append(GarageSpawnSeparator);
            foreach (var extra in extras)
                sb.Append(ExtraCarSeparator).Append(extra);
            sb.Append(EntrySeparator);
        }
        return sb.ToString();
    }

    private static Dictionary<string, (List<string> SpawnIds, List<string> Extras)>? ParseGarages(string? fingerprint)
    {
        if (string.IsNullOrEmpty(fingerprint)) return null;

        var map = new Dictionary<string, (List<string>, List<string>)>();
        foreach (var entry in fingerprint!.Split(EntrySeparator))
        {
            if (entry.Length == 0) continue;
            int split = entry.IndexOf(GarageSeparator);
            if (split <= 0) continue;

            var garageId = entry.Substring(0, split);
            var body = entry.Substring(split + 1);

            // The spawn list is comma-terminated, so the extras (if any) start at the first '+'.
            int extras = body.IndexOf(ExtraCarSeparator);
            var spawnPart = extras < 0 ? body : body.Substring(0, extras);
            var extraPart = extras < 0 ? "" : body.Substring(extras + 1);

            map[garageId] = (NonEmpty(spawnPart, GarageSpawnSeparator), NonEmpty(extraPart, ExtraCarSeparator));
        }
        return map.Count > 0 ? map : null;
    }

    private static List<string> NonEmpty(string joined, char separator)
    {
        var parts = new List<string>();
        foreach (var part in joined.Split(separator))
            if (part.Length > 0) parts.Add(part);
        return parts;
    }
}
