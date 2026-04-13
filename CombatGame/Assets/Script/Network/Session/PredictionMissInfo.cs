public struct PredictionMissInfo
{
    public int Frame;
    public byte PredictedBits;
    public byte ConfirmedBits;
    public bool IsValid;

    public PredictionMissInfo(int frame, byte predictedBits, byte confirmedBits)
    {
        Frame = frame;
        PredictedBits = predictedBits;
        ConfirmedBits = confirmedBits;
        IsValid = true;
    }

    public static PredictionMissInfo Invalid()
    {
        return new PredictionMissInfo
        {
            Frame = -1,
            PredictedBits = 0,
            ConfirmedBits = 0,
            IsValid = false
        };
    }

    public override string ToString()
    {
        return $"frame={Frame}, predicted={PredictedBits}, confirmed={ConfirmedBits}, valid={IsValid}";
    }
}
