using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.CashRegister;
using DV.LocoRestoration;
using DV.Shops;
using DV.ThingTypes;
using UnityEngine;
using CustomDemonstrators.Saves;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

internal static class DemonstratorSlots
{
    // Everything a live slot owns, kept so a slot removed mid-game can be taken apart again.
    private sealed class BuiltSlot
    {
        public GarageType_v2 Garage = null!;
        public GarageCarSpawner? Spawner;
        public GameObject? Home;
        public LocoRestorationController? Controller;
        public CashRegisterWithModules? Register;
        public GenericThingCashRegisterModule? Order;
        public GenericThingCashRegisterModule? Install;
        public CargoType_v2? CargoTemplate;
        public CargoType_v2? Cargo;
    }

    private static readonly Dictionary<string, BuiltSlot> _slots = [];
    private static bool _built;

    // Scene objects die with the world, the ScriptableObjects we registered into Globals do not, so they
    // are torn out here and rebuilt on the next load rather than accumulating across sessions.
    internal static void Reset()
    {
        _built = false;
        SlotTypes.Reset();

        var types = Globals.G?.Types;
        if (types != null && _slots.Count > 0)
        {
            foreach (var slot in _slots.Values)
            {
                types.garages.Remove(slot.Garage);
                if (slot.Cargo != null) types.cargos.Remove(slot.Cargo);
            }
            types.RecalculateCaches();
        }

        // Parts cargos are rebuilt from scratch each load to guarantee the next load starts from a pristine copy
        foreach (var slot in _slots.Values)
        {
            if (slot.Cargo != null) UnityEngine.Object.Destroy(slot.Cargo);
        }

        _slots.Clear();
    }

    internal static void BuildAll()
    {
        if (_built) return;
        _built = true;

        var types = Globals.G?.Types;
        if (types == null) return;

        var template = Template();
        if (template == null)
        {
            if (Main.Settings.AdditionalSlots.Count > 0)
                Main.Logger.Warning("No vanilla demonstrator to use as a template; additional demonstrator slots were skipped.");
            return;
        }

        var wanted = DesiredLocoIds().ToList();
        if (wanted.Count == 0) return;

        var spawnPoints = AllSpawnPoints();
        Main.Logger.Log($"Building {wanted.Count} additional demonstrator slot(s) from template "
            + $"{template.locoLivery.id}; {spawnPoints.Length} wreck anchor(s) pooled across "
            + $"{LocoRestorationController.allLocoRestorationControllers.Count} restoration(s).");

        bool added = false;
        foreach (var locoId in wanted)
        {
            if (Build(locoId, template, spawnPoints)) added = true;
        }

        if (added) types.RecalculateCaches();
    }

    // Brings the live world in line with the current settings for the force-respawn button
    internal static void Reconcile()
    {
        var types = Globals.G?.Types;
        if (types == null) return;

        var wanted = new HashSet<string>(DesiredLocoIds());
        bool changed = false;

        // Removals first to prevent double claims
        foreach (var locoId in _slots.Keys.Where(id => !wanted.Contains(id)).ToList())
        {
            if (Remove(locoId)) changed = true;
        }

        var template = Template();
        if (template != null)
        {
            var spawnPoints = AllSpawnPoints();
            foreach (var locoId in wanted.Where(id => !_slots.ContainsKey(id)))
            {
                if (Build(locoId, template, spawnPoints)) changed = true;
            }
        }
        else if (wanted.Any(id => !_slots.ContainsKey(id)))
        {
            Main.Logger.Warning("No vanilla demonstrator to use as a template; new demonstrator slots were skipped.");
        }

        if (changed) types.RecalculateCaches();
    }

    private static bool Build(string locoId, LocoRestorationController template, LocoRestorationSpawnPoint[] spawnPoints)
    {
        var types = Globals.G?.Types;
        if (types == null) return false;

        var loco = Livery(locoId);
        if (loco == null)
        {
            Main.Logger.Warning($"Additional demonstrator '{locoId}' was skipped: no such livery is loaded.");
            return false;
        }

        var tender = TenderFor(locoId);

        // A livery may back only one garage
        foreach (var claimed in new[] { loco, tender })
        {
            if (claimed == null) continue;
            var owner = types.garages.FirstOrDefault(g => g?.garageCarLiveries?.Contains(claimed) == true);
            if (owner != null)
            {
                Main.Logger.Warning($"Additional demonstrator '{locoId}' was skipped: {claimed.id} is already spawned by garage {owner.id}.");
                return false;
            }
        }

        var garage = SlotTypes.GetOrCreateGarage(locoId, loco, tender, template.garageSpawner.garageType);
        types.garages.Add(garage);
        SlotTypes.AllowSummoning(garage);

        var built = new BuiltSlot { Garage = garage };
        _slots[locoId] = built;

        built.Home = SlotScene.CreateHome(locoId, template.garageSpawner, out var stall);
        built.Spawner = SlotScene.CreateSpawner(locoId, garage, built.Home, template.garageSpawner);
        built.Register = SlotScene.FindRegister(template.orderPartsModule);
        (built.Order, built.Install) = SlotScene.CreateRegisterModules(locoId, template, built.Register);
        built.CargoTemplate = template.locoPartCargo;

        built.Controller = SlotScene.CreateController(
            loco, tender, template, built.Spawner, built.Order, built.Install, spawnPoints,
            SlotScene.DestinationTrackFor(locoId, stall, built.Home, template.destinationTrackId));
        DemonstratorRespawner.SettleNewDemonstrator(built.Controller);

        Main.Logger.Log($"Built additional demonstrator slot for {locoId}.");
        return true;
    }

    private static bool Remove(string locoId)
    {
        if (!_slots.TryGetValue(locoId, out var slot)) return false;
        _slots.Remove(locoId);

        Main.Logger.Log($"Removing additional demonstrator slot for {locoId}.");

        // Its restoration no longer counts as done, so the museum shouldn't keep offering the summon.
        GarageUnlocks.Revoke(slot.Garage);

        if (slot.Controller != null)
        {
            LocoRestorationController.allLocoRestorationControllers.Remove(slot.Controller);
            DemonstratorCars.Retire(slot.Controller, slot.Spawner);
            slot.Controller.StopAllCoroutines();
            UnityEngine.Object.Destroy(slot.Controller.gameObject);
        }

        SlotScene.DetachRegisterModules(slot.Register, slot.Order, slot.Install);

        if (slot.Spawner != null) UnityEngine.Object.Destroy(slot.Spawner.gameObject);
        if (slot.Home != null) UnityEngine.Object.Destroy(slot.Home);

        var types = Globals.G?.Types;
        types?.garages.Remove(slot.Garage);
        SlotTypes.RevokeSummoning(slot.Garage);
        if (slot.Cargo != null)
        {
            types?.cargos.Remove(slot.Cargo);
            SlotTypes.ForgetCargo(slot.Cargo);
            UnityEngine.Object.Destroy(slot.Cargo);
        }

        MuseumStalls.Release(locoId);
        DemonstratorCars.ReconcileSpawnPointUsage();
        return true;
    }

    private static IEnumerable<string> DesiredLocoIds()
    {
        if (SaveGuard.AllowDemonstratorChanges())
            return Main.Settings.AdditionalSlots.Select(s => s.LocoId).Where(id => !string.IsNullOrEmpty(id));

        var baked = SaveConfig.Demonstrators;
        if (baked == null) return [];
        var vanilla = VanillaGarages.VanillaDemonstratorIds();
        return baked.Keys.Where(id => !vanilla.Contains(id));
    }

    internal static CargoType_v2? OwnCargoFor(LocoRestorationController controller)
    {
        if (!SlotTypes.IsSlotGarage(controller.garageSpawner?.garageType)) return null;

        var locoId = controller.locoLivery?.id;
        if (locoId == null || !_slots.TryGetValue(locoId, out var slot)) return null;
        if (slot.Cargo != null) return slot.Cargo;

        slot.Cargo = SlotTypes.CloneCargo(slot.CargoTemplate, locoId);
        if (slot.Cargo != null) Globals.G?.Types?.RecalculateCaches();
        return slot.Cargo;
    }

    internal static LocoRestorationController? Template() =>
        LocoRestorationController.allLocoRestorationControllers
            .Where(c => c != null && c.locoLivery != null && c.garageSpawner?.garageType != null
                && c.garageSpawner.locoSpawnPoint != null && c.orderPartsModule != null
                && c.installPartsModule != null && !SlotTypes.IsSlotGarage(c.garageSpawner.garageType))
            .OrderBy(c => c.locoLivery.id, StringComparer.Ordinal)
            .FirstOrDefault();

    private static LocoRestorationSpawnPoint[] AllSpawnPoints() =>
        [.. LocoRestorationController.allLocoRestorationControllers
            .Where(c => c != null && c.spawnPoints != null)
            .SelectMany(c => c.spawnPoints)
            .Where(p => p != null)
            .Distinct()];

    internal static IEnumerable<(string LocoId, GarageType_v2 Garage, LocoRestorationController? Controller,
        CargoType_v2? OwnCargo, GameObject? Home)> LiveSlots() =>
        _slots.Select(kv => (kv.Key, kv.Value.Garage, kv.Value.Controller, kv.Value.Cargo, kv.Value.Home));

    private static TrainCarLivery? Livery(string id) =>
        Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == id);

    private static TrainCarLivery? TenderFor(string locoId)
    {
        if (!SaveGuard.AllowDemonstratorChanges()
            && SaveConfig.Demonstrators is { } baked && baked.TryGetValue(locoId, out var entry))
        {
            return entry.TenderId != null ? Livery(entry.TenderId) : null;
        }

        var id = Main.Settings.GetTenderId(locoId);
        return string.IsNullOrEmpty(id) ? null : Livery(id!);
    }
}
