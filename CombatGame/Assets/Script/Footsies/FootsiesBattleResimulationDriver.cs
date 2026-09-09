using UnityEngine;
using System.Diagnostics;

namespace Footsies
{
    public class FootsiesBattleResimulationDriver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BattleCore battleCore;
        [SerializeField] private FootsiesBattleInputRouter inputRouter;
        [SerializeField] private FootsiesBattleRollbackCoordinator rollbackCoordinator;
        [SerializeField] private FootsiesBattleInputHistory inputHistory;

        public void ProcessResimulationIfNeeded()
        {
            if (battleCore == null || inputRouter == null || rollbackCoordinator == null || inputHistory == null)
            {
                return;
            }

            if (!rollbackCoordinator.DidRollbackThisStep)
            {
                return;
            }

            int fromFrame = rollbackCoordinator.LastRollbackRestoreFromFrame;
            int toFrame = rollbackCoordinator.LastRollbackRestoreToFrame;

            if (fromFrame < 0 || toFrame < fromFrame)
            {
                return;
            }

            // 重要:
            // snapshot は「その frame 開始時点の状態」として保存されている前提なので、
            // rollback target frame 自体を再計算しないと、訂正された入力が反映されない。
            FileLogger.WriteLine(
                $"[FootsiesBattleResimulationDriver] Begin resim from={fromFrame} to={toFrame}");

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                for (int frame = fromFrame; frame <= toFrame; frame++)
                {
                    rollbackCoordinator.StoreSnapshot(frame,battleCore.CaptureSnapshot());
                    byte p1Bits = ResolveBitsForPlayer(0, frame);
                    byte p2Bits = ResolveBitsForPlayer(1, frame);

                    inputRouter.SetOverrideInputs(
                        FootsiesInputFrame.FromBits(p1Bits),
                        FootsiesInputFrame.FromBits(p2Bits)
                    );

                    battleCore.BeginResimulationFrame(frame);
                    battleCore.DoFixedUpdate();
                }
            }
            finally
            {
                stopwatch.Stop();
                inputRouter.ClearOverrideInputs();

                battleCore.RecordRollback(
                    fromFrame,
                    toFrame,
                    rollbackCoordinator.LastPredictedBits,
                    rollbackCoordinator.LastConfirmedBits,
                    rollbackCoordinator.LastRestoreTimeMs,
                    stopwatch.Elapsed.TotalMilliseconds,
                    rollbackCoordinator.LastP1PositionBeforeRollback,
                    rollbackCoordinator.LastP2PositionBeforeRollback);
                battleCore.CompleteResimulation();
            }

            FileLogger.WriteLine(
                $"[FootsiesBattleResimulationDriver] End resim from={fromFrame} to={toFrame}");
        }

        public bool TryEvaluateCorrection(FootsiesBattleSnapshot origin, int fromFrame, int currentFrame,
            out FootsiesBattleSnapshot corrected, out System.Collections.Generic.Dictionary<int,FootsiesBattleSnapshot> repaired)
        {
            corrected=null;
            repaired=new System.Collections.Generic.Dictionary<int,FootsiesBattleSnapshot>();
            if(battleCore == null || inputRouter == null || inputHistory == null || origin == null
                || origin.roundState != BattleCore.RoundStateType.Fight || battleCore.roundState != BattleCore.RoundStateType.Fight) return false;
            var shown=battleCore.CaptureSnapshot();
            battleCore.BeginPredictionVerification();
            try
            {
                battleCore.RestoreSnapshot(origin);
                for(int frame=fromFrame;frame<currentFrame;frame++)
                {
                    repaired[frame]=battleCore.CaptureSnapshot();
                    inputRouter.SetOverrideInputs(FootsiesInputFrame.FromBits(ResolveBitsForPlayer(0,frame)), FootsiesInputFrame.FromBits(ResolveBitsForPlayer(1,frame)));
                    battleCore.BeginResimulationFrame(frame);
                    battleCore.DoFixedUpdate();
                    if(battleCore.roundState != BattleCore.RoundStateType.Fight) return false;
                }
                corrected=battleCore.CaptureSnapshot();
                return true;
            }
            finally
            {
                inputRouter.ClearOverrideInputs();
                battleCore.RestoreSnapshot(shown);
                battleCore.EndPredictionVerification();
            }
        }

        private byte ResolveBitsForPlayer(int playerId, int frame)
        {
            if (inputHistory.TryGetInput(playerId, frame, out byte exactBits))
            {
                return exactBits;
            }

            if(inputHistory.TryGetAppliedPrediction(playerId,frame,out byte predictedBits)) return predictedBits;

            if (inputHistory.TryGetLatestInputAtOrBefore(playerId, frame, out byte latestBits))
            {
                return latestBits;
            }

            return 0;
        }
    }
}
