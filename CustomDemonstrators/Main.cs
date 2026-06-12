using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace CustomDemonstrators;

public static class Main
{
    internal static Settings Settings { get; private set; } = null!;

    private static bool Load(UnityModManager.ModEntry modEntry)
    {
        Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

        Harmony? harmony = null;
        try
        {
            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            // Other plugin startup logic
        }
        catch (Exception ex)
        {
            modEntry.Logger.LogException($"Failed to load {modEntry.Info.DisplayName}:", ex);
            harmony?.UnpatchAll(modEntry.Info.Id);
            return false;
        }

        modEntry.OnGUI = SettingsGUI.OnGUI;
        modEntry.OnSaveGUI = entry => Settings.Save(entry);
        return true;
    }
}
