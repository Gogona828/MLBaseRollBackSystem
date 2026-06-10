using System.Collections.Generic;
using UnityEngine;

public class NetworkInputReceiver : MonoBehaviour, INetworkPacketHandler
{
    [Header("Delay Simulation")]
    [SerializeField] private bool useVariableDelay = true;
    [SerializeField] private int fixedInputDelayFrames = 2;
    [SerializeField] private VariableInputDelayProfile variableDelayProfile = new VariableInputDelayProfile();

    [Header("Temporary Test Override")]
    [SerializeField] private bool forceFixedDelayForTest = false;
    [SerializeField] private int forcedFixedDelayFrames = 2;

    [Header("Prediction")]
    [SerializeField] private PredictionMismatchDetector predictionMismatchDetector;

    private RemoteInputBuffer remoteInputBuffer = new RemoteInputBuffer();

    private byte lastConfirmedBitsPlayer0 = 0;
    private byte lastConfirmedBitsPlayer1 = 0;

    // 追加:
    // 「相手入力が 0,1,2... と連続して確定済みである最後のフレーム」
    private int latestContiguousConfirmedRemoteFrame = -1;

    public RemoteInputBuffer Buffer => remoteInputBuffer;
    public int FixedInputDelayFrames => fixedInputDelayFrames;

    [SerializeField] private Footsies.FootsiesBattleInputHistory inputHistory;

    private void Awake()
    {
    }

    public void UseFixedDelayForTest(int delayFrames)
    {
        FileLogger.WriteLine(
            $"[NetworkInputReceiver] UseFixedDelayForTest (No-op, delay system is disabled)");
    }

    public void UseSceneConfiguredDelayMode()
    {
        FileLogger.WriteLine("[NetworkInputReceiver] UseSceneConfiguredDelayMode (No-op, delay system is disabled)");
    }

    public void ProcessDelayedInputsForCurrentStep()
    {
        // No-op: inputs are processed immediately in HandlePacket.
    }

    public void HandlePacket(NetworkPacket packet)
    {
        if (packet.packetType != NetworkPacketType.Input)
        {
            return;
        }

        InputPacket inputPacket = new InputPacket(packet.playerId, packet.frame, packet.inputBits);
        remoteInputBuffer.Store(inputPacket);

        if (inputHistory != null)
        {
            inputHistory.StoreInput(inputPacket.playerId, inputPacket.frame, inputPacket.inputBits);
        }

        if (inputPacket.playerId == 0)
        {
            lastConfirmedBitsPlayer0 = inputPacket.inputBits;
        }
        else if (inputPacket.playerId == 1)
        {
            lastConfirmedBitsPlayer1 = inputPacket.inputBits;
        }

        FileLogger.WriteLine(
            $"[NetworkInputReceiver] Processed input immediately: frame={packet.frame}, player={packet.playerId}, bits={packet.inputBits}");

        if (predictionMismatchDetector != null)
        {
            predictionMismatchDetector.ConfirmIfPredicted(packet.frame, packet.inputBits);
        }

        UpdateLatestContiguousConfirmedRemoteFrame();
    }

    private int ResolveDelayFrames()
    {
        return 0;
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
        return 0;
    }

    // 追加:
    public int GetLatestContiguousConfirmedRemoteFrame()
    {
        return latestContiguousConfirmedRemoteFrame;
    }

    public void ClearBuffer()
    {
        remoteInputBuffer.Clear();

        lastConfirmedBitsPlayer0 = 0;
        lastConfirmedBitsPlayer1 = 0;
        latestContiguousConfirmedRemoteFrame = -1;
    }

    private void UpdateLatestContiguousConfirmedRemoteFrame()
    {
        int probe = latestContiguousConfirmedRemoteFrame + 1;

        while (remoteInputBuffer.TryGetInput(probe, out _))
        {
            latestContiguousConfirmedRemoteFrame = probe;
            probe++;
        }
    }
}
