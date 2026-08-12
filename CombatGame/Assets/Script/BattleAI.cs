using System.Collections.Generic;
using UnityEngine;

namespace Footsies
{
    /// <summary>
    /// オフライン対戦用のルールベースCPU。
    /// 距離と相手の行動を見て、プレイヤーと同じ左右・攻撃入力だけを生成する。
    /// </summary>
    public class BattleAI
    {
        public class FightState
        {
            public float distanceX;
            public int opponentActionId;
            public bool isOpponentDamage;
            public bool isOpponentGuardBreak;
            public bool isOpponentBlocking;
            public bool isOpponentNormalAttack;
            public bool isOpponentSpecialAttack;
        }

        private const float FarDistance = 4.2f;
        private const float ApproachDistance = 3.0f;
        private const float AttackDistance = 2.8f;
        private const float TooCloseDistance = 1.8f;

        private readonly BattleCore battleCore;
        private readonly Queue<int> moveQueue = new Queue<int>();
        private readonly Queue<int> attackQueue = new Queue<int>();

        private int movementChoiceIndex;
        private int attackChoiceIndex;
        private int defensiveFramesRemaining;
        private int lastOpponentActionId = -1;

        public BattleAI(BattleCore core)
        {
            battleCore = core;
        }

        public int getNextAIInput()
        {
            return GetNextAIInput();
        }

        public int GetNextAIInput()
        {
            FightState fightState = ReadFightState();
            if (fightState == null)
            {
                return 0;
            }

            bool opponentActionChanged = fightState.opponentActionId != lastOpponentActionId;
            lastOpponentActionId = fightState.opponentActionId;

            ApplyReactionRules(fightState, opponentActionChanged);
            if (defensiveFramesRemaining > 0)
            {
                defensiveFramesRemaining--;
                return GetBackwardInput();
            }

            if (moveQueue.Count == 0)
            {
                SelectMovement(fightState);
            }

            if (attackQueue.Count == 0)
            {
                SelectAttack(fightState);
            }

            int input = 0;
            if (moveQueue.Count > 0)
            {
                input |= moveQueue.Dequeue();
            }

            if (attackQueue.Count > 0)
            {
                input |= attackQueue.Dequeue();
            }

            return input;
        }

        private void ApplyReactionRules(FightState fightState, bool opponentActionChanged)
        {
            if (!opponentActionChanged)
            {
                return;
            }

            // 必殺技と近距離の通常技には後退入力でガードする。
            if (fightState.isOpponentSpecialAttack
                || (fightState.isOpponentNormalAttack && fightState.distanceX <= AttackDistance))
            {
                moveQueue.Clear();
                attackQueue.Clear();
                defensiveFramesRemaining = fightState.isOpponentSpecialAttack ? 20 : 10;
                return;
            }

            // 被弾・ガード崩れを確認したら、現在のプランを破棄して反撃する。
            if (fightState.isOpponentDamage || fightState.isOpponentGuardBreak)
            {
                moveQueue.Clear();
                attackQueue.Clear();

                if (fightState.distanceX > AttackDistance)
                {
                    AddMidApproach2();
                }

                AddTwoHitImmediateAttack();
            }
        }

        private void SelectMovement(FightState fightState)
        {
            if (fightState.distanceX > FarDistance)
            {
                if (movementChoiceIndex++ % 2 == 0)
                {
                    AddFarApproach1();
                }
                else
                {
                    AddFarApproach2();
                }

                return;
            }

            if (fightState.distanceX > ApproachDistance)
            {
                if (movementChoiceIndex++ % 2 == 0)
                {
                    AddMidApproach1();
                }
                else
                {
                    AddMidApproach2();
                }

                return;
            }

            if (fightState.distanceX < TooCloseDistance)
            {
                if (movementChoiceIndex++ % 2 == 0)
                {
                    AddFallBack1();
                }
                else
                {
                    AddFallBack2();
                }

                return;
            }

            // 相手がガードを固めている間は一度間合いを外す。
            if (fightState.isOpponentBlocking)
            {
                AddFallBack1();
            }
            else
            {
                AddNeutralMovement();
            }
        }

        private void SelectAttack(FightState fightState)
        {
            if (fightState.isOpponentDamage || fightState.isOpponentGuardBreak)
            {
                AddTwoHitImmediateAttack();
                return;
            }

            if (fightState.distanceX > FarDistance)
            {
                // 遠距離では接近を優先し、ときどき接近中に必殺技を溜める。
                if (attackChoiceIndex++ % 3 == 2)
                {
                    AddDelaySpecialAttack();
                }
                else
                {
                    AddNoAttack();
                }

                return;
            }

            if (fightState.distanceX > AttackDistance)
            {
                if (fightState.isOpponentNormalAttack)
                {
                    AddTwoHitImmediateAttack();
                }
                else if (attackChoiceIndex++ % 3 == 2)
                {
                    AddDelaySpecialAttack();
                }
                else
                {
                    AddNoAttack();
                }

                return;
            }

            // 攻撃間合いでは単発、連係、必殺技を順番に使う。
            switch (attackChoiceIndex++ % 4)
            {
                case 0:
                    AddOneHitImmediateAttack();
                    break;
                case 1:
                case 3:
                    AddTwoHitImmediateAttack();
                    break;
                default:
                    AddImmediateSpecialAttack();
                    break;
            }
        }

        private void AddNeutralMovement()
        {
            Enqueue(moveQueue, 0, 18);
        }

        private void AddFarApproach1()
        {
            AddForwardInputQueue(24);
            Enqueue(moveQueue, 0, 6);
        }

        private void AddFarApproach2()
        {
            AddForwardDashInputQueue();
            AddForwardInputQueue(18);
            Enqueue(moveQueue, 0, 6);
        }

        private void AddMidApproach1()
        {
            AddForwardInputQueue(14);
            Enqueue(moveQueue, 0, 8);
        }

        private void AddMidApproach2()
        {
            AddForwardDashInputQueue();
            Enqueue(moveQueue, 0, 12);
        }

        private void AddFallBack1()
        {
            AddBackwardInputQueue(16);
            Enqueue(moveQueue, 0, 8);
        }

        private void AddFallBack2()
        {
            AddBackwardDashInputQueue();
            Enqueue(moveQueue, 0, 12);
        }

        private void AddNoAttack()
        {
            Enqueue(attackQueue, 0, 24);
        }

        private void AddOneHitImmediateAttack()
        {
            attackQueue.Enqueue(GetAttackInput());
            Enqueue(attackQueue, 0, 18);
        }

        private void AddTwoHitImmediateAttack()
        {
            attackQueue.Enqueue(GetAttackInput());
            Enqueue(attackQueue, 0, 3);
            attackQueue.Enqueue(GetAttackInput());
            Enqueue(attackQueue, 0, 18);
        }

        private void AddImmediateSpecialAttack()
        {
            Enqueue(attackQueue, GetAttackInput(), 60);
            attackQueue.Enqueue(0);
            Enqueue(attackQueue, 0, 18);
        }

        private void AddDelaySpecialAttack()
        {
            Enqueue(attackQueue, GetAttackInput(), 90);
            attackQueue.Enqueue(0);
            Enqueue(attackQueue, 0, 18);
        }

        private void AddForwardInputQueue(int frameCount)
        {
            Enqueue(moveQueue, GetForwardInput(), frameCount);
        }

        private void AddBackwardInputQueue(int frameCount)
        {
            Enqueue(moveQueue, GetBackwardInput(), frameCount);
        }

        private void AddForwardDashInputQueue()
        {
            moveQueue.Enqueue(GetForwardInput());
            moveQueue.Enqueue(0);
            moveQueue.Enqueue(GetForwardInput());
        }

        private void AddBackwardDashInputQueue()
        {
            moveQueue.Enqueue(GetBackwardInput());
            moveQueue.Enqueue(0);
            moveQueue.Enqueue(GetBackwardInput());
        }

        private FightState ReadFightState()
        {
            if (battleCore == null || battleCore.fighter1 == null || battleCore.fighter2 == null)
            {
                return null;
            }

            int opponentActionId = battleCore.fighter1.currentActionID;
            return new FightState
            {
                distanceX = Mathf.Abs(battleCore.fighter2.position.x - battleCore.fighter1.position.x),
                opponentActionId = opponentActionId,
                isOpponentDamage = opponentActionId == (int)CommonActionID.DAMAGE,
                isOpponentGuardBreak = opponentActionId == (int)CommonActionID.GUARD_BREAK,
                isOpponentBlocking = opponentActionId == (int)CommonActionID.GUARD_CROUCH
                    || opponentActionId == (int)CommonActionID.GUARD_STAND
                    || opponentActionId == (int)CommonActionID.GUARD_M,
                isOpponentNormalAttack = opponentActionId == (int)CommonActionID.N_ATTACK
                    || opponentActionId == (int)CommonActionID.B_ATTACK,
                isOpponentSpecialAttack = opponentActionId == (int)CommonActionID.N_SPECIAL
                    || opponentActionId == (int)CommonActionID.B_SPECIAL
            };
        }

        private static void Enqueue(Queue<int> queue, int input, int frameCount)
        {
            for (int i = 0; i < frameCount; i++)
            {
                queue.Enqueue(input);
            }
        }

        private static int GetAttackInput()
        {
            return (int)InputDefine.Attack;
        }

        // CPUは2P側（左向き）なので、左が前進、右が後退。
        private static int GetForwardInput()
        {
            return (int)InputDefine.Left;
        }

        private static int GetBackwardInput()
        {
            return (int)InputDefine.Right;
        }
    }
}
