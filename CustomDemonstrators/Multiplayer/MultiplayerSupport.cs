using System;
using System.Runtime.CompilerServices;
using UnityModManagerNet;

namespace CustomDemonstrators.Multiplayer;

internal static class MultiplayerSupport
{
    private const string MultiplayerModId = "Multiplayer";

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
            Main.Logger.LogException("Multiplayer support failed to initialize:", ex);
        }
    }

    // Kept out of TryInit so the API's types are only resolved inside its try
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InitHooks() => MultiplayerHooks.Init();

    internal static void SetHostingProbe(Func<bool> probe) => _hostingProbe = probe;
}
