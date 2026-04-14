using System;

[Serializable]
public struct ExperimentResultRecord
{
    public string MachineLabel;
    public string TestLabel;
    public int DelayFrames;

    public int Predictions;
    public int Hits;
    public int Misses;

    public int RollbackRequests;
    public int SuppressedRollbacks;

    public int Resimulations;
    public int TotalResimulatedFrames;
    public int LastResimulationFrameCount;

    public int WarpDetections;
    public int GhostHitCandidates;

    public string Timestamp;

    public string ToCsvRow()
    {
        return string.Join(",",
            Timestamp,
            MachineLabel,
            TestLabel,
            DelayFrames,
            Predictions,
            Hits,
            Misses,
            RollbackRequests,
            SuppressedRollbacks,
            Resimulations,
            TotalResimulatedFrames,
            LastResimulationFrameCount,
            WarpDetections,
            GhostHitCandidates
        );
    }

    public static string CsvHeader()
    {
        return "Timestamp,MachineLabel,TestLabel,DelayFrames,Predictions,Hits,Misses,RollbackRequests,SuppressedRollbacks,Resimulations,TotalResimulatedFrames,LastResimulationFrameCount,WarpDetections,GhostHitCandidates";
    }
}
