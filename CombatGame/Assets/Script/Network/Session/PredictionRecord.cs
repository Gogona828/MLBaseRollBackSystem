public struct PredictionRecord
{
    public int Frame;
    public byte PredictedBits;
    public byte ConfirmedBits;
    public PredictionResultState ResultState;

    public PredictionRecord(int frame, byte predictedBits)
    {
        Frame = frame;
        PredictedBits = predictedBits;
        ConfirmedBits = 0;
        ResultState = PredictionResultState.Pending;
    }

    public void Confirm(byte confirmedBits)
    {
        ConfirmedBits = confirmedBits;
        ResultState = (PredictedBits == confirmedBits)
            ? PredictionResultState.Hit
            : PredictionResultState.Miss;
    }

    public override string ToString()
    {
        return $"frame={Frame}, predicted={PredictedBits}, confirmed={ConfirmedBits}, result={ResultState}";
    }
}
