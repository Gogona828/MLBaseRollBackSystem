using UnityEngine;

public class PredictedRemoteFrameInputSource : IFrameInputSource
{
    private readonly RemoteInputBuffer remoteInputBuffer;
    private readonly IRemoteInputPredictor predictor;
    private readonly PredictionMismatchDetector mismatchDetector;

    public bool LastReadUsedPrediction { get; private set; }
    public int LastReadFrame { get; private set; }
    public byte LastReadBits { get; private set; }

    public PredictedRemoteFrameInputSource(
        RemoteInputBuffer remoteInputBuffer,
        IRemoteInputPredictor predictor,
        PredictionMismatchDetector mismatchDetector)
    {
        this.remoteInputBuffer = remoteInputBuffer;
        this.predictor = predictor;
        this.mismatchDetector = mismatchDetector;
    }

    public byte GetInputBits(int frame)
    {
        LastReadFrame = frame;

        if (remoteInputBuffer != null && remoteInputBuffer.TryGetInput(frame, out byte confirmedBits))
        {
            predictor?.OnConfirmedInput(frame, confirmedBits);
            mismatchDetector?.ConfirmIfPredicted(frame, confirmedBits);

            LastReadUsedPrediction = false;
            LastReadBits = confirmedBits;

            FileLogger.WriteLine(
                $"[PredictedRemoteFrameInputSource] frame={frame} confirmed bits={confirmedBits}");

            return confirmedBits;
        }

        byte predictedBits = predictor != null ? predictor.PredictInputBits(frame) : (byte)0;

        mismatchDetector?.RecordPrediction(frame, predictedBits);

        LastReadUsedPrediction = true;
        LastReadBits = predictedBits;

        FileLogger.WriteLine(
            $"[PredictedRemoteFrameInputSource] frame={frame} predicted bits={predictedBits}");

        return predictedBits;
    }

    public void ResetPredictor()
    {
        predictor?.Reset();
        mismatchDetector?.Reset();

        LastReadUsedPrediction = false;
        LastReadFrame = -1;
        LastReadBits = 0;
    }
}
