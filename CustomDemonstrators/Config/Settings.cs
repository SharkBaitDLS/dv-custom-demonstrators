using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using DV;
using DV.ThingTypes;
using UnityEngine;
using UnityModManagerNet;

namespace CustomDemonstrators.Config;

public class Settings : UnityModManager.ModSettings
{
    // Serialized through the array accessor below because XmlSerializer can't handle dictionaries
    [XmlIgnore] public Dictionary<string, string> LiveryReplacements { get; set; } = [];

    public Replacement[] Replacements
    {
        get => [.. LiveryReplacements.Select(kv => new Replacement { LiveryId = kv.Key, ReplacementId = kv.Value })];
        set => LiveryReplacements = (value ?? []).ToDictionary(r => r.LiveryId, r => r.ReplacementId);
    }

    // Extra cars appended to an ordinary garage's spawned consist, keyed by garage id. Lets a single
    // garage spawn an arbitrary-length consist.
    [XmlIgnore] internal Dictionary<string, List<string>> GarageExtraCars { get; set; } = [];

    public GarageExtraEntry[] GarageExtras
    {
        get => [.. GarageExtraCars
            .Where(kv => kv.Value is { Count: > 0 })
            .Select(kv => new GarageExtraEntry { GarageId = kv.Key, LiveryIds = [.. kv.Value] })];
        set => GarageExtraCars = (value ?? [])
            .Where(e => !string.IsNullOrEmpty(e.GarageId))
            .GroupBy(e => e.GarageId)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(e => e.LiveryIds ?? [])
                      .Where(s => !string.IsNullOrEmpty(s))
                      .Distinct()
                      .ToList());
    }

    // Lets slots be added past the roundhouse stalls the mod knows about, on the understanding that the
    // player then has to say where each extra one lives. That will consequently get weird, as the player
    // will have to either pick a track that multiple demonstrators will share, or cram their extra demonstrator
    // onto some other track entirely. Either way, we don't want to make this available by default and will
    // leave it as an opt-in with an appropriate warning about how fucked your gamestate can get by doing this.
    public bool OverrideSlotLimit { get; set; }

    // Demonstrator slots this mod adds on top of the game's six. A slot is identified by the loco it
    // restores rather than by a synthetic id since the game keys restoration save state by locoLivery.id.
    // One loco can back exactly one slot, and every per-slot setting below (tender, cargo) keys
    // off that same id for vanilla and additional slots alike.
    [XmlIgnore] internal List<AdditionalSlot> AdditionalSlots { get; set; } = [];

    public AdditionalSlotEntry[] ExtraDemonstrators
    {
        get => [.. AdditionalSlots.Select(s => new AdditionalSlotEntry
        {
            LocoId = s.LocoId,
            HasHome = s.Home.HasValue,
            HomeX = s.Home?.x ?? 0f,
            HomeY = s.Home?.y ?? 0f,
            HomeZ = s.Home?.z ?? 0f,
            HomeYaw = s.HomeYaw,
        })];
        set => AdditionalSlots = [.. (value ?? [])
            .Where(e => !string.IsNullOrEmpty(e.LocoId))
            .GroupBy(e => e.LocoId)
            .Select(g => g.First())
            .Select(e => new AdditionalSlot
            {
                LocoId = e.LocoId,
                Home = e.HasHome ? new Vector3(e.HomeX, e.HomeY, e.HomeZ) : null,
                HomeYaw = e.HomeYaw,
            })];
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
        })];
        set => Demonstrators = (value ?? []).ToDictionary(e => e.LiveryId, e => new DemonstratorOverride
        {
            TenderId = string.IsNullOrEmpty(e.TenderId) ? null : e.TenderId,
            CargoId = string.IsNullOrEmpty(e.CargoId) ? null : e.CargoId,
        });
    }

    public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

    // Resolves the configured replacement livery for an original garage livery, if any.
    internal TrainCarLivery? GetReplacement(TrainCarLivery original)
    {
        if (!LiveryReplacements.TryGetValue(original.id, out var replacementId)) return null;
        return Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == replacementId);
    }

    // The extra consist liveries configured for a garage (beyond its replaced default car).
    internal IReadOnlyList<string> GetExtraCars(string garageId) =>
        GarageExtraCars.TryGetValue(garageId, out var l) ? l : [];

    internal void AddExtraCar(string garageId, string liveryId)
    {
        if (!GarageExtraCars.TryGetValue(garageId, out var l))
            GarageExtraCars[garageId] = l = [];
        if (!l.Contains(liveryId)) l.Add(liveryId);
    }

    internal void RemoveExtraCar(string garageId, string liveryId)
    {
        if (!GarageExtraCars.TryGetValue(garageId, out var l)) return;
        l.Remove(liveryId);
        if (l.Count == 0) GarageExtraCars.Remove(garageId);
    }

    internal void ClearExtraCars(string garageId) => GarageExtraCars.Remove(garageId);

    internal void ClearDemonstratorOverride(string slotId) => Demonstrators.Remove(slotId);

    internal string? GetTenderId(string slotId) =>
        Demonstrators.TryGetValue(slotId, out var o) ? o.TenderId : null;

    internal void SetTenderId(string slotId, string? secondCarId) =>
        Mutate(slotId, o => o.TenderId = secondCarId);

    // The configured parts-cargo choice for a demonstrator slot.
    // The default of null triggers auto-detection on load.
    internal string? GetPartsCargoId(string slotId) =>
        Demonstrators.TryGetValue(slotId, out var o) ? o.CargoId : null;

    internal void SetPartsCargoId(string slotId, string? cargoId) =>
        Mutate(slotId, o => o.CargoId = cargoId);

    // Applies a change to a slot's override, creating it on demand and dropping it once it's all-default
    // so the saved file (and the apply step) only carry slots the player actually customized.
    private void Mutate(string slotId, System.Action<DemonstratorOverride> change)
    {
        if (!Demonstrators.TryGetValue(slotId, out var o))
            o = new DemonstratorOverride();
        change(o);
        if (string.IsNullOrEmpty(o.TenderId) && string.IsNullOrEmpty(o.CargoId))
            Demonstrators.Remove(slotId);
        else
            Demonstrators[slotId] = o;
    }

    internal bool IsAdditionalSlot(string locoId) => AdditionalSlots.Any(s => s.LocoId == locoId);

    internal AdditionalSlot? GetAdditionalSlot(string locoId) =>
        AdditionalSlots.FirstOrDefault(s => s.LocoId == locoId);

    internal void AddAdditionalSlot(string locoId)
    {
        if (IsAdditionalSlot(locoId)) return;
        AdditionalSlots.Add(new AdditionalSlot { LocoId = locoId });
    }

    // Drops the slot along with the quest tuning that only existed to serve it.
    internal void RemoveAdditionalSlot(string locoId)
    {
        AdditionalSlots.RemoveAll(s => s.LocoId == locoId);
        Demonstrators.Remove(locoId);
    }

    internal void SetAdditionalSlotHome(string locoId, Vector3? home, float yaw)
    {
        if (GetAdditionalSlot(locoId) is not AdditionalSlot slot) return;
        slot.Home = home;
        slot.HomeYaw = yaw;
    }

    internal class AdditionalSlot
    {
        public string LocoId = "";

        // A position placed by hand, which takes precedence over any museum stall.
        public Vector3? Home;
        public float HomeYaw;
    }

    public class AdditionalSlotEntry
    {
        [XmlAttribute] public string LocoId { get; set; } = "";
        [XmlAttribute] public bool HasHome { get; set; }
        [XmlAttribute] public float HomeX { get; set; }
        [XmlAttribute] public float HomeY { get; set; }
        [XmlAttribute] public float HomeZ { get; set; }
        [XmlAttribute] public float HomeYaw { get; set; }
    }

    internal class DemonstratorOverride
    {
        public string? TenderId;
        public string? CargoId;
    }

    public class Replacement
    {
        [XmlAttribute] public string LiveryId { get; set; } = "";
        [XmlAttribute] public string ReplacementId { get; set; } = "";
    }

    public class GarageExtraEntry
    {
        [XmlAttribute] public string GarageId { get; set; } = "";
        [XmlArray("Cars"), XmlArrayItem("Car")] public string[] LiveryIds { get; set; } = [];
    }

    public class DemonstratorEntry
    {
        [XmlAttribute] public string LiveryId { get; set; } = "";
        [XmlAttribute] public string TenderId { get; set; } = "";
        [XmlAttribute] public string CargoId { get; set; } = "";
    }
}
