using UnityEngine;

public class HelloPacketSpamSender : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private int playerId = 0;

    [Header("References")]
    [SerializeField] private UdpP2PTransport transport;

    [Header("Timing")]
    [SerializeField] private float sendIntervalSeconds = 1.0f;

    private float timer = 0f;

    private void Update()
    {
        if (transport == null)
        {
            Debug.LogWarning("[HelloPacketSpamSender] transport is null");
            return;
        }

        if (!transport.IsStarted)
        {
            return;
        }

        timer += Time.deltaTime;

        if (timer >= sendIntervalSeconds)
        {
            timer = 0f;

            NetworkPacket packet = new NetworkPacket(
                NetworkPacketType.Hello,
                playerId,
                -1,
                0
            );

            transport.Send(packet);
            Debug.Log($"[HelloPacketSpamSender] Sent Hello from player {playerId}");
        }
    }
}
