#if DEBUG
using System.Linq;
using System.Reflection;
using DV.InventorySystem;
using DV.Localization;
using DV.LocoRestoration;
using DV.Utils;
using HarmonyLib;
using UnityEngine;

namespace CustomDemonstrators;

// Cheat menu items for convenience when testing
internal static class DebugCheats
{
    internal static void Draw()
    {
        DrawMoney();
        DrawDemonstratorTeleports();
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
