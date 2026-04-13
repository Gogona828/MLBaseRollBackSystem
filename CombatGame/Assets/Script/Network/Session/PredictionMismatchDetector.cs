using UnityEngine;

public class PredictionMismatchDetector : MonoBehaviour
{
    private PredictionHistoryBuffer historyBuffer = new PredictionHistoryBuffer();

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
            FileLogger.WriteLine(
                $"[PredictionMismatchDetector] MISS frame={frame}, predicted={record.PredictedBits}, confirmed={confirmedBits}");
        }
    }

    public void ResetDetector()
    {
        TotalPredictions = 0;
        TotalHits = 0;
        TotalMisses = 0;
        historyBuffer.Clear();
    }

    public string GetSummary()
    {
        return $"predictions={TotalPredictions}, hits={TotalHits}, misses={TotalMisses}";
    }
}
