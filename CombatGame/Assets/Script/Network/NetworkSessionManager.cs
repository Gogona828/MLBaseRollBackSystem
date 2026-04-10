using UnityEngine;

public class NetworkSessionManager : MonoBehaviour, INetworkPacketHandler
{
    [Header("Player")]
    [SerializeField] private int playerId = 0;

    [Header("References")]
    [SerializeField] private UdpP2PTransport transport;
    [SerializeField] private NetworkFrameClock frameClock;

    [Header("Handshake Timing")]
    [SerializeField] private float helloIntervalSeconds = 0.5f;
    [SerializeField] private float readyIntervalSeconds = 0.5f;
    [SerializeField] private float startDelaySeconds = 1.0f;

    public NetworkSessionState State { get; private set; } = NetworkSessionState.WaitingForPeer;
    public bool Running => State == NetworkSessionState.Running;
    public bool PeerHelloReceived { get; private set; }
    public bool PeerReadyReceived { get; private set; }

    private float helloTimer = 0f;
    private float readyTimer = 0f;
    private float startTimer = 0f;
    private bool localReadySent = false;

    private void Start()
    {
        if (frameClock != null)
        {
            frameClock.ResetClock();
        }
    }

    private void Update()
    {
        if (transport == null || !transport.IsStarted)
        {
            return;
        }

        switch (State)
        {
            case NetworkSessionState.WaitingForPeer:
                UpdateWaitingForPeer();
                break;

            case NetworkSessionState.WaitingForReady:
                UpdateWaitingForReady();
                break;

            case NetworkSessionState.Running:
                break;
        }
    }

    private void UpdateWaitingForPeer()
    {
        helloTimer += Time.deltaTime;

        if (helloTimer >= helloIntervalSeconds)
        {
            helloTimer = 0f;
            SendHello();
        }

        if (PeerHelloReceived)
        {
            State = NetworkSessionState.WaitingForReady;
            readyTimer = 0f;
            startTimer = 0f;
            Debug.Log("[NetworkSessionManager] Peer hello received. Enter WaitingForReady.");
        }
    }

    private void UpdateWaitingForReady()
    {
        helloTimer += Time.deltaTime;
        readyTimer += Time.deltaTime;

        // WaitingForReady 中も Hello を送り続ける
        if (helloTimer >= helloIntervalSeconds)
        {
            helloTimer = 0f;
            SendHello();
        }

        // Ready も定期送信する
        if (!localReadySent || readyTimer >= readyIntervalSeconds)
        {
            readyTimer = 0f;
            SendReady();
            localReadySent = true;
        }

        if (PeerReadyReceived)
        {
            startTimer += Time.deltaTime;

            if (startTimer >= startDelaySeconds)
            {
                State = NetworkSessionState.Running;

                if (frameClock != null)
                {
                    frameClock.ResetClock();
                }

                Debug.Log("[NetworkSessionManager] Running started. Frame clock reset to 0.");
            }
        }
    }

    public void HandlePacket(NetworkPacket packet)
    {
        switch (packet.packetType)
        {
            case NetworkPacketType.Hello:
                PeerHelloReceived = true;
                Debug.Log($"[NetworkSessionManager] Received Hello from player {packet.playerId}");

                if (State == NetworkSessionState.WaitingForPeer)
                {
                    State = NetworkSessionState.WaitingForReady;
                    readyTimer = 0f;
                    startTimer = 0f;
                    Debug.Log("[NetworkSessionManager] Transition to WaitingForReady by Hello.");
                }
                break;

            case NetworkPacketType.Ready:
                PeerReadyReceived = true;

                // Ready が届くということは、相手は起動済み・通信可能
                // なので Hello も受信済み相当として扱う
                if (!PeerHelloReceived)
                {
                    PeerHelloReceived = true;
                    Debug.Log("[NetworkSessionManager] Treat Ready as implicit Hello.");
                }

                if (State == NetworkSessionState.WaitingForPeer)
                {
                    State = NetworkSessionState.WaitingForReady;
                    readyTimer = 0f;
                    startTimer = 0f;
                    Debug.Log("[NetworkSessionManager] Transition to WaitingForReady by Ready.");
                }

                Debug.Log($"[NetworkSessionManager] Received Ready from player {packet.playerId}");
                break;
        }
    }

    private void SendHello()
    {
        NetworkPacket packet = new NetworkPacket(NetworkPacketType.Hello, playerId, -1, 0);
        transport.Send(packet);
        Debug.Log("[NetworkSessionManager] Sent Hello");
    }

    private void SendReady()
    {
        NetworkPacket packet = new NetworkPacket(NetworkPacketType.Ready, playerId, -1, 0);
        transport.Send(packet);
        Debug.Log("[NetworkSessionManager] Sent Ready");
    }
}
