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
            FileLogger.WriteLine("[NetworkPacketDispatcher] transport is null");
            return;
        }

        if (!transport.IsStarted)
        {
            return;
        }

        while (transport.TryDequeue(out NetworkPacket packet))
        {
            FileLogger.WriteLine($"[NetworkPacketDispatcher] Dispatching {packet.packetType} player={packet.playerId} frame={packet.frame}");
            Dispatch(packet);
        }
    }

    private void Dispatch(NetworkPacket packet)
    {
        switch (packet.packetType)
        {
            case NetworkPacketType.Hello:
            case NetworkPacketType.Ready:
            case NetworkPacketType.Start:
                if (sessionManager != null)
                {
                    sessionManager.HandlePacket(packet);
                }
                else
                {
                    FileLogger.WriteLine("[NetworkPacketDispatcher] sessionManager is null");
                }
                break;

            case NetworkPacketType.Input:
                if (inputReceiver != null)
                {
                    inputReceiver.HandlePacket(packet);
                }
                else
                {
                    FileLogger.WriteLine("[NetworkPacketDispatcher] inputReceiver is null");
                }
                break;
        }
    }
}
