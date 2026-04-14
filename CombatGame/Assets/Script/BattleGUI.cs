using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Footsies
{
    /// <summary>
    /// Compute the fight area that is going to be on screen and update fighter sprites position
    /// Also update the debug display
    /// </summary>
    public class BattleGUI : MonoBehaviour
    {
        #region serialize field

        [SerializeField]
        private GameObject _battleCoreGameObject;

        [SerializeField]
        private GameObject fighter1ImageObject;

        [SerializeField]
        private GameObject fighter2ImageObject;

        [SerializeField]
        private GameObject hitEffectObject1;

        [SerializeField]
        private GameObject hitEffectObject2;

        [SerializeField]
        private float _battleBoxLineWidth = 2f;

        [SerializeField]
        private GUIStyle debugTextStyle;

        [SerializeField]
        private bool drawDebug = false;

        #endregion

        #region private field

        private BattleCore battleCore;

        private Vector2 battleAreaTopLeftPoint;
        private Vector2 battleAreaBottomRightPoint;

        private Vector2 fightPointToScreenScale;
        private float centerPoint;

        private RectTransform rectTransform;

        private Image fighter1Image;
        private Image fighter2Image;

        private Animator hitEffectAnimator1;
        private Animator hitEffectAnimator2;

        #endregion

        private void Awake()
        {
            rectTransform = gameObject.GetComponent<RectTransform>();

            if (_battleCoreGameObject != null)
            {
                battleCore = _battleCoreGameObject.GetComponent<BattleCore>();
                if (battleCore != null)
                {
                    battleCore.damageHandler += OnDamageHandler;
                }
            }

            if (fighter1ImageObject != null)
                fighter1Image = fighter1ImageObject.GetComponent<Image>();

            if (fighter2ImageObject != null)
                fighter2Image = fighter2ImageObject.GetComponent<Image>();

            if (hitEffectObject1 != null)
                hitEffectAnimator1 = hitEffectObject1.GetComponent<Animator>();

            if (hitEffectObject2 != null)
                hitEffectAnimator2 = hitEffectObject2.GetComponent<Animator>();
        }

        private void OnDestroy()
        {
            if (battleCore != null)
            {
                battleCore.damageHandler -= OnDamageHandler;
            }
        }

        private void FixedUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F12))
            {
                drawDebug = !drawDebug;
            }

            if (battleCore == null || rectTransform == null)
            {
                return;
            }

            if (battleCore.fighter1 == null || battleCore.fighter2 == null)
            {
                return;
            }

            CalculateBattleArea();
            CalculateFightPointToScreenScale();

            UpdateSpriteSafe();
        }

        void OnGUI()
        {
            if (!drawDebug)
            {
                return;
            }

            if (battleCore == null || battleCore.fighters == null)
            {
                return;
            }

            battleCore.fighters.ForEach((f) =>
            {
                if (f != null)
                {
                    DrawFighter(f);
                }
            });

            var labelRect = new Rect(Screen.width * 0.4f, Screen.height * 0.95f, Screen.width * 0.2f, Screen.height * 0.05f);
            if (debugTextStyle != null)
            {
                debugTextStyle.alignment = TextAnchor.UpperCenter;
            }
            GUI.Label(labelRect, "F1=Pause/Resume, F2=Frame Step, F12=Debug Draw", debugTextStyle);
        }

        void UpdateSpriteSafe()
        {
            UpdateOneSpriteSafe(battleCore.fighter1, fighter1Image);
            UpdateOneSpriteSafe(battleCore.fighter2, fighter2Image);
        }

        void UpdateOneSpriteSafe(Fighter fighter, Image fighterImage)
        {
            if (fighter == null || fighterImage == null)
            {
                return;
            }

            Sprite sprite = fighter.GetCurrentMotionSpriteSafe();
            if (sprite != null)
            {
                fighterImage.sprite = sprite;
            }

            var position = fighterImage.transform.position;
            position.x = TransformHorizontalFightPointToScreen(fighter.position.x) + fighter.spriteShakePosition;
            fighterImage.transform.position = position;
        }

        void DrawFighter(Fighter fighter)
        {
            if (fighter == null || battleCore == null)
            {
                return;
            }

            var labelRect = new Rect(0, Screen.height * 0.86f, Screen.width * 0.22f, 50);
            if (fighter.isFaceRight)
            {
                labelRect.x = Screen.width * 0.01f;
                if (debugTextStyle != null)
                    debugTextStyle.alignment = TextAnchor.UpperLeft;
            }
            else
            {
                labelRect.x = Screen.width * 0.77f;
                if (debugTextStyle != null)
                    debugTextStyle.alignment = TextAnchor.UpperRight;
            }

            GUI.Label(labelRect, fighter.position.ToString(), debugTextStyle);

            labelRect.y += Screen.height * 0.03f;
            var frameAdvantage = battleCore.GetFrameAdvantage(fighter.isFaceRight);
            var frameAdvText = frameAdvantage > 0 ? "+" + frameAdvantage : frameAdvantage.ToString();
            GUI.Label(labelRect, "Frame: " + fighter.currentActionFrame + "/" + fighter.currentActionFrameCount
                + "(" + frameAdvText + ")", debugTextStyle);

            labelRect.y += Screen.height * 0.03f;
            GUI.Label(labelRect, "Stun: " + fighter.currentHitStunFrame, debugTextStyle);

            labelRect.y += Screen.height * 0.03f;
            GUI.Label(labelRect, "Action: " + fighter.currentActionID + " " + (CommonActionID)fighter.currentActionID, debugTextStyle);

            if (fighter.hurtboxes != null)
            {
                foreach (var hurtbox in fighter.hurtboxes)
                {
                    DrawFightBox(hurtbox.rect, Color.yellow, true);
                }
            }

            if (fighter.pushbox != null)
            {
                DrawFightBox(fighter.pushbox.rect, Color.blue, true);
            }

            if (fighter.hitboxes != null)
            {
                foreach (var hitbox in fighter.hitboxes)
                {
                    if (hitbox.proximity)
                        DrawFightBox(hitbox.rect, Color.gray, true);
                    else
                        DrawFightBox(hitbox.rect, Color.red, true);
                }
            }
        }

        void DrawFightBox(Rect fightPointRect, Color color, bool isFilled)
        {
            var screenRect = new Rect();
            screenRect.width = fightPointRect.width * fightPointToScreenScale.x;
            screenRect.height = fightPointRect.height * fightPointToScreenScale.y;
            screenRect.x = TransformHorizontalFightPointToScreen(fightPointRect.x) - (screenRect.width / 2);
            screenRect.y = battleAreaBottomRightPoint.y - (fightPointRect.y * fightPointToScreenScale.y) - screenRect.height;

            DrawBox(screenRect, color, isFilled);
        }

        void DrawBox(Rect rect, Color color, bool isFilled)
        {
            float startX = rect.x;
            float startY = rect.y;
            float width = rect.width;
            float height = rect.height;
            float endX = startX + width;
            float endY = startY + height;

            Draw.DrawLine(new Vector2(startX, startY), new Vector2(endX, startY), color, _battleBoxLineWidth);
            Draw.DrawLine(new Vector2(startX, startY), new Vector2(startX, endY), color, _battleBoxLineWidth);
            Draw.DrawLine(new Vector2(endX, endY), new Vector2(endX, startY), color, _battleBoxLineWidth);
            Draw.DrawLine(new Vector2(endX, endY), new Vector2(startX, endY), color, _battleBoxLineWidth);

            if (isFilled)
            {
                Color rectColor = color;
                rectColor.a = 0.25f;
                Draw.DrawRect(new Rect(startX, startY, width, height), rectColor);
            }
        }

        float TransformHorizontalFightPointToScreen(float x)
        {
            return (x * fightPointToScreenScale.x) + centerPoint;
        }

        float TransformVerticalFightPointToScreen(float y)
        {
            return (Screen.height - battleAreaBottomRightPoint.y) + (y * fightPointToScreenScale.y);
        }

        void CalculateBattleArea()
        {
            if (rectTransform == null)
            {
                return;
            }

            Vector3[] v = new Vector3[4];
            rectTransform.GetWorldCorners(v);
            battleAreaTopLeftPoint = new Vector2(v[1].x, Screen.height - v[1].y);
            battleAreaBottomRightPoint = new Vector2(v[3].x, Screen.height - v[3].y);
        }

        void CalculateFightPointToScreenScale()
        {
            if (battleCore == null)
            {
                return;
            }

            if (Mathf.Approximately(battleCore.battleAreaWidth, 0f) || Mathf.Approximately(battleCore.battleAreaMaxHeight, 0f))
            {
                return;
            }

            fightPointToScreenScale.x = (battleAreaBottomRightPoint.x - battleAreaTopLeftPoint.x) / battleCore.battleAreaWidth;
            fightPointToScreenScale.y = (battleAreaBottomRightPoint.y - battleAreaTopLeftPoint.y) / battleCore.battleAreaMaxHeight;

            centerPoint = (battleAreaBottomRightPoint.x + battleAreaTopLeftPoint.x) / 2;
        }

        private void OnDamageHandler(Fighter damagedFighter, Vector2 damagedPos, DamageResult damageResult)
        {
            if (battleCore == null || damagedFighter == null)
            {
                return;
            }

            if (damagedFighter == battleCore.fighter1)
            {
                if (fighter2Image != null)
                {
                    fighter2Image.transform.SetAsLastSibling();
                }

                RequestHitEffect(hitEffectAnimator1, damagedPos, damageResult);
            }
            else if (damagedFighter == battleCore.fighter2)
            {
                if (fighter1Image != null)
                {
                    fighter1Image.transform.SetAsLastSibling();
                }

                RequestHitEffect(hitEffectAnimator2, damagedPos, damageResult);
            }
        }

        private void RequestHitEffect(Animator hitEffectAnimator, Vector2 damagedPos, DamageResult damageResult)
        {
            if (hitEffectAnimator == null)
            {
                return;
            }

            hitEffectAnimator.SetTrigger("Hit");

            var position = hitEffectAnimator.transform.position;
            position.x = TransformHorizontalFightPointToScreen(damagedPos.x);
            position.y = TransformVerticalFightPointToScreen(damagedPos.y);
            hitEffectAnimator.transform.position = position;

            if (damageResult == DamageResult.GuardBreak)
                hitEffectAnimator.transform.localScale = new Vector3(5, 5, 1);
            else if (damageResult == DamageResult.Damage)
                hitEffectAnimator.transform.localScale = new Vector3(2, 2, 1);
            else if (damageResult == DamageResult.Guard)
                hitEffectAnimator.transform.localScale = new Vector3(1, 1, 1);

            hitEffectAnimator.transform.SetAsLastSibling();
        }
    }
}
