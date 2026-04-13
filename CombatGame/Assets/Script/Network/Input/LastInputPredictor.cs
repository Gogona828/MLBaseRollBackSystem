public class LastInputPredictor : IRemoteInputPredictor
{
    private byte lastConfirmedInputBits = 0;
    private bool hasConfirmedInput = false;

    public byte PredictInputBits(int frame)
    {
        if (hasConfirmedInput)
        {
            return lastConfirmedInputBits;
        }

        return 0;
    }

    public void OnConfirmedInput(int frame, byte inputBits)
    {
        lastConfirmedInputBits = inputBits;
        hasConfirmedInput = true;
    }

    public void Reset()
    {
        lastConfirmedInputBits = 0;
        hasConfirmedInput = false;
    }
}
