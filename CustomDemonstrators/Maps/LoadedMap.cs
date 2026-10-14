using System;
using System.Reflection;
using UnityModManagerNet;

namespace CustomDemonstrators.Maps;

internal static class LoadedMap
{
    // Mapify's own name for the vanilla map/the one we use when Mapify isn't installed
    internal const string DefaultName = "Default";

    private static Type? _maps;
    private static bool _resolved;

    internal static bool IsCustom => !DefaultName.Equals(Name, StringComparison.Ordinal);

    internal static string Name
    {
        get
        {
            var maps = Maps();
            if (maps == null || Static(maps, "IsDefaultMap") is not false) return DefaultName;

            // LoadedMap throws while no map is loaded, which Static reports as null.
            var info = Static(maps, "LoadedMap");
            return info?.GetType().GetField("name")?.GetValue(info) as string ?? DefaultName;
        }
    }

    // A file shipped in the loaded custom map's own folder, or null on the game's map.
    internal static string? MapAsset(string fileName)
    {
        var maps = Maps();
        if (maps == null || !IsCustom) return null;

        try
        {
            return maps.GetMethod("GetMapAsset", BindingFlags.Public | BindingFlags.Static)?
                .Invoke(null, [fileName, null]) as string;
        }
        catch (TargetInvocationException e)
        {
            Main.Logger.Warning($"Mapify couldn't say where the loaded map's files are: {e.InnerException?.Message}");
            return null;
        }
    }

    private static Type? Maps()
    {
        if (_resolved) return _maps;
        _resolved = true;

        var mod = UnityModManager.FindMod("Mapify");
        if (mod == null || !mod.Active || mod.Assembly == null) return null;

        _maps = mod.Assembly.GetType("Mapify.Map.Maps");
        if (_maps == null)
            Main.Logger.Warning("Mapify is installed but its map registry wasn't found");
        return _maps;
    }

    private static object? Static(Type type, string property)
    {
        try
        {
            return type.GetProperty(property, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }
}
