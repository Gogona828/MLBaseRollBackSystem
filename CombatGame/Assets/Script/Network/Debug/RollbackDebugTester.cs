using UnityEngine;

public class RollbackDebugTester : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkFrameClock frameClock;
    [SerializeField] private RollbackCoordinator rollbackCoordinator;
    [SerializeField] private RollbackStateTester player1StateTester;
    [SerializeField] private RollbackStateTester player2StateTester;
    [SerializeField] private PredictedInputTester predictedInputTester;

    [Header("Debug Keys")]
    [SerializeField] private KeyCode rollbackKey = KeyCode.R;

    [Header("Settings")]
    [SerializeField] private int rollbackFramesBack = 30;

    public void ProcessSimulationForFrame(int frame)
    {
        if (predictedInputTester == null || player1StateTester == null || player2StateTester == null)
        {
            return;
        }

        byte p1Bits = predictedInputTester.GetP1InputBitsForSimulation(frame);
        byte p2Bits = predictedInputTester.GetP2InputBitsForSimulation(frame);

        player1StateTester.ApplyInput(p1Bits);
        player2StateTester.ApplyInput(p2Bits);

        rollbackCoordinator?.SaveSnapshotForFrame(frame);
    }

    public void ProcessDebugRollbackRequest()
    {
        if (frameClock == null || rollbackCoordinator == null)
        {
            return;
        }

        if (!Input.GetKeyDown(rollbackKey))
        {
            return;
        }

        int targetFrame = Mathf.Max(0, frameClock.CurrentFrame - rollbackFramesBack);
        rollbackCoordinator.RequestRollback(targetFrame);
    }
}
