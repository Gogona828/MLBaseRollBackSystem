using System.Reflection;
using UnityEngine;

namespace Footsies
{
    /// <summary>
    /// 既存の Fighter.vitalHealth が 1 固定のままでも、
    /// BattleCore.damageHandler の直後に HP を補正して、
    /// 「3回ダメージを当てたらラウンド取得」にするためのルール補正。
    /// </summary>
    public class ThreeHitRoundRule : MonoBehaviour
    {
        [SerializeField] private int hitsToWinRound = 3;

        private BattleCore battleCore;
        private bool subscribed;
        private BattleCore.RoundStateType lastRoundState = BattleCore.RoundStateType.Stop;

        private int fighter1DamageTakenHits;
        private int fighter2DamageTakenHits;
        private bool healthInitializedForCurrentFight;

        private static readonly BindingFlags FighterBackingFieldFlags =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly FieldInfo VitalHealthField =
            typeof(Fighter).GetField("<vitalHealth>k__BackingField", FighterBackingFieldFlags);

        private static readonly FieldInfo CurrentActionIdField =
            typeof(Fighter).GetField("<currentActionID>k__BackingField", FighterBackingFieldFlags);

        private static readonly FieldInfo CurrentActionFrameField =
            typeof(Fighter).GetField("<currentActionFrame>k__BackingField", FighterBackingFieldFlags);

        private static readonly FieldInfo CurrentHitStunFrameField =
            typeof(Fighter).GetField("<currentHitStunFrame>k__BackingField", FighterBackingFieldFlags);

        public void Configure(int hitsToWinRound)
        {
            this.hitsToWinRound = Mathf.Max(1, hitsToWinRound);
            ResetRoundCounters();
            TrySubscribe();
            ForceRoundHealth();
        }

        private void Awake()
        {
            hitsToWinRound = Mathf.Max(1, hitsToWinRound);
            TrySubscribe();
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();
            ResetRoundCounters();
            ForceRoundHealth();
        }

        private void Update()
        {
            TrySubscribe();

            if (battleCore == null)
            {
                return;
            }

            if (battleCore.roundState != lastRoundState)
            {
                if (battleCore.roundState == BattleCore.RoundStateType.Intro
                    || battleCore.roundState == BattleCore.RoundStateType.Fight)
                {
                    ResetRoundCounters();
                    ForceRoundHealth();
                }

                lastRoundState = battleCore.roundState;
            }

            if (battleCore.roundState == BattleCore.RoundStateType.Fight
                && !healthInitializedForCurrentFight)
            {
                ForceRoundHealth();
                healthInitializedForCurrentFight = true;
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void TrySubscribe()
        {
            if (subscribed)
            {
                return;
            }

            if (battleCore == null)
            {
                battleCore = GetComponent<BattleCore>();
                if (battleCore == null)
                {
                    battleCore = FindObjectOfType<BattleCore>();
                }
            }

            if (battleCore == null)
            {
                return;
            }

            battleCore.damageHandler += OnDamageResolved;
            subscribed = true;
            lastRoundState = battleCore.roundState;

            FileLogger.WriteLine($"[ThreeHitRoundRule] subscribed hitsToWinRound={hitsToWinRound}");
        }

        private void Unsubscribe()
        {
            if (!subscribed || battleCore == null)
            {
                return;
            }

            battleCore.damageHandler -= OnDamageResolved;
            subscribed = false;
        }

        private void OnDamageResolved(Fighter damaged, Vector2 damagePos, DamageResult damageResult)
        {
            if (battleCore == null || damaged == null)
            {
                return;
            }

            if (damageResult != DamageResult.Damage)
            {
                return;
            }

            if (damaged == battleCore.fighter1)
            {
                fighter1DamageTakenHits++;
                ApplyRemainingHealth(damaged, fighter1DamageTakenHits);
            }
            else if (damaged == battleCore.fighter2)
            {
                fighter2DamageTakenHits++;
                ApplyRemainingHealth(damaged, fighter2DamageTakenHits);
            }
        }

        private void ResetRoundCounters()
        {
            fighter1DamageTakenHits = 0;
            fighter2DamageTakenHits = 0;
            healthInitializedForCurrentFight = false;
        }

        private void ForceRoundHealth()
        {
            if (battleCore == null)
            {
                return;
            }

            SetVitalHealth(battleCore.fighter1, hitsToWinRound);
            SetVitalHealth(battleCore.fighter2, hitsToWinRound);
        }

        private void ApplyRemainingHealth(Fighter damaged, int damageTakenHits)
        {
            int remainingHealth = Mathf.Max(0, hitsToWinRound - damageTakenHits);
            SetVitalHealth(damaged, remainingHealth);

            if (remainingHealth <= 0)
            {
                ForceAction(damaged, (int)CommonActionID.DEAD);
            }
            else if (damaged.currentActionID == (int)CommonActionID.DEAD)
            {
                ForceAction(damaged, (int)CommonActionID.DAMAGE);
            }

            FileLogger.WriteLine(
                $"[ThreeHitRoundRule] damagedHits={damageTakenHits}/{hitsToWinRound}, remainingHealth={remainingHealth}");
        }

        private void SetVitalHealth(Fighter fighter, int value)
        {
            if (fighter == null || VitalHealthField == null)
            {
                return;
            }

            VitalHealthField.SetValue(fighter, Mathf.Max(0, value));
        }

        private void ForceAction(Fighter fighter, int actionId)
        {
            if (fighter == null)
            {
                return;
            }

            CurrentActionIdField?.SetValue(fighter, actionId);
            CurrentActionFrameField?.SetValue(fighter, 0);
            CurrentHitStunFrameField?.SetValue(fighter, 0);
            fighter.UpdateBoxes();
        }
    }
}
