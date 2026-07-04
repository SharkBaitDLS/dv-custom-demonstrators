using System.Collections.Generic;
using System.Text;

namespace CustomDemonstrators;

// Owns the on-disk format of the demonstrator save fingerprint and can parse it into the mod's config
// so that we can safely adopt an existing save file's configuration when the mod's settings have diverged
// from it.
internal static class SaveConfig
{
    private const char EntrySeparator = ';';
    private const char SpawnSeparator = '>';
    private const char TenderSeparator = '+';
    private const string NoTender = "-";

    private static Dictionary<string, (string SpawnId, string? TenderId)>? _demo;
    private static bool _parsed;

    // Original-primary-loco id -> the loco + tender the save was written with. Null on a save the mod never
    // touched.
    internal static Dictionary<string, (string SpawnId, string? TenderId)>? Demonstrators
    {
        get
        {
            if (_parsed) return _demo;
            if (SaveState.Data() == null) return null;
            _parsed = true;
            _demo = ParseDemonstrators(SaveGuard.StoredDemonstratorFingerprint);
            return _demo;
        }
    }

    internal static void Reset()
    {
        _demo = null;
        _parsed = false;
    }

    internal static string SerializeDemonstrators(IEnumerable<(string PrimaryId, string SpawnId, string? TenderId)> entries)
    {
        var sb = new StringBuilder();
        foreach (var (primaryId, spawnId, tenderId) in entries)
        {
            sb.Append(primaryId).Append(SpawnSeparator)
              .Append(spawnId).Append(TenderSeparator)
              .Append(tenderId ?? NoTender).Append(EntrySeparator);
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
}
