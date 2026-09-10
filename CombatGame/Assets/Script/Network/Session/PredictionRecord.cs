public struct PredictionRecord
{
    public int Frame;
    public System.DateTimeOffset PredictionTimestamp;
    public System.DateTimeOffset? ConfirmationTimestamp;
    public Footsies.PredictionTrace Trace;
    public byte PredictedBits;
    public byte ConfirmedBits;
    public PredictionResultState ResultState;

    public PredictionRecord(int frame, byte predictedBits, Footsies.PredictionTrace trace = null)
    {
        Frame = frame;
        PredictionTimestamp = System.DateTimeOffset.UtcNow;
        ConfirmationTimestamp = null;
        Trace = trace;
        PredictedBits = predictedBits;
        ConfirmedBits = 0;
        ResultState = PredictionResultState.Pending;
    }

    public void Confirm(byte confirmedBits)
    {
        ConfirmationTimestamp = System.DateTimeOffset.UtcNow;
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
