using System;
using System.Linq;
using System.Reflection;
using DV.CashRegister;
using DV.LocoRestoration;
using DV.Shops;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;
using CustomDemonstrators.Saves;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

internal static class SlotScene
{
    // Where a slot ended up, so the pieces built after the marker don't have to work it out again.
    internal readonly struct SlotHome(GameObject marker, string? stall, bool placed)
    {
        internal readonly GameObject Marker = marker;

        // The museum stall it claimed, if it got one.
        internal readonly string? Stall = stall;

        // Whether it sits where the player put it rather than in a stall.
        internal readonly bool Placed = placed;
    }

    internal static SlotHome CreateHome(string locoId, GarageCarSpawner template)
    {
        var anchor = template.locoSpawnPoint.transform;
        var marker = new GameObject($"CustomDemonstrators_{locoId}_Home");
        marker.transform.SetParent(anchor, worldPositionStays: false);

        // Settings only speak for a save they were baked into; otherwise the save's own record is all we have.
        var placement = Placement(locoId);
        if (placement is (Vector3 offset, float yaw))
        {
            marker.transform.localPosition = offset;
            marker.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return new SlotHome(marker, null, placed: true);
        }

        var claimed = MuseumStalls.StallFor(locoId);
        if (MuseumStalls.Midpoint(claimed) is Vector3 midpoint)
        {
            marker.transform.position = midpoint;
            return new SlotHome(marker, claimed, placed: false);
        }

        if (!string.IsNullOrEmpty(claimed))
        {
            Main.Logger.Warning($"Additional demonstrator '{locoId}' is assigned stall '{claimed}' but no "
                + "such track exists in this world; falling back to sharing the template slot's stall.");
        }
        else
        {
            // Things will get cozy if you clear both locomotives but it should otherwise work *okay*
            Main.Logger.Warning($"Additional demonstrator '{locoId}' has no stall and will share the "
                + "template slot's.");
        }
        return new SlotHome(marker, null, placed: false);
    }

    // The placement to build this slot at, taken from the settings while they're the ones this save was baked
    // from and copied into the save as we go, so a later load can rebuild it without them.
    private static (Vector3 Offset, float Yaw)? Placement(string locoId)
    {
        if (!SaveGuard.AllowDemonstratorChanges()) return MuseumStalls.PlacementFor(locoId);

        var slot = Main.Settings.GetAdditionalSlot(locoId);
        var placement = slot?.Home is Vector3 offset ? (offset, slot.HomeYaw) : ((Vector3, float)?)null;
        MuseumStalls.RecordPlacement(locoId, placement);
        return placement;
    }

    internal static GarageCarSpawner CreateSpawner(
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

    private static readonly MethodInfo? SetUnitsToBuy =
        AccessTools.Method(typeof(CashRegisterModule), "SetUnitsToBuy", [typeof(float)]);

    internal static CashRegisterWithModules? FindRegister(CashRegisterModule module) =>
        Resources.FindObjectsOfTypeAll<CashRegisterWithModules>()
            .FirstOrDefault(r => r != null
                && r.gameObject.scene.IsValid()
                && r.registerModules != null
                && r.registerModules.Contains(module));

    internal static (GenericThingCashRegisterModule order, GenericThingCashRegisterModule install)
        CreateRegisterModules(string locoId, LocoRestorationController template, CashRegisterWithModules? register)
    {
        var host = register != null ? register.gameObject : template.orderPartsModule.gameObject;

        var order = AddModule(host, template.orderPartsModule);
        var install = AddModule(host, template.installPartsModule);

        if (register != null)
        {
            register.registerModules = [.. register.registerModules, order, install];

            if (register.isActiveAndEnabled && RegisterUnitsChanged != null)
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
        InitializeData?.Invoke(module, null);
        return module;
    }

    internal static void DetachRegisterModules(
        CashRegisterWithModules? register, params GenericThingCashRegisterModule?[] modules)
    {
        if (register != null && register.registerModules != null)
        {
            register.registerModules =
                [.. register.registerModules.Where(m => m != null && !modules.Contains(m))];
        }

        foreach (var module in modules)
        {
            if (module == null) continue;
            // Zero the basket first so a pending purchase doesn't linger in the register's total.
            SetUnitsToBuy?.Invoke(module, [0f]);
            UnityEngine.Object.Destroy(module);
        }
    }

    internal static string? TrackNameAt(Vector3 position) => RailTrack.GetClosest(position).track?.name;

    internal static string DestinationTrackFor(string locoId, SlotHome home, string fallback)
    {
        // A stall claimed by name needs no searching
        if (!string.IsNullOrEmpty(home.Stall)) return home.Stall!;

        // A hand-placed slot has to find the right spot
        if (!home.Placed || home.Marker == null) return fallback;

        var track = TrackNameAt(home.Marker.transform.position);
        if (track == null)
        {
            Main.Logger.Warning($"Additional demonstrator '{locoId}' found no track near its garage position; "
                + $"its restoration will be delivered to {fallback} instead.");
            return fallback;
        }

        Main.Logger.Log($"Additional demonstrator '{locoId}' will be restored on track {track}.");
        return track;
    }

    internal static LocoRestorationController CreateController(
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
}
