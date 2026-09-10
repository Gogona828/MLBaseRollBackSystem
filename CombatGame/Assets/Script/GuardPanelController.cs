using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Footsies
{
    /// <summary>
    /// Set guard sprite numbers with guard health from each player
    /// </summary>
    [ExecuteAlways]
    public class GuardPanelController : MonoBehaviour
    {
        [SerializeField]
        private GameObject _battleCoreGameObject;
        
        [SerializeField]
        private bool isPlayerOne;

        [SerializeField]
        private GameObject[] guardImageObjects;

        [Header("HP & Scale Settings")]
        [Tooltip("Inspectorで直接HP数（最大ガード耐久値）を変更できる値")]
        [SerializeField, Range(1, 10)]
        private int maxGuardHealth = 5;

        [Tooltip("BattleCoreが割り当てられている場合、BattleCoreのHP設定と同期するか")]
        [SerializeField]
        private bool syncWithBattleCore = true;

        [Tooltip("HP数に応じてアイコンの間隔も自動調整するか")]
        [SerializeField]
        private bool adjustSpacingWithScale = true;

        [SerializeField]
        private float baseSpacing = 125f;

        [SerializeField]
        private float baseStartX = 75f;

        #region private field

        private BattleCore battleCore;

        private int currentGuardHealth = 0;
        private int lastMaxGuardHealth = -1;

        #endregion

        public int MaxGuardHealth
        {
            get => maxGuardHealth;
            set
            {
                maxGuardHealth = value;
                ApplyLayoutAndScale();
                UpdateGuardHealthImages();
            }
        }

        public GameObject[] GuardImageObjects => guardImageObjects;

        void Awake()
        {
            InitBattleCore();
            currentGuardHealth = maxGuardHealth;
            ApplyLayoutAndScale();
            UpdateGuardHealthImages();
        }

        void Start()
        {
            InitBattleCore();
            SyncFromBattleCoreIfAvailable();
            ApplyLayoutAndScale();
            UpdateGuardHealthImages();
        }

        private void InitBattleCore()
        {
            if (battleCore == null && _battleCoreGameObject != null)
            {
                battleCore = _battleCoreGameObject.GetComponent<BattleCore>();
            }
        }

        private void OnValidate()
        {
            if (guardImageObjects != null && guardImageObjects.Length > 0)
            {
                ApplyLayoutAndScale();
                if (!Application.isPlaying)
                {
                    currentGuardHealth = maxGuardHealth;
                    UpdateGuardHealthImages();
                }
            }
        }

        void Update()
        {
            if (Application.isPlaying)
            {
                SyncFromBattleCoreIfAvailable();

                int health = getGuardHealth();
                if (currentGuardHealth != health || lastMaxGuardHealth != maxGuardHealth)
                {
                    currentGuardHealth = health;
                    lastMaxGuardHealth = maxGuardHealth;
                    ApplyLayoutAndScale();
                    UpdateGuardHealthImages();
                }
            }
        }

        private void SyncFromBattleCoreIfAvailable()
        {
            if (syncWithBattleCore && battleCore != null)
            {
                int coreHp = battleCore.InitialGuardHealth;
                if (coreHp > 0 && maxGuardHealth != coreHp)
                {
                    maxGuardHealth = coreHp;
                    ApplyLayoutAndScale();
                }
            }
        }

        private int getGuardHealth()
        {
            if (battleCore == null)
                return maxGuardHealth;

            if (isPlayerOne)
                return battleCore.fighter1 != null ? battleCore.fighter1.guardHealth : maxGuardHealth;
            else
                return battleCore.fighter2 != null ? battleCore.fighter2.guardHealth : maxGuardHealth;
        }

        /// <summary>
        /// HPの数に応じてGuard Imageのスケールを縮小。HPが3以下のときは1.0f。
        /// </summary>
        public float CalculateScale()
        {
            if (maxGuardHealth <= 3)
            {
                return 1.0f;
            }
            return 3.0f / maxGuardHealth;
        }

        /// <summary>
        /// スケールと配置間隔を適用
        /// </summary>
        public void ApplyLayoutAndScale()
        {
            if (guardImageObjects == null || guardImageObjects.Length == 0) return;

            float scale = CalculateScale();
            float sign = isPlayerOne ? 1f : -1f;

            for (int i = 0; i < guardImageObjects.Length; i++)
            {
                if (guardImageObjects[i] == null) continue;

                // スケール変更
                guardImageObjects[i].transform.localScale = new Vector3(scale, scale, 1f);

                // 間隔の自動調整
                if (adjustSpacingWithScale)
                {
                    RectTransform rt = guardImageObjects[i].GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        float spacing = (maxGuardHealth > 3) ? (baseSpacing * scale) : baseSpacing;
                        float posX = sign * (baseStartX + i * spacing);
                        rt.anchoredPosition = new Vector2(posX, rt.anchoredPosition.y);
                    }
                }
            }
        }

        private void UpdateGuardHealthImages()
        {
            if (guardImageObjects == null) return;

            for (int i = 0; i < guardImageObjects.Length; i++)
            {
                if (guardImageObjects[i] == null) continue;

                // maxGuardHealth を超えるインデックスのオブジェクトは常に非表示
                if (i >= maxGuardHealth)
                {
                    guardImageObjects[i].SetActive(false);
                }
                else if (i <= currentGuardHealth - 1)
                {
                    guardImageObjects[i].SetActive(true);
                }
                else
                {
                    guardImageObjects[i].SetActive(false);
                }
            }
        }
    }
}
