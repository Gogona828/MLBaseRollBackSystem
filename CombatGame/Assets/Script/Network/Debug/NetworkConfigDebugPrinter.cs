using UnityEngine;

public class NetworkConfigDebugPrinter : MonoBehaviour
{
    [SerializeField] private string machineLabel = "Unknown";
    [SerializeField] private int playerId = -1;
    [SerializeField] private UdpP2PTransport transport;

    private void Start()
    {
        Debug.Log($"[NetworkConfigDebugPrinter] machineLabel={machineLabel}, playerId={playerId}");

        if (transport == null)
        {
            Debug.LogWarning("[NetworkConfigDebugPrinter] transport is null");
        }
    }
}
