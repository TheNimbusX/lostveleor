using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Одна рамка ячейки вещи и фон редкости (владелец 23 сентября, вариант А).
    /// Раньше выбор и наведение рисовали свои контуры поверх рамки редкости — у ячейки
    /// «появлялась ещё одна рамка». Теперь рамка одна и меняется сама:
    /// обычная — светящаяся линия цвета редкости; наведение — светлеет; выбор — толще и светлее.
    /// Фон — рисованная акварель своей редкости (24 сентября: градиент выглядел «как html»),
    /// поэтому редкость видна и у выбранной вещи. Пустая ячейка — тонкая тихая рамка без свечения.
    /// Ячейка «Дыма и света» (UiInkKit.Cell, SlotOrb) — без рисованных спрайтов: тонкая линия
    /// цвета редкости, мягкий свет редкости за вещью и слой света выбора (<see cref="Glow"/>).
    /// Свой файл обязателен: компонент стоит в префабах.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class WcSlotState : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public const int Empty = -1;
        /// <summary>Ячейка без редкости (зелье): серебряная рамка, без цветного фона.</summary>
        public const int Plain = -2;

        [Tooltip("Единственная рамка ячейки")] public Image Frame;
        [Tooltip("Фон редкости; у пустой ячейки выключен")] public Image Fill;
        [Tooltip("Рамка вещи: светящаяся линия")] public Sprite FrameSprite;
        [Tooltip("Рамка выбранной: толще и ярче")] public Sprite SelectedSprite;
        [Tooltip("Рамка пустой ячейки и зелья: тонкая, без свечения. Пусто — та же, что у вещи")] public Sprite QuietSprite;
        [Tooltip("Насколько свечение рамки выходит за ячейку, единиц Canvas (у тихой рамки 0)")] public float GlowBleed = 14f;
        [Tooltip("Рисованный фон: обычная, редкая, эпическая, уникальная. Пусто — фон красится цветом редкости")]
        public Sprite[] FillSprites = new Sprite[4];

        [Tooltip("-1 — пусто, -2 — без редкости, 0..3 — обычная, редкая, эпическая, уникальная")]
        public int Rarity = Empty;
        public bool Selected;
        [Tooltip("Цвет рамки выбранной")] public UiTheme.Role SelectedRole = UiTheme.Role.Text;

        [Header("Сила")]
        [Range(0f, 1f)] public float EmptyAlpha = .32f;
        [Tooltip("Фон без рисунка (цветом): обычная, редкая, эпическая, уникальная")]
        public float[] FillAlpha = { .1f, .3f, .4f, .38f };
        [Tooltip("Рисованный фон: обычная, редкая, эпическая, уникальная")]
        public float[] ArtAlpha = { .9f, 1f, 1f, 1f };
        [Tooltip("Рамка обычной вещи тише редких")] [Range(0f, 1f)] public float CommonFrameAlpha = .75f;
        [Range(0f, 1f)] public float HoverLighten = .7f;
        [Tooltip("Насколько фон без рисунка ярче при наведении")] public float HoverFill = .14f;
        [Tooltip("Секунд на проявление наведения")] public float Fade = .12f;
        [Tooltip("Рамка ячейки без редкости (зелье): у тонкого кольца «Дыма и света» тише серебра пака")]
        [Range(0f, 1f)] public float PlainAlpha = 1f;

        // «Дым и свет» (26 сентября): рисованных спрайтов у ячейки нет — поля спрайтов пустые, и
        // тогда меняются только цвет и сила; фон редкости — мягкий свет за вещью. Выбор и
        // наведение дополнительно зажигают слой света. У ячеек пака слоя нет — всё как раньше.
        [Header("Дым и свет")]
        [Tooltip("Свет за вещью: у выбранной горит цветом выбора, при наведении проступает цветом редкости. Пусто — нет")]
        public Graphic Glow;
        [Range(0f, 1f)] public float GlowSelected = .42f;
        [Range(0f, 1f)] public float GlowHover = .3f;

        bool _hovered;
        float _hover;

        public static UiTheme.Role RoleFor(int rarity) => rarity switch
        {
            1 => UiTheme.Role.Rare,
            2 => UiTheme.Role.Epic,
            3 => UiTheme.Role.Unique,
            _ => UiTheme.Role.Common,
        };

        public void Set(int rarity, bool selected)
        {
            if (Rarity == rarity && Selected == selected) return;
            Rarity = rarity;
            Selected = selected;
            Apply();
        }

        /// <summary>Наведение без мыши — для кадров префаба в редакторе.</summary>
        public void SetHover(float amount)
        {
            _hover = Mathf.Clamp01(amount);
            Apply();
        }

        public void OnPointerEnter(PointerEventData _) => _hovered = true;
        public void OnPointerExit(PointerEventData _) => _hovered = false;

        void OnEnable()
        {
            UiTheme.Changed += Apply;
            _hovered = false;
            _hover = 0f;
            Apply();
        }

        void OnDisable() => UiTheme.Changed -= Apply;
        // Из OnValidate нельзя включать объекты (Unity ругается) — только перекрасить.
        void OnValidate() => Apply(false);

        void Update()
        {
            float target = _hovered ? 1f : 0f;
            if (Mathf.Approximately(_hover, target)) return;
            _hover = Mathf.MoveTowards(_hover, target, Time.unscaledDeltaTime / Mathf.Max(.01f, Fade));
            Apply();
        }

        public void Apply() => Apply(true);

        void Apply(bool activate)
        {
            if (this == null) return;
            UiTheme theme = UiTheme.Current;
            bool item = Rarity >= 0;
            Color tone = item ? theme.Get(RoleFor(Rarity)) : theme.Get(UiTheme.Role.PanelLine);
            if (Fill != null)
            {
                Fill.enabled = item;
                if (item)
                {
                    Sprite art = FillSprites != null && Rarity < FillSprites.Length ? FillSprites[Rarity] : null;
                    if (art != null)
                    {
                        if (Fill.sprite != art) Fill.sprite = art;
                        if (Fill.type != Image.Type.Simple) Fill.type = Image.Type.Simple;
                        Fill.color = new Color(1f, 1f, 1f, ArtAlpha != null && Rarity < ArtAlpha.Length ? ArtAlpha[Rarity] : 1f);
                    }
                    else
                    {
                        Color fill = tone;
                        fill.a = (FillAlpha != null && Rarity < FillAlpha.Length ? FillAlpha[Rarity] : .3f) + _hover * HoverFill;
                        Fill.color = fill;
                    }
                }
            }
            if (Glow != null)
            {
                float strength = Mathf.Max(Selected ? GlowSelected : 0f, _hover * GlowHover);
                Color glow = Selected ? theme.Get(SelectedRole) : item ? tone : theme.Get(UiTheme.Role.Text);
                glow.a = strength;
                Glow.color = glow;
                // Погасший свет не рисуется: ячеек в сетке сотня. Из OnValidate — только цвет.
                bool lit = strength > .002f;
                if (activate && Glow.enabled != lit) Glow.enabled = lit;
            }
            if (Frame == null) return;
            // Рамку раньше прятала WcRarity у редких (была вторая, «Рамка редкой»); теперь она всегда одна.
            if (activate && !Frame.gameObject.activeSelf) Frame.gameObject.SetActive(true);
            bool quiet = !item && !Selected && QuietSprite != null;
            Sprite sprite = Selected && SelectedSprite != null ? SelectedSprite : quiet ? QuietSprite : FrameSprite;
            if (sprite != null && Frame.sprite != sprite) Frame.sprite = sprite;
            // Светящаяся рамка шире ячейки на поле свечения; тихая — ровно по ячейке.
            float bleed = quiet || QuietSprite == null ? 0f : GlowBleed;
            var rect = Frame.rectTransform;
            if (rect.offsetMax.x != bleed || rect.offsetMin.x != -bleed)
            {
                rect.offsetMin = new Vector2(-bleed, -bleed);
                rect.offsetMax = new Vector2(bleed, bleed);
            }
            Color frame;
            if (Selected) frame = theme.Get(SelectedRole);
            else
            {
                frame = tone;
                if (Rarity == Empty) frame.a *= EmptyAlpha;
                else if (Rarity == Plain) frame.a *= PlainAlpha;
                else if (Rarity == 0) frame.a *= CommonFrameAlpha;
                frame = Color.Lerp(frame, new Color(1f, 1f, 1f, Mathf.Max(frame.a, .9f)), _hover * HoverLighten);
            }
            Frame.color = frame;
        }
    }
}
