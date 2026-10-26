using System;
using System.IO;
using System.Reflection;
using UnityModManagerNet;

namespace CustomDemonstrators.Multiplayer;

internal static class MultiplayerSupport
{
    private const string MultiplayerModId = "Multiplayer";
    private const string HooksAssembly = "CustomDemonstrators.Multiplayer.dll";
    private const string HooksType = "CustomDemonstrators.Multiplayer.MultiplayerHooks";

    private static Func<bool>? _hostingProbe;

    internal static bool IsJoined => JoinedGame.IsActive;

    internal static bool IsHosting => _hostingProbe?.Invoke() == true;

    internal static void TryInit()
    {
        if (UnityModManager.FindMod(MultiplayerModId) is not { Active: true }) return;

        try
        {
            if (InitHooks())
            {
                Main.Logger.Log("Multiplayer support enabled.");
            }
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Multiplayer support failed to initialize:",
                ex is TargetInvocationException { InnerException: { } inner } ? inner : ex);
        }
    }

    private static bool InitHooks()
    {
        var path = Path.Combine(Main.ModPath, HooksAssembly);
        if (!File.Exists(path))
        {
            Main.Logger.Warning($"{HooksAssembly} is missing, so multiplayer support is off.");
            return false;
        }

        var init = Assembly.LoadFrom(path).GetType(HooksType)?.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
        if (init == null)
        {
            Main.Logger.Warning($"{HooksAssembly} doesn't match this version, so multiplayer support is off.");
            return false;
        }

        _hostingProbe = init.Invoke(null,
        [
            (Action<string>)Main.Logger.Log,
            (Action<string, Exception>)Main.Logger.LogException,
            (Func<byte[]>)(() => HostRecord.Capture().ToBytes()),
            (Action)JoinedGame.Begin,
            (Action)JoinedGame.End,
            (Action<byte[]>)HostRecordReceived,
            (Action<string>)JoinedGame.Unreadable,
        ]) as Func<bool>;

        return _hostingProbe != null;
    }

    private static void HostRecordReceived(byte[] data)
    {
        HostRecord record;
        try
        {
            record = HostRecord.FromBytes(data);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Couldn't read the host's custom demonstrators state:", ex);
            JoinedGame.Unreadable("it couldn't be read");
            return;
        }

        JoinedGame.Receive(record);
    }
}
