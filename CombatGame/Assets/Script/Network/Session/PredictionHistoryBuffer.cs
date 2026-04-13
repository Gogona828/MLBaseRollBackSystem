using System.Collections.Generic;

public class PredictionHistoryBuffer
{
    private readonly Dictionary<int, PredictionRecord> records = new Dictionary<int, PredictionRecord>();

    public void RecordPrediction(int frame, byte predictedBits)
    {
        if (records.ContainsKey(frame))
        {
            return;
        }

        records[frame] = new PredictionRecord(frame, predictedBits);
    }

    public bool TryGetRecord(int frame, out PredictionRecord record)
    {
        return records.TryGetValue(frame, out record);
    }

    public bool HasPredictionForFrame(int frame)
    {
        return records.ContainsKey(frame);
    }

    public bool TryConfirmPrediction(int frame, byte confirmedBits, out PredictionRecord updatedRecord)
    {
        if (!records.TryGetValue(frame, out PredictionRecord record))
        {
            updatedRecord = default;
            return false;
        }

        if (record.ResultState != PredictionResultState.Pending)
        {
            updatedRecord = record;
            return true;
        }

        record.Confirm(confirmedBits);
        records[frame] = record;
        updatedRecord = record;
        return true;
    }

    public void Clear()
    {
        records.Clear();
    }
}
