using System;
using TMPro;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Тема UI «Ночная акварель» (P4, утверждена владельцем 22 сентября 2026).
    /// Эталон: ART/UI/concepts-2026-09-22/final-2/kit-sheet-2.png, -3-controls, -4-hud.
    ///
    /// ОДНО МЕСТО ДЛЯ ПРАВКИ РУКАМИ. Ассет лежит в Resources/UI/UiTheme.asset.
    /// Цвета, шрифты и спрайты пака меняются в инспекторе; элементы с
    /// <see cref="ThemeColor"/> и <see cref="ThemeFont"/> подхватывают правку сразу,
    /// в том числе вне Play.
    ///
    /// Спрайты пака белые (tools/ui-kit/build_watercolor.py): цвет им даёт тема,
    /// поэтому смена оранжевого или цвета редкости не требует перерисовки.
    /// Форма общая — скругление 14 у окон и 8 у ячеек, проволока 1,5 единицы Canvas; менять её
    /// надо в скрипте пака, а не здесь, иначе рамки и заливки разойдутся.
    /// </summary>
    [CreateAssetMenu(menuName = "Разлом/UI/Тема", fileName = "UiTheme")]
    public sealed class UiTheme : ScriptableObject
    {
        public enum Role
        {
            Veil, Panel, PanelLine, Highlight, Track,
            Text, TextMuted, TextOnAccent,
            Accent, AccentHover, AccentPressed, Disabled,
            Common, Rare,
            Lavidium, Health, Experience, Coins,
            Good, Bad,
            Epic, Unique,
        }

        public enum FontRole { Heading, Body }

        [Header("Основа")]
        [Tooltip("Затемнение мира под окнами")] public Color Veil = Hex("070A10", .62f);
        [Tooltip("Заливка окон, ячеек, карточек")] public Color Panel = Hex("111620", .92f);
        [Tooltip("Серебро рамок и разделителей (блики нарисованы в спрайте)")] public Color PanelLine = Hex("D8E1EE", 1f);
        [Tooltip("Свет по верхней кромке панели")] public Color Highlight = Hex("FFFFFF", .07f);
        [Tooltip("Пустая часть полос и слайдеров")] public Color Track = Hex("1B2029", .95f);

        [Header("Текст")]
        public Color Text = Hex("F4F7FB");
        public Color TextMuted = Hex("93A2BC");
        [Tooltip("Текст на оранжевой кнопке")] public Color TextOnAccent = Hex("FFF6EE");

        [Header("Акцент и состояния")]
        [Tooltip("Единственный акцент: выбранное, основная кнопка, маркер пункта")] public Color Accent = Hex("FD7442");
        public Color AccentHover = Hex("FF8C5E");
        public Color AccentPressed = Hex("DF5E30");
        public Color Disabled = Hex("3C4048", .6f);

        [Header("Редкость")]
        public Color Common = Hex("A6B3C8");
        public Color Rare = Hex("3BF0F5");
        [Tooltip("Эпическая: фиолет")] public Color Epic = Hex("A765FF");
        [Tooltip("Уникальная: огненно-красная, не путать с оранжевым акцентом выбора")] public Color Unique = Hex("FF5236");

        [Header("Ресурсы")]
        public Color Lavidium = Hex("FA883C");
        public Color Health = Hex("F34F37");
        public Color Experience = Hex("E6EBF2");
        [Tooltip("Медь, не золото: рядом синий нельзя сочетать с жёлтым")] public Color Coins = Hex("D98A5A");

        [Header("Сравнение статов")]
        public Color Good = Hex("8FE3A8");
        public Color Bad = Hex("FF6A5A");

        [Header("Шрифты")]
        [Tooltip("Заголовки, меню, названия — Philosopher")] public TMP_FontAsset Heading;
        [Tooltip("Текст, цифры, подписи — Nunito")] public TMP_FontAsset Body;
        [Tooltip("Цифры урона над врагами — Nunito Bold")] public TMP_FontAsset Numbers;

        [Header("Размеры текста, единицы Canvas (1920×1080)")]
        public float TitleSize = 44f;
        public float HeadingSize = 28f;
        public float BodySize = 20f;
        public float CaptionSize = 16f;

        [Header("Отступы, единицы Canvas")]
        public float SpaceS = 8f;
        public float SpaceM = 16f;
        public float SpaceL = 24f;

        [Header("Спрайты пака (Assets/UI/Kit/Watercolor)")]
        public Sprite Fill;
        public Sprite Frame;
        public Sprite FrameBold;
        public Sprite HighlightSprite;
        [Tooltip("Тень внизу панели (тинт — вуаль)")] public Sprite ShadeSprite;
        [Header("Малый радиус (8): ячейки, клавиши, строки, флажок")]
        public Sprite FillSmall;
        public Sprite FrameSmall;
        public Sprite FrameBoldSmall;
        public Sprite HighlightSmall;
        public Sprite ShadeSmall;
        public Sprite GlowSmall;
        public Sprite InnerGlowSmall;
        [Header("Остальное")]
        [Tooltip("Свечение наружу: элемент со свечением шире на 24 единицы с каждой стороны")] public Sprite Glow;
        public Sprite InnerGlow;
        [Tooltip("Свечение кнопки-капсулы")] public Sprite ButtonGlow;
        public Sprite FrameDashed;
        [Tooltip("Кнопка-капсула, высота 56")] public Sprite ButtonFill;
        public Sprite ButtonFrame;
        [Tooltip("Плашка значения, высота 40")] public Sprite PillFill;
        public Sprite PillFrame;
        [Tooltip("Ярлык редкости, высота 30")] public Sprite TagFill;
        public Sprite TagFrame;
        [Tooltip("Полоса, высота 14")] public Sprite BarFill;
        public Sprite BarFrame;
        public Sprite CircleFill;
        public Sprite CircleFrame;
        public Sprite CircleFrameBold;
        public Sprite CircleRing;
        public Sprite DiamondSmall;
        public Sprite DiamondMedium;
        public Sprite DiamondLarge;
        public Sprite DiamondFrame;
        public Sprite DiamondFrameSmall;
        public Sprite DiamondFill;
        public Sprite Gem;
        public Sprite Cross;
        public Sprite Check;
        public Sprite Plus;
        public Sprite ChevronDown;
        public Sprite ChevronRight;
        public Sprite TriangleUp;
        public Sprite TriangleDown;
        public Sprite VeilRadial;
        public Sprite VeilLinear;
        [Tooltip("Сплошной белый: линии, подчёркивания, заполнение полос")] public Sprite Pixel;
        [Tooltip("Акварельное зерно: слой Tiled поверх заливки, прозрачность 3–8%")] public Sprite Grain;
        [Tooltip("Ручка переключателя и слайдера")] public Sprite Knob;
        [Header("Боевой HUD")]
        [Tooltip("Полоса высотой 16–26: здоровье героя, босс")] public Sprite BarFillLarge;
        public Sprite BarFrameLarge;
        [Tooltip("Тонкое кольцо портрета")] public Sprite CircleFrameLarge;
        [Tooltip("Рамка без верхней прямой: заголовок разрывает рамку (полоса босса)")] public Sprite FrameOpenTop;
        [Tooltip("Кольцо из делений: перезарядка")] public Sprite RingTicks;
        [Tooltip("Свечение кольца баффа: слой шире элемента на четверть с каждой стороны")] public Sprite RingGlow;
        [Tooltip("Мягкое пятно света: за портретом, значком баффа, критом")] public Sprite Blob;
        [Tooltip("Зарево низкого здоровья: сильнее слева")] public Sprite Haze;
        [Tooltip("Искра крита")] public Sprite Spark;
        [Tooltip("Стрелка героя на миникарте")] public Sprite Arrow;
        [Tooltip("Указатель подсказки: линии и ромб на острие, смотрит вправо")] public Sprite Pointer;
        [Tooltip("Мышь для подсказки «левый клик»")] public Sprite Mouse;

        /// <summary>Правка темы в инспекторе: элементы перекрашиваются сразу.</summary>
        public static event Action Changed;

        const string ResourcePath = "UI/UiTheme";
        static UiTheme _current;

        public static UiTheme Current
        {
            get
            {
                if (_current != null) return _current;
                _current = Resources.Load<UiTheme>(ResourcePath);
                // Без ассета — цвета по умолчанию, чтобы UI не падал до первой сборки темы.
                if (_current == null) { _current = CreateInstance<UiTheme>(); _current.hideFlags = HideFlags.DontSave; }
                return _current;
            }
        }

        public Color Get(Role role) => role switch
        {
            Role.Veil => Veil,
            Role.Panel => Panel,
            Role.PanelLine => PanelLine,
            Role.Highlight => Highlight,
            Role.Track => Track,
            Role.Text => Text,
            Role.TextMuted => TextMuted,
            Role.TextOnAccent => TextOnAccent,
            Role.Accent => Accent,
            Role.AccentHover => AccentHover,
            Role.AccentPressed => AccentPressed,
            Role.Disabled => Disabled,
            Role.Common => Common,
            Role.Rare => Rare,
            Role.Lavidium => Lavidium,
            Role.Health => Health,
            Role.Experience => Experience,
            Role.Coins => Coins,
            Role.Good => Good,
            Role.Bad => Bad,
            Role.Epic => Epic,
            Role.Unique => Unique,
            _ => Color.magenta,
        };

        public TMP_FontAsset Get(FontRole role) => role == FontRole.Heading ? Heading : Body;

        void OnValidate()
        {
            if (Resources.Load<UiTheme>(ResourcePath) == this) _current = this;
            Changed?.Invoke();
        }

        static Color Hex(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color c);
            c.a = alpha;
            return c;
        }
    }
}
