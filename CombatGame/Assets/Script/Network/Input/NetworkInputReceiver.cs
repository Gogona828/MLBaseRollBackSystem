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
    private InputDelaySimulator delaySimulator;

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
        delaySimulator = new InputDelaySimulator();
    }

    public void UseFixedDelayForTest(int delayFrames)
    {
        forceFixedDelayForTest = true;
        forcedFixedDelayFrames = Mathf.Max(0, delayFrames);

        FileLogger.WriteLine(
            $"[NetworkInputReceiver] UseFixedDelayForTest delayFrames={forcedFixedDelayFrames}");
    }

    public void UseSceneConfiguredDelayMode()
    {
        forceFixedDelayForTest = false;

        FileLogger.WriteLine("[NetworkInputReceiver] UseSceneConfiguredDelayMode");
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

        UpdateLatestContiguousConfirmedRemoteFrame();
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

        int delayFrames = ResolveDelayFrames();
        delaySimulator.Enqueue(inputPacket, delayFrames);

        FileLogger.WriteLine(
            $"[NetworkInputReceiver] Enqueued delayed input frame={packet.frame}, player={packet.playerId}, bits={packet.inputBits}, delay={delayFrames}");
    }

    private int ResolveDelayFrames()
    {
        if (forceFixedDelayForTest)
        {
            return Mathf.Max(0, forcedFixedDelayFrames);
        }

        int delayFrames = fixedInputDelayFrames;

        if (useVariableDelay && variableDelayProfile != null)
        {
            delayFrames = variableDelayProfile.SampleDelayFrames();
        }

        return Mathf.Max(0, delayFrames);
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

    // 追加:
    public int GetLatestContiguousConfirmedRemoteFrame()
    {
        return latestContiguousConfirmedRemoteFrame;
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
