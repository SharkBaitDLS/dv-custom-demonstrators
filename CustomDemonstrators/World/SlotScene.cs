using System;
using System.Linq;
using System.Reflection;
using DV.CashRegister;
using DV.LocoRestoration;
using DV.Shops;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

internal static class SlotScene
{
    internal static GameObject CreateHome(string locoId, GarageCarSpawner template, out string? stall)
    {
        var anchor = template.locoSpawnPoint.transform;
        var marker = new GameObject($"CustomDemonstrators_{locoId}_Home");
        marker.transform.SetParent(anchor, worldPositionStays: false);
        stall = null;

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
            // Things will get cozy if you clear both locomotives but it should otherwise work *okay*
            Main.Logger.Warning($"Additional demonstrator '{locoId}' has no stall and will share the "
                + "template slot's.");
        }
        return marker;
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

    internal static string DestinationTrackFor(string locoId, string? stall, GameObject? home, string fallback)
    {
        // A stall claimed by name needs no searching
        if (!string.IsNullOrEmpty(stall)) return stall!;

        // A hand-placed slot has to find the right spot
        if (home == null || Main.Settings.GetAdditionalSlot(locoId)?.Home == null) return fallback;

        var track = TrackNameAt(home.transform.position);
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
