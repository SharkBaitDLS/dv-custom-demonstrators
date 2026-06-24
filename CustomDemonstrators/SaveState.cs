using System.Reflection;
using DV.Utils;

namespace CustomDemonstrators;

// Shared, read-only access to the currently loaded save.
internal static class SaveState
{
    internal static SaveGameData? Data() => Manager()?.data;

    internal static bool IsNewSession => Manager()?.IsNewSession == true;

    // Read the singleton without triggering SingletonBehaviour's auto-create
    private static SaveGameManager? Manager() =>
        typeof(SingletonBehaviour<SaveGameManager>)
            .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)?
            .GetValue(null) as SaveGameManager;
}
