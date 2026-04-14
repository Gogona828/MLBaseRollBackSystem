using System.Collections.Generic;
using UnityEngine;

public class NetworkInputReceiver : MonoBehaviour, INetworkPacketHandler
{
    [Header("Delay Simulation")]
    [SerializeField] private bool useVariableDelay = true;
    [SerializeField] private int fixedInputDelayFrames = 2;
    [SerializeField] private VariableInputDelayProfile variableDelayProfile = new VariableInputDelayProfile();

    [Header("Prediction")]
    [SerializeField] private PredictionMismatchDetector predictionMismatchDetector;

    private RemoteInputBuffer remoteInputBuffer = new RemoteInputBuffer();
    private InputDelaySimulator delaySimulator;

    private byte lastConfirmedBitsPlayer0 = 0;
    private byte lastConfirmedBitsPlayer1 = 0;

    public RemoteInputBuffer Buffer => remoteInputBuffer;
    public int FixedInputDelayFrames => fixedInputDelayFrames;
    
    [SerializeField] private Footsies.FootsiesBattleInputHistory inputHistory;

    private void Awake()
    {
        delaySimulator = new InputDelaySimulator();
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
            
            if (inputHistory != null)
            {
                inputHistory.StoreInput(packet.playerId, packet.frame, packet.inputBits);
            }

            if (packet.playerId == 0)
            {
                lastConfirmedBitsPlayer0 = packet.inputBits;
            }
            else if (packet.playerId == 1)
            {
                lastConfirmedBitsPlayer1 = packet.inputBits;
            }

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
            delaySimulator = new InputDelaySimulator();
        }

        int delayFrames = fixedInputDelayFrames;

        if (useVariableDelay && variableDelayProfile != null)
        {
            delayFrames = variableDelayProfile.SampleDelayFrames();
        }

        delaySimulator.Enqueue(inputPacket, delayFrames);

        FileLogger.WriteLine(
            $"[NetworkInputReceiver] Enqueued delayed input frame={packet.frame}, player={packet.playerId}, bits={packet.inputBits}, delay={delayFrames}");
    }

    public bool TryGetRemoteInput(int frame, out byte inputBits)
    {
        bool found = remoteInputBuffer.TryGetInput(frame, out inputBits);

        if (found)
        {
            FileLogger.WriteLine(
                $"[NetworkInputReceiver] TryGetRemoteInput HIT frame={frame}, bits={inputBits}");
        }

        return found;
    }

    public byte GetLastConfirmedBitsForPlayer(int playerId)
    {
        if (playerId == 0)
        {
            return lastConfirmedBitsPlayer0;
        }

        if (playerId == 1)
        {
            return lastConfirmedBitsPlayer1;
        }

        return 0;
    }

    public int GetPendingDelayedInputCount()
    {
        if (delaySimulator == null)
        {
            return 0;
        }

        return delaySimulator.GetPendingCount();
    }

    public void ClearBuffer()
    {
        remoteInputBuffer.Clear();

        if (delaySimulator != null)
        {
            delaySimulator.Clear();
        }

        lastConfirmedBitsPlayer0 = 0;
        lastConfirmedBitsPlayer1 = 0;
    }
}
