using UnityEngine;

namespace Footsies
{
    public static class RollbackStateComparison
    {
        public static bool CanContinue(FootsiesBattleSnapshot shown, FootsiesBattleSnapshot corrected, float positionTolerance)
        {
            if(shown == null || corrected == null) return false;
            return shown.roundState == corrected.roundState && shown.frameCount == corrected.frameCount
                && shown.fighter1RoundWon == corrected.fighter1RoundWon && shown.fighter2RoundWon == corrected.fighter2RoundWon
                && shown.hasPendingKO == corrected.hasPendingKO && shown.pendingKOFighterSlot == corrected.pendingKOFighterSlot
                && shown.pendingKOStableFrames == corrected.pendingKOStableFrames
                && FighterMatches(shown.fighter1,corrected.fighter1,positionTolerance)
                && FighterMatches(shown.fighter2,corrected.fighter2,positionTolerance);
        }
        private static bool FighterMatches(FootsiesFighterSnapshot a, FootsiesFighterSnapshot b, float tolerance)
        {
            if(a == null || b == null || float.IsNaN(tolerance) || float.IsInfinity(tolerance)) return false;
            tolerance=Mathf.Max(0,tolerance);
            return a.vitalHealth == b.vitalHealth && a.guardHealth == b.guardHealth
                && a.currentActionID == b.currentActionID && a.currentActionFrame == b.currentActionFrame
                && a.currentActionHitCount == b.currentActionHitCount && a.currentHitStunFrame == b.currentHitStunFrame
                && a.isFaceRight == b.isFaceRight && a.velocityX == b.velocityX
                && (a.position-b.position).sqrMagnitude <= tolerance*tolerance;
        }
    }
}
