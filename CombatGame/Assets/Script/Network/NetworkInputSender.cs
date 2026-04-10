using UnityEngine;

public class NetworkInputSender : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private int playerId = 0;

    [Header("Local Keys")]
    [SerializeField] private KeyCode leftKey = KeyCode.A;
    [SerializeField] private KeyCode rightKey = KeyCode.D;
    [SerializeField] private KeyCode attackKey = KeyCode.J;

    [Header("References")]
    [SerializeField] private NetworkFrameClock frameClock;
    [SerializeField] private UdpP2PTransport transport;

    public byte LastLocalInputBits { get; private set; }

    private void Update()
    {
        if (frameClock == null || transport == null || !transport.IsStarted)
        {
            return;
        }

        int frame = frameClock.CurrentFrame;
        byte inputBits = InputEncoder.ReadLocalInputBits(leftKey, rightKey, attackKey);

        LastLocalInputBits = inputBits;

        InputPacket packet = new InputPacket(playerId, frame, inputBits);
        transport.Send(packet);

        frameClock.Tick();
    }
}
