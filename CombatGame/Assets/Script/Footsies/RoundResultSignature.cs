using System;
using UnityEngine;

namespace Footsies
{
    [Serializable]
    public struct RoundResultSignature
    {
        public int roundSerial;
        public int senderPlayerId;

        public int koStateFrame;
        public int rollbackTargetFrame;

        // 1 = P1 が負け, 2 = P2 が負け, -1 = 不明
        public int loserSlot;

        public int p1Hp;
        public int p2Hp;

        public int p1Action;
        public int p2Action;

        public int p1PosX1000;
        public int p2PosX1000;

        public int agreedStateHash;

        public static RoundResultSignature Create(
            int senderPlayerId,
            int roundSerial,
            int koStateFrame,
            int rollbackTargetFrame,
            Fighter fighter1,
            Fighter fighter2)
        {
            RoundResultSignature sig = new RoundResultSignature
            {
                roundSerial = roundSerial,
                senderPlayerId = senderPlayerId,
                koStateFrame = koStateFrame,
                rollbackTargetFrame = rollbackTargetFrame,
                loserSlot = ResolveLoserSlot(fighter1, fighter2),
                p1Hp = fighter1 != null ? fighter1.vitalHealth : -1,
                p2Hp = fighter2 != null ? fighter2.vitalHealth : -1,
                p1Action = fighter1 != null ? fighter1.currentActionID : -1,
                p2Action = fighter2 != null ? fighter2.currentActionID : -1,
                p1PosX1000 = fighter1 != null ? Mathf.RoundToInt(fighter1.position.x * 1000f) : 0,
                p2PosX1000 = fighter2 != null ? Mathf.RoundToInt(fighter2.position.x * 1000f) : 0,
            };

            sig.agreedStateHash = sig.ComputeHash();
            return sig;
        }

        public int ComputeHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + roundSerial;
                hash = hash * 31 + koStateFrame;
                hash = hash * 31 + rollbackTargetFrame;
                hash = hash * 31 + loserSlot;
                hash = hash * 31 + p1Hp;
                hash = hash * 31 + p2Hp;
                hash = hash * 31 + p1Action;
                hash = hash * 31 + p2Action;
                hash = hash * 31 + p1PosX1000;
                hash = hash * 31 + p2PosX1000;
                return hash;
            }
        }

        public bool EqualsForAgreement(RoundResultSignature other)
        {
            return roundSerial == other.roundSerial
                && loserSlot == other.loserSlot
                && agreedStateHash == other.agreedStateHash;
        }

        public static int ResolveLoserSlot(Fighter fighter1, Fighter fighter2)
        {
            bool p1Dead = fighter1 != null && fighter1.isDead;
            bool p2Dead = fighter2 != null && fighter2.isDead;

            if (p1Dead && !p2Dead)
            {
                return 1;
            }

            if (!p1Dead && p2Dead)
            {
                return 2;
            }

            return -1;
        }

        public override string ToString()
        {
            return $"round={roundSerial}, sender={senderPlayerId}, koFrame={koStateFrame}, rollbackTarget={rollbackTargetFrame}, loser={loserSlot}, p1hp={p1Hp}, p2hp={p2Hp}, p1a={p1Action}, p2a={p2Action}, p1x={p1PosX1000}, p2x={p2PosX1000}, hash={agreedStateHash}";
        }
    }
}
