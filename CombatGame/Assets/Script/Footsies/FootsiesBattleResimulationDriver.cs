using UnityEngine;

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

            FileLogger.WriteLine(
                $"[FootsiesBattleResimulationDriver] Begin resim from={fromFrame + 1} to={toFrame}");

            for (int frame = fromFrame + 1; frame <= toFrame; frame++)
            {
                byte p1Bits = ResolveBitsForPlayer(0, frame);
                byte p2Bits = ResolveBitsForPlayer(1, frame);

                inputRouter.SetOverrideInputs(
                    FootsiesInputFrame.FromBits(p1Bits),
                    FootsiesInputFrame.FromBits(p2Bits)
                );

                battleCore.DoFixedUpdate();
            }

            inputRouter.ClearOverrideInputs();

            FileLogger.WriteLine(
                $"[FootsiesBattleResimulationDriver] End resim from={fromFrame + 1} to={toFrame}");
        }

        private byte ResolveBitsForPlayer(int playerId, int frame)
        {
            if (inputHistory.TryGetInput(playerId, frame, out byte exactBits))
            {
                return exactBits;
            }

            if (inputHistory.TryGetLatestInputAtOrBefore(playerId, frame, out byte latestBits))
            {
                return latestBits;
            }

            return 0;
        }
    }
}
