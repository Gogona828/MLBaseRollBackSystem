public class NetworkBattleInputRouter
{
    private readonly IFrameInputSource p1InputSource;
    private readonly IFrameInputSource p2InputSource;

    public NetworkBattleInputRouter(IFrameInputSource p1InputSource, IFrameInputSource p2InputSource)
    {
        this.p1InputSource = p1InputSource;
        this.p2InputSource = p2InputSource;
    }

    public byte GetP1InputBits(int frame)
    {
        return p1InputSource.GetInputBits(frame);
    }

    public byte GetP2InputBits(int frame)
    {
        return p2InputSource.GetInputBits(frame);
    }
}
