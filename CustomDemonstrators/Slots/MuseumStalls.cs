using System;
using System.Collections.Generic;
using System.Globalization;
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

    // Where each additional slot ended up, keyed by loco id, so we can reconstruct the state from
    // an existing save safely. A value is either a stall track name or a placement the player made by hand.
    private static readonly SavedMap _saved =
        new("CustomDemonstrators_StallLocos", "CustomDemonstrators_StallTracks");

    private const char PlacementPrefix = '@';

    internal static void Reset() => _saved.Reset();

    // Hands a removed slot's stall back, so the next one added can have it.
    internal static void Release(string locoId) => _saved.Set(locoId, null);

    // The stall a slot should use, claiming one on the spot if it has none yet. Ensures that
    // even if a user does a force respawn that re-orders locomotives that were already present,
    // they keep their existing allocations and only the new locomotives are assigned new stalls.
    internal static string? StallFor(string locoId)
    {
        var kept = _saved.Get(locoId);
        if (IsStall(kept)) return kept;

        var held = new HashSet<string>(_saved.Values.Where(IsStall));
        var stall = Prescribed.FirstOrDefault(s => !held.Contains(s));
        if (string.IsNullOrEmpty(stall))
        {
            Main.Logger.Warning($"No free museum stall left for '{locoId}'. Give it a track by hand in the "
                + "mod menu, or it will share another demonstrator's track.");
            return null;
        }

        _saved.Set(locoId, stall!);
        return stall;
    }

    private static bool IsStall(string? value) =>
        !string.IsNullOrEmpty(value) && value![0] != PlacementPrefix;

    internal static (Vector3 Offset, float Yaw)? PlacementFor(string locoId) => Decode(_saved.Get(locoId));

    // Whether loading with these settings would throw away a hand placement the save is holding. That is the
    // one direction that loses information: settings which know nothing of where a slot stands would clear
    // the save's record and drop the slot into a stall, or the template's. A placement that merely differs is
    // a move the player made in the menu, and still applies on the next load like any other setting.
    internal static bool WouldErasePlacement(string locoId, Vector3? home) =>
        home == null && PlacementFor(locoId) != null;

    // Writes the player's current placement into the save, so the slot can be rebuilt. Clearing one gives the
    // slot back to the stalls, but must leave a stall entry alone — that isn't a placement to begin with.
    internal static void RecordPlacement(string locoId, (Vector3 Offset, float Yaw)? placement)
    {
        if (placement is (Vector3 offset, float yaw)) _saved.Set(locoId, Encode(offset, yaw));
        else if (PlacementFor(locoId) != null) _saved.Set(locoId, null);
    }

    // .NET Framework's default float formatting is lossy, and a placement that drifts every
    // time it goes through the save would slowly walk the demonstrator off its track.
    private static string Encode(Vector3 offset, float yaw) => string.Join("/",
        [PlacementPrefix + offset.x.ToString("R", CultureInfo.InvariantCulture),
         offset.y.ToString("R", CultureInfo.InvariantCulture),
         offset.z.ToString("R", CultureInfo.InvariantCulture),
         yaw.ToString("R", CultureInfo.InvariantCulture)]);

    private static (Vector3 Offset, float Yaw)? Decode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value![0] != PlacementPrefix) return null;

        var parts = value.Substring(1).Split('/');
        if (parts.Length != 4) return null;

        var numbers = new float[4];
        for (int i = 0; i < 4; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
            {
                Main.Logger.Warning($"Ignoring an unreadable saved placement '{value}'.");
                return null;
            }
        }
        return (new Vector3(numbers[0], numbers[1], numbers[2]), numbers[3]);
    }

    private static RailTrack? Track(string? name) =>
        string.IsNullOrEmpty(name)
            ? null
            : SingletonBehaviour<RailTrackRegistryBase>.Instance?.GetTrackWithName(name);

    // A point on the stall to hang the garage off. Anything on the track will do — the garage spawner and
    // the controller both resolve back to the nearest track — so the midpoint is the safest choice.
    internal static Vector3? Midpoint(string? name)
    {
        var curve = Track(name)?.curve;
        return curve != null && curve.pointCount > 0 ? curve.GetPointAt(0.5f) : null;
    }
}
