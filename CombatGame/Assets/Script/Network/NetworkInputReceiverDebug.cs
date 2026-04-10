using UnityEngine;

public class NetworkInputReceiverDebug : MonoBehaviour
{
    [SerializeField] private UdpP2PTransport transport;

    private void Update()
    {
        if (transport == null)
        {
            return;
        }

        while (transport.TryDequeue(out InputPacket packet))
        {
            Debug.Log(
                $"[RECV] player={packet.playerId}, frame={packet.frame}, bits={packet.inputBits}, readable={InputEncoder.ToReadableString(packet.inputBits)}");
        }
    }
}
