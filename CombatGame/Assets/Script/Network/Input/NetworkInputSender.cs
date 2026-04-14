using UnityEngine;

public class NetworkInputSender : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private int playerId = 0;

    [Header("Local Keys")]
    [SerializeField] private KeyCode leftKey = KeyCode.A;
    [SerializeField] private KeyCode rightKey = KeyCode.D;
    [SerializeField] private KeyCode attackKey = KeyCode.Space;

    [Header("References")]
    [SerializeField] private UdpP2PTransport transport;
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private DebugAutoInputSequence debugAutoInputSequence;

    [Header("Debug")]
    [SerializeField] private bool useDebugAutoInput = false;

    [Header("Local Preview")]
    [SerializeField] private bool updateLocalBitsWithoutSending = true;
    
    [SerializeField] private Footsies.FootsiesBattleInputHistory inputHistory;

    private int lastSentFrame = -1;

    public byte LastLocalInputBits { get; private set; }

    private void Update()
    {
        if (!updateLocalBitsWithoutSending)
        {
            return;
        }

        LastLocalInputBits = ReadCurrentLocalInputBits();
    }

    public void ProcessSendForFrame(int frame)
    {
        if (frame < 0)
        {
            return;
        }

        if (frame == lastSentFrame)
        {
            return;
        }

        byte inputBits = ReadCurrentLocalInputBits();
        LastLocalInputBits = inputBits;
        
        if (inputHistory != null)
        {
            inputHistory.StoreInput(playerId, frame, inputBits);
        }

        if (transport == null || sessionManager == null)
        {
            return;
        }

        if (!transport.IsStarted)
        {
            return;
        }

        if (!sessionManager.Running)
        {
            return;
        }

        NetworkPacket packet = new NetworkPacket(
            NetworkPacketType.Input,
            playerId,
            frame,
            inputBits,
            0
        );

        transport.Send(packet);
        lastSentFrame = frame;

        FileLogger.WriteLine($"[NetworkInputSender] Sent Input frame={frame} bits={inputBits}");
    }

    private byte ReadCurrentLocalInputBits()
    {
        if (useDebugAutoInput && debugAutoInputSequence != null)
        {
            return debugAutoInputSequence.GetBits();
        }

        return InputEncoder.ReadLocalInputBits(leftKey, rightKey, attackKey);
    }

    public void ResetSenderState()
    {
        lastSentFrame = -1;
        LastLocalInputBits = 0;
    }
}
