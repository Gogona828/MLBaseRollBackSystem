public struct DelayedInputEntry
{
    public InputPacket Packet;
    public int RemainingDelayFrames;

    public DelayedInputEntry(InputPacket packet, int remainingDelayFrames)
    {
        Packet = packet;
        RemainingDelayFrames = remainingDelayFrames;
    }
}
