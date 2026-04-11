using UnityEngine;

public class HelloPacketSpamReceiver : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UdpP2PTransport transport;

    private void Update()
    {
        if (transport == null)
        {
            Debug.LogWarning("[HelloPacketSpamReceiver] transport is null");
            return;
        }

        if (!transport.IsStarted)
        {
            return;
        }

        while (transport.TryDequeue(out NetworkPacket packet))
        {
            Debug.Log($"[HelloPacketSpamReceiver] Received {packet.packetType} from player {packet.playerId}");
        }
    }
}
