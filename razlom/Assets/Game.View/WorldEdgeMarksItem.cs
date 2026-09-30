using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Одна тлеющая метка у края экрана (владелец 30.09, выбор 2a): клуб дыма, тёмный диск с тонким
    /// кольцом, знак врага тушью, шеврон наружу — к цели, «×N» у слитой метки и подпись «Вендиго · 14 м»
    /// при первом появлении. Вид — в префабе WorldEdgeMarksWc (правится руками), что показывать —
    /// решает <see cref="WorldEdgeMarks"/>. Свой файл обязателен: компонент стоит в префабе.
    /// </summary>
    public sealed class WorldEdgeMarksItem : MonoBehaviour
    {
        public enum Look { Enemy, Threat, Goal }

        [Tooltip("Проявление и уход метки (без огня: метки всплывают часто)")] public UiInkGroup Ink;
        [Tooltip("Мягкий свет за диском: красный пульс угрозы, спокойное золото выхода и тайника")] public Graphic Glow;
        [Tooltip("Кольцо диска: серебро у врага, красное у угрозы, акцент у выхода и тайника")] public ThemeColor Ring;
        [Tooltip("Что пульсирует у угрозы (диск с кольцом)")] public RectTransform Pulse;
        public RawImage Icon;
        public ThemeColor IconColor;
        [Tooltip("Узел шеврона в центре метки: поворачивается к цели, стрелка лежит на нём справа (+x)")] public RectTransform Chevron;
        public ThemeColor ChevronColor;
        [Tooltip("«×2» в углу слитой метки")] public TMP_Text Count;
        public GameObject CountRow;
        [Tooltip("Подпись «Вендиго · 14 м»: встаёт внутрь экрана от метки")] public RectTransform Label;
        public CanvasGroup LabelGroup;
        public TMP_Text LabelText;

        [Header("Краски")]
        public Color GlowThreat = new Color(1f, .42f, .36f, 1f);
        public Color GlowGoal = new Color(1f, .92f, .78f, 1f);
        [Tooltip("Ровный свет выхода и тайника: спокойный, без пульса (огня у портрета и миникарты нет)")]
        [Range(0f, 1f)] public float GoalGlow = .2f;

        Look _look = (Look)(-1);
        Texture _icon;
        int _count = -1, _side = -1;
        string _label;
        Action _hidden;

        /// <summary>Уходит (Ink.Hide ещё идёт): место занято, но цели у него уже нет.</summary>
        public bool Leaving { get; private set; }

        // Колбэк ухода создаётся один раз: Hide зовётся часто, новая лямбда на каждый уход — мусор.
        Action Hidden => _hidden ??= () =>
        {
            if (this != null && Leaving) gameObject.SetActive(false);
            Leaving = false;
        };

        /// <summary>Появиться (включение запускает проявление группы) или вернуться посреди ухода.</summary>
        public void Appear()
        {
            if (Leaving)
            {
                Leaving = false;
                if (Ink != null) Ink.Show();
            }
            if (gameObject.activeSelf) return;
            gameObject.SetActive(true);
            // После выключения прозрачность света (CanvasRenderer) могла сброситься — вид ставится заново.
            _look = (Look)(-1);
        }

        /// <summary>Уйти проявлением наоборот; потом узел выключается.</summary>
        public void Leave()
        {
            if (!gameObject.activeSelf || Leaving) return;
            Leaving = true;
            if (Ink != null && Ink.isActiveAndEnabled) Ink.Hide(Hidden);
            else Hidden();
        }

        /// <summary>Сразу выключить (новая арена, место отдают другой цели): без ухода.</summary>
        public void HideNow()
        {
            Leaving = false;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        public void SetLook(Look look)
        {
            if (look == _look) return;
            _look = look;
            UiTheme.Role line = look == Look.Threat ? UiTheme.Role.Bad : look == Look.Goal ? UiTheme.Role.Accent : UiTheme.Role.PanelLine;
            if (Ring != null) Ring.SetRole(line, look == Look.Enemy ? .55f : .9f);
            if (IconColor != null) IconColor.SetRole(look == Look.Threat ? UiTheme.Role.Bad : UiTheme.Role.Text, look == Look.Enemy ? .92f : 1f);
            if (ChevronColor != null) ChevronColor.SetRole(line, look == Look.Enemy ? .8f : 1f);
            if (Glow != null)
            {
                Color glow = look == Look.Threat ? GlowThreat : GlowGoal;
                glow.a = 1f;
                Glow.color = glow;
                Glow.canvasRenderer.SetAlpha(look == Look.Goal ? GoalGlow : 0f);
            }
            if (Pulse != null) Pulse.localScale = Vector3.one;
        }

        public void SetIcon(Texture icon)
        {
            if (Icon == null || icon == _icon) return;
            _icon = icon;
            Icon.texture = icon;
            Icon.enabled = icon != null;
        }

        public void SetCount(int count)
        {
            if (count == _count) return;
            _count = count;
            bool shown = count > 1;
            if (CountRow != null && CountRow.activeSelf != shown) CountRow.SetActive(shown);
            if (shown && Count != null) Count.text = WorldEdgeMarksLayout.CountText(count);
        }

        /// <summary>Шеврон смотрит на цель: угол от +x против часовой, градусы.</summary>
        public void SetChevron(float degrees)
        {
            if (Chevron != null) Chevron.localEulerAngles = new Vector3(0f, 0f, degrees);
        }

        /// <summary>Пульс угрозы: сила 0..1 уже с множителем «Вспышки и мерцание».</summary>
        public void SetPulse(float glow, float scale)
        {
            if (Glow != null) Glow.canvasRenderer.SetAlpha(glow);
            if (Pulse != null) Pulse.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// Подпись: прозрачность (0 — только знак) и текст, внутрь экрана от метки на стороне
        /// <paramref name="side"/> (WorldEdgeMarksLayout.SideBottom…SideLeft). Текст меняется только
        /// вместе со строкой: метры пересчитывает вызывающий, когда меняется целое число.
        /// </summary>
        public void SetLabel(float alpha, string text, int side, float radius)
        {
            if (Label == null) return;
            bool shown = alpha > .001f;
            if (Label.gameObject.activeSelf != shown) Label.gameObject.SetActive(shown);
            if (!shown) return;
            if (LabelGroup != null) LabelGroup.alpha = alpha;
            if (text != null && !ReferenceEquals(text, _label) && LabelText != null)
            {
                _label = text;
                LabelText.text = text;
            }
            if (side == _side) return;
            _side = side;
            float gap = radius + 10f;
            Vector2 pivot, at;
            switch (side)
            {
                case WorldEdgeMarksLayout.SideBottom: pivot = new Vector2(.5f, 0f); at = new Vector2(0f, gap); break;
                case WorldEdgeMarksLayout.SideTop: pivot = new Vector2(.5f, 1f); at = new Vector2(0f, -gap); break;
                case WorldEdgeMarksLayout.SideLeft: pivot = new Vector2(0f, .5f); at = new Vector2(gap, 0f); break;
                default: pivot = new Vector2(1f, .5f); at = new Vector2(-gap, 0f); break;
            }
            Label.anchorMin = Label.anchorMax = new Vector2(.5f, .5f);
            Label.pivot = pivot;
            Label.anchoredPosition = at;
        }
    }
}
