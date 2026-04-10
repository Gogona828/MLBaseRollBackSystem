using System;
using System.IO;
using System.Text;

public static class InputPacketSerializer
{
    public static byte[] Serialize(InputPacket packet)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8))
        {
            writer.Write(packet.playerId);
            writer.Write(packet.frame);
            writer.Write(packet.inputBits);
            writer.Flush();
            return ms.ToArray();
        }
    }

    public static InputPacket Deserialize(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms, Encoding.UTF8))
        {
            int playerId = reader.ReadInt32();
            int frame = reader.ReadInt32();
            byte inputBits = reader.ReadByte();

            return new InputPacket(playerId, frame, inputBits);
        }
    }
}
