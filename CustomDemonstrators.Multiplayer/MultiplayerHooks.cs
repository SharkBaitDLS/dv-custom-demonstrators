using System;
using MPAPI;
using MPAPI.Interfaces;

namespace CustomDemonstrators.Multiplayer;

public static class MultiplayerHooks
{
    private static Action<string> _log = _ => { };
    private static Action<string, Exception> _logException = (_, _) => { };
    private static Func<byte[]> _captureHostRecord = () => [];
    private static Action _joining = () => { };
    private static Action<byte[]> _hostRecordReceived = _ => { };
    private static Action<string> _unreadable = _ => { };

    public static Func<bool>? Init(
        Action<string> log,
        Action<string, Exception> logException,
        Func<byte[]> captureHostRecord,
        Action joining,
        Action left,
        Action<byte[]> hostRecordReceived,
        Action<string> unreadable)
    {
        if (!MultiplayerAPI.IsMultiplayerLoaded)
        {
            return null;
        }

        _log = log;
        _logException = logException;
        _captureHostRecord = captureHostRecord;
        _joining = joining;
        _hostRecordReceived = hostRecordReceived;
        _unreadable = unreadable;

        MultiplayerAPI.ServerStarted += OnServerStarted;
        MultiplayerAPI.ClientStarted += OnClientStarted;
        MultiplayerAPI.ClientStopped += left;
        return () => MultiplayerAPI.Instance is { IsHost: true, IsSinglePlayer: false };
    }

    private static void OnServerStarted(IServer server) =>
        server.OnPlayerConnected += player => SendWhenLoaded(server, player);

    private static void SendWhenLoaded(IServer server, IPlayer player)
    {
        if (player.IsHost) return;

        if (WorldStreamingInit.IsLoaded)
        {
            Send(server, player);
            return;
        }

        // Someone can get in while the host is still loading, before there is a world to describe
        void OnLoaded()
        {
            WorldStreamingInit.LoadingFinished -= OnLoaded;
            Send(server, player);
        }
        WorldStreamingInit.LoadingFinished += OnLoaded;
    }

    private static void Send(IServer server, IPlayer player)
    {
        try
        {
            var data = _captureHostRecord();
            server.SendSerializablePacketToPlayer(new HostRecordPacket { Data = data }, player, reliable: true);
            _log($"Sent this game's demonstrator and garage setup to {player.DisplayName} ({data.Length} bytes).");
        }
        catch (Exception ex)
        {
            _logException($"Failed to send this game's custom demonstrators state to {player.DisplayName}:", ex);
        }
    }

    private static void OnClientStarted(IClient client)
    {
        if (MultiplayerAPI.Instance?.IsHost == true) return;

        client.RegisterSerializablePacket<HostRecordPacket>(OnHostRecordPacket);
        _joining();
    }

    private static void OnHostRecordPacket(HostRecordPacket packet)
    {
        if (packet.Protocol != HostRecordPacket.CurrentProtocol)
        {
            _unreadable("it was sent by a different version of Custom Demonstrators");
            return;
        }

        _hostRecordReceived(packet.Data);
    }
}
