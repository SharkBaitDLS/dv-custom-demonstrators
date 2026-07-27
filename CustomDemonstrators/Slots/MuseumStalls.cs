using System;
using System.Collections.Generic;
using System.Linq;
using DV.Utils;
using UnityEngine;
using CustomDemonstrators.Saves;

namespace CustomDemonstrators.Slots;

internal static class MuseumStalls
{
    // Exclude 21-26, which carry the museum's service structures, some random bogeys, and the flatcar.
    // Ordered such that new demonstrators will take slots adjacent to the vanilla ones expanding outwards.
    private static readonly string[] Prescribed =
    [
        "[Y]_[CS]_[M-11-P]",
        "[Y]_[CS]_[M-18-P]",
        "[Y]_[CS]_[M-10-P]",
        "[Y]_[CS]_[M-19-P]",
        "[Y]_[CS]_[M-09-P]",
        "[Y]_[CS]_[M-20-P]",
        "[Y]_[CS]_[M-08-P]",
        "[Y]_[CS]_[M-07-P]",
        "[Y]_[CS]_[M-06-P]",
        "[Y]_[CS]_[M-05-P]",
        "[Y]_[CS]_[M-04-P]",
        "[Y]_[CS]_[M-03-P]",
        "[Y]_[CS]_[M-02-P]",
    ];

    internal static IReadOnlyList<string> All => Prescribed;

    internal static int FreeCount() =>
        Math.Max(0, Prescribed.Length - Main.Settings.AdditionalSlots.Count(s => !s.Home.HasValue));

    private const string StallLocosKey = "CustomDemonstrators_StallLocos";
    private const string StallTracksKey = "CustomDemonstrators_StallTracks";

    private static Dictionary<string, string>? _saved;

    internal static void Reset() => _saved = null;

    private static Dictionary<string, string> Saved()
    {
        if (_saved != null) return _saved;

        _saved = [];
        var data = SaveState.Data();
        var locos = data?.GetStringArray(StallLocosKey);
        var tracks = data?.GetStringArray(StallTracksKey);
        if (locos == null || tracks == null) return _saved;

        for (int i = 0; i < Math.Min(locos.Length, tracks.Length); i++) _saved[locos[i]] = tracks[i];
        return _saved;
    }

    private static void Persist()
    {
        var data = SaveState.Data();
        if (data == null || _saved == null) return;
        data.SetStringArray(StallLocosKey, [.. _saved.Keys]);
        data.SetStringArray(StallTracksKey, [.. _saved.Values]);
    }

    // Hands a removed slot's stall back, so the next one added can have it.
    internal static void Release(string locoId)
    {
        if (Saved().Remove(locoId)) Persist();
    }

    // The stall a slot should use, claiming one on the spot if it has none yet. Ensures that
    // even if a user does a force respawn that re-orders locomotives that were already present,
    // they keep their existing allocations and only the new locomotives are assigned new stalls.
    internal static string? StallFor(string locoId)
    {
        var saved = Saved();
        if (saved.TryGetValue(locoId, out var kept) && !string.IsNullOrEmpty(kept)) return kept;

        var held = new HashSet<string>(saved.Values.Where(s => !string.IsNullOrEmpty(s)));
        var stall = Prescribed.FirstOrDefault(s => !held.Contains(s));
        if (string.IsNullOrEmpty(stall))
        {
            Main.Logger.Warning($"No free museum stall left for '{locoId}'. Give it a track by hand in the "
                + "mod menu, or it will share another demonstrator's track.");
            return null;
        }

        saved[locoId] = stall!;
        Persist();
        return stall;
    }

    private static RailTrack? Track(string? name) =>
        string.IsNullOrEmpty(name)
            ? null
            : SingletonBehaviour<RailTrackRegistryBase>.Instance?.GetTrackWithName(name);

    // A point on the stall to hang the garage off. Anything on the track will do — the garage spawner and
    // the restoration both resolve back to the nearest track — so the midpoint is the safest choice.
    internal static Vector3? Midpoint(string? name)
    {
        var curve = Track(name)?.curve;
        return curve != null && curve.pointCount > 0 ? curve.GetPointAt(0.5f) : null;
    }
}
