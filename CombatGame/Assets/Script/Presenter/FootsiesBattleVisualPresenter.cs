using UnityEngine;
using UnityEngine.UI;

namespace Footsies
{
    /// <summary>
    /// Rollback コア state は変更せず、
    /// Canvas 上の RectTransform / Image に対して
    /// 表示だけを滑らかに追従させる最小 View 層。
    ///
    /// 安全版:
    /// - 初期の anchoredPosition を基準として保持
    /// - まずは X だけ追従
    /// - Y はデフォルトで固定
    ///
    /// 使い方:
    /// - BattleScene の Canvas 内に空オブジェクトを作ってこの script を付ける
    /// - battleCore を割り当てる
    /// - p1.root / p2.root に各キャラ Image の RectTransform を割り当てる
    /// - p1.image / p2.image に各 Image を割り当てる
    ///
    /// 注意:
    /// - 他の script が同じ RectTransform.anchoredPosition を毎フレーム更新しているなら、
    ///   その位置更新は止めること
    /// </summary>
    public class FootsiesBattleCanvasVisualPresenter : MonoBehaviour
    {
        [System.Serializable]
        private class FighterCanvasViewBinding
        {
            [Header("Scene References")]
            public RectTransform root;
            public Image image;

            [Header("Follow Axes")]
            [Tooltip("X 方向の補間追従を行う")]
            public bool followX = true;

            [Tooltip("Y 方向の補間追従を行う。最初は false 推奨")]
            public bool followY = false;

            [Header("Mapping")]
            [Tooltip("fight 1 unit を Canvas 上で何 px とするか")]
            public float canvasUnitsPerFightUnit = 120f;

            [Tooltip("X に追加する微調整 px")]
            public float extraX = 0f;

            [Tooltip("Y に追加する微調整 px")]
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

            [Header("Origin")]
            [Tooltip("true なら開始時の anchoredPosition を基準点として使う")]
            public bool useInitialAnchoredPositionAsOrigin = true;

            [Tooltip("手動で基準点を指定したいときだけ使う")]
            public Vector2 manualOrigin = Vector2.zero;

            [HideInInspector] public Vector2 baseAnchoredPosition;
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

        private void Awake()
        {
            InitializeBinding(p1);
            InitializeBinding(p2);
        }

        private void LateUpdate()
        {
            if (battleCore == null)
            {
                return;
            }

            UpdateFighterView(battleCore.fighter1, p1);
            UpdateFighterView(battleCore.fighter2, p2);
        }

        private void InitializeBinding(FighterCanvasViewBinding view)
        {
            if (view == null || view.root == null)
            {
                return;
            }

            view.baseAnchoredPosition = view.useInitialAnchoredPositionAsOrigin
                ? view.root.anchoredPosition
                : view.manualOrigin;

            view.renderAnchoredPosition = view.root.anchoredPosition;
            view.renderVelocity = Vector2.zero;
            view.initialized = true;
        }

        private void UpdateFighterView(Fighter fighter, FighterCanvasViewBinding view)
        {
            if (fighter == null || view == null || view.root == null)
            {
                return;
            }

            if (!view.initialized)
            {
                InitializeBinding(view);
            }

            Vector2 target = BuildTargetAnchoredPosition(fighter, view);

            float distance = Vector2.Distance(view.renderAnchoredPosition, target);

            if (distance >= view.snapDistance)
            {
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

            view.root.anchoredPosition = view.renderAnchoredPosition;

            UpdateSprite(fighter, view);
            UpdateFacing(fighter, view);
        }

        private Vector2 BuildTargetAnchoredPosition(Fighter fighter, FighterCanvasViewBinding view)
        {
            Vector2 target = view.baseAnchoredPosition;

            if (view.followX)
            {
                target.x = view.baseAnchoredPosition.x
                    + (fighter.position.x * view.canvasUnitsPerFightUnit)
                    + (fighter.spriteShakePosition * view.shakeScale)
                    + view.extraX;
            }

            if (view.followY)
            {
                target.y = view.baseAnchoredPosition.y
                    + (fighter.position.y * view.canvasUnitsPerFightUnit)
                    + view.extraY;
            }
            else
            {
                // Y は開始時の見た目位置を維持
                target.y = view.baseAnchoredPosition.y + view.extraY;
            }

            return target;
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

        [ContextMenu("Reinitialize Origins From Current UI")]
        public void ReinitializeOriginsFromCurrentUI()
        {
            ReinitializeOne(p1);
            ReinitializeOne(p2);
        }

        private void ReinitializeOne(FighterCanvasViewBinding view)
        {
            if (view == null || view.root == null)
            {
                return;
            }

            view.baseAnchoredPosition = view.root.anchoredPosition;
            view.renderAnchoredPosition = view.root.anchoredPosition;
            view.renderVelocity = Vector2.zero;
            view.initialized = true;
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

            Vector2 target = BuildTargetAnchoredPosition(fighter, view);
            view.renderAnchoredPosition = target;
            view.renderVelocity = Vector2.zero;
            view.root.anchoredPosition = target;
            view.initialized = true;

            UpdateSprite(fighter, view);
            UpdateFacing(fighter, view);
        }
    }
}
