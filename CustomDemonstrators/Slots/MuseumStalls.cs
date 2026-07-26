using System.Collections.Generic;
using System.Linq;
using DV.LocoRestoration;
using DV.Utils;
using UnityEngine;

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

    // The first prescribed stall nothing else has taken, or null when they are all spoken for.
    internal static string? ClaimFree()
    {
        var taken = Taken();
        return Prescribed.FirstOrDefault(s => !taken.Contains(s));
    }

    internal static int FreeCount()
    {
        var taken = Taken();
        return Prescribed.Count(s => !taken.Contains(s));
    }

    internal static HashSet<string> Taken()
    {
        var taken = new HashSet<string>();

        foreach (var controller in LocoRestorationController.allLocoRestorationControllers)
        {
            if (controller != null && !string.IsNullOrEmpty(controller.destinationTrackId))
                taken.Add(controller.destinationTrackId);
        }
        foreach (var slot in Main.Settings.AdditionalSlots)
        {
            if (!string.IsNullOrEmpty(slot.Stall)) taken.Add(slot.Stall!);
        }
        return taken;
    }

    // The stall a slot should use, claiming one on the spot if it has none yet.
    internal static string? StallFor(string locoId)
    {
        var slot = Main.Settings.GetAdditionalSlot(locoId);
        if (slot == null) return null;
        if (!string.IsNullOrEmpty(slot.Stall)) return slot.Stall;

        slot.Stall = ClaimFree();
        if (slot.Stall == null)
        {
            Main.Logger.Warning($"No free museum stall left for '{locoId}'. Give it a track by hand in the "
                + "mod menu, or it will share another demonstrator's track.");
        }
        return slot.Stall;
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
