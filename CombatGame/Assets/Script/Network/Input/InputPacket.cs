using System;

[Serializable]
public struct InputPacket
{
    public int playerId;
    public int frame;
    public byte inputBits;

    public InputPacket(int playerId, int frame, byte inputBits)
    {
        this.playerId = playerId;
        this.frame = frame;
        this.inputBits = inputBits;
    }

    public override string ToString()
    {
        return $"playerId={playerId}, frame={frame}, inputBits={inputBits}";
    }
}