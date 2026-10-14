using System.Globalization;
using System.Linq;
using System.Text;
using DV.OriginShift;
using UnityEngine;

namespace CustomDemonstrators.Maps;

// Helpers for writing a map's demonstrators.json from inside the game
internal static class MapAuthoring
{
    private static string _here = "";

    internal static void Draw()
    {
        GUILayout.Label($"Map authoring: {LoadedMap.Name}", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.Label($"demonstrators.json: {MapStalls.Source ?? "none, looked in " + string.Join(" and ", MapStalls.Candidates())}");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Where am I", GUILayout.Width(120))) _here = Here();
        if (GUILayout.Button("Copy pose", GUILayout.Width(120)) && Pose() is string pose)
        {
            GUIUtility.systemCopyBuffer = pose;
            _here = $"Copied {pose}";
        }
        if (GUILayout.Button("Copy nearest track", GUILayout.Width(150)) && NearestTrack() is var (track, _))
        {
            GUIUtility.systemCopyBuffer = track.name;
            _here = $"Copied {track.name}";
        }
        if (GUILayout.Button("Log warehouses", GUILayout.Width(130))) LogWarehouses();
        if (GUILayout.Button("Re-check file", GUILayout.Width(110)))
        {
            // Re-read from disk so a file can be iterated on without reloading the world
            MapStalls.Reset();
            MapStalls.Report();
        }
        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(_here)) GUILayout.Label(_here);

        GUILayout.EndVertical();
        GUILayout.Space(6);
    }

    private static (Vector3 Position, float Yaw)? PlayerPose()
    {
        var player = PlayerManager.PlayerTransform;
        var anchor = WorldMover.OriginShiftParent;
        if (player == null || anchor == null) return null;

        var yaw = Mathf.Repeat(player.eulerAngles.y - anchor.eulerAngles.y, 360f);
        return (anchor.InverseTransformPoint(player.position), yaw);
    }

    // Formatted to paste straight into the file as a board, register or wreck spawn
    private static string? Pose()
    {
        if (PlayerPose() is not var (position, yaw)) return null;
        return string.Format(CultureInfo.InvariantCulture,
            "{{ \"position\": [{0:0.00}, {1:0.00}, {2:0.00}], \"yaw\": {3:0} }}", position.x, position.y, position.z, yaw);
    }

    private static string Here()
    {
        if (Pose() is not string pose) return "Not in the world yet.";
        return NearestTrack() is var (track, distance)
            ? $"{pose}, {distance:0.0} m from track {track.name}"
            : $"{pose}, no track nearby";
    }

    private static (RailTrack Track, float Distance)? NearestTrack()
    {
        var player = PlayerManager.PlayerTransform;
        if (player == null) return null;

        var (track, point) = RailTrack.GetClosest(player.position);
        if (track == null || point == null) return null;

        // Track points are held unshifted, the player's position isn't
        var onTrack = (Vector3)point.Value.position + OriginShift.currentMove;
        return (track, Vector3.Distance(onTrack, player.position));
    }

    private static void LogWarehouses()
    {
        var sb = new StringBuilder($"Warehouse machines on map '{LoadedMap.Name}' (use the track name as partsWarehouse):\n");
        var anchor = WorldMover.OriginShiftParent;
        foreach (var machine in MapStalls.WarehouseMachines().OrderBy(m => m.warehouseTrackName))
        {
            var at = anchor != null ? anchor.InverseTransformPoint(machine.transform.position) : machine.transform.position;
            sb.AppendLine($"  {machine.warehouseTrackName} at {at}");
        }
        Main.Logger.Log(sb.ToString());
        _here = "Logged the warehouse machines.";
    }
}
