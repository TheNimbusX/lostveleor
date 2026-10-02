using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Плитка ЛКМ в боевом HUD (владелец 02.10, DESIGN «Пелаг — новая структура набора»: «ЛКМ показывается в HUD
    /// слотом, как навык»). Базовая атака — всегда серия саблей; позже у неё свои формы и таланты, тогда значок
    /// будет меняться по форме.
    ///
    /// Вид — плитка способности (SlotWidget: клякса, круглая иконка под маской, огненное кольцо готовности, волна
    /// готовности, кейкап на нижней кромке) того же размера, первой слева от способности 1 с тем же зазором. Без
    /// перезарядки, плашки нехватки ресурса и огонька усилений — у серии их нет. На кейкапе вместо букв — мышь
    /// с левой кнопкой из набора (UiTheme.Mouse, wc_mouse; так подсказка «Левый клик» нарисована на витрине набора).
    /// Иконка — Icon_Cleave из набора иконок способностей (сабля, взмах; новой картинки нет).
    ///
    /// Места под плитку в полосе не было: полоса шире на плитку с зазором, герой со всплывашками ушёл на половину
    /// этого влево, ряд, кувырок и зелья — на половину вправо, середина HUD на месте. Свежая сборка — константы
    /// (StripLeft, AttackX, AbilitiesX), готовый префаб — миграция v4.
    /// </summary>
    public static partial class CombatHudWcBuilder
    {
        /// <summary>Место плитки ЛКМ в полосе: плитка и зазор ряда.</summary>
        const float AttackRoom = Slot + SlotGap;
        const string AttackIconPath = "Assets/Resources/UI/Abilities/Icon_Cleave.png";
        /// <summary>Кейкап плитки — как у способностей; мышь в нём — по высоте, доля кейкапа (на витрине набора 30 из 56).</summary>
        const float AttackKeySize = 26f, AttackMouseRatio = .6f;

        /// <summary>
        /// Плитка ЛКМ: панель «Атака» (BottomCenter, опора — левый нижний угол) в точке <paramref name="position"/>,
        /// внутри плитка способности без перезарядки, нехватки ресурса и усилений.
        /// </summary>
        static void BuildAttack(RectTransform root, CombatHudView view, Vector2 position)
        {
            RectTransform panel = Box(Node("Атака", root), BottomCenter, Vector2.zero, position, new Vector2(Slot, Slot));
            view.AttackPanel = panel;
            RectTransform tile = Stretch(Node("Плитка ЛКМ", panel));
            HudSlotWidget widget = SlotWidget(tile, string.Empty, AttackKeySize, upgrades: false);

            // У серии нет перезарядки и цены: вуаль с кольцом делений, секунды и плашка недостачи не нужны.
            if (widget.Cooldown != null) Object.DestroyImmediate(widget.Cooldown.gameObject);
            widget.Cooldown = null;
            widget.CooldownRing = null;
            if (widget.CooldownText != null) Object.DestroyImmediate(widget.CooldownText.gameObject);
            widget.CooldownText = null;
            if (widget.ResourceBadge != null) Object.DestroyImmediate(widget.ResourceBadge);
            widget.ResourceBadge = null;
            widget.ResourceText = null;

            widget.Art.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AttackIconPath);
            if (widget.Art.texture == null) Debug.LogWarning("[ui-kit] Нет иконки " + AttackIconPath + ": плитка ЛКМ без картинки");
            widget.Art.uvRect = new Rect(view.IconCrop, view.IconCrop, 1f - view.IconCrop * 2f, 1f - view.IconCrop * 2f);

            MouseKeycap(widget);
            view.Attack = widget;
        }

        /// <summary>
        /// Кейкап плитки — тот же квадрат листа 5, что у способностей, но вместо буквы мышь с левой кнопкой: кремовая,
        /// проявляется вместе с кромкой. Буквы нет — плитку не трогают подгонка ширины по подписи и смена клавиш.
        /// </summary>
        static void MouseKeycap(HudSlotWidget widget)
        {
            RectTransform cap = widget.Key != null ? widget.Key.transform.parent as RectTransform : null;
            if (cap == null)
            {
                Debug.LogWarning("[ui-kit] У плитки ЛКМ нет кейкапа: мышь не нарисована");
                return;
            }
            Object.DestroyImmediate(widget.Key.gameObject);
            widget.Key = null;
            Image mouse = Mark(cap, "Мышь", T.Mouse, Role.Text, 1f, new Vector2(.5f, .5f), Vector2.zero, AttackKeySize * AttackMouseRatio);
            if (T.Mouse == null) Debug.LogWarning("[ui-kit] В теме нет спрайта мыши (UiTheme.Mouse): кейкап ЛКМ пустой");
            mouse.material = UiInkKit.Plain;
            var reveal = mouse.gameObject.AddComponent<UiInkReveal>();
            reveal.Seed = .7f;
            reveal.Origin = new Vector2(.5f, .5f);
            reveal.Delay = .2f;
        }

        /// <summary>
        /// v4 — плитка ЛКМ (02.10). Полоса шире на <see cref="AttackRoom"/>: герой (со строкой эффектов) и всплывашки
        /// над портретом — на половину влево, ряд способностей, кувырок и зелья — на половину вправо; дым под
        /// способностями, нить света и угли — шире на столько же, огонёк-разделитель — вслед за кувырком. Плитка
        /// встаёт левее ряда на плитку с зазором. Всё — сдвигами от того, что лежит в префабе: ручные правки остаются.
        /// </summary>
        static void MigrateTo4(GameObject root, CombatHudView view)
        {
            if (view.AttackPanel != null) return;
            RectTransform row = view.AbilityPanel;
            if (row == null)
            {
                Debug.LogWarning("[ui-kit] В боевом HUD нет ряда способностей (AbilityPanel): плитка ЛКМ не поставлена");
                return;
            }
            float half = AttackRoom * .5f;
            ShiftX(view.HeroPanel, -half, "Герой");
            ShiftX(view.Toasts != null ? (RectTransform)view.Toasts.transform : null, -half, "Всплывашки");
            ShiftX(row, half, "Способности");
            ShiftX(view.DashPanel, half, "Кувырок");
            ShiftX(view.PotionPanel, half, "Зелья");

            RectTransform strip = view.Strip;
            if (strip != null)
            {
                // Левый край — на половину влево, ширина — на всю плитку: середина полосы на месте при любой опоре.
                float width = strip.sizeDelta.x;
                float left = strip.anchoredPosition.x - strip.pivot.x * width;
                strip.sizeDelta = new Vector2(width + AttackRoom, strip.sizeDelta.y);
                strip.anchoredPosition = new Vector2(left - half + strip.pivot.x * (width + AttackRoom), strip.anchoredPosition.y);
                foreach (string part in new[] { "Дым под способностями", "Нить света", "Угли" })
                {
                    if (strip.Find(part) is RectTransform piece) piece.sizeDelta += new Vector2(AttackRoom, 0f);
                    else Debug.LogWarning("[ui-kit] Нет «Полоса/" + part + "» в боевом HUD: не расширено под плитку ЛКМ");
                }
                // Разделитель привязан к левому краю полосы (ушёл на половину влево), а стоит у кувырка (на половину вправо).
                if (strip.Find("Разделитель") is RectTransform divider) divider.anchoredPosition += new Vector2(AttackRoom, 0f);
                else Debug.LogWarning("[ui-kit] Нет «Полоса/Разделитель» в боевом HUD: огонёк у кувырка не сдвинут");
            }
            else Debug.LogWarning("[ui-kit] В боевом HUD нет полосы (Strip): дым под плиткой ЛКМ не расширен");

            if (row.anchorMin != BottomCenter || row.anchorMax != BottomCenter || row.pivot != Vector2.zero)
                Debug.LogWarning("[ui-kit] Ряд способностей привязан не так, как у сборщика: плитку ЛКМ проверить руками");
            BuildAttack((RectTransform)root.transform, view, row.anchoredPosition - new Vector2(AttackRoom, 0f));
            // В иерархии — перед рядом: проявление и порядок рисования как у соседей.
            view.AttackPanel.SetSiblingIndex(row.GetSiblingIndex());
        }

        static void ShiftX(RectTransform rect, float dx, string what)
        {
            if (rect == null)
            {
                Debug.LogWarning("[ui-kit] Нет «" + what + "» в боевом HUD: не сдвинуто под плитку ЛКМ");
                return;
            }
            rect.anchoredPosition += new Vector2(dx, 0f);
        }
    }
}
