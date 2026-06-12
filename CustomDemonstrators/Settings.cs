using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using DV;
using DV.ThingTypes;
using UnityModManagerNet;

namespace CustomDemonstrators;

public class Settings : UnityModManager.ModSettings
{
    // Serialized through the array accessor below because XmlSerializer can't handle dictionaries
    [XmlIgnore] public Dictionary<string, string> LiveryReplacements { get; set; } = [];

    public Replacement[] Replacements
    {
        get => [.. LiveryReplacements.Select(kv => new Replacement { LiveryId = kv.Key, ReplacementId = kv.Value })];
        set => LiveryReplacements = (value ?? []).ToDictionary(r => r.LiveryId, r => r.ReplacementId);
    }

    public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

    // Resolves the configured replacement livery for an original garage livery, if any. 
    internal TrainCarLivery? GetReplacement(TrainCarLivery original)
    {
        if (!LiveryReplacements.TryGetValue(original.id, out var replacementId)) return null;
        return Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == replacementId);
    }

    public class Replacement
    {
        [XmlAttribute] public string LiveryId { get; set; } = "";
        [XmlAttribute] public string ReplacementId { get; set; } = "";
    }
}
