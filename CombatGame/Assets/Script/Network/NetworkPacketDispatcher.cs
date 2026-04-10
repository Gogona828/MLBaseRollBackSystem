using UnityEngine;

public class NetworkPacketDispatcher : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UdpP2PTransport transport;
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private NetworkInputReceiver inputReceiver;

    private void Update()
    {
        if (transport == null)
        {
            Debug.LogWarning("[NetworkPacketDispatcher] transport is null");
            return;
        }

        if (!transport.IsStarted)
        {
            return;
        }

        while (transport.TryDequeue(out NetworkPacket packet))
        {
            Debug.Log($"[NetworkPacketDispatcher] Dispatching {packet.packetType} player={packet.playerId} frame={packet.frame}");
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
                else
                {
                    Debug.LogWarning("[NetworkPacketDispatcher] sessionManager is null");
                }
                break;

            case NetworkPacketType.Input:
                if (inputReceiver != null)
                {
                    inputReceiver.HandlePacket(packet);
                }
                else
                {
                    Debug.LogWarning("[NetworkPacketDispatcher] inputReceiver is null");
                }
                break;
        }
    }
}
