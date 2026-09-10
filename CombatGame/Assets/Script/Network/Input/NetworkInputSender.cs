using UnityEngine;

public class NetworkInputSender : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private int playerId = 0;

    [Header("Local Keys")]
    [SerializeField] private KeyCode leftKey = KeyCode.A;
    [SerializeField] private KeyCode rightKey = KeyCode.D;
    [SerializeField] private KeyCode attackKey = KeyCode.Space;

    [Header("References")]
    [SerializeField] private UdpP2PTransport transport;
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private DebugAutoInputSequence debugAutoInputSequence;

    [Header("Debug")]
    [SerializeField] private bool useDebugAutoInput = false;

    [Header("Local Preview")]
    [SerializeField] private bool updateLocalBitsWithoutSending = true;

    [SerializeField] private Footsies.FootsiesBattleInputHistory inputHistory;

    [Header("LAN input buffer")]
    [Tooltip("LANの通常の到着差を吸収する入力先送りフレーム数。両PCで同じ値を使用する。")]
    [Min(0)] [SerializeField] private int lanInputBufferFrames = 3;
    private bool useLanInputBuffer;
    private bool seededBuffer;
    private Footsies.BattleCore battleCore;
    public int LastSentInputFrame => lastSentFrame < 0 ? -1 : lastSentFrame+InputBufferFrames;
    public int InputBufferFrames => useLanInputBuffer ? lanInputBufferFrames : 0;
    public void EnableLanInputBuffer()
    {
        useLanInputBuffer=true;
        lanInputBufferFrames=Mathf.Clamp(lanInputBufferFrames,0,12);
        battleCore=FindObjectOfType<Footsies.BattleCore>(true);
    }
    private int lastSentFrame = -1;

    public byte LastLocalInputBits { get; private set; }
    public bool UsesDebugAutoInput => useDebugAutoInput;

    public void ConfigureRuntime(
        int playerId,
        KeyCode leftKey,
        KeyCode rightKey,
        KeyCode attackKey,
        bool useDebugAutoInput)
    {
        this.playerId = playerId;
        this.leftKey = leftKey;
        this.rightKey = rightKey;
        this.attackKey = attackKey;
        this.useDebugAutoInput = useDebugAutoInput;

        ResetSenderState();

        if (debugAutoInputSequence != null)
        {
            debugAutoInputSequence.ResetSequence();
        }

        FileLogger.WriteLine(
            $"[NetworkInputSender] ConfigureRuntime playerId={playerId}, leftKey={leftKey}, rightKey={rightKey}, attackKey={attackKey}, useDebugAutoInput={useDebugAutoInput}");
    }

    private void Update()
    {
        if (!updateLocalBitsWithoutSending || useLanInputBuffer)
        {
            return;
        }

        // 自動入力シーケンスは ProcessSendForFrame() 内だけで進める。
        // ここで進めると render frame 数に依存してしまう。
        if (useDebugAutoInput)
        {
            return;
        }

        LastLocalInputBits = ReadCurrentLocalInputBits();
    }

    public void ProcessSendForFrame(int frame)
    {
        if (frame < 0)
        {
            return;
        }

        if (frame == lastSentFrame)
        {
            return;
        }

        if(useLanInputBuffer && (transport == null || sessionManager == null || !transport.IsStarted || !sessionManager.Running)) return;
        byte inputBits = useLanInputBuffer && battleCore != null && battleCore.roundState != Footsies.BattleCore.RoundStateType.Fight
            ? (byte)0 : ReadCurrentLocalInputBits();
        int sendFrame=frame+InputBufferFrames;
        if(useLanInputBuffer && !seededBuffer)
        {
            for(int initial=frame;initial<sendFrame;initial++)
            {
                inputHistory?.StoreInput(playerId,initial,0);
                transport.Send(new NetworkPacket(NetworkPacketType.Input,playerId,initial,0,0));
            }
            seededBuffer=true;
        }
        LastLocalInputBits = inputBits;

        if (inputHistory != null)
        {
            inputHistory.StoreInput(playerId, sendFrame, inputBits);
        }

        if(useLanInputBuffer)
            LastLocalInputBits=inputHistory != null && inputHistory.TryGetInput(playerId,frame,out byte scheduled) ? scheduled : (byte)0;

        if (transport == null || sessionManager == null)
        {
            return;
        }

        if (!transport.IsStarted)
        {
            return;
        }

        if (!sessionManager.Running)
        {
            return;
        }

        NetworkPacket packet = new NetworkPacket(
            NetworkPacketType.Input,
            playerId,
            sendFrame,
            inputBits,
            0
        );

        transport.Send(packet);
        lastSentFrame = frame;

        FileLogger.WriteLine($"[NetworkInputSender] Sent Input frame={sendFrame} bits={inputBits}");
    }

    private byte ReadCurrentLocalInputBits()
    {
        if (useDebugAutoInput && debugAutoInputSequence != null)
        {
            return debugAutoInputSequence.GetBits();
        }

        return InputEncoder.ReadLocalInputBits(leftKey, rightKey, attackKey);
    }

    public void ResetSenderState()
    {
        seededBuffer=false;
        lastSentFrame = -1;
        LastLocalInputBits = 0;
    }
}
