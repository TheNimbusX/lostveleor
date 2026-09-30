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
    ///
    /// Единый набор (лист 5, 30 сентября): шкала текста 56/32/24/18/14, кейкап, кнопки и отступы — поля ниже,
    /// умолчания и правила без Unity — UiTheme.Tokens.cs. Размер текста в коде — <see cref="Size"/>, не числом.
    /// </summary>
    [CreateAssetMenu(menuName = "Разлом/UI/Тема", fileName = "UiTheme")]
    public sealed partial class UiTheme : ScriptableObject
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
            Smoke, SmokeDeep,
        }

        public enum FontRole { Heading, Body }

        [Header("Основа")]
        [Tooltip("Затемнение мира под окнами (25 сентября: темнее — окна концептов стоят на глубокой тени)")] public Color Veil = Hex("070A10", .78f);
        [Tooltip("Заливка окон, ячеек, карточек (25 сентября: почти непрозрачная — окна не должны просвечивать)")] public Color Panel = Hex("111620", .97f);
        [Tooltip("Серебро рамок и разделителей (блики нарисованы в спрайте)")] public Color PanelLine = Hex("D8E1EE", 1f);
        [Tooltip("Свет по верхней кромке панели")] public Color Highlight = Hex("FFFFFF", .07f);
        [Tooltip("Пустая часть полос и слайдеров")] public Color Track = Hex("1B2029", .95f);

        [Header("Текст")]
        public Color Text = Hex(TextHex);
        public Color TextMuted = Hex(TextMutedHex);
        [Tooltip("Текст на оранжевой кнопке")] public Color TextOnAccent = Hex(TextOnAccentHex);

        [Tooltip("Чернильный дым материала «Дым и свет» (владелец 25 сентября): подложка текста и значков прямо над миром")] public Color Smoke = Hex("121923", .93f);
        [Tooltip("Глубокий дым: колонны окон поверх притемнённого мира (итоги, пауза) — почти чёрный и плотный")] public Color SmokeDeep = Hex("04060A", 1f);

        [Header("Акцент и состояния")]
        [Tooltip("Единственный акцент: выбранное, основная кнопка, маркер пункта")] public Color Accent = Hex(AccentHex);
        public Color AccentHover = Hex(AccentHoverHex);
        public Color AccentPressed = Hex(AccentPressedHex);
        public Color Disabled = Hex("3C4048", .6f);

        [Header("Редкость")]
        public Color Common = Hex(CommonHex);
        public Color Rare = Hex(RareHex);
        [Tooltip("Эпическая: фиолет")] public Color Epic = Hex(EpicHex);
        [Tooltip("Уникальная: огненно-красная, не путать с оранжевым акцентом выбора")] public Color Unique = Hex(UniqueHex);

        [Header("Ресурсы")]
        public Color Lavidium = Hex(LavidiumHex);
        public Color Health = Hex(HealthHex);
        public Color Experience = Hex("E6EBF2");
        [Tooltip("Медь, не золото: рядом синий нельзя сочетать с жёлтым")] public Color Coins = Hex(CoinsHex);

        [Header("Сравнение статов")]
        public Color Good = Hex(GoodHex);
        public Color Bad = Hex(BadHex);

        [Header("Шрифты")]
        [Tooltip("Заголовки, меню, названия — Philosopher")] public TMP_FontAsset Heading;
        [Tooltip("Текст, цифры, подписи — Nunito")] public TMP_FontAsset Body;
        [Tooltip("Цифры урона над врагами — Philosopher Bold, светлая антиква листа HUD")] public TMP_FontAsset Numbers;

        // Прежние TitleSize 44 / HeadingSize 28 / BodySize 20 / CaptionSize 16 нигде не читались; шкала листа 5 —
        // под новыми именами, чтобы старые числа из ассета её не перебили.
        [Header("Шкала текста листа 5, единицы Canvas (1920×1080)")]
        [Tooltip("Заголовок экрана — Philosopher")] public float TextDisplay = DisplaySizeDefault;
        [Tooltip("Раздел, заголовок окна подтверждения — Philosopher")] public float TextTitle = TitleSizeDefault;
        [Tooltip("Название карточки, подсказки, кнопки — Philosopher")] public float TextHeading = HeadingSizeDefault;
        [Tooltip("Основной текст, подписи клавиш — Nunito")] public float TextBody = BodySizeDefault;
        [Tooltip("Мелкие подписи — Nunito")] public float TextCaption = CaptionSizeDefault;

        [Header("Клавиша и кнопки, единицы Canvas")]
        [Tooltip("Кейкап листа 5: тёмный скруглённый квадрат, кремовая буква, тонкая кромка")] public float KeycapSize = KeycapSizeDefault;
        [Tooltip("Малый кейкап: строка подсказки, кнопка футера")] public float KeycapSmall = KeycapSmallDefault;
        [Tooltip("Основная и вторичная кнопка, большая")] public float ButtonHeight = ButtonHeightDefault;
        [Tooltip("Основная и вторичная кнопка, малая")] public float ButtonHeightSmall = ButtonHeightSmallDefault;

        [Header("Отступы, единицы Canvas")]
        public float SpaceXS = SpaceXSDefault;
        public float SpaceS = SpaceSDefault;
        public float SpaceM = SpaceMDefault;
        public float SpaceL = SpaceLDefault;
        public float SpaceXL = SpaceXLDefault;

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
            Role.Smoke => Smoke,
            Role.SmokeDeep => SmokeDeep,
            _ => Color.magenta,
        };

        public TMP_FontAsset Get(FontRole role) => role == FontRole.Heading ? Heading : Body;

        /// <summary>Живой размер ступени шкалы текста (правка в ассете темы).</summary>
        public float Size(TextStep step)
        {
            switch (step)
            {
                case TextStep.Display: return TextDisplay;
                case TextStep.Title: return TextTitle;
                case TextStep.Heading: return TextHeading;
                case TextStep.Body: return TextBody;
                default: return TextCaption;
            }
        }

        /// <summary>Шрифт ступени: 56/32/24 — Philosopher, 18/14 — Nunito (лист 5, «правило регистра»).</summary>
        public static FontRole FontFor(TextStep step) => step <= TextStep.Heading ? FontRole.Heading : FontRole.Body;

        /// <summary>«#RRGGBB» роли — для rich text подписей.</summary>
        public string HexOf(Role role) => "#" + ColorUtility.ToHtmlStringRGB(Get(role));

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
