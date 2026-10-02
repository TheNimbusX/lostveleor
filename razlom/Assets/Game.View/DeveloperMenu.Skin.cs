using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Скин меню разработчика (F8): читаемая тёмная панель служебного окна, не игровой «Дым и свет».
    /// Nunito (правило шрифтов 22.09), цвета — константы UiTheme.Tokens. Размеры шрифтов и отступов умножаются на
    /// масштаб (<see cref="DevMenuRules.Scale"/>), стили пересобираются при его смене.
    ///
    /// Текстуры — обычные Texture2D без HideFlags.DontSave (память: объект с DontSave пережил выход из Play и дал
    /// голубой экран); меню уничтожает их в OnDestroy через <see cref="Dispose"/>.
    /// </summary>
    public sealed class DeveloperMenuSkin
    {
        // Палитра панели (аудит F8, раздел 2.6). Акценты и текст — из токенов темы.
        private static readonly Color Panel = Hex("0F131A", .97f);
        private static readonly Color Dim = new Color(0f, 0f, 0f, .35f);
        private static readonly Color HeaderFill = Hex("161C26");
        private static readonly Color CardFill = Hex("141A23");
        private static readonly Color CardEdge = Hex("263042");
        private static readonly Color DangerFill = Hex("1E1316");
        private static readonly Color DangerEdge = Hex("5A2A2A");
        private static readonly Color ButtonFill = Hex("222B39");
        private static readonly Color ButtonHover = Hex("2C3749");
        private static readonly Color ButtonDown = Hex("36435A");
        private static readonly Color FieldFill = Hex("0B0F15");
        private static readonly Color FieldEdge = Hex("3A4760");
        private static readonly Color DangerButton = Hex("3A1A1A");
        private static readonly Color DangerButtonHover = Hex("4A2121");
        private static readonly Color TrackFill = Hex("10151D");

        internal static readonly Color Text = Hex(UiTheme.TextHex);
        internal static readonly Color Muted = Hex(UiTheme.TextMutedHex);
        internal static readonly Color Accent = Hex(UiTheme.AccentHex);
        internal static readonly Color AccentHover = Hex(UiTheme.AccentHoverHex);
        internal static readonly Color OnAccent = Hex(UiTheme.TextOnAccentHex);
        internal static readonly Color Good = Hex(UiTheme.GoodHex);
        internal static readonly Color Bad = Hex(UiTheme.BadHex);
        internal static readonly Color Amber = Hex(UiTheme.LavidiumHex);

        private Texture2D _panel, _dim, _header, _card, _dangerCard, _button, _buttonHover, _buttonDown, _accent, _accentHover,
            _danger, _dangerHover, _armed, _field, _track, _chipOn, _chipOff, _chipWarn, _chipDanger, _chipNeutral, _line;
        private Font _regular, _semibold, _bold;
        private float _scale = -1f;

        public float Scale => _scale;

        public GUIStyle Title, SectionHeader, Label, Hint, ErrorText, Button, ButtonOn, ButtonDanger, ButtonArmed, ToggleRow,
            Tab, TabOn, Field, Card, DangerCard, Header, Footer, Body, Chip, ChipOn, ChipOff, ChipWarn, ChipDanger, ChipNeutral,
            ChipAccent, Scrollbar, Small, SmallOn, QuickButton, QuickArmed, QuickToggle, RowLabel, RowHint, RowNumber;

        public Texture2D PanelTexture => _panel;
        public Texture2D DimTexture => _dim;
        public Texture2D LineTexture => _line;

        /// <summary>Пересобрать стили под масштаб. Только из OnGUI: копии стилей GUI.skin доступны лишь там.</summary>
        public void Ensure(float scale)
        {
            if (_panel == null) CreateTextures();
            if (_regular == null) LoadFonts();
            if (Mathf.Abs(scale - _scale) < .001f && Title != null) return;
            _scale = scale;
            Build(scale);
        }

        public void Dispose()
        {
            foreach (var texture in new[] { _panel, _dim, _header, _card, _dangerCard, _button, _buttonHover, _buttonDown, _accent,
                         _accentHover, _danger, _dangerHover, _armed, _field, _track, _chipOn, _chipOff, _chipWarn, _chipDanger,
                         _chipNeutral, _line })
                if (texture != null) Object.Destroy(texture);
            _panel = null;
            Title = null;
            _scale = -1f;
        }

        public int Px(float value) => Mathf.Max(1, Mathf.RoundToInt(value * _scale));

        private void LoadFonts()
        {
            _regular = Resources.Load<Font>("UI/Fonts/Nunito-Regular");
            _semibold = Resources.Load<Font>("UI/Fonts/Nunito-SemiBold") ?? _regular;
            _bold = Resources.Load<Font>("UI/Fonts/Nunito-Bold") ?? _semibold;
        }

        private void CreateTextures()
        {
            _panel = Solid(Panel);
            _dim = Solid(Dim);
            _header = Solid(HeaderFill);
            _card = Framed(CardFill, CardEdge);
            _dangerCard = Framed(DangerFill, DangerEdge);
            _button = Solid(ButtonFill);
            _buttonHover = Solid(ButtonHover);
            _buttonDown = Solid(ButtonDown);
            _accent = Solid(Accent);
            _accentHover = Solid(AccentHover);
            _danger = Solid(DangerButton);
            _dangerHover = Solid(DangerButtonHover);
            _armed = Solid(Bad);
            _field = Framed(FieldFill, FieldEdge);
            _track = Solid(TrackFill);
            _chipOn = Solid(Hex("1E3326"));
            _chipOff = Solid(Hex("0E1218"));
            _chipWarn = Solid(Hex("33241A"));
            _chipDanger = Solid(DangerButton);
            _chipNeutral = Solid(Hex("1B222D"));
            _line = Solid(CardEdge);
        }

        private void Build(float s)
        {
            Font regular = _regular != null ? _regular : GUI.skin.font;
            Font semibold = _semibold != null ? _semibold : regular;
            Font bold = _bold != null ? _bold : semibold;

            Title = TextStyle(bold, 18, Text);
            Title.alignment = TextAnchor.MiddleLeft;
            SectionHeader = TextStyle(bold, 13, Accent);
            SectionHeader.margin = Offsets(0, 0, 0, 6);
            Label = TextStyle(regular, 16, Text);
            Label.wordWrap = true;
            Hint = TextStyle(regular, 13, Muted);
            Hint.wordWrap = true;
            Hint.margin = Offsets(0, 0, 2, 6);
            // Подписи в одну строку с кнопками высотой 32: по центру строки, без переноса.
            RowLabel = TextStyle(regular, 16, Text);
            RowLabel.fixedHeight = Px(32);
            RowLabel.alignment = TextAnchor.MiddleLeft;
            RowLabel.margin = Offsets(4, 4, 3, 3);
            RowNumber = new GUIStyle(RowLabel) { alignment = TextAnchor.MiddleCenter };
            RowHint = TextStyle(regular, 13, Muted);
            RowHint.fixedHeight = Px(32);
            RowHint.alignment = TextAnchor.MiddleLeft;
            RowHint.margin = Offsets(0, 0, 3, 3);
            ErrorText = TextStyle(regular, 13, Bad);
            ErrorText.wordWrap = true;
            ErrorText.margin = Offsets(0, 0, 0, 6);

            Button = ButtonStyle(semibold, 15, 34, _button, _buttonHover, _buttonDown, Text, Text);
            ButtonOn = ButtonStyle(semibold, 15, 34, _accent, _accentHover, _accent, OnAccent, OnAccent);
            ButtonDanger = ButtonStyle(semibold, 15, 34, _danger, _dangerHover, _dangerHover, Bad, Bad);
            ButtonArmed = ButtonStyle(bold, 15, 34, _armed, _armed, _armed, Hex("200C0A"), Hex("200C0A"));
            QuickButton = ButtonStyle(semibold, 15, 38, _button, _buttonHover, _buttonDown, Text, Text);
            QuickButton.margin = Offsets(0, 6, 0, 0);
            QuickArmed = ButtonStyle(bold, 15, 38, _armed, _armed, _armed, Hex("200C0A"), Hex("200C0A"));
            QuickArmed.margin = Offsets(0, 6, 0, 0);
            QuickToggle = ButtonStyle(semibold, 15, 38, _button, _buttonHover, _buttonDown, Text, Text);
            QuickToggle.margin = Offsets(0, 6, 0, 0);
            QuickToggle.alignment = TextAnchor.MiddleLeft;
            QuickToggle.padding = Offsets(12, 66, 4, 4);
            ToggleRow = ButtonStyle(semibold, 15, 34, _button, _buttonHover, _buttonDown, Text, Text);
            ToggleRow.alignment = TextAnchor.MiddleLeft;
            ToggleRow.padding = Offsets(12, 74, 4, 4);
            Small = ButtonStyle(semibold, 15, 32, _button, _buttonHover, _buttonDown, Text, Text);
            Small.padding = Offsets(6, 6, 2, 2);
            Small.margin = Offsets(0, 4, 3, 3);
            SmallOn = ButtonStyle(semibold, 15, 32, _accent, _accentHover, _accent, OnAccent, OnAccent);
            SmallOn.padding = Offsets(6, 6, 2, 2);
            SmallOn.margin = Offsets(0, 4, 3, 3);
            Tab = ButtonStyle(semibold, 15, 36, _header, _buttonHover, _buttonDown, Muted, Text);
            Tab.margin = Offsets(0, 4, 0, 0);
            TabOn = ButtonStyle(bold, 15, 36, _accent, _accentHover, _accent, OnAccent, OnAccent);
            TabOn.margin = Offsets(0, 4, 0, 0);

            Field = new GUIStyle(GUI.skin.textField)
            {
                font = regular,
                fontSize = Px(16),
                fixedHeight = Px(32),
                alignment = TextAnchor.MiddleLeft,
                padding = Offsets(8, 8, 4, 4),
                margin = Offsets(0, 0, 2, 4),
                border = new RectOffset(1, 1, 1, 1),
            };
            SetAll(Field, _field, Text);
            Field.focused.background = _field;
            Field.focused.textColor = Text;

            Card = Box(_card, 12);
            DangerCard = Box(_dangerCard, 12);
            Header = Box(_header, 0);
            Footer = Box(_header, 0);
            Body = new GUIStyle { padding = Offsets(0, 0, 0, 0) };

            ChipOn = ChipStyle(bold, _chipOn, Good);
            ChipOff = ChipStyle(bold, _chipOff, Muted);
            ChipWarn = ChipStyle(bold, _chipWarn, Amber);
            ChipDanger = ChipStyle(bold, _chipDanger, Bad);
            ChipNeutral = ChipStyle(bold, _chipNeutral, Muted);
            ChipAccent = ChipStyle(bold, _accent, OnAccent);
            Chip = ChipNeutral;

            // Имя «verticalscrollbar» сохраняется: ползунок и стрелки IMGUI ищет в GUI.skin по нему.
            Scrollbar = new GUIStyle(GUI.skin.verticalScrollbar) { fixedWidth = Px(10), margin = Offsets(4, 0, 0, 0) };
            Scrollbar.normal.background = _track;
        }

        public GUIStyle ChipFor(DevChip kind)
        {
            switch (kind)
            {
                case DevChip.On: return ChipOn;
                case DevChip.Off: return ChipOff;
                case DevChip.Warn: return ChipWarn;
                case DevChip.Danger: return ChipDanger;
                case DevChip.Accent: return ChipAccent;
                default: return ChipNeutral;
            }
        }

        private GUIStyle TextStyle(Font font, float size, Color color)
        {
            var style = new GUIStyle
            {
                font = font,
                fontSize = Px(size),
                richText = false,
                clipping = TextClipping.Clip,
                stretchWidth = true,
                padding = Offsets(0, 0, 2, 2),
                margin = Offsets(0, 0, 2, 2),
            };
            style.normal.textColor = color;
            return style;
        }

        private GUIStyle ButtonStyle(Font font, float size, float height, Texture2D normal, Texture2D hover, Texture2D active,
            Color text, Color hoverText)
        {
            var style = new GUIStyle
            {
                font = font,
                fontSize = Px(size),
                fixedHeight = Px(height),
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                stretchWidth = true,
                padding = Offsets(10, 10, 4, 4),
                margin = Offsets(0, 0, 3, 3),
            };
            style.normal.background = normal;
            style.normal.textColor = text;
            style.hover.background = hover;
            style.hover.textColor = hoverText;
            style.active.background = active;
            style.active.textColor = hoverText;
            style.focused.background = normal;
            style.focused.textColor = text;
            style.onNormal.background = normal;
            style.onNormal.textColor = text;
            style.onHover.background = hover;
            style.onHover.textColor = hoverText;
            style.onActive.background = active;
            style.onActive.textColor = hoverText;
            return style;
        }

        private GUIStyle ChipStyle(Font font, Texture2D fill, Color text)
        {
            var style = new GUIStyle
            {
                font = font,
                fontSize = Px(12),
                fixedHeight = Px(20),
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Overflow,
                stretchWidth = false,
                padding = Offsets(7, 7, 1, 1),
                margin = Offsets(0, 6, 0, 0),
            };
            style.normal.background = fill;
            style.normal.textColor = text;
            return style;
        }

        private GUIStyle Box(Texture2D fill, float padding)
        {
            var style = new GUIStyle
            {
                border = new RectOffset(1, 1, 1, 1),
                padding = Offsets(padding, padding, padding, padding),
                margin = Offsets(0, 0, 0, 0),
            };
            style.normal.background = fill;
            return style;
        }

        private RectOffset Offsets(float left, float right, float top, float bottom)
            => new RectOffset(Mathf.RoundToInt(left * _scale), Mathf.RoundToInt(right * _scale),
                Mathf.RoundToInt(top * _scale), Mathf.RoundToInt(bottom * _scale));

        private static void SetAll(GUIStyle style, Texture2D background, Color text)
        {
            style.normal.background = background;
            style.normal.textColor = text;
            style.hover.background = background;
            style.hover.textColor = text;
            style.active.background = background;
            style.active.textColor = text;
            style.onNormal.background = background;
            style.onNormal.textColor = text;
        }

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Developer menu", wrapMode = TextureWrapMode.Clamp };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>3×3: заливка с рамкой в 1 px (border стиля = 1).</summary>
        private static Texture2D Framed(Color fill, Color edge)
        {
            var texture = new Texture2D(3, 3, TextureFormat.RGBA32, false)
            {
                name = "Developer menu",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    texture.SetPixel(x, y, x == 1 && y == 1 ? fill : edge);
            texture.Apply(false, false);
            return texture;
        }

        private static Color Hex(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            color.a = alpha;
            return color;
        }
    }
}
