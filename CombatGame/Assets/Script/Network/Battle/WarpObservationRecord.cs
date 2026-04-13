using System;

[Serializable]
public struct WarpObservationRecord
{
    public int Frame;
    public float PredictedPosX;
    public float ConfirmedPosX;
    public float DistanceError;
    public bool Detected;

    public WarpObservationRecord(
        int frame,
        float predictedPosX,
        float confirmedPosX,
        float distanceError,
        bool detected)
    {
        Frame = frame;
        PredictedPosX = predictedPosX;
        ConfirmedPosX = confirmedPosX;
        DistanceError = distanceError;
        Detected = detected;
    }

    public override string ToString()
    {
        return $"frame={Frame}, predictedPosX={PredictedPosX}, confirmedPosX={ConfirmedPosX}, distanceError={DistanceError}, detected={Detected}";
    }
}
