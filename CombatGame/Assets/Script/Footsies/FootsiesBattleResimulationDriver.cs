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
                byte p1Bits = 0;
                byte p2Bits = 0;

                inputHistory.TryGetInput(0, frame, out p1Bits);
                inputHistory.TryGetInput(1, frame, out p2Bits);

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
    }
}
