using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV;
using DV.CashRegister;
using DV.LocoRestoration;
using DV.Shops;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;
using CustomDemonstrators.Saves;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

// Builds demonstrator slots the game doesn't ship with. Each configured slot gets its own garage type,
// garage spawner, restoration cash-register modules and LocoRestorationController, assembled from a
// vanilla demonstrator used as a template.
//
// Nothing here needs custom save code: the game keys restoration state by LocoRestorationController.SaveID
// (which is locoLivery.id) and garage unlocks by garage id string, so a slot whose loco is unique persists
// and reloads through the vanilla paths. Ids the game no longer recognises are dropped on load, which is
// what makes removing a slot (or the whole mod) degrade quietly.
internal static class DemonstratorSlotFactory
{
    internal const string GarageIdPrefix = "CustomDemonstrators_garage_";
    internal const string CargoIdPrefix = "CustomDemonstrators_parts_";

    // Synthetic v1 enum values for the types we create. DVObjectModel.RecalculateCaches builds v1-keyed
    // dictionaries with Dictionary.Add, so a value colliding with a real one takes down the whole game
    // data cache.
    //
    // Custom Car Loader hands out its own synthetic TrainCarType values counting down from -1000, so
    // staying in high positives keeps us clear of it whatever order the two mods load in.
    private const int SyntheticV1Base = 30000;

    // Cargo mapping, saved alongside the world. A car carrying restoration parts stores the cargo as its
    // v1 int, so a slot whose number moved between sessions would leave a mid-quest save holding parts the
    // game can no longer resolve. CCL has the same problem with liveries and solves it the same way:
    // record what was handed out and reuse it, rather than trying to re-derive it.
    private const string CargoMapIdsKey = "CustomDemonstrators_CargoIds";
    private const string CargoMapValuesKey = "CustomDemonstrators_CargoValues";

    private static Dictionary<string, int>? _cargoV1;

    // Garage types outlive a world so that tearing one down can't strand a GarageCarSpawner that still
    // reads its liveries while the scene unloads; they are unregistered from game data instead.
    private static readonly Dictionary<string, GarageType_v2> _garageCache = [];

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

    internal static bool IsSlotGarage(GarageType_v2? garage) =>
        garage != null && garage.id.StartsWith(GarageIdPrefix, StringComparison.Ordinal);

    internal static bool IsSlotCargo(CargoType_v2? cargo) =>
        cargo != null && cargo.id.StartsWith(CargoIdPrefix, StringComparison.Ordinal);

    // Scene objects die with the world; the ScriptableObjects we registered into Globals do not, so they
    // are torn out here and rebuilt on the next load rather than accumulating across sessions.
    internal static void Reset()
    {
        _built = false;
        _cargoV1 = null; // the mapping belongs to the save, so re-read it for whichever loads next

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

        // Parts cargos are rebuilt from scratch each load: RestorationPartsCustomizer renames and reskins
        // them in place, and re-cloning is what guarantees the next load starts from a pristine copy.
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
        // Worth stating plainly in every log: whether there were spare wreck anchors to go around is the
        // first thing to check if an added slot's wreck doesn't turn up.
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

    // Brings the live world in line with the current settings, for the force-respawn button. Adding a slot
    // is additive and safe; removing one gives back a finished loco and deletes an unfinished wreck, which
    // is the same bargain the button already makes for changing a demonstrator.
    internal static void Reconcile()
    {
        var types = Globals.G?.Types;
        if (types == null) return;

        var wanted = new HashSet<string>(DesiredLocoIds());
        bool changed = false;

        // Removals first: a loco moving from one slot to another would otherwise be claimed twice, and
        // RecalculateCaches throws on a livery that two garages both spawn.
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

    private static bool Remove(string locoId)
    {
        if (!_slots.TryGetValue(locoId, out var slot)) return false;
        _slots.Remove(locoId);

        Main.Logger.Log($"Removing additional demonstrator slot for {locoId}.");

        // Its restoration no longer counts as done, so the museum shouldn't keep offering the summon.
        GarageUnlocks.Revoke(slot.Garage);

        if (slot.Controller != null)
        {
            // Drop it from the registry by hand: Destroy defers OnDestroy to the end of the frame, and
            // anything walking the list before then would treat this slot as still live.
            LocoRestorationController.allLocoRestorationControllers.Remove(slot.Controller);
            GarageReplacementApplier.RetireDemonstratorCars(slot.Controller, slot.Spawner);
            slot.Controller.StopAllCoroutines();
            UnityEngine.Object.Destroy(slot.Controller.gameObject);
        }

        DetachRegisterModules(slot);

        if (slot.Spawner != null) UnityEngine.Object.Destroy(slot.Spawner.gameObject);
        if (slot.Home != null) UnityEngine.Object.Destroy(slot.Home);

        var types = Globals.G?.Types;
        types?.garages.Remove(slot.Garage);
        RevokeSummoning(slot.Garage);
        if (slot.Cargo != null)
        {
            types?.cargos.Remove(slot.Cargo);
            UnityEngine.Object.Destroy(slot.Cargo);
        }

        GarageReplacementApplier.ReconcileSpawnPointUsage();
        return true;
    }

    // The register keeps a plain array and re-subscribes to every entry on enable, so a destroyed module
    // left in it would throw the next time the player opens the shop.
    private static void DetachRegisterModules(BuiltSlot slot)
    {
        if (slot.Register != null && slot.Register.registerModules != null)
        {
            slot.Register.registerModules =
                [.. slot.Register.registerModules.Where(m => m != null && m != slot.Order && m != slot.Install)];
        }

        foreach (var module in new[] { slot.Order, slot.Install })
        {
            if (module == null) continue;
            // Zero the basket first so a pending purchase doesn't linger in the register's total.
            SetUnitsToBuy?.Invoke(module, [0f]);
            UnityEngine.Object.Destroy(module);
        }
    }

    private static readonly MethodInfo? SetUnitsToBuy =
        AccessTools.Method(typeof(CashRegisterModule), "SetUnitsToBuy", [typeof(float)]);

    // The slots to build. Settings drive a save the mod owns; a save whose demonstrators were baked with
    // different settings rebuilds exactly what it already contains, so loading it doesn't quietly add or
    // drop museum locos behind the player's back (the GUI's force-respawn button is the way through).
    private static IEnumerable<string> DesiredLocoIds()
    {
        if (SaveGuard.AllowDemonstratorChanges())
            return Main.Settings.AdditionalSlots.Select(s => s.LocoId).Where(id => !string.IsNullOrEmpty(id));

        var baked = SaveConfig.Demonstrators;
        if (baked == null) return [];
        var vanilla = GarageVehicles.VanillaDemonstratorIds();
        return baked.Keys.Where(id => !vanilla.Contains(id));
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

        // A livery may back only one garage: DVObjectModel.RecalculateCaches maps livery -> garage with
        // Dictionary.Add and would throw for the whole game otherwise.
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

        var garage = GetOrCreateGarageType(locoId, loco, tender, template.garageSpawner.garageType);
        types.garages.Add(garage);
        AllowSummoning(garage);

        var built = new BuiltSlot { Garage = garage };
        _slots[locoId] = built;

        built.Home = CreateHomeMarker(locoId, template.garageSpawner, out var stall);
        built.Spawner = CreateGarageSpawner(locoId, garage, built.Home, template.garageSpawner);
        built.Register = template.orderPartsModule.GetComponentInParent<CashRegisterWithModules>();
        (built.Order, built.Install) = CreateRegisterModules(locoId, template, built.Register);
        built.CargoTemplate = template.locoPartCargo;

        built.Controller = CreateController(
            loco, tender, template, built.Spawner, built.Order, built.Install, spawnPoints,
            DestinationTrackFor(locoId, stall, built.Home, template.destinationTrackId));
        GarageReplacementApplier.SettleNewDemonstrator(built.Controller);

        Main.Logger.Log($"Built additional demonstrator slot for {locoId}.");
        return true;
    }

    // ---- game data -------------------------------------------------------------------------------

    private static GarageType_v2 GetOrCreateGarageType(
        string locoId, TrainCarLivery loco, TrainCarLivery? tender, GarageType_v2 template)
    {
        if (!_garageCache.TryGetValue(locoId, out var garage) || garage == null)
        {
            garage = ScriptableObject.CreateInstance<GarageType_v2>();
            garage.name = garage.id = GarageIdPrefix + locoId;
            // Unlocked garages are saved as id strings, so this number only has to be unique right now.
            garage.v1 = (Garage)FreeV1([
                .. Globals.G!.Types.garages.Select(g => (int)g.v1),
                .. _garageCache.Values.Where(g => g != null).Select(g => (int)g.v1),
            ]);
            _garageCache[locoId] = garage;
        }

        // Named after the loco rather than the template so unlock messages and the radio read sensibly.
        garage.localizationKey = loco.localizationKey;
        garage.summonPrice = template.summonPrice;
        garage.garageCarLiveries = tender != null ? [loco, tender] : [loco];
        FreeRoamField?.SetValue(garage, FreeRoamField.GetValue(template));
        return garage;
    }

    private static readonly FieldInfo? FreeRoamField =
        AccessTools.Field(typeof(GarageType_v2), "freeRoamAvailability");

    // The comms radio enumerates CarSpawner's own serialized array rather than the Globals garage list,
    // so a summonable garage has to be appended there too.
    private static void AllowSummoning(GarageType_v2 garage)
    {
        var spawner = CarSpawner.Instance;
        if (spawner?.crewVehicleGarages == null) return;
        if (spawner.crewVehicleGarages.Contains(garage)) return;
        spawner.crewVehicleGarages = [.. spawner.crewVehicleGarages, garage];
    }

    private static void RevokeSummoning(GarageType_v2 garage)
    {
        var spawner = CarSpawner.Instance;
        if (spawner?.crewVehicleGarages == null) return;
        spawner.crewVehicleGarages = [.. spawner.crewVehicleGarages.Where(g => g != garage)];
    }

    // The copy of the parts cargo a slot is allowed to rewrite, made on first use.
    //
    // Only the generic-crate fall-through actually rewrites a cargo — renaming and reskinning it in place
    // to match the loco — and that is unsafe to do to the template's, which belongs to another slot. A slot
    // pointed at an existing cargo (chosen or auto-detected) never rewrites anything and so never needs a
    // copy; making one anyway would burn a saved cargo number it has no use for.
    internal static CargoType_v2? OwnCargoFor(LocoRestorationController controller)
    {
        if (!IsSlotGarage(controller.garageSpawner?.garageType)) return null;

        var locoId = controller.locoLivery?.id;
        if (locoId == null || !_slots.TryGetValue(locoId, out var slot)) return null;
        if (slot.Cargo != null) return slot.Cargo;

        slot.Cargo = CloneCargo(slot.CargoTemplate, locoId);
        if (slot.Cargo != null) Globals.G?.Types?.RecalculateCaches();
        return slot.Cargo;
    }

    private static CargoType_v2? CloneCargo(CargoType_v2? template, string locoId)
    {
        if (template == null) return null;
        var clone = UnityEngine.Object.Instantiate(template);
        clone.name = clone.id = CargoIdPrefix + locoId;
        clone.v1 = CargoV1(clone.id);
        Globals.G.Types.cargos.Add(clone);
        return clone;
    }

    // The first value at or above the base that nobody has taken. Fine for anything whose number doesn't
    // have to survive a reload — garage unlocks are saved as id strings, so garages qualify.
    private static int FreeV1(HashSet<int> used)
    {
        int value = SyntheticV1Base;
        while (used.Contains(value)) value++;
        return value;
    }

    // The saved number for this cargo if it still holds, otherwise a fresh one that gets written back into
    // the save. Reallocating is a loss — a save mid-way through a parts delivery is carrying the old value
    // — but it beats the alternative of two cargos sharing a number and taking out RecalculateCaches.
    private static CargoType CargoV1(string cargoId)
    {
        _cargoV1 ??= LoadCargoMapping();

        var used = new HashSet<int>(Globals.G!.Types.cargos.Select(c => (int)c.v1));

        if (_cargoV1.TryGetValue(cargoId, out var saved))
        {
            if (!used.Contains(saved)) return (CargoType)saved;
            Main.Logger.Warning($"Parts cargo {cargoId} can't reuse its saved id {saved}, something else now "
                + "holds it. Any restoration parts already loaded onto a car will be lost.");
        }

        used.UnionWith(_cargoV1.Values);
        int value = FreeV1(used);
        _cargoV1[cargoId] = value;
        SaveCargoMapping();
        return (CargoType)value;
    }

    private static Dictionary<string, int> LoadCargoMapping()
    {
        var map = new Dictionary<string, int>();
        var data = SaveState.Data();
        var ids = data?.GetStringArray(CargoMapIdsKey);
        var values = data?.GetIntArray(CargoMapValuesKey);
        if (ids == null || values == null) return map;

        for (int i = 0; i < Math.Min(ids.Length, values.Length); i++)
        {
            map[ids[i]] = values[i];
        }
        return map;
    }

    private static void SaveCargoMapping()
    {
        var data = SaveState.Data();
        if (data == null || _cargoV1 == null) return;
        data.SetStringArray(CargoMapIdsKey, [.. _cargoV1.Keys]);
        data.SetIntArray(CargoMapValuesKey, [.. _cargoV1.Values]);
    }

    // ---- scene objects ---------------------------------------------------------------------------

    // Where the restored loco is parked once the museum hands it over. Parented to the template slot's own
    // marker so it inherits whatever origin-shift handling that marker has, which also makes the stored
    // offset stable without us tracking world shifts.
    // Puts the slot on its stall, and reports the stall's track name when it is known by name rather than
    // only by position. Parented to the template slot's marker either way so it inherits that marker's
    // origin-shift handling; setting a world position through that parent stays correct under shifts.
    private static GameObject CreateHomeMarker(string locoId, GarageCarSpawner template, out string? stall)
    {
        var anchor = template.locoSpawnPoint.transform;
        var marker = new GameObject($"CustomDemonstrators_{locoId}_Home");
        marker.transform.SetParent(anchor, worldPositionStays: false);
        stall = null;

        // A hand-placed position wins: it exists for stalls the prescribed list doesn't cover.
        var slot = Main.Settings.GetAdditionalSlot(locoId);
        if (slot?.Home is Vector3 offset)
        {
            marker.transform.localPosition = offset;
            marker.transform.localRotation = Quaternion.Euler(0f, slot.HomeYaw, 0f);
            return marker;
        }

        var claimed = MuseumStalls.StallFor(locoId);
        if (MuseumStalls.Midpoint(claimed) is Vector3 midpoint)
        {
            marker.transform.position = midpoint;
            stall = claimed;
            return marker;
        }

        if (!string.IsNullOrEmpty(claimed))
        {
            Main.Logger.Warning($"Additional demonstrator '{locoId}' is assigned stall '{claimed}' but no "
                + "such track exists in this world; falling back to sharing the template slot's stall.");
        }
        else
        {
            // Still spawns — the garage snaps to the nearest track — but two demonstrators end up on one.
            Main.Logger.Warning($"Additional demonstrator '{locoId}' has no stall and will share the "
                + "template slot's.");
        }
        return marker;
    }

    private static GarageCarSpawner CreateGarageSpawner(
        string locoId, GarageType_v2 garage, GameObject home, GarageCarSpawner template)
    {
        var go = new GameObject($"CustomDemonstrators_{locoId}_Garage");
        go.SetActive(false); // configure before Awake registers it against its liveries
        go.transform.SetParent(template.transform.parent, worldPositionStays: false);

        var spawner = go.AddComponent<GarageCarSpawner>();
        spawner.garageType = garage;
        spawner.locoSpawnPoint = home;
        spawner.spawnLocoPlayerSqrDistanceFromTrack = template.spawnLocoPlayerSqrDistanceFromTrack;
        spawner.flipSpawnLoco = template.flipSpawnLoco;

        go.SetActive(true);
        return spawner;
    }

    private static readonly MethodInfo? InitializeData =
        AccessTools.Method(typeof(CashRegisterModule), "InitializeData");

    private static readonly MethodInfo? RegisterUnitsChanged =
        AccessTools.Method(typeof(CashRegisterWithModules), "OnUnitsToBuyChanged");

    // The museum's single register serves every demonstrator; its modules carry no geometry of their own,
    // so a new slot only needs two more components in the register's array.
    private static (GenericThingCashRegisterModule order, GenericThingCashRegisterModule install)
        CreateRegisterModules(string locoId, LocoRestorationController template, CashRegisterWithModules? register)
    {
        var host = register != null ? register.gameObject : template.orderPartsModule.gameObject;

        var order = AddModule(host, template.orderPartsModule);
        var install = AddModule(host, template.installPartsModule);

        if (register != null)
        {
            register.registerModules = [.. register.registerModules, order, install];

            // The register only subscribes to its modules in OnEnable, which has long since run, so wire
            // the new ones up by hand. SetupListeners uses -= on an equivalent delegate when the register
            // is next disabled, so this doesn't leak or double up.
            if (RegisterUnitsChanged != null)
            {
                var handler = (Action)Delegate.CreateDelegate(typeof(Action), register, RegisterUnitsChanged);
                order.OnUnitsToBuyChanged += handler;
                install.OnUnitsToBuyChanged += handler;
            }
        }
        else
        {
            Main.Logger.Warning($"Additional demonstrator '{locoId}' could not find the museum cash register; "
                + "its parts purchases will not appear on the register display.");
        }

        return (order, install);
    }

    private static GenericThingCashRegisterModule AddModule(GameObject host, GenericThingCashRegisterModule template)
    {
        var module = host.AddComponent<GenericThingCashRegisterModule>();
        module.price = template.price;
        module.localizationKey = template.localizationKey;
        // Start() would do this at the end of the frame; do it now so the module is priced correctly if
        // anything reads it first.
        InitializeData?.Invoke(module, null);
        return module;
    }

    // The track the wreck has to be hauled to before its parts can be ordered. Each demonstrator is
    // delivered to the roundhouse stall it will end up living in, so this is derived from the garage
    // position rather than configured separately — and derived with the same closest-track search the
    // garage spawner uses, so the two can't disagree about which stall that is.
    private static string DestinationTrackFor(string locoId, string? stall, GameObject? home, string fallback)
    {
        // A stall claimed by name needs no searching; it is the answer to both halves of the question.
        if (!string.IsNullOrEmpty(stall)) return stall!;

        // A hand-placed slot only knows a position, so find the track under it the same way the garage will.
        if (home == null || Main.Settings.GetAdditionalSlot(locoId)?.Home == null) return fallback;

        var track = RailTrack.GetClosest(home.transform.position).track;
        if (track == null)
        {
            Main.Logger.Warning($"Additional demonstrator '{locoId}' found no track near its garage position; "
                + $"its restoration will be delivered to {fallback} instead.");
            return fallback;
        }

        Main.Logger.Log($"Additional demonstrator '{locoId}' will be restored on track {track.name}.");
        return track.name;
    }

    private static LocoRestorationController CreateController(
        TrainCarLivery loco, TrainCarLivery? tender, LocoRestorationController template,
        GarageCarSpawner spawner, GenericThingCashRegisterModule order, GenericThingCashRegisterModule install,
        LocoRestorationSpawnPoint[] spawnPoints, string destinationTrackId)
    {
        var go = new GameObject($"CustomDemonstrators_{loco.id}_Restoration");
        go.SetActive(false); // Awake registers the controller and Start spawns the wreck; configure first
        go.transform.SetParent(template.transform.parent, worldPositionStays: false);

        var controller = go.AddComponent<LocoRestorationController>();

        // Everything the slot shares with the museum at large — restoration license, destination track,
        // themes, part delivery warehouse — is copied wholesale, then the per-slot pieces are overridden.
        foreach (var field in typeof(LocoRestorationController)
            .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            field.SetValue(controller, field.GetValue(template));
        }

        controller.locoLivery = loco;
        controller.secondCarLivery = tender;
        controller.garageSpawner = spawner;
        controller.orderPartsModule = order;
        controller.installPartsModule = install;
        controller.spawnPoints = spawnPoints;
        controller.destinationTrackId = destinationTrackId;
        // locoPartCargo stays the template's, copied above: a slot only takes a copy of its own once
        // something actually needs to rewrite one. See OwnCargoFor.
        //
        // restorationAreaAnchor/Radius also stay the template's on purpose: that one covers the museum
        // grounds as a whole for the parts drop-off, so every slot legitimately shares it.

        // A wreck can only leave its blocked state if a LocoZoneBlocker exists on the car or can be built
        // from a prefab. Custom locos often ship without one, so fall back to the template's.
        if (controller.locoBlockerPrefab == null)
            controller.locoBlockerPrefab = Blocker(template.locoLivery);
        if (controller.secondCarBlockerPrefab == null)
            controller.secondCarBlockerPrefab = Blocker(template.secondCarLivery) ?? controller.locoBlockerPrefab;

        go.SetActive(true);
        return controller;
    }

    private static GameObject? Blocker(TrainCarLivery? livery) =>
        livery?.prefab?.GetComponentInChildren<LocoZoneBlocker>(includeInactive: true)?.gameObject;

    // ---- template ---------------------------------------------------------------------------------

    // A stable choice matters beyond cosmetics: home positions are stored relative to the template slot's
    // marker, so picking a different template between sessions would move every placed slot.
    internal static LocoRestorationController? Template() =>
        LocoRestorationController.allLocoRestorationControllers
            .Where(c => c != null && c.locoLivery != null && c.garageSpawner?.garageType != null
                && c.garageSpawner.locoSpawnPoint != null && c.orderPartsModule != null
                && c.installPartsModule != null && !IsSlotGarage(c.garageSpawner.garageType))
            .OrderBy(c => c.locoLivery.id, StringComparer.Ordinal)
            .FirstOrDefault();

    // Vanilla ships more wreck anchors than demonstrators and every controller claims a free one via the
    // shared pointUsed flag, so pooling them across all slots is what lets extra wrecks find a spot.
    private static LocoRestorationSpawnPoint[] AllSpawnPoints() =>
        [.. LocoRestorationController.allLocoRestorationControllers
            .Where(c => c != null && c.spawnPoints != null)
            .SelectMany(c => c.spawnPoints)
            .Where(p => p != null)
            .Distinct()];

    // Diagnostics for the debug panel: what a slot actually ended up wired to, as opposed to configured.
    internal static IEnumerable<(string LocoId, GarageType_v2 Garage, LocoRestorationController? Controller,
        CargoType_v2? OwnCargo, GameObject? Home)> LiveSlots() =>
        _slots.Select(kv => (kv.Key, kv.Value.Garage, kv.Value.Controller, kv.Value.Cargo, kv.Value.Home));

    internal static IReadOnlyDictionary<string, int> CargoMapping() => _cargoV1 ?? LoadCargoMapping();

    private static TrainCarLivery? Livery(string id) =>
        Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == id);

    // Mirrors how the rest of the mod resolves a slot: current settings when the save is ours to change,
    // otherwise whatever the save was baked with, so a rebuilt slot keeps the consist it already had.
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
