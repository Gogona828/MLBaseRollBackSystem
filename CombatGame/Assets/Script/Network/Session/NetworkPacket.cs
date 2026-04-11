using System;

[Serializable]
public struct NetworkPacket
{
    public NetworkPacketType packetType;
    public int playerId;
    public int frame;
    public byte inputBits;
    public int startDelayFrames;

    public NetworkPacket(
        NetworkPacketType packetType,
        int playerId,
        int frame,
        byte inputBits,
        int startDelayFrames = 0)
    {
        this.packetType = packetType;
        this.playerId = playerId;
        this.frame = frame;
        this.inputBits = inputBits;
        this.startDelayFrames = startDelayFrames;
    }

    public override string ToString()
    {
        return $"type={packetType}, playerId={playerId}, frame={frame}, inputBits={inputBits}, startDelayFrames={startDelayFrames}";
    }
}
