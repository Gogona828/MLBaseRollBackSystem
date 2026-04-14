using UnityEngine;
using UnityEngine.UI;

namespace Footsies
{
    public class FootsiesBattleCanvasWarpPresenter : MonoBehaviour
    {
        [System.Serializable]
        private class FighterCanvasBinding
        {
            [Header("Root / Image")]
            public RectTransform root;
            public RectTransform imageRect;
            public Image image;

            [Header("Axes")]
            public bool followX = true;
            public bool followY = false;

            [Header("Mapping")]
            public float canvasUnitsPerFightUnit = 30f;
            public float extraRootX = 0f;
            public float extraRootY = 0f;

            [Header("Shake")]
            public float shakeScale = 1f;

            [Header("Origin")]
            public bool useCurrentRootAsOrigin = true;
            public Vector2 manualRootOrigin = Vector2.zero;

            [HideInInspector] public Vector2 baseRootAnchoredPosition;
            [HideInInspector] public Vector2 baseImageAnchoredPosition;
            [HideInInspector] public Vector3 baseImageLocalEulerAngles;
            [HideInInspector] public bool initialized;
            [HideInInspector] public bool initialGameFaceRight;
            [HideInInspector] public Sprite lastSprite;
            [HideInInspector] public bool lastFaceRight;
        }

        [Header("Core")]
        [SerializeField] private BattleCore battleCore;

        [Header("Player 1")]
        [SerializeField] private FighterCanvasBinding p1 = new FighterCanvasBinding();

        [Header("Player 2")]
        [SerializeField] private FighterCanvasBinding p2 = new FighterCanvasBinding();

        private void Awake()
        {
            InitializeBinding(p1, true);
            InitializeBinding(p2, false);
        }

        private void LateUpdate()
        {
            if (battleCore == null)
            {
                return;
            }

            UpdateBinding(battleCore.fighter1, p1);
            UpdateBinding(battleCore.fighter2, p2);
        }

        private void InitializeBinding(FighterCanvasBinding binding, bool initialGameFaceRight)
        {
            if (binding == null || binding.root == null)
            {
                return;
            }

            binding.baseRootAnchoredPosition = binding.useCurrentRootAsOrigin
                ? binding.root.anchoredPosition
                : binding.manualRootOrigin;

            if (binding.imageRect != null)
            {
                binding.baseImageAnchoredPosition = binding.imageRect.anchoredPosition;
                binding.baseImageLocalEulerAngles = binding.imageRect.localEulerAngles;
            }
            else
            {
                binding.baseImageAnchoredPosition = Vector2.zero;
                binding.baseImageLocalEulerAngles = Vector3.zero;
            }

            binding.initialGameFaceRight = initialGameFaceRight;
            binding.lastFaceRight = initialGameFaceRight;
            binding.initialized = true;
        }

        private void UpdateBinding(Fighter fighter, FighterCanvasBinding binding)
        {
            if (fighter == null || binding == null || binding.root == null)
            {
                return;
            }

            if (!binding.initialized)
            {
                return;
            }

            Vector2 rootTarget = BuildRootTarget(fighter, binding);
            binding.root.anchoredPosition = rootTarget;

            if (binding.imageRect != null)
            {
                Vector2 imageTarget = binding.baseImageAnchoredPosition;
                imageTarget.x += fighter.spriteShakePosition * binding.shakeScale;
                binding.imageRect.anchoredPosition = imageTarget;
            }

            if (binding.image != null)
            {
                Sprite sprite = fighter.GetCurrentMotionSpriteSafe();
                if (sprite != binding.lastSprite)
                {
                    binding.image.sprite = sprite;
                    binding.lastSprite = sprite;
                }
            }

            UpdateFacingByYRotation(fighter, binding);
        }

        private Vector2 BuildRootTarget(Fighter fighter, FighterCanvasBinding binding)
        {
            Vector2 target = binding.baseRootAnchoredPosition;

            if (binding.followX)
            {
                target.x = binding.baseRootAnchoredPosition.x
                    + fighter.position.x * binding.canvasUnitsPerFightUnit
                    + binding.extraRootX;
            }

            if (binding.followY)
            {
                target.y = binding.baseRootAnchoredPosition.y
                    + fighter.position.y * binding.canvasUnitsPerFightUnit
                    + binding.extraRootY;
            }
            else
            {
                target.y = binding.baseRootAnchoredPosition.y + binding.extraRootY;
            }

            return target;
        }

        private void UpdateFacingByYRotation(Fighter fighter, FighterCanvasBinding binding)
        {
            RectTransform targetRect = binding.imageRect != null ? binding.imageRect : binding.root;
            if (targetRect == null)
            {
                return;
            }

            bool currentFaceRight = fighter.isFaceRight;
            if (currentFaceRight == binding.lastFaceRight)
            {
                return;
            }

            Vector3 euler = binding.baseImageLocalEulerAngles;

            // 初期のゲーム向きと同じなら初期回転を使う
            // 逆向きなら Y を 180 足す
            if (currentFaceRight != binding.initialGameFaceRight)
            {
                euler.y += 180f;
            }

            targetRect.localEulerAngles = NormalizeEuler(euler);
            binding.lastFaceRight = currentFaceRight;
        }

        private Vector3 NormalizeEuler(Vector3 euler)
        {
            euler.x = NormalizeAngle(euler.x);
            euler.y = NormalizeAngle(euler.y);
            euler.z = NormalizeAngle(euler.z);
            return euler;
        }

        private float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle < 0f)
            {
                angle += 360f;
            }
            return angle;
        }

        [ContextMenu("Reinitialize Root Origins From Current UI")]
        public void ReinitializeRootOriginsFromCurrentUI()
        {
            ReinitOne(p1, true);
            ReinitOne(p2, false);
        }

        private void ReinitOne(FighterCanvasBinding binding, bool initialGameFaceRight)
        {
            if (binding == null || binding.root == null)
            {
                return;
            }

            binding.baseRootAnchoredPosition = binding.root.anchoredPosition;

            if (binding.imageRect != null)
            {
                binding.baseImageAnchoredPosition = binding.imageRect.anchoredPosition;
                binding.baseImageLocalEulerAngles = binding.imageRect.localEulerAngles;
            }

            binding.initialGameFaceRight = initialGameFaceRight;
            binding.lastFaceRight = initialGameFaceRight;
            binding.initialized = true;
        }
    }
}
