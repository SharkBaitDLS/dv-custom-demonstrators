using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using CustomDemonstrators.World;

namespace CustomDemonstrators.Maps;

// The demonstrators.json metadata for a custom map
internal sealed class MapDefinition
{
    [JsonProperty("version")] public int Version { get; set; }

    [JsonProperty("partsWarehouse")] public string? PartsWarehouse { get; set; }

    [JsonProperty("register")] public PoseDefinition? Register { get; set; }

    [JsonProperty("wreckSpawns")] public List<PoseDefinition>? WreckSpawns { get; set; }

    [JsonProperty("stalls")] public List<StallDefinition>? Stalls { get; set; }
}

internal sealed class StallDefinition
{
    [JsonProperty("id")] public string? Id { get; set; }

    [JsonProperty("track")] public string? Track { get; set; }

    [JsonProperty("board")] public PoseDefinition? Board { get; set; }

    [JsonProperty("wreckSpawns")] public List<PoseDefinition>? WreckSpawns { get; set; }

    [JsonProperty("default")] public StallDefault? Default { get; set; }
}

internal sealed class StallDefault
{
    [JsonProperty("loco")] public string? Loco { get; set; }
    [JsonProperty("secondCar")] public string? SecondCar { get; set; }
}

internal sealed class PoseDefinition
{
    [JsonProperty("position")] public float[]? Position { get; set; }
    [JsonProperty("yaw")] public float Yaw { get; set; }

    internal Placement? ToPlacement() =>
        Position is { Length: 3 } p ? new Placement(new Vector3(p[0], p[1], p[2]), Yaw) : null;
}
