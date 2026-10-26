using System;
using MPAPI;
using MPAPI.Interfaces;

namespace CustomDemonstrators.Multiplayer;

internal static class MultiplayerHooks
{
    internal static bool Init()
    {
        if (!MultiplayerAPI.IsMultiplayerLoaded)
        {
            return false;
        }

        MultiplayerAPI.ServerStarted += OnServerStarted;
        MultiplayerAPI.ClientStarted += OnClientStarted;
        MultiplayerAPI.ClientStopped += JoinedGame.End;
        MultiplayerSupport.SetHostingProbe(() => MultiplayerAPI.Instance is { IsHost: true, IsSinglePlayer: false });
        return true;
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
            var data = HostRecord.Capture().ToBytes();
            server.SendSerializablePacketToPlayer(new HostRecordPacket { Data = data }, player, reliable: true);
            Main.Logger.Log($"Sent this game's demonstrator and garage setup to {player.DisplayName} ({data.Length} bytes).");
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to send this game's custom demonstrators state to {player.DisplayName}:", ex);
        }
    }

    private static void OnClientStarted(IClient client)
    {
        if (MultiplayerAPI.Instance?.IsHost == true) return;

        client.RegisterSerializablePacket<HostRecordPacket>(OnHostRecordPacket);
        JoinedGame.Begin();
    }

    private static void OnHostRecordPacket(HostRecordPacket packet)
    {
        if (packet.Protocol != HostRecordPacket.CurrentProtocol)
        {
            JoinedGame.Unreadable("it was sent by a different version of Custom Demonstrators");
            return;
        }

        HostRecord record;
        try
        {
            record = HostRecord.FromBytes(packet.Data);
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
