using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Палатка снаряжения на Canvas по концепту владельца
    /// ART/UI/concepts-2026-09-15/v3-tent.png: сверху лента «Снаряжение», слева
    /// Пелаг на ковре со слотами вокруг и полосой статов, справа сумка с
    /// вкладками, внизу подсказки; карточка предмета всплывает рядом с ячейкой.
    ///
    /// Префаб собирает CampTentBuilder, дальше он правится руками. Здесь только
    /// ссылки на части и внешний вид (сцена героя, досчёт статов, перелёт вещи);
    /// что надеть и что показать — решает CampInventoryView.
    /// </summary>
    public sealed class CampTentView : MonoBehaviour
    {
        [HideInInspector] public int LayoutVersion;

        [Header("Общее")]
        public CanvasGroup Backdrop;
        [Tooltip("Размытый снимок лагеря под палаткой")] public UiBackdropBlur BackdropBlur;
        public RectTransform TitleRibbon;
        public RectTransform HintBar;
        public Button Close;
        [Tooltip("«I · Закрыть» в подсказках — тоже кнопка")] public Button CloseHint;
        [Tooltip("Строка сообщений: «не подходит для слота» и т. п.")] public TMP_Text Feedback;
        public TMP_Text Gold;
        public TMP_Text Shards;
        public TMP_Text Lavidium;
        public TMP_Text Hint;

        [Header("Герой")]
        public RectTransform HeroPanel;
        [Tooltip("Рисованный Пелаг. Пусто — живой портрет из студии (Portrait)")] public Image HeroArt;
        public RawImage Portrait;
        public TMP_Text HeroName;
        [Tooltip("Урон, броня, здоровье, лавидий")] public TMP_Text[] StatValues = new TMP_Text[4];
        [Tooltip("Оружие, броня, кольцо, талисман")] public CampTentCell[] Worn = new CampTentCell[4];

        [Header("Сцена героя")]
        [Tooltip("Узел героя: дышит, опираясь на ступни (pivot внизу)")] public RectTransform HeroStage;
        [Tooltip("Тёплое пятно света за Пелагом")] public Image HeroLight;
        [Tooltip("Свечение по краю ковра")] public Image RugGlow;
        [Tooltip("Область, где летают пылинки")] public RectTransform MotesArea;
        [Tooltip("Шаблон пылинки; сам выключен")] public Image MoteTemplate;
        [Range(0, 40)] public int MoteCount = 14;
        [Tooltip("Вдох-выдох героя, секунд")] public float BreathPeriod = 3f;
        [Range(0f, .05f)] public float BreathAmount = .01f;
        [Range(0f, 1f)] public float LightMin = .55f;
        [Range(0f, 1f)] public float LightMax = .8f;

        [Header("Сумка")]
        public RectTransform BagPanel;
        public RectTransform BagGrid;
        [Tooltip("Шаблон ячейки сумки; сам остаётся выключенным")] public CampTentCell BagTemplate;
        public TMP_Text BagCount;
        [Tooltip("Все, оружие, броня, кольца, талисманы")] public Button[] Filters = new Button[5];
        [Tooltip("Значки фильтров; пусто — у вкладки подпись")] public Image[] FilterIcons = new Image[5];
        [Tooltip("Коралловая подложка выбранной вкладки; переезжает")] public RectTransform FilterIndicator;

        [Header("Карточка предмета")]
        [Tooltip("Всплывает рядом с ячейкой под мышью или выбранной")] public RectTransform Tooltip;
        public CanvasGroup TooltipGroup;
        [Tooltip("Полоса цвета редкости сверху карточки")] public Image TooltipBand;
        [Tooltip("Зазор между ячейкой и карточкой")] public float TooltipGap = 14f;
        public RectTransform DetailPanel;
        public Image ItemFrame;
        public Image ItemArt;
        public TMP_Text ItemTitle;
        public TMP_Text ItemRarity;
        public TMP_Text ItemKind;
        public TMP_Text ItemStats;
        public Button Equip;
        public Button Unequip;

        [Header("Редкости: обычная, редкая, эпическая, уникальная")]
        public Sprite EmptyFrame;
        public Sprite SelectedFrame;
        public Sprite[] RarityFrames = new Sprite[4];
        public Color[] RarityColours =
        {
            new Color32(0x8C, 0x86, 0x7A, 0xFF),
            new Color32(0x1F, 0x9F, 0xB0, 0xFF),
            new Color32(0x86, 0x4F, 0xC9, 0xFF),
            new Color32(0xD6, 0x4A, 0x36, 0xFF),
        };
        [Tooltip("Цвет кольца пустого круглого слота")] public Color EmptyRing = Color.white;
        public string[] RarityNames = { "Обычная", "Редкая", "Эпическая", "Уникальная" };

        [Header("Надевание")]
        [Tooltip("Полёт значка из сумки в слот и обратно, секунд")] public float FlyDuration = .28f;
        [Tooltip("Высота дуги полёта")] public float FlyArc = 70f;
        [Tooltip("Досчёт статов, секунд")] public float CountDuration = .4f;
        public Color StatUp = new Color32(0x8C, 0xE0, 0x7A, 0xFF);
        public Color StatDown = new Color32(0xFF, 0x7A, 0x66, 0xFF);

        [Header("Появление")]
        public float OpenDuration = .22f;
        [Tooltip("Откуда приезжает панель (сдвиг от места)")] public Vector2 PanelOffset = new Vector2(0f, -26f);
        [Tooltip("Задержка между панелями, секунд")] public float PanelStagger = .06f;
        public float FilterDuration = .18f;
        public float TooltipDuration = .12f;

        readonly Dictionary<RectTransform, Vector2> _rest = new Dictionary<RectTransform, Vector2>();
        bool _tooltipShown;

        readonly float[] _statShown = { float.NaN, float.NaN, float.NaN, float.NaN };
        Color _statColour = Color.white;

        struct Mote { public RectTransform Rect; public Image Image; public Vector2 Start; public float Speed, Phase, Life, Born; }
        Mote[] _motes;
        Vector3 _heroScale = Vector3.one;

        public Sprite FrameFor(int rarity) =>
            rarity >= 0 && rarity < RarityFrames.Length && RarityFrames[rarity] != null ? RarityFrames[rarity] : EmptyFrame;

        public Color ColourFor(int rarity) =>
            rarity >= 0 && rarity < RarityColours.Length ? RarityColours[rarity] : EmptyRing;

        public string NameFor(int rarity) =>
            rarity >= 0 && rarity < RarityNames.Length ? RarityNames[rarity] : "";

        void Awake()
        {
            if (HeroStage != null) _heroScale = HeroStage.localScale;
            if (StatValues.Length > 0 && StatValues[0] != null) _statColour = StatValues[0].color;
            BuildMotes();
        }

        /// <summary>Скрыть всё до снимка фона: размытие не должно захватить саму палатку.</summary>
        public void HideForSnapshot()
        {
            if (Backdrop != null) Backdrop.alpha = 0f;
            foreach (RectTransform panel in Panels())
                if (panel != null && panel.GetComponent<CanvasGroup>() is CanvasGroup group) group.alpha = 0f;
            ShowTooltip(false, true);
        }

        /// <summary>Фон проявляется, лента и панели приезжают по очереди.</summary>
        public void PlayOpen()
        {
            if (Backdrop != null)
            {
                Backdrop.alpha = 0f;
                UiMotion.FadeTo(Backdrop, 1f, OpenDuration);
            }
            int index = 0;
            foreach (RectTransform panel in Panels())
            {
                if (panel == null) continue;
                CanvasGroup group = panel.GetComponent<CanvasGroup>();
                if (group == null) group = panel.gameObject.AddComponent<CanvasGroup>();
                // Место запоминается один раз: Arrive считает местом текущую позицию,
                // и повторное открытие посреди анимации сдвигало бы панель всё дальше.
                if (!_rest.TryGetValue(panel, out Vector2 rest)) _rest[panel] = rest = panel.anchoredPosition;
                panel.anchoredPosition = rest;
                UiMotion.Arrive(panel, group, PanelOffset, OpenDuration, index++ * PanelStagger);
            }
        }

        /// <summary>Подложка вкладки переезжает под выбранную.</summary>
        public void ShowFilter(int index, bool instant)
        {
            if (FilterIndicator == null || index < 0 || index >= Filters.Length || Filters[index] == null) return;
            var target = (RectTransform)Filters[index].transform;
            FilterIndicator.sizeDelta = target.sizeDelta;
            if (instant) FilterIndicator.anchoredPosition = target.anchoredPosition;
            else UiMotion.MoveTo(FilterIndicator, target.anchoredPosition, FilterDuration);
        }

        public void ShowTooltip(bool shown, bool instant = false)
        {
            if (TooltipGroup == null || (shown == _tooltipShown && !instant)) return;
            _tooltipShown = shown;
            if (instant) TooltipGroup.alpha = shown ? 1f : 0f;
            else UiMotion.FadeTo(TooltipGroup, shown ? 1f : 0f, TooltipDuration);
        }

        /// <summary>
        /// Карточка встаёт справа от ячейки, а если не влезает в экран — слева;
        /// по высоте прижимается к краю экрана.
        /// </summary>
        public void PlaceTooltip(RectTransform cell)
        {
            if (Tooltip == null || cell == null || !(Tooltip.parent is RectTransform space)) return;
            Vector2 centre = space.InverseTransformPoint(cell.TransformPoint(cell.rect.center));
            Vector2 half = cell.rect.size * .5f * (cell.lossyScale.x / Mathf.Max(.0001f, space.lossyScale.x));
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
        /// Стат досчитывает от показанного к новому; рост вспыхивает зелёным,
        /// падение красным. Первое значение ставится сразу.
        /// </summary>
        public void ShowStat(int index, float value)
        {
            if (index < 0 || index >= StatValues.Length || StatValues[index] == null) return;
            TMP_Text label = StatValues[index];
            float from = _statShown[index];
            _statShown[index] = value;
            if (float.IsNaN(from) || Mathf.Approximately(from, value))
            {
                if (float.IsNaN(from)) label.text = Mathf.RoundToInt(value).ToString();
                return;
            }
            Color flash = value > from ? StatUp : StatDown;
            UiMotion.Play(label, 40, CountDuration, t =>
            {
                label.text = Mathf.RoundToInt(Mathf.Lerp(from, value, t)).ToString();
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

        void BuildMotes()
        {
            if (MotesArea == null || MoteTemplate == null || MoteCount <= 0) return;
            MoteTemplate.gameObject.SetActive(false);
            _motes = new Mote[MoteCount];
            float now = UiMotion.Now;
            for (int i = 0; i < MoteCount; i++)
            {
                Image image = Instantiate(MoteTemplate, MotesArea);
                image.gameObject.SetActive(true);
                image.raycastTarget = false;
                _motes[i] = new Mote { Rect = image.rectTransform, Image = image };
                Respawn(ref _motes[i], now - UnityEngine.Random.Range(0f, 6f));
            }
        }

        void Respawn(ref Mote mote, float now)
        {
            // Пылинка привязана к центру области: старт в нижней половине, дальше всплывает.
            Vector2 half = MotesArea.rect.size * .5f;
            mote.Start = new Vector2(UnityEngine.Random.Range(-half.x, half.x), UnityEngine.Random.Range(-half.y, 0f));
            mote.Speed = UnityEngine.Random.Range(12f, 26f);
            mote.Phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            mote.Life = UnityEngine.Random.Range(4f, 7f);
            mote.Born = now;
            float size = UnityEngine.Random.Range(4f, 9f);
            mote.Rect.sizeDelta = new Vector2(size, size);
        }

        void LateUpdate()
        {
            float now = UiMotion.Now;
            float wave = .5f + .5f * Mathf.Sin(now / Mathf.Max(.2f, BreathPeriod) * Mathf.PI * 2f);
            if (HeroStage != null)
                HeroStage.localScale = new Vector3(_heroScale.x, _heroScale.y * (1f + BreathAmount * (wave - .5f) * 2f), _heroScale.z);
            if (HeroLight != null)
            {
                Color c = HeroLight.color;
                c.a = Mathf.Lerp(LightMin, LightMax, wave);
                HeroLight.color = c;
            }
            if (RugGlow != null)
            {
                Color c = RugGlow.color;
                c.a = Mathf.Lerp(.25f, .55f, 1f - wave);
                RugGlow.color = c;
            }
            if (_motes == null) return;
            for (int i = 0; i < _motes.Length; i++)
            {
                ref Mote mote = ref _motes[i];
                if (mote.Rect == null) continue;
                float age = now - mote.Born;
                if (age > mote.Life) { Respawn(ref mote, now); age = 0f; }
                float life = age / mote.Life;
                mote.Rect.anchoredPosition = mote.Start + new Vector2(Mathf.Sin(age * .9f + mote.Phase) * 10f, age * mote.Speed);
                Color c = mote.Image.color;
                c.a = Mathf.Sin(life * Mathf.PI) * .8f;
                mote.Image.color = c;
            }
        }

        IEnumerable<RectTransform> Panels()
        {
            yield return TitleRibbon;
            yield return HeroPanel;
            yield return BagPanel;
            yield return HintBar;
        }
    }
}
