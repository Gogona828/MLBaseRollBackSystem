using UnityEngine;

public class PredictionMismatchDetector : MonoBehaviour
{
    private PredictionHistoryBuffer historyBuffer = new PredictionHistoryBuffer();

    private PredictionMissInfo earliestPendingMissInfo = PredictionMissInfo.Invalid();
    private int lastConsumedMissFrame = -1;

    public int TotalPredictions { get; private set; }
    public int TotalHits { get; private set; }
    public int TotalMisses { get; private set; }

    public void RecordPrediction(int frame, byte predictedBits)
    {
        if (historyBuffer.HasPredictionForFrame(frame))
        {
            return;
        }

        historyBuffer.RecordPrediction(frame, predictedBits);
        TotalPredictions++;

        FileLogger.WriteLine(
            $"[PredictionMismatchDetector] Recorded prediction frame={frame}, bits={predictedBits}");
    }

    public void ConfirmIfPredicted(int frame, byte confirmedBits)
    {
        if (!historyBuffer.TryConfirmPrediction(frame, confirmedBits, out PredictionRecord record))
        {
            return;
        }

        if (record.ResultState == PredictionResultState.Hit)
        {
            TotalHits++;
            FileLogger.WriteLine(
                $"[PredictionMismatchDetector] HIT frame={frame}, predicted={record.PredictedBits}, confirmed={confirmedBits}");
        }
        else if (record.ResultState == PredictionResultState.Miss)
        {
            TotalMisses++;

            PredictionMissInfo missInfo = new PredictionMissInfo(
                frame,
                record.PredictedBits,
                confirmedBits
            );

            RegisterPendingMiss(missInfo);

            FileLogger.WriteLine(
                $"[PredictionMismatchDetector] MISS frame={frame}, predicted={record.PredictedBits}, confirmed={confirmedBits}");
        }
    }

    private void RegisterPendingMiss(PredictionMissInfo missInfo)
    {
        if (!missInfo.IsValid)
        {
            return;
        }

        if (missInfo.Frame <= lastConsumedMissFrame)
        {
            return;
        }

        if (!earliestPendingMissInfo.IsValid)
        {
            earliestPendingMissInfo = missInfo;
            return;
        }

        if (missInfo.Frame < earliestPendingMissInfo.Frame)
        {
            earliestPendingMissInfo = missInfo;
        }
    }

    public bool TryConsumeEarliestPendingMiss(out PredictionMissInfo missInfo)
    {
        if (!earliestPendingMissInfo.IsValid)
        {
            missInfo = PredictionMissInfo.Invalid();
            return false;
        }

        missInfo = earliestPendingMissInfo;
        lastConsumedMissFrame = missInfo.Frame;
        earliestPendingMissInfo = PredictionMissInfo.Invalid();
        return true;
    }

    public void ResetDetector()
    {
        TotalPredictions = 0;
        TotalHits = 0;
        TotalMisses = 0;

        historyBuffer.Clear();
        earliestPendingMissInfo = PredictionMissInfo.Invalid();
        lastConsumedMissFrame = -1;
    }

    public string GetSummary()
    {
        return $"predictions={TotalPredictions}, hits={TotalHits}, misses={TotalMisses}";
    }
}
