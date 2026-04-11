using System;

[Serializable]
public struct NetworkPacket
{
    public NetworkPacketType packetType;
    public int playerId;
    public int frame;
    public byte inputBits;

    public NetworkPacket(NetworkPacketType packetType, int playerId, int frame, byte inputBits)
    {
        this.packetType = packetType;
        this.playerId = playerId;
        this.frame = frame;
        this.inputBits = inputBits;
    }

    public override string ToString()
    {
        return $"type={packetType}, playerId={playerId}, frame={frame}, inputBits={inputBits}";
    }
}
