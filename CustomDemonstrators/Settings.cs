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

    // Per-demonstrator quest tuning, keyed by the original demonstrator livery id
    [XmlIgnore] internal Dictionary<string, DemonstratorOverride> Demonstrators { get; set; } = [];

    public DemonstratorEntry[] DemonstratorOverrides
    {
        get => [.. Demonstrators.Select(kv => new DemonstratorEntry
        {
            LiveryId = kv.Key,
            TenderId = kv.Value.TenderId ?? "",
            CargoId = kv.Value.CargoId ?? "",
            OrderPrice = kv.Value.OrderPrice ?? -1f,
            InstallPrice = kv.Value.InstallPrice ?? -1f,
        })];
        set => Demonstrators = (value ?? []).ToDictionary(e => e.LiveryId, e => new DemonstratorOverride
        {
            TenderId = string.IsNullOrEmpty(e.TenderId) ? null : e.TenderId,
            CargoId = string.IsNullOrEmpty(e.CargoId) ? null : e.CargoId,
            OrderPrice = e.OrderPrice < 0f ? null : e.OrderPrice,
            InstallPrice = e.InstallPrice < 0f ? null : e.InstallPrice,
        });
    }

    public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

    // Resolves the configured replacement livery for an original garage livery, if any.
    internal TrainCarLivery? GetReplacement(TrainCarLivery original)
    {
        if (!LiveryReplacements.TryGetValue(original.id, out var replacementId)) return null;
        return Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == replacementId);
    }

    internal string? GetTenderId(string slotId) =>
        Demonstrators.TryGetValue(slotId, out var o) ? o.TenderId : null;

    internal void SetTenderId(string slotId, string? secondCarId) =>
        Mutate(slotId, o => o.TenderId = secondCarId);

    // The configured parts-cargo choice for a demonstrator slot.
    // The default of null triggers auto-detection on load.
    internal string? GetPartsCargoId(string slotId) =>
        Demonstrators.TryGetValue(slotId, out var o) ? o.CargoId : null;

    internal float? GetOrderPrice(string slotId) =>
        Demonstrators.TryGetValue(slotId, out var o) ? o.OrderPrice : null;

    internal float? GetInstallPrice(string slotId) =>
        Demonstrators.TryGetValue(slotId, out var o) ? o.InstallPrice : null;

    internal void SetPartsCargoId(string slotId, string? cargoId) =>
        Mutate(slotId, o => o.CargoId = cargoId);

    internal void SetOrderPrice(string slotId, float? price) =>
        Mutate(slotId, o => o.OrderPrice = price);

    internal void SetInstallPrice(string slotId, float? price) =>
        Mutate(slotId, o => o.InstallPrice = price);

    // Applies a change to a slot's override, creating it on demand and dropping it once it's all-default
    // so the saved file (and the apply step) only carry slots the player actually customized.
    private void Mutate(string slotId, System.Action<DemonstratorOverride> change)
    {
        if (!Demonstrators.TryGetValue(slotId, out var o))
            o = new DemonstratorOverride();
        change(o);
        if (string.IsNullOrEmpty(o.TenderId) && string.IsNullOrEmpty(o.CargoId)
            && o.OrderPrice == null && o.InstallPrice == null)
            Demonstrators.Remove(slotId);
        else
            Demonstrators[slotId] = o;
    }

    internal class DemonstratorOverride
    {
        public string? TenderId;
        public string? CargoId;
        public float? OrderPrice;
        public float? InstallPrice;
    }

    public class Replacement
    {
        [XmlAttribute] public string LiveryId { get; set; } = "";
        [XmlAttribute] public string ReplacementId { get; set; } = "";
    }

    public class DemonstratorEntry
    {
        [XmlAttribute] public string LiveryId { get; set; } = "";
        [XmlAttribute] public string TenderId { get; set; } = "";
        [XmlAttribute] public string CargoId { get; set; } = "";
        [XmlAttribute] public float OrderPrice { get; set; } = -1f;
        [XmlAttribute] public float InstallPrice { get; set; } = -1f;
    }
}
