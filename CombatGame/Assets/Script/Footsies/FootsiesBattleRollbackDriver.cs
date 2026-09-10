using UnityEngine;

namespace Footsies
{
    public class FootsiesBattleRollbackDriver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BattleCore battleCore;
        [SerializeField] private NetworkSessionManager sessionManager;
        [SerializeField] private NetworkFrameClock frameClock;
        [SerializeField] private NetworkInputSender inputSender;
        [SerializeField] private NetworkInputReceiver inputReceiver;
        [SerializeField] private AutoRollbackTrigger autoRollbackTrigger;
        [SerializeField] private FootsiesBattleRollbackCoordinator battleRollbackCoordinator;
        [SerializeField] private FootsiesBattleResimulationDriver battleResimulationDriver;

        private NetworkPacketDispatcher packetDispatcher;
        private FootsiesPredictedRemoteInputSource[] predictionSources;
        private void Start()
        {
            predictionSources = FindObjectsOfType<FootsiesPredictedRemoteInputSource>();
            packetDispatcher = FindObjectOfType<NetworkPacketDispatcher>();
        }
        public void BeginSynchronizedRound(int nextFrame)
        {
            FindObjectOfType<FootsiesBattleInputHistory>()?.ClearAll();
            FindObjectOfType<PredictionMismatchDetector>()?.ResetDetector();
            autoRollbackTrigger?.ResetTrigger();
            battleRollbackCoordinator.ClearAll();
            inputSender.ResetSenderState();
            inputReceiver?.BeginSynchronizedRound(nextFrame);
            frameClock.SetFrame(nextFrame);
            if(predictionSources != null)
                foreach(var source in predictionSources) source.ResetForNewRound();
        }

        private void FixedUpdate()
        {
            if (battleCore == null ||
                sessionManager == null ||
                frameClock == null ||
                inputSender == null ||
                battleRollbackCoordinator == null)
            {
                return;
            }

            if (!sessionManager.Running || battleCore.WaitingForSynchronizedRound)
            {
                return;
            }

            packetDispatcher?.PumpPackets();
            int currentFrame = frameClock.CurrentFrame;

            battleRollbackCoordinator.BeginStep();

            // 1. まず現在 frame 開始時点の snapshot を保存
            battleRollbackCoordinator.SaveSnapshotForCurrentFrame();

            // 2. rollback/resim でも currentFrame の local 入力を使えるように、先に送信＆履歴保存
            inputSender.ProcessSendForFrame(currentFrame);

            // 3. delayed input を解放して confirmed 化
            if (inputReceiver != null)
            {
                inputReceiver.ProcessDelayedInputsForCurrentStep();
            }

            // 4. miss があれば rollback request
            if (autoRollbackTrigger != null)
            {
                autoRollbackTrigger.ProcessAutoRollback();
            }

            // 5. rollback 実行
            battleRollbackCoordinator.ProcessRollbackIfNeeded();

            // Replay only completed frames. Simulate this frame once, with a fresh
            // prediction built after correction rather than the pre-rollback state.
            if (battleRollbackCoordinator.DidRollbackThisStep && battleResimulationDriver != null)
            {
                battleResimulationDriver.ProcessResimulationIfNeeded();
                battleRollbackCoordinator.SaveSnapshotForCurrentFrame();
            }
            if(predictionSources != null)
                foreach(var source in predictionSources) source.PreparePredictionForFrame(currentFrame);
            battleCore.DoFixedUpdate();

            // 7. frame を進める
            frameClock.Tick();
        }
    }
}
