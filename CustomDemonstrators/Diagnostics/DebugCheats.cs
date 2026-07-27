#if DEBUG
using System.Linq;
using System.Reflection;
using System.Text;
using DV.InventorySystem;
using DV.Localization;
using DV.LocoRestoration;
using DV.Utils;
using HarmonyLib;
using UnityEngine;
using CustomDemonstrators.Saves;
using CustomDemonstrators.World;

namespace CustomDemonstrators.Diagnostics;

// Cheat menu items for convenience when testing
internal static class DebugCheats
{
    internal static void Draw()
    {
        DrawMoney();
        DrawDemonstratorTeleports();
        DrawAdditionalSlots();
        DrawReports();
    }

    private static void DrawReports()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Log slot + save state", GUILayout.Width(200)))
            LogSlotState();
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
    }

    // What the slots this mod added actually resolved to at runtime, which is mostly invisible from in
    // game: which anchor the wreck took, whether the parts cargo is a copy we own or an existing one, and
    // whether the save is carrying the state we think it is.
    private static void DrawAdditionalSlots()
    {
        var slots = DemonstratorSlots.LiveSlots().ToList();
        if (slots.Count == 0) return;

        GUILayout.Label("Additional demonstrators (debug):", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
        GUILayout.BeginVertical(GUI.skin.box);

        foreach (var (locoId, garage, controller, ownCargo, home) in slots)
        {
            GUILayout.Label($"{locoId} → garage {garage.id} (v1 {(int)garage.v1})");
            if (controller == null)
            {
                GUILayout.Label("    no controller!");
                continue;
            }

            var wreck = _restorationLocoField?.GetValue(controller) as TrainCar;
            GUILayout.Label($"    {controller.State}, wreck: {(wreck != null ? wreck.ID : "none")}"
                + $", anchor: {AnchorOf(controller, wreck)}");
            GUILayout.Label($"    parts: {controller.locoPartCargo?.id ?? "none"}"
                + $" ({(ownCargo != null ? "own copy" : "existing cargo, no copy made")})"
                + $", order ${controller.orderPartsModule?.price ?? -1f:N0}");
            GUILayout.Label($"    stall: {controller.destinationTrackId}"
                + $" @ {(home != null ? home.transform.position.ToString("F1") : "none")}");
        }

        GUILayout.EndVertical();
        GUILayout.Space(6);
    }

    private static string AnchorOf(LocoRestorationController controller, TrainCar? wreck)
    {
        if (wreck == null || controller.spawnPoints == null) return "-";
        var nearest = controller.spawnPoints
            .Where(p => p != null)
            .OrderBy(p => (p.transform.position - wreck.transform.position).sqrMagnitude)
            .FirstOrDefault();
        if (nearest == null) return "-";
        float distance = (nearest.transform.position - wreck.transform.position).magnitude;
        return $"{nearest.name} ({distance:F0}m)";
    }

    // Dumps to the log rather than the panel: this is the stuff worth pasting into a bug report, and the
    // save keys are the only way to tell a value that round-tripped from one that merely looks right.
    private static void LogSlotState()
    {
        var sb = new StringBuilder("CustomDemonstrators slot state\n");

        var anchors = LocoRestorationController.allLocoRestorationControllers
            .Where(c => c != null && c.spawnPoints != null)
            .SelectMany(c => c.spawnPoints).Where(p => p != null).Distinct().ToList();
        sb.AppendLine($"  wreck anchors: {anchors.Count(p => p.pointUsed)}/{anchors.Count} used");

        foreach (var controller in LocoRestorationController.allLocoRestorationControllers)
        {
            if (controller == null) continue;
            bool ours = SlotTypes.IsSlotGarage(controller.garageSpawner?.garageType);
            // Vanilla gives every demonstrator its own stall, so two slots sharing a track here means a
            // placement was missed rather than that the game works that way.
            sb.AppendLine($"  {(ours ? "[added]" : "[vanilla]")} {controller.SaveID}: {controller.State}"
                + $", stall {controller.destinationTrackId}"
                + $", parts {controller.locoPartCargo?.id}"
                + $", tender {controller.secondCarLivery?.id ?? "none"}");
        }

        foreach (var kv in SlotTypes.CargoMapping())
        {
            sb.AppendLine($"  cargo mapping: {kv.Key} = {kv.Value}");
        }

        var data = SaveState.Data();
        if (data == null)
        {
            sb.AppendLine("  no save data loaded");
        }
        else
        {
            // Proves the round trip: anything here came back off disk, not just out of our own memory.
            var restoration = data.GetJObject("Restoration_Locos");
            sb.AppendLine($"  save Restoration_Locos: "
                + (restoration == null ? "missing" : string.Join(", ", restoration.Properties().Select(p => p.Name))));
            sb.AppendLine($"  save Garages: {string.Join(", ", data.GetStringArray("Garages") ?? [])}");
            sb.AppendLine($"  save cargo ids: {string.Join(", ", data.GetStringArray("CustomDemonstrators_CargoIds") ?? [])}");
            sb.AppendLine($"  save cargo values: {string.Join(", ", (data.GetIntArray("CustomDemonstrators_CargoValues") ?? []).Select(v => v.ToString()))}");
            sb.AppendLine($"  save demonstrator fingerprint: {data.GetString("CustomDemonstrators_DemonstratorFingerprint")}");
            sb.AppendLine($"  live demonstrator fingerprint: {SaveGuard.DemonstratorFingerprint()}");
        }

        Main.Logger.Log(sb.ToString());
    }

    private static void DrawMoney()
    {
        var inventory = SingletonBehaviour<Inventory>.Instance;
        if (inventory == null) return;

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Money: ${inventory.PlayerMoney:N0}", GUILayout.Width(220));
        if (GUILayout.Button("+ $1,000,000", GUILayout.Width(120)))
            inventory.AddMoney(1_000_000);
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
    }

    private static void DrawDemonstratorTeleports()
    {
        if (PlayerManager.PlayerTransform == null) return;
        var controllers = LocoRestorationController.allLocoRestorationControllers;
        if (controllers == null || controllers.Count == 0) return;

        GUILayout.Label("Demonstrators (teleport):", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
        GUILayout.BeginVertical(GUI.skin.box);
        foreach (var c in controllers)
        {
            if (c == null) continue;
            string locoName = Loc(c.locoLivery?.localizationKey, c.locoLivery?.id ?? "?");
            var target = WreckTarget(c);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{locoName}  ({c.State})", GUILayout.Width(320));
            if (target != null)
            {
                if (GUILayout.Button("Teleport", GUILayout.Width(100)))
                    PlayerManager.TeleportPlayer(target.position, target.rotation, null, useRotation: true);
            }
            else
            {
                GUILayout.Label("(no location)", GUILayout.Width(100));
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
        GUILayout.Space(6);
    }

    private static readonly FieldInfo? _restorationLocoField = AccessTools.Field(typeof(LocoRestorationController), "loco");

    private static Transform? WreckTarget(LocoRestorationController c)
    {
        if (_restorationLocoField?.GetValue(c) is TrainCar loco && loco != null)
            return loco.transform;

        // In case of a bug where we cause the wreck not to spawn, take us to where it *should* be
        if (c.spawnPoints != null)
        {
            var sp = c.spawnPoints.FirstOrDefault(p => p != null && p.pointUsed)
                     ?? c.spawnPoints.FirstOrDefault(p => p != null);
            if (sp != null) return sp.transform;
        }
        return null;
    }

    private static string Loc(string? key, string fallback) =>
        string.IsNullOrEmpty(key) ? fallback : LocalizationAPI.L(key);
}
#endif
