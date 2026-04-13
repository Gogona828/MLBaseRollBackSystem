using UnityEngine;

public class NetworkSimulationDriver : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkFrameClock frameClock;
    [SerializeField] private NetworkSessionManager sessionManager;

    [Header("Simulation")]
    [SerializeField] private bool useFixedUpdate = true;

    public int LastSimulatedFrame { get; private set; } = -1;

    private void Update()
    {
        if (!useFixedUpdate)
        {
            StepSimulation();
        }
    }

    private void FixedUpdate()
    {
        if (useFixedUpdate)
        {
            StepSimulation();
        }
    }

    private void StepSimulation()
    {
        if (frameClock == null)
        {
            return;
        }

        if (sessionManager == null || !sessionManager.Running)
        {
            return;
        }

        LastSimulatedFrame = frameClock.CurrentFrame;
        frameClock.Tick();
    }
}
