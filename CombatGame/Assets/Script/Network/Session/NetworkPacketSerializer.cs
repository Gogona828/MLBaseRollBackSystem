using System.IO;
using System.Text;

public static class NetworkPacketSerializer
{
    public static byte[] Serialize(NetworkPacket packet)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8))
        {
            writer.Write((byte)packet.packetType);
            writer.Write(packet.playerId);
            writer.Write(packet.frame);
            writer.Write(packet.inputBits);
            writer.Write(packet.startDelayFrames);
            writer.Flush();
            return ms.ToArray();
        }
    }

    public static NetworkPacket Deserialize(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms, Encoding.UTF8))
        {
            NetworkPacketType packetType = (NetworkPacketType)reader.ReadByte();
            int playerId = reader.ReadInt32();
            int frame = reader.ReadInt32();
            byte inputBits = reader.ReadByte();
            int startDelayFrames = reader.ReadInt32();

            return new NetworkPacket(packetType, playerId, frame, inputBits, startDelayFrames);
        }
    }
}
