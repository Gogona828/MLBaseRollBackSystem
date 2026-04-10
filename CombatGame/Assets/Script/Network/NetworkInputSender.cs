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
        if (frameClock == null)
        {
            Debug.LogWarning("[NetworkInputSender] frameClock is null");
            return;
        }

        if (transport == null)
        {
            Debug.LogWarning("[NetworkInputSender] transport is null");
            return;
        }

        if (!transport.IsStarted)
        {
            Debug.LogWarning("[NetworkInputSender] transport is not started");
            return;
        }

        int frame = frameClock.CurrentFrame;
        byte inputBits = InputEncoder.ReadLocalInputBits(leftKey, rightKey, attackKey);

        LastLocalInputBits = inputBits;

        InputPacket packet = new InputPacket(playerId, frame, inputBits);
        transport.Send(packet);

        Debug.Log($"[SEND] player={playerId}, frame={frame}, bits={inputBits}, readable={InputEncoder.ToReadableString(inputBits)}");

        frameClock.Tick();
    }
}
