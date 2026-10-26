using System.Collections;
using DV.Utils;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using CustomDemonstrators.Config;
using CustomDemonstrators.Saves;

namespace CustomDemonstrators.Multiplayer;

internal static class JoinedGame
{
    private const float WaitSeconds = 10f;

    internal static bool IsActive { get; private set; }

    private static int _connection;

    private static bool _settled;
    private static bool _holdingLoad;
    private static HostRecord? _record;

    private static bool _borrowing;

    internal static void Begin()
    {
        _connection++;
        IsActive = true;
        _settled = false;
        _holdingLoad = false;
        _record = null;
    }

    internal static void End()
    {
        if (!IsActive) return;

        IsActive = false;
        _holdingLoad = false;
        _record = null;
        RestoreOwnSettings();
    }

    internal static void Receive(HostRecord record)
    {
        if (!IsActive) return;

        if (_settled)
        {
            Main.Logger.Warning("The host's demonstrator and garage setup arrived after the world started loading, "
                + "too late to build it.");
            return;
        }

        _record = record;
        Main.Logger.Log("Received the host's demonstrator and garage setup.");
        Settle();
    }

    internal static void Unreadable(string why)
    {
        if (!IsActive || _settled) return;

        Main.Logger.Warning($"The host sent its demonstrator and garage setup, but {why}.");
        Settle();
    }

    private static void Settle()
    {
        _settled = true;
        if (!_holdingLoad) return;

        _holdingLoad = false;
        SceneSwitcher.SwitchToScene(DVScenes.Game);
    }

    // Starting the world is what the Multiplayer mod does with the host's save
    [HarmonyPatch(typeof(SceneSwitcher), nameof(SceneSwitcher.SwitchToScene), typeof(DVScenes))]
    private static class SceneSwitcher_SwitchToScene_Patch
    {
        private static bool Prefix(DVScenes enumVal) => !HoldLoad(enumVal);
    }

    private static bool HoldLoad(DVScenes scene)
    {
        if (!IsActive || _settled || scene != DVScenes.Game) return false;

        if (!_holdingLoad)
        {
            _holdingLoad = true;
            SingletonBehaviour<CoroutineManager>.Instance.Run(GiveUpWaiting(_connection));
        }
        return true;
    }

    private static IEnumerator GiveUpWaiting(int forConnection)
    {
        float started = Time.realtimeSinceStartup;
        while (IsActive && forConnection == _connection && !_settled)
        {
            if (Time.realtimeSinceStartup - started >= WaitSeconds)
            {
                Main.Logger.Warning("The host didn't send its demonstrator and garage setup.");
                Settle();
                yield break;
            }
            yield return null;
        }
    }

    internal static void OnSaveFound()
    {
        if (!IsActive) return;

        if (_record == null)
        {
            Main.Logger.Warning("The host's demonstrators and garages may not appear "
                + "as their state could not be determined.");
            Borrow(new Settings());
            return;
        }

        Borrow(_record.Settings);

        var data = SaveState.Data()?.GetJsonObject();
        if (data != null)
        {
            foreach (var property in _record.Save.Properties())
                data[property.Name] = property.Value.DeepClone();
        }

        GarageOwnership.UseHostsOwnedCarTypes(_record.OwnedCarTypes);

        Main.Logger.Log("Building the host's demonstrators and garages from their save data");
    }

    private static void Borrow(Settings host)
    {
        if (!_borrowing) Main.Settings.Save(Main.ModEntry);

        _borrowing = true;
        Main.Settings = host;
    }

    private static void RestoreOwnSettings()
    {
        if (!_borrowing) return;

        _borrowing = false;
        Main.Settings = UnityModManager.ModSettings.Load<Settings>(Main.ModEntry);
    }
}
