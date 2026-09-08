using System.Globalization;
using UnityEngine;

namespace CustomDemonstrators.Saves;

// How a home for either a demonstrator or work train the player placed by hand is written into a save
internal static class PlacedHomes
{
    internal const char Prefix = '@';

    internal static bool IsPlacement(string? value) =>
        !string.IsNullOrEmpty(value) && value![0] == Prefix;

    // .NET Framework's default float formatting is lossy, and a placement that drifts every
    // time it goes through the save would slowly walk the spawn off its track.
    internal static string Encode(Vector3 offset, float yaw) => string.Join("/",
        [Prefix + offset.x.ToString("R", CultureInfo.InvariantCulture),
         offset.y.ToString("R", CultureInfo.InvariantCulture),
         offset.z.ToString("R", CultureInfo.InvariantCulture),
         yaw.ToString("R", CultureInfo.InvariantCulture)]);

    internal static (Vector3 Offset, float Yaw)? Decode(string? value)
    {
        if (!IsPlacement(value)) return null;

        var parts = value!.Substring(1).Split('/');
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
}
