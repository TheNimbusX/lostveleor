using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Палатка снаряжения по концепту владельца `ART/UI/concepts-2026-09-22/tent-O-portrait-card.png`
    /// (22 сентября), в языке боевого HUD и паузы. Слева карточка героя: портрет, имя, уровень
    /// и опыт, 4 слота, лист характеристик, зелья. Справа вкладки «Сумка / Атлас». Фон — живой
    /// лагерь, без размытия. С 26 сентября — материал «Дым и свет» (CampTentWcBuilder): ячейки
    /// UiInkKit.Cell, рамка и свет редкости — у их WcSlotState, спрайтов рамок редкости нет.
    ///
    /// Префаб собирает CampTentBuilder, дальше он правится руками. Здесь только ссылки на части
    /// и внешний вид; что надеть и что показать — решает CampInventoryView.
    /// </summary>
    public sealed class CampTentView : MonoBehaviour
    {
        [HideInInspector] public int LayoutVersion;

        [Header("Общее")]
        [Tooltip("Прозрачный слой, который ловит клики мимо окон")] public CanvasGroup Backdrop;
        public Button Close;
        [Tooltip("Строка сообщений: «не подходит для слота» и т. п.")] public TMP_Text Feedback;

        [Header("Карточка героя")]
        public RectTransform HeroPanel;
        public RawImage Portrait;
        public TMP_Text HeroName;
        public TMP_Text Level;
        [Tooltip("Заливка опыта; ширину задаёт anchorMax.x")] public RectTransform XpFill;
        public TMP_Text XpText;
        [Tooltip("Оружие, броня, кольцо, талисман")] public CampTentCell[] Worn = new CampTentCell[4];
        [Tooltip("Строки листа героя по порядку StatRows в CampInventoryView")] public RectTransform[] StatRows = new RectTransform[12];
        public TMP_Text[] StatLabels = new TMP_Text[12];
        public TMP_Text[] StatValues = new TMP_Text[12];
        [Tooltip("Значки статов по тем же строкам: для подсказки стата")] public Sprite[] StatIcons = new Sprite[12];

        [Header("Зелья: малое и большое здоровья, малое и большое лавидия")]
        public Button[] Potions = new Button[4];
        public Image[] PotionIcons = new Image[4];
        public TMP_Text[] PotionCounts = new TMP_Text[4];
        [Tooltip("Отметка зелья, стоящего в HUD")] public GameObject[] PotionSelected = new GameObject[4];
        [Tooltip("Одна рамка зелья: толстая оранжевая у стоящего в HUD (вместо PotionSelected)")] public WcSlotState[] PotionStates = new WcSlotState[4];

        [Header("Правая панель")]
        public RectTransform BagPanel;
        public Button BagTab;
        public Button AtlasTab;
        public GameObject BagPage;
        public GameObject AtlasPage;
        [Tooltip("Спрайты вкладок и фильтров: выбранная и обычная")] public Sprite TabOn, TabOff;

        [Header("Сумка")]
        public RectTransform BagGrid;
        [Tooltip("Шаблон ячейки сумки; сам остаётся выключенным")] public CampTentCell BagTemplate;
        public TMP_Text BagCount;
        [Tooltip("Все, оружие, броня, кольца, талисманы")] public Button[] Filters = new Button[5];

        [Header("Атлас")]
        public RectTransform AtlasGrid;
        [Tooltip("Шаблон ячейки атласа; сам остаётся выключенным")] public CampAtlasEntry AtlasTemplate;
        public TMP_Text AtlasCount;

        [Header("Подсказка")]
        [Tooltip("Карточка у ячейки под мышью: вещь, стат или зелье")] public RectTransform Tooltip;
        public CanvasGroup TooltipGroup;
        [Tooltip("Зазор между ячейкой и карточкой")] public float TooltipGap = 14f;
        public Image ItemFrame;
        public Image ItemArt;
        public TMP_Text ItemTitle;
        public TMP_Text ItemRarity;
        public TMP_Text ItemKind;
        public TMP_Text ItemStats;
        [Tooltip("Карточка сама раскладывается (VerticalLayoutGroup + ContentSizeFitter): код только ставит тексты")]
        public bool TooltipAutoLayout;
        [Tooltip("«Дым и свет»: состояние ячейки картинки (рамка и свет цвета редкости). Пусто — рамка меняет спрайт (FrameFor)")]
        public WcSlotState ItemState;
        [Tooltip("«Дым и свет»: проявление карточки; заново — когда она всплывает из скрытой")]
        public UiInkGroup TooltipInk;

        [Header("Редкости: обычная, редкая, эпическая, уникальная")]
        public Sprite EmptyFrame;
        public Sprite[] RarityFrames = new Sprite[4];
        public Color[] RarityColours =
        {
            new Color32(0x8C, 0x86, 0x7A, 0xFF),
            new Color32(0x3F, 0xD2, 0xE0, 0xFF),
            new Color32(0xA9, 0x6B, 0xE8, 0xFF),
            new Color32(0xE2, 0x56, 0x3F, 0xFF),
        };
        [Tooltip("Цвет пустого слота")] public Color EmptyRing = Color.white;
        public string[] RarityNames = { "Обычная", "Редкая", "Эпическая", "Уникальная" };

        [Header("Надевание")]
        [Tooltip("Полёт значка из сумки в слот и обратно, секунд")] public float FlyDuration = .28f;
        [Tooltip("Высота дуги полёта")] public float FlyArc = 70f;
        [Tooltip("Досчёт статов, секунд")] public float CountDuration = .4f;
        public Color StatUp = new Color32(0x8C, 0xE0, 0x7A, 0xFF);
        public Color StatDown = new Color32(0xFF, 0x7A, 0x66, 0xFF);

        [Header("Появление")]
        public float OpenDuration = .22f;
        [Tooltip("Откуда приезжают панели (сдвиг от места)")] public Vector2 PanelOffset = new Vector2(0f, -26f);
        [Tooltip("Задержка между панелями, секунд")] public float PanelStagger = .06f;
        public float TooltipDuration = .12f;

        readonly Dictionary<RectTransform, Vector2> _rest = new Dictionary<RectTransform, Vector2>();
        bool _tooltipShown;
        readonly float[] _statShown = new float[12];
        readonly bool[] _statKnown = new bool[12];
        Color _statColour = Color.white;

        public Sprite FrameFor(int rarity) =>
            rarity >= 0 && rarity < RarityFrames.Length && RarityFrames[rarity] != null ? RarityFrames[rarity] : EmptyFrame;

        public Color ColourFor(int rarity) =>
            rarity >= 0 && rarity < RarityColours.Length ? RarityColours[rarity] : EmptyRing;

        public string NameFor(int rarity) =>
            rarity >= 0 && rarity < RarityNames.Length ? RarityNames[rarity] : "";

        void Awake()
        {
            if (StatValues.Length > 0 && StatValues[0] != null) _statColour = StatValues[0].color;
        }

        public void HideForOpen()
        {
            foreach (RectTransform panel in Panels())
                if (panel != null && panel.GetComponent<CanvasGroup>() is CanvasGroup group) group.alpha = 0f;
            ShowTooltip(false, true);
        }

        /// <summary>Панели приезжают по очереди.</summary>
        public void PlayOpen()
        {
            int index = 0;
            foreach (RectTransform panel in Panels())
            {
                if (panel == null) continue;
                CanvasGroup group = panel.GetComponent<CanvasGroup>();
                if (group == null) group = panel.gameObject.AddComponent<CanvasGroup>();
                // Место запоминается один раз: Arrive считает местом текущую позицию.
                if (!_rest.TryGetValue(panel, out Vector2 rest)) _rest[panel] = rest = panel.anchoredPosition;
                panel.anchoredPosition = rest;
                UiMotion.Arrive(panel, group, PanelOffset, OpenDuration, index++ * PanelStagger);
            }
        }

        /// <summary>Выбранная вкладка или фильтр — коралловые, остальные — синие.</summary>
        public void ShowFilter(int index)
        {
            for (int i = 0; i < Filters.Length; i++)
            {
                if (Filters[i] == null) continue;
                // Префаб CampTentWc: выбранная — оранжевая подпись и подчёркивание (нить света).
                if (TabOn == null) CampShopView.SetTab(Filters[i], i == index);
                else if (Filters[i].image != null) Filters[i].image.sprite = i == index ? TabOn : TabOff;
            }
        }

        public void ShowPage(bool atlas)
        {
            if (BagPage != null) BagPage.SetActive(!atlas);
            if (AtlasPage != null) AtlasPage.SetActive(atlas);
            if (TabOn == null)
            {
                CampShopView.SetTab(BagTab, !atlas);
                CampShopView.SetTab(AtlasTab, atlas);
            }
            else
            {
                if (BagTab != null && BagTab.image != null) BagTab.image.sprite = atlas ? TabOff : TabOn;
                if (AtlasTab != null && AtlasTab.image != null) AtlasTab.image.sprite = atlas ? TabOn : TabOff;
            }
            ShowTooltip(false, true);
        }

        /// <summary>
        /// Рамка картинки в карточке по редкости (-1 — без редкости: стат, зелье). У ячейки «Дыма и
        /// света» — цвет рамки и свет за вещью (WcSlotState), у прежней палатки — спрайт рамки.
        /// </summary>
        public void SetItemFrame(int rarity)
        {
            if (ItemState != null) ItemState.Set(rarity >= 0 ? rarity : WcSlotState.Empty, false);
            else if (ItemFrame != null && FrameFor(rarity) is Sprite frame) ItemFrame.sprite = frame;
        }

        public void ShowTooltip(bool shown, bool instant = false)
        {
            if (TooltipGroup == null || (shown == _tooltipShown && !instant)) return;
            // Карточка всплывает из скрытой — чернила проявляются заново (без огня). С ячейки на ячейку
            // она не успевает погаснуть: тогда только меняется текст, дым не мигает на каждом наведении.
            if (shown && !instant && TooltipInk != null && TooltipInk.isActiveAndEnabled && TooltipGroup.alpha < .5f) TooltipInk.Show();
            _tooltipShown = shown;
            if (instant) TooltipGroup.alpha = shown ? 1f : 0f;
            else UiMotion.FadeTo(TooltipGroup, shown ? 1f : 0f, TooltipDuration);
        }

        /// <summary>
        /// Карточка встаёт справа от цели, а если не влезает в экран — слева;
        /// по высоте прижимается к краю экрана.
        /// </summary>
        public void PlaceTooltip(RectTransform target)
        {
            if (Tooltip == null || target == null || !(Tooltip.parent is RectTransform space)) return;
            Vector2 centre = space.InverseTransformPoint(target.TransformPoint(target.rect.center));
            Vector2 half = target.rect.size * .5f * (target.lossyScale.x / Mathf.Max(.0001f, space.lossyScale.x));
            Vector2 size = Tooltip.rect.size;
            Rect bounds = space.rect;
            float x = centre.x + half.x + TooltipGap;
            if (x + size.x > bounds.xMax) x = centre.x - half.x - TooltipGap - size.x;
            float y = Mathf.Clamp(centre.y + half.y, bounds.yMin + size.y, bounds.yMax);
            Tooltip.pivot = new Vector2(0f, 1f);
            Tooltip.anchorMin = Tooltip.anchorMax = new Vector2(.5f, .5f);
            Tooltip.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>
        /// Стат досчитывает от показанного к новому; рост вспыхивает зелёным, падение красным.
        /// Первое значение ставится сразу. format превращает число в строку («75», «12%»).
        /// </summary>
        public void ShowStat(int index, float value, Func<float, string> format)
        {
            if (index < 0 || index >= StatValues.Length || StatValues[index] == null) return;
            TMP_Text label = StatValues[index];
            bool known = _statKnown[index];
            float from = _statShown[index];
            _statShown[index] = value;
            _statKnown[index] = true;
            if (!known || Mathf.Approximately(from, value))
            {
                if (!known) label.text = format(value);
                return;
            }
            Color flash = value > from ? StatUp : StatDown;
            UiMotion.Play(label, 40, CountDuration, t =>
            {
                label.text = format(Mathf.Lerp(from, value, t));
                label.color = Color.Lerp(flash, _statColour, Mathf.Clamp01((t - .5f) * 2f));
                label.rectTransform.localScale = Vector3.one * (1f + .12f * Mathf.Sin(t * Mathf.PI));
            });
        }

        /// <summary>
        /// Копия значка летит по дуге из одной ячейки в другую; по прилёте
        /// ячейка-цель показывает свой значок и вспыхивает цветом редкости.
        /// </summary>
        public void Fly(Sprite icon, RectTransform from, CampTentCell to, Color colour, Action done)
        {
            if (icon == null || from == null || to == null || !(transform is RectTransform space))
            {
                done?.Invoke();
                return;
            }
            var flying = new GameObject("Flying item", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)flying.transform;
            rect.SetParent(space, false);
            rect.SetAsLastSibling();
            var image = flying.GetComponent<Image>();
            image.sprite = icon;
            image.preserveAspect = true;
            image.raycastTarget = false;

            var target = (RectTransform)to.transform;
            Vector2 start = space.InverseTransformPoint(from.TransformPoint(from.rect.center));
            Vector2 end = space.InverseTransformPoint(target.TransformPoint(target.rect.center));
            Vector2 startSize = from.rect.size * .8f, endSize = target.rect.size * .7f;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            to.Hold = true;
            UiMotion.Play(rect, 41, FlyDuration, t =>
            {
                Vector2 p = Vector2.LerpUnclamped(start, end, t);
                p.y += Mathf.Sin(t * Mathf.PI) * FlyArc;
                rect.anchoredPosition = p;
                rect.sizeDelta = Vector2.LerpUnclamped(startSize, endSize, t) * (1f + .25f * Mathf.Sin(t * Mathf.PI));
            }, UiMotion.EaseOut, () =>
            {
                if (flying != null) Destroy(flying);
                if (to != null)
                {
                    to.Hold = false;
                    to.Flash(colour);
                }
                done?.Invoke();
            });
        }

        IEnumerable<RectTransform> Panels()
        {
            yield return HeroPanel;
            yield return BagPanel;
        }
    }
}
