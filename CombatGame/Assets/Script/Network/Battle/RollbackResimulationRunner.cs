using UnityEngine;

public class RollbackResimulationRunner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RollbackCoordinator rollbackCoordinator;
    [SerializeField] private RollbackStateTester player1StateTester;
    [SerializeField] private RollbackStateTester player2StateTester;
    [SerializeField] private PredictedInputTester predictedInputTester;

    public int TotalResimulations { get; private set; }
    public int TotalResimulatedFrames { get; private set; }
    public int LastResimulationFrameCount { get; private set; }

    public void ProcessResimulationIfNeeded()
    {
        if (rollbackCoordinator == null ||
            player1StateTester == null ||
            player2StateTester == null ||
            predictedInputTester == null)
        {
            return;
        }

        if (!rollbackCoordinator.TryConsumeResimulationRequest(out RollbackResimulationRequest request))
        {
            return;
        }

        if (!request.IsValid)
        {
            return;
        }

        FileLogger.WriteLine($"[RollbackResimulationRunner] Start resimulation {request}");

        int resimulatedFrameCount = 0;

        for (int frame = request.TargetFrame; frame < request.ResumeFrameExclusive; frame++)
        {
            byte p1Bits = predictedInputTester.GetP1InputBitsForSimulation(frame);
            byte p2Bits = predictedInputTester.GetP2InputBitsForSimulation(frame);

            player1StateTester.ApplyInput(p1Bits);
            player2StateTester.ApplyInput(p2Bits);

            rollbackCoordinator.SaveSnapshotForFrame(frame);

            resimulatedFrameCount++;
            TotalResimulatedFrames++;

            FileLogger.WriteLine(
                $"[RollbackResimulationRunner] Resimulated frame={frame}, p1Bits={p1Bits}, p2Bits={p2Bits}");
        }

        LastResimulationFrameCount = resimulatedFrameCount;
        TotalResimulations++;

        FileLogger.WriteLine(
            $"[RollbackResimulationRunner] Finished resimulation {request}, resimulatedFrameCount={resimulatedFrameCount}");
    }

    public void ResetRunner()
    {
        TotalResimulations = 0;
        TotalResimulatedFrames = 0;
        LastResimulationFrameCount = 0;
    }
}
