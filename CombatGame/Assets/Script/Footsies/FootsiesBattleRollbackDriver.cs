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

            if (!sessionManager.Running)
            {
                return;
            }

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

            // 6. rollback が起きたら resim、起きていなければ通常 1 frame 進める
            if (battleRollbackCoordinator.DidRollbackThisStep)
            {
                if (battleResimulationDriver != null)
                {
                    battleResimulationDriver.ProcessResimulationIfNeeded();
                }

                FileLogger.WriteLine(
                    $"[FootsiesBattleRollbackDriver] Skipped normal simulation for frame={currentFrame} because rollback occurred this step.");
            }
            else
            {
                battleCore.DoFixedUpdate();
            }

            // 7. frame を進める
            frameClock.Tick();
        }
    }
}
