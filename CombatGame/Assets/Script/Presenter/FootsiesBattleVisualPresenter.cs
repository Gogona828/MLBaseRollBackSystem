using UnityEngine;
using UnityEngine.UI;

namespace Footsies
{
    /// <summary>
    /// Rollback コア state は変更せず、
    /// Canvas 上の RectTransform / Image に対して
    /// 表示だけを滑らかに追従させる最小 View 層。
    ///
    /// 想定:
    /// - BattleCore.fighter1 / fighter2 がゲームの真実の state
    /// - Canvas 内に P1 / P2 の Image がある
    /// - その RectTransform の anchoredPosition をこの script が更新する
    ///
    /// 注意:
    /// - 既存の別スクリプトが同じ RectTransform.anchoredPosition を毎フレーム更新している場合、
    ///   その位置更新は止めてください
    /// - Sprite の切り替えもこの script に任せるなら、既存の Image.sprite 更新も止めてください
    /// </summary>
    public class FootsiesBattleCanvasVisualPresenter : MonoBehaviour
    {
        [System.Serializable]
        private class FighterCanvasViewBinding
        {
            [Header("Scene References")]
            public RectTransform root;
            public Image image;

            [Header("Canvas Position Mapping")]
            [Tooltip("fight 座標 0,0 を Canvas 上のどこへ置くか")]
            public Vector2 canvasOrigin = Vector2.zero;

            [Tooltip("fight の 1 unit を Canvas 上で何 px として扱うか")]
            public float canvasUnitsPerFightUnit = 100f;

            [Header("Optional Offsets")]
            public float extraX = 0f;
            public float extraY = 0f;

            [Header("Smoothing")]
            [Tooltip("小さいズレは SmoothDamp で滑らかに寄せる")]
            public float smoothTime = 0.045f;

            [Tooltip("これ以上ズレたら補間せず即スナップ")]
            public float snapDistance = 80f;

            [Tooltip("追従最大速度")]
            public float maxSpeed = 10000f;

            [Header("Visual")]
            [Tooltip("spriteShakePosition 1 あたり何 px ずらすか")]
            public float shakeScale = 1.0f;

            [Tooltip("faceRight=false のとき横反転する")]
            public bool flipByLocalScale = true;

            [HideInInspector] public Vector2 renderAnchoredPosition;
            [HideInInspector] public Vector2 renderVelocity;
            [HideInInspector] public bool initialized;
            [HideInInspector] public Sprite lastSprite;
            [HideInInspector] public bool lastFaceRight = true;
        }

        [Header("Core")]
        [SerializeField] private BattleCore battleCore;

        [Header("Player 1 View")]
        [SerializeField] private FighterCanvasViewBinding p1 = new FighterCanvasViewBinding();

        [Header("Player 2 View")]
        [SerializeField] private FighterCanvasViewBinding p2 = new FighterCanvasViewBinding();

        private void LateUpdate()
        {
            if (battleCore == null)
            {
                return;
            }

            UpdateFighterView(battleCore.fighter1, p1);
            UpdateFighterView(battleCore.fighter2, p2);
        }

        private void UpdateFighterView(Fighter fighter, FighterCanvasViewBinding view)
        {
            if (fighter == null || view == null || view.root == null)
            {
                return;
            }

            Vector2 target = BuildTargetAnchoredPosition(fighter, view);

            if (!view.initialized)
            {
                view.renderAnchoredPosition = target;
                view.renderVelocity = Vector2.zero;
                view.initialized = true;
            }
            else
            {
                float distance = Vector2.Distance(view.renderAnchoredPosition, target);

                if (distance >= view.snapDistance)
                {
                    // rollback で大きくズレたときは即スナップ
                    view.renderAnchoredPosition = target;
                    view.renderVelocity = Vector2.zero;
                }
                else
                {
                    view.renderAnchoredPosition = Vector2.SmoothDamp(
                        current: view.renderAnchoredPosition,
                        target: target,
                        currentVelocity: ref view.renderVelocity,
                        smoothTime: Mathf.Max(0.0001f, view.smoothTime),
                        maxSpeed: Mathf.Max(0.01f, view.maxSpeed),
                        deltaTime: Time.unscaledDeltaTime
                    );
                }
            }

            view.root.anchoredPosition = view.renderAnchoredPosition;

            UpdateSprite(fighter, view);
            UpdateFacing(fighter, view);
        }

        private Vector2 BuildTargetAnchoredPosition(Fighter fighter, FighterCanvasViewBinding view)
        {
            float x = view.canvasOrigin.x
                + (fighter.position.x * view.canvasUnitsPerFightUnit)
                + (fighter.spriteShakePosition * view.shakeScale)
                + view.extraX;

            float y = view.canvasOrigin.y
                + (fighter.position.y * view.canvasUnitsPerFightUnit)
                + view.extraY;

            return new Vector2(x, y);
        }

        private void UpdateSprite(Fighter fighter, FighterCanvasViewBinding view)
        {
            if (view.image == null)
            {
                return;
            }

            Sprite sprite = fighter.GetCurrentMotionSpriteSafe();
            if (sprite != view.lastSprite)
            {
                view.image.sprite = sprite;
                view.lastSprite = sprite;
            }
        }

        private void UpdateFacing(Fighter fighter, FighterCanvasViewBinding view)
        {
            if (!view.flipByLocalScale || view.root == null)
            {
                return;
            }

            bool faceRight = fighter.isFaceRight;
            if (faceRight == view.lastFaceRight)
            {
                return;
            }

            Vector3 localScale = view.root.localScale;
            float absX = Mathf.Abs(localScale.x);
            if (absX <= 0.0001f)
            {
                absX = 1f;
            }

            localScale.x = faceRight ? absX : -absX;
            view.root.localScale = localScale;
            view.lastFaceRight = faceRight;
        }

        [ContextMenu("Snap UI To Core State")]
        public void SnapUIToCoreState()
        {
            if (battleCore == null)
            {
                return;
            }

            SnapOne(battleCore.fighter1, p1);
            SnapOne(battleCore.fighter2, p2);
        }

        private void SnapOne(Fighter fighter, FighterCanvasViewBinding view)
        {
            if (fighter == null || view == null || view.root == null)
            {
                return;
            }

            view.renderAnchoredPosition = BuildTargetAnchoredPosition(fighter, view);
            view.renderVelocity = Vector2.zero;
            view.root.anchoredPosition = view.renderAnchoredPosition;
            view.initialized = true;

            UpdateSprite(fighter, view);
            UpdateFacing(fighter, view);
        }
    }
}
