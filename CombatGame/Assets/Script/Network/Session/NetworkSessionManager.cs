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

    [Header("Start Sync")]
    [SerializeField] private int startDelayFrames = 60;

    public NetworkSessionState State { get; private set; } = NetworkSessionState.WaitingForPeer;
    public bool Running => State == NetworkSessionState.Running;
    public bool PeerHelloReceived { get; private set; }
    public bool PeerReadyReceived { get; private set; }
    public bool StartReceived { get; private set; }

    private float helloTimer = 0f;
    private float readyTimer = 0f;
    private bool localReadySent = false;
    private bool localStartSent = false;
    private float countdownSeconds = -1f;

    public void ConfigureRuntime(int playerId, int startDelayFrames)
    {
        this.playerId = playerId;
        this.startDelayFrames = Mathf.Max(0, startDelayFrames);
        ResetSessionState();

        FileLogger.WriteLine(
            $"[NetworkSessionManager] ConfigureRuntime playerId={playerId}, startDelayFrames={this.startDelayFrames}");
    }

    public void ResetSessionState()
    {
        State = NetworkSessionState.WaitingForPeer;
        PeerHelloReceived = false;
        PeerReadyReceived = false;
        StartReceived = false;

        helloTimer = 0f;
        readyTimer = 0f;
        localReadySent = false;
        localStartSent = false;
        countdownSeconds = -1f;

        if (frameClock != null)
        {
            frameClock.ResetClock();
        }
    }

    private void Start()
    {
        if (frameClock != null)
        {
            frameClock.ResetClock();
        }

        FileLogger.WriteLine($"[NetworkSessionManager] Start playerId={playerId} state={State}");
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

            case NetworkSessionState.WaitingForStart:
                UpdateWaitingForStart();
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
            FileLogger.WriteLine("[NetworkSessionManager] Peer hello received. Enter WaitingForReady.");
        }
    }

    private void UpdateWaitingForReady()
    {
        helloTimer += Time.deltaTime;
        readyTimer += Time.deltaTime;

        if (helloTimer >= helloIntervalSeconds)
        {
            helloTimer = 0f;
            SendHello();
        }

        if (!localReadySent || readyTimer >= readyIntervalSeconds)
        {
            readyTimer = 0f;
            SendReady();
            localReadySent = true;
        }

        if (PeerReadyReceived)
        {
            if (playerId == 0 && !localStartSent)
            {
                SendStart(startDelayFrames);
                localStartSent = true;
                StartReceived = true;
                BeginStartCountdown(startDelayFrames);
                State = NetworkSessionState.WaitingForStart;

                FileLogger.WriteLine(
                    $"[NetworkSessionManager] Sent Start with delayFrames={startDelayFrames}. Enter WaitingForStart.");
            }
        }
    }

    private void UpdateWaitingForStart()
    {
        if (countdownSeconds < 0f)
        {
            return;
        }

        countdownSeconds -= Time.unscaledDeltaTime;

        if (countdownSeconds <= 0f)
        {
            StartRunning();
        }
    }

    private void BeginStartCountdown(int delayFrames)
    {
        countdownSeconds = Mathf.Max(0f, delayFrames * Time.fixedDeltaTime);
    }

    private void StartRunning()
    {
        State = NetworkSessionState.Running;
        countdownSeconds = -1f;

        if (frameClock != null)
        {
            frameClock.ResetClock();
        }

        FileLogger.WriteLine("[NetworkSessionManager] Running started. Frame clock reset to 0.");
    }

    public void HandlePacket(NetworkPacket packet)
    {
        FileLogger.WriteLine($"[NetworkSessionManager] HandlePacket type={packet.packetType} from player={packet.playerId}");

        switch (packet.packetType)
        {
            case NetworkPacketType.Hello:
                PeerHelloReceived = true;
                FileLogger.WriteLine($"[NetworkSessionManager] Received Hello from player {packet.playerId}");

                if (State == NetworkSessionState.WaitingForPeer)
                {
                    State = NetworkSessionState.WaitingForReady;
                    readyTimer = 0f;
                    FileLogger.WriteLine("[NetworkSessionManager] Transition to WaitingForReady by Hello.");
                }
                break;

            case NetworkPacketType.Ready:
                PeerReadyReceived = true;

                if (!PeerHelloReceived)
                {
                    PeerHelloReceived = true;
                    FileLogger.WriteLine("[NetworkSessionManager] Treat Ready as implicit Hello.");
                }

                if (State == NetworkSessionState.WaitingForPeer)
                {
                    State = NetworkSessionState.WaitingForReady;
                    readyTimer = 0f;
                    FileLogger.WriteLine("[NetworkSessionManager] Transition to WaitingForReady by Ready.");
                }

                FileLogger.WriteLine($"[NetworkSessionManager] Received Ready from player {packet.playerId}");
                break;

            case NetworkPacketType.Start:
                StartReceived = true;
                BeginStartCountdown(packet.startDelayFrames);
                State = NetworkSessionState.WaitingForStart;

                FileLogger.WriteLine(
                    $"[NetworkSessionManager] Received Start from player {packet.playerId} delayFrames={packet.startDelayFrames}. Enter WaitingForStart.");
                break;
        }
    }

    private void SendHello()
    {
        NetworkPacket packet = new NetworkPacket(NetworkPacketType.Hello, playerId, -1, 0, 0);
        transport.Send(packet);
        FileLogger.WriteLine("[NetworkSessionManager] Sent Hello");
    }

    private void SendReady()
    {
        NetworkPacket packet = new NetworkPacket(NetworkPacketType.Ready, playerId, -1, 0, 0);
        transport.Send(packet);
        FileLogger.WriteLine("[NetworkSessionManager] Sent Ready");
    }

    private void SendStart(int delayFrames)
    {
        NetworkPacket packet = new NetworkPacket(NetworkPacketType.Start, playerId, -1, 0, delayFrames);
        transport.Send(packet);
        FileLogger.WriteLine($"[NetworkSessionManager] Sent Start delayFrames={delayFrames}");
    }
}
