using UnityEngine;

public class NetworkBattleInputBridgeTester : MonoBehaviour
{
    [Header("Role")]
    [SerializeField] private bool isLocalPlayerP1 = true;

    [Header("References")]
    [SerializeField] private NetworkFrameClock frameClock;
    [SerializeField] private NetworkInputReceiver networkInputReceiver;

    private IFrameInputSource localSource;
    private IFrameInputSource remoteSource;
    private NetworkBattleInputRouter router;
    private NetworkBattleInputBridge bridge;

    private void Start()
    {
        if (frameClock == null)
        {
            Debug.LogError("[NetworkBattleInputBridgeTester] frameClock is null");
            return;
        }

        if (networkInputReceiver == null)
        {
            Debug.LogError("[NetworkBattleInputBridgeTester] networkInputReceiver is null");
            return;
        }

        localSource = new LocalFrameInputSource();
        remoteSource = new RemoteFrameInputSource(networkInputReceiver.Buffer);

        if (isLocalPlayerP1)
        {
            router = new NetworkBattleInputRouter(localSource, remoteSource);
            Debug.Log("[NetworkBattleInputBridgeTester] Local player is P1");
        }
        else
        {
            router = new NetworkBattleInputRouter(remoteSource, localSource);
            Debug.Log("[NetworkBattleInputBridgeTester] Local player is P2");
        }

        bridge = new NetworkBattleInputBridge(router);
    }

    private void Update()
    {
        if (bridge == null || frameClock == null)
        {
            return;
        }

        int frame = frameClock.CurrentFrame;

        if (frame % 30 != 0)
        {
            return;
        }

        FrameInputState p1 = bridge.GetP1InputState(frame);
        FrameInputState p2 = bridge.GetP2InputState(frame);

        Debug.Log($"[BRIDGE] frame={frame} | P1=({p1}) | P2=({p2})");
    }
}
