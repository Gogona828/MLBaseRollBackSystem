public class NetworkBattleInputBridge
{
    private readonly NetworkBattleInputRouter router;

    public NetworkBattleInputBridge(NetworkBattleInputRouter router)
    {
        this.router = router;
    }

    public FrameInputState GetP1InputState(int frame)
    {
        byte bits = router.GetP1InputBits(frame);
        return FrameInputConverter.FromBits(bits);
    }

    public FrameInputState GetP2InputState(int frame)
    {
        byte bits = router.GetP2InputBits(frame);
        return FrameInputConverter.FromBits(bits);
    }
}
