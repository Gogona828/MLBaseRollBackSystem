using UnityEngine;

public class NetworkInputRoutingTester : MonoBehaviour
{
    [Header("Role")]
    [SerializeField] private bool isLocalPlayerP1 = true;

    [Header("References")]
    [SerializeField] private NetworkFrameClock frameClock;
    [SerializeField] private NetworkInputReceiver networkInputReceiver;

    private IFrameInputSource localSource;
    private IFrameInputSource remoteSource;
    private NetworkBattleInputRouter router;

    private void Start()
    {
        if (frameClock == null)
        {
            Debug.LogError("[NetworkInputRoutingTester] frameClock is null");
            return;
        }

        if (networkInputReceiver == null)
        {
            Debug.LogError("[NetworkInputRoutingTester] networkInputReceiver is null");
            return;
        }

        localSource = new LocalFrameInputSource();
        remoteSource = new RemoteFrameInputSource(networkInputReceiver.Buffer);

        if (isLocalPlayerP1)
        {
            router = new NetworkBattleInputRouter(localSource, remoteSource);
            Debug.Log("[NetworkInputRoutingTester] Local player is P1");
        }
        else
        {
            router = new NetworkBattleInputRouter(remoteSource, localSource);
            Debug.Log("[NetworkInputRoutingTester] Local player is P2");
        }
    }

    private void Update()
    {
        if (router == null || frameClock == null)
        {
            return;
        }

        int frame = frameClock.CurrentFrame;

        byte p1Bits = router.GetP1InputBits(frame);
        byte p2Bits = router.GetP2InputBits(frame);

        string p1Readable = InputEncoder.ToReadableString(p1Bits);
        string p2Readable = InputEncoder.ToReadableString(p2Bits);

        Debug.Log(
            $"[ROUTER] frame={frame} | P1={p1Bits} ({p1Readable}) | P2={p2Bits} ({p2Readable})");
    }
}
