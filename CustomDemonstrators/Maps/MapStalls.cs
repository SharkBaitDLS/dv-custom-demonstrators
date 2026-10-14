using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DV.ThingTypes;
using DV.Utils;
using Newtonsoft.Json;
using UnityEngine;
using CustomDemonstrators.World;

namespace CustomDemonstrators.Maps;

// A stall from a custom map's demonstrators.json
internal sealed class MapStall(
    string id, string slotId, RailTrack track, TrainCarLivery loco, TrainCarLivery? secondCar,
    Placement? board, IReadOnlyList<Placement> wreckSpawns)
{
    internal readonly string Id = id;

    internal readonly string SlotId = slotId;

    internal readonly RailTrack Track = track;
    internal readonly TrainCarLivery Loco = loco;
    internal readonly TrainCarLivery? SecondCar = secondCar;
    internal readonly Placement? Board = board;
    internal readonly IReadOnlyList<Placement> WreckSpawns = wreckSpawns;
}

// The demonstrator stalls the loaded map defines, read from its demonstrators.json.
internal static class MapStalls
{
    internal const string FileName = "demonstrators.json";
    private const int CurrentVersion = 1;

    internal sealed class Resolution
    {
        internal readonly List<MapStall> Stalls = [];
        internal readonly List<string> Problems = [];
        internal WarehouseMachineController? Warehouse;
        internal Placement? Register;
    }

    private static bool _loaded;
    private static MapDefinition? _definition;
    private static string? _source;
    private static bool _reported;

    internal static void Reset()
    {
        _loaded = false;
        _definition = null;
        _source = null;
        _reported = false;
    }

    // The file the definition came from, or null when this map has none.
    internal static string? Source
    {
        get
        {
            Load();
            return _source;
        }
    }

    internal static IEnumerable<string> Candidates()
    {
        if (LoadedMap.MapAsset(FileName) is string own) yield return own;

#if DEBUG
        var name = new string([.. LoadedMap.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)]);
        yield return Path.Combine(Path.Combine(Main.ModPath, "Maps"), name + ".json");
#endif
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        var path = Candidates().FirstOrDefault(File.Exists);
        if (path == null) return;

        _source = path;
        try
        {
            _definition = JsonConvert.DeserializeObject<MapDefinition>(File.ReadAllText(path));
            if (_definition == null) Main.Logger.Warning($"{path} is empty; this map gets no demonstrators.");
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Main.Logger.Warning($"Couldn't read {path}, so this map will have no demonstrators: {e.Message}");
        }
    }

    // Checks the definition against the loaded world. Null when the map defines no demonstrators.
    internal static Resolution? Resolve()
    {
        Load();
        if (_definition == null) return null;

        var resolution = new Resolution();
        var problems = resolution.Problems;

        if (_definition.Version != CurrentVersion)
            problems.Add($"version {_definition.Version} isn't one this mod knows (expected {CurrentVersion}); reading it anyway");

        resolution.Warehouse = Warehouse(_definition.PartsWarehouse, problems);
        resolution.Register = Pose(_definition.Register, "register", problems);
        if (_definition.Register == null) problems.Add("no register given; it will stand beside the first stall's board");

        var shared = Poses(_definition.WreckSpawns, "shared wreck spawn", problems);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var claimed = new Dictionary<TrainCarLivery, string>();
        foreach (var stall in _definition.Stalls ?? [])
        {
            if (Stall(stall, ids, claimed, shared, problems) is MapStall built) resolution.Stalls.Add(built);
        }

        if (resolution.Warehouse == null && resolution.Stalls.Count > 0)
        {
            problems.Add($"no stall can be built without a parts warehouse");
            resolution.Stalls.Clear();
        }

        return resolution;
    }

    private static MapStall? Stall(StallDefinition stall, HashSet<string> ids,
        Dictionary<TrainCarLivery, string> claimed, IReadOnlyList<Placement> shared, List<string> problems)
    {
        if (string.IsNullOrEmpty(stall.Id))
        {
            problems.Add("a stall has no id and was skipped");
            return null;
        }

        var id = stall.Id!;
        if (!ids.Add(id))
        {
            problems.Add($"stall '{id}' is defined more than once, the first definition will be used.");
            return null;
        }

        var track = string.IsNullOrEmpty(stall.Track) ? null : Track(stall.Track!);
        if (track == null)
        {
            problems.Add($"stall '{id}' names track '{stall.Track}', which isn't in this map.");
            return null;
        }

        if (stall.Default == null || string.IsNullOrEmpty(stall.Default.Loco))
        {
            problems.Add($"stall '{id}' has no default loco and will be empty unless a user overrides it.");
            return null;
        }

        var loco = GameTypes.Livery(stall.Default.Loco);
        if (loco == null)
        {
            problems.Add($"stall '{id}' wants loco '{stall.Default.Loco}', which isn't loaded. Is its mod installed?");
            return null;
        }
        if (!CarTypes.IsLocomotive(loco) || loco.requiredLicense == null)
        {
            problems.Add($"stall '{id}' wants '{loco.id}', which isn't a license-gated locomotive and cannot be used.");
            return null;
        }

        TrainCarLivery? secondCar = null;
        if (!string.IsNullOrEmpty(stall.Default.SecondCar))
        {
            secondCar = GameTypes.Livery(stall.Default.SecondCar);
            if (secondCar == null)
            {
                problems.Add($"stall '{id}' wants second car '{stall.Default.SecondCar}', which isn't available.");
                return null;
            }
        }

        // One livery can only be spawned by one thing, so the first stall in the file keeps it
        foreach (var car in new[] { loco, secondCar })
        {
            if (car == null) continue;
            if (claimed.TryGetValue(car, out var holder))
            {
                problems.Add($"stall '{id}' wants '{car.id}', which stall '{holder}' already owns.");
                return null;
            }
        }
        claimed[loco] = id;
        if (secondCar != null) claimed[secondCar] = id;

        var own = Poses(stall.WreckSpawns, $"stall '{id}' wreck spawn", problems);
        var spawns = own.Count > 0 ? own : shared;
        if (spawns.Count == 0)
        {
            problems.Add($"stall '{id}' has no wreck spawns of its own and the map has no common ones defined.");
            return null;
        }

        var board = Pose(stall.Board, $"stall '{id}' board", problems);
        return new MapStall(id, $"{LoadedMap.Name}/{id}", track, loco, secondCar, board, spawns);
    }

    private static WarehouseMachineController? Warehouse(string? trackName, List<string> problems)
    {
        if (string.IsNullOrEmpty(trackName))
        {
            problems.Add("no partsWarehouse given");
            return null;
        }

        var machine = WarehouseMachines().FirstOrDefault(m => m.warehouseTrackName == trackName);
        if (machine == null)
            problems.Add($"partsWarehouse '{trackName}' doesn't match the track of any warehouse machine in this world");
        return machine;
    }

    internal static IEnumerable<WarehouseMachineController> WarehouseMachines() =>
        Resources.FindObjectsOfTypeAll<WarehouseMachineController>()
            .Where(m => m != null && m.gameObject.scene.IsValid());

    private static RailTrack? Track(string name) =>
        SingletonBehaviour<RailTrackRegistryBase>.Instance is var registry && registry != null
            ? registry.GetTrackWithName(name)
            : null;

    private static Placement? Pose(PoseDefinition? pose, string what, List<string> problems)
    {
        if (pose == null) return null;
        if (pose.ToPlacement() is Placement placement) return placement;

        problems.Add($"{what} needs a position of three numbers and was ignored");
        return null;
    }

    private static List<Placement> Poses(List<PoseDefinition>? poses, string what, List<string> problems) =>
        [.. (poses ?? []).Select(p => Pose(p, what, problems)).OfType<Placement>()];

    internal static void ReportOnce()
    {
        if (_reported) return;
        _reported = true;
        Report();
    }

    internal static void Report()
    {
        var resolution = Resolve();
        if (resolution == null)
        {
            var looked = Candidates().ToList();
            Main.Logger.Log($"Map '{LoadedMap.Name}' defines no demonstrators"
                + (looked.Count > 0 ? $" (looked for {string.Join(", ", looked)})." : "."));
            return;
        }

        var sb = new StringBuilder($"Map '{LoadedMap.Name}' demonstrators, from {_source}:\n");
        sb.AppendLine($"  parts warehouse: {(resolution.Warehouse != null ? resolution.Warehouse.warehouseTrackName : "none")}");
        sb.AppendLine($"  register: {Describe(resolution.Register)}");
        foreach (var stall in resolution.Stalls)
        {
            sb.AppendLine($"  stall '{stall.Id}': {stall.Loco.id}"
                + (stall.SecondCar != null ? $" + {stall.SecondCar.id}" : "")
                + $" on {stall.Track.name}, board {Describe(stall.Board)}, {stall.WreckSpawns.Count} wreck spawn(s)");
        }
        foreach (var problem in resolution.Problems)
        {
            sb.AppendLine($"  problem: {problem}");
        }
        Main.Logger.Log(sb.ToString());
    }

    private static string Describe(Placement? placement) =>
        placement is Placement p ? $"at {p.Offset} facing {p.Yaw:0}°" : "automatic";
}
