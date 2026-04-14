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

            [Header("Facing")]
            public bool flipImageByLocalScale = true;

            [Header("Origin")]
            public bool useCurrentRootAsOrigin = true;
            public Vector2 manualRootOrigin = Vector2.zero;

            [HideInInspector] public Vector2 baseRootAnchoredPosition;
            [HideInInspector] public Vector2 baseImageAnchoredPosition;
            [HideInInspector] public bool initialized;
            [HideInInspector] public Sprite lastSprite;
            [HideInInspector] public bool lastFaceRight = true;
        }

        [Header("Core")]
        [SerializeField] private BattleCore battleCore;

        [Header("Player 1")]
        [SerializeField] private FighterCanvasBinding p1 = new FighterCanvasBinding();

        [Header("Player 2")]
        [SerializeField] private FighterCanvasBinding p2 = new FighterCanvasBinding();

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

            UpdateBinding(battleCore.fighter1, p1);
            UpdateBinding(battleCore.fighter2, p2);
        }

        private void InitializeBinding(FighterCanvasBinding binding)
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
            }
            else
            {
                binding.baseImageAnchoredPosition = Vector2.zero;
            }

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
                InitializeBinding(binding);
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

            if (binding.flipImageByLocalScale)
            {
                RectTransform flipTarget = binding.imageRect != null ? binding.imageRect : binding.root;
                bool faceRight = fighter.isFaceRight;

                if (faceRight != binding.lastFaceRight)
                {
                    Vector3 scale = flipTarget.localScale;
                    float absX = Mathf.Abs(scale.x);
                    if (absX <= 0.0001f)
                    {
                        absX = 1f;
                    }

                    scale.x = faceRight ? absX : -absX;
                    flipTarget.localScale = scale;
                    binding.lastFaceRight = faceRight;
                }
            }
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

        [ContextMenu("Reinitialize Root Origins From Current UI")]
        public void ReinitializeRootOriginsFromCurrentUI()
        {
            ReinitOne(p1);
            ReinitOne(p2);
        }

        private void ReinitOne(FighterCanvasBinding binding)
        {
            if (binding == null || binding.root == null)
            {
                return;
            }

            binding.baseRootAnchoredPosition = binding.root.anchoredPosition;

            if (binding.imageRect != null)
            {
                binding.baseImageAnchoredPosition = binding.imageRect.anchoredPosition;
            }

            binding.initialized = true;
        }
    }
}
