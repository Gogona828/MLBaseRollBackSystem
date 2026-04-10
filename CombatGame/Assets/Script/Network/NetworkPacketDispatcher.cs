using UnityEngine;

public class NetworkPacketDispatcher : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UdpP2PTransport transport;
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private NetworkInputReceiver inputReceiver;

    private void Update()
    {
        if (transport == null || !transport.IsStarted)
        {
            return;
        }

        while (transport.TryDequeue(out NetworkPacket packet))
        {
            Dispatch(packet);
        }
    }

    private void Dispatch(NetworkPacket packet)
    {
        switch (packet.packetType)
        {
            case NetworkPacketType.Hello:
            case NetworkPacketType.Ready:
                if (sessionManager != null)
                {
                    sessionManager.HandlePacket(packet);
                }
                break;

            case NetworkPacketType.Input:
                if (inputReceiver != null)
                {
                    inputReceiver.HandlePacket(packet);
                }
                break;
        }
    }
}
