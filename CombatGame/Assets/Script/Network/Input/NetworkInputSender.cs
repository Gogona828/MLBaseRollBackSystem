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
    [SerializeField] private NetworkFrameClock frameClock;
    [SerializeField] private UdpP2PTransport transport;
    [SerializeField] private NetworkSessionManager sessionManager;

    private int lastSentFrame = -1;

    public byte LastLocalInputBits { get; private set; }

    private void Update()
    {
        if (frameClock == null || transport == null || sessionManager == null)
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

        int frame = frameClock.CurrentFrame;

        // 同じ frame を複数回送らない
        if (frame == lastSentFrame)
        {
            return;
        }

        byte inputBits = InputEncoder.ReadLocalInputBits(leftKey, rightKey, attackKey);
        LastLocalInputBits = inputBits;

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

    public void ResetSenderState()
    {
        lastSentFrame = -1;
        LastLocalInputBits = 0;
    }
}
