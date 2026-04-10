using UnityEngine;

public class NetworkInputReceiver : MonoBehaviour, INetworkPacketHandler
{
    private RemoteInputBuffer remoteInputBuffer = new RemoteInputBuffer();

    public RemoteInputBuffer Buffer => remoteInputBuffer;

    public void HandlePacket(NetworkPacket packet)
    {
        if (packet.packetType != NetworkPacketType.Input)
        {
            return;
        }

        InputPacket inputPacket = new InputPacket(packet.playerId, packet.frame, packet.inputBits);
        remoteInputBuffer.Store(inputPacket);

        Debug.Log($"[NetworkInputReceiver] Stored input frame={packet.frame}, player={packet.playerId}, bits={packet.inputBits}");
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
