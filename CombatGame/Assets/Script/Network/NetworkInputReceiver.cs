using UnityEngine;

public class NetworkInputReceiver : MonoBehaviour
{
    [SerializeField] private UdpP2PTransport transport;

    private RemoteInputBuffer remoteInputBuffer = new RemoteInputBuffer();

    public RemoteInputBuffer Buffer => remoteInputBuffer;

    private void Update()
    {
        if (transport == null)
        {
            return;
        }

        while (transport.TryDequeue(out InputPacket packet))
        {
            remoteInputBuffer.Store(packet);
        }
    }

    public bool TryGetRemoteInput(int frame, out byte inputBits)
    {
        return remoteInputBuffer.TryGetInput(frame, out inputBits);
    }

    public void ClearBuffer()
    {
        remoteInputBuffer.Clear();
    }
}
