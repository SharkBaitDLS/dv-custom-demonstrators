using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;
using CustomDemonstrators.Config;

namespace CustomDemonstrators;

public static class Main
{
    internal static Settings Settings { get; private set; } = null!;

    internal static UnityModManager.ModEntry.ModLogger Logger { get; private set; } = null!;

    internal static string ModPath { get; private set; } = null!;

    private static bool Load(UnityModManager.ModEntry modEntry)
    {
        Logger = modEntry.Logger;
        ModPath = modEntry.Path;
        Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

        Harmony? harmony = null;
        try
        {
            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
        catch (Exception ex)
        {
            Logger.LogException($"Failed to load {modEntry.Info.DisplayName}:", ex);
            harmony?.UnpatchAll(modEntry.Info.Id);
            return false;
        }

        modEntry.OnGUI = SettingsGUI.OnGUI;
        modEntry.OnSaveGUI = entry => Settings.Save(entry);
        return true;
    }
}
