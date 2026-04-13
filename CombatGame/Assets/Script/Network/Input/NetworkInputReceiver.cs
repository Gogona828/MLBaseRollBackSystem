using System.Collections.Generic;
using UnityEngine;

public class NetworkInputReceiver : MonoBehaviour, INetworkPacketHandler
{
    [Header("Delay Simulation")]
    [SerializeField] private int fixedInputDelayFrames = 0;

    [Header("Prediction")]
    [SerializeField] private PredictionMismatchDetector predictionMismatchDetector;

    private RemoteInputBuffer remoteInputBuffer = new RemoteInputBuffer();
    private InputDelaySimulator delaySimulator;

    public RemoteInputBuffer Buffer => remoteInputBuffer;
    public int FixedInputDelayFrames => fixedInputDelayFrames;

    private void Awake()
    {
        delaySimulator = new InputDelaySimulator(fixedInputDelayFrames);
    }

    public void ProcessDelayedInputsForCurrentStep()
    {
        if (delaySimulator == null)
        {
            return;
        }

        List<InputPacket> releasedPackets = delaySimulator.TickAndCollectReleasedPackets();

        for (int i = 0; i < releasedPackets.Count; i++)
        {
            InputPacket packet = releasedPackets[i];
            remoteInputBuffer.Store(packet);

            FileLogger.WriteLine(
                $"[NetworkInputReceiver] Released delayed input frame={packet.frame}, player={packet.playerId}, bits={packet.inputBits}");

            if (predictionMismatchDetector != null)
            {
                predictionMismatchDetector.ConfirmIfPredicted(packet.frame, packet.inputBits);
            }
        }
    }

    public void HandlePacket(NetworkPacket packet)
    {
        if (packet.packetType != NetworkPacketType.Input)
        {
            return;
        }

        InputPacket inputPacket = new InputPacket(packet.playerId, packet.frame, packet.inputBits);

        if (delaySimulator == null)
        {
            delaySimulator = new InputDelaySimulator(fixedInputDelayFrames);
        }

        delaySimulator.Enqueue(inputPacket);

        FileLogger.WriteLine(
            $"[NetworkInputReceiver] Enqueued delayed input frame={packet.frame}, player={packet.playerId}, bits={packet.inputBits}, delay={fixedInputDelayFrames}");
    }

    public bool TryGetRemoteInput(int frame, out byte inputBits)
    {
        return remoteInputBuffer.TryGetInput(frame, out inputBits);
    }

    public void ClearBuffer()
    {
        remoteInputBuffer.Clear();

        if (delaySimulator != null)
        {
            delaySimulator.Clear();
        }
    }
}
