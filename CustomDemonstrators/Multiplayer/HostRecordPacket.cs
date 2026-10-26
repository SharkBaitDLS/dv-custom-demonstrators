using System.IO;
using MPAPI.Interfaces.Packets;

namespace CustomDemonstrators.Multiplayer;

public class HostRecordPacket : ISerializablePacket
{
    public const int CurrentProtocol = 1;

    public int Protocol = CurrentProtocol;
    public byte[] Data = [];

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(Protocol);
        writer.Write(Data.Length);
        writer.Write(Data);
    }

    public void Deserialize(BinaryReader reader)
    {
        Protocol = reader.ReadInt32();
        if (Protocol != CurrentProtocol) return;

        Data = reader.ReadBytes(reader.ReadInt32());
    }
}
