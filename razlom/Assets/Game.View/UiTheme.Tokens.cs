using System;

namespace Game.View
{
    /// <summary>
    /// ЕДИНЫЙ НАБОР — числа листа 5 (ART/UI/concepts-2026-09-30-hud-polish/5-kit-one-language.png, выбор владельца
    /// 30.09: «5 ок» — эталон для единообразия всех окон): шкала текста 56 / 32 / 24 / 18 / 14 (заголовки Philosopher,
    /// текст Nunito), один кейкап (тёмный скруглённый квадрат, кремовая буква, тонкая кромка), высоты кнопок, отступы
    /// и цвета темы по умолчанию.
    ///
    /// Этот файл без Unity: его собирают и тесты (tools/Combat.Presentation.Tests). Здесь — умолчания и правила;
    /// живые значения — поля ассета темы (UiTheme.cs: <c>TextDisplay</c>…, <c>KeycapSize</c>…), правятся в инспекторе.
    /// Сборщики окон и виды берут размеры через <c>UiTheme.Current.Size(TextStep)</c>, а не числом в коде.
    /// </summary>
    public sealed partial class UiTheme
    {
        /// <summary>Ступень шкалы текста листа 5.</summary>
        public enum TextStep : byte
        {
            /// <summary>56 — заголовок экрана («Выбери награду», «Настройки»).</summary>
            Display,
            /// <summary>32 — раздел, заголовок окна подтверждения.</summary>
            Title,
            /// <summary>24 — название карточки, подсказки, кнопки.</summary>
            Heading,
            /// <summary>18 — основной текст описаний, подписи клавиш.</summary>
            Body,
            /// <summary>14 — мелкие подписи, «КАПС» разделов.</summary>
            Caption,
        }

        public const float DisplaySizeDefault = 56f;
        public const float TitleSizeDefault = 32f;
        public const float HeadingSizeDefault = 24f;
        public const float BodySizeDefault = 18f;
        public const float CaptionSizeDefault = 14f;

        /// <summary>Размер ступени по умолчанию (без ассета темы — в тестах и до первой сборки темы).</summary>
        public static float DefaultSize(TextStep step)
        {
            switch (step)
            {
                case TextStep.Display: return DisplaySizeDefault;
                case TextStep.Title: return TitleSizeDefault;
                case TextStep.Heading: return HeadingSizeDefault;
                case TextStep.Body: return BodySizeDefault;
                default: return CaptionSizeDefault;
            }
        }

        /// <summary>
        /// Ближайшая ступень шкалы к размеру <paramref name="size"/>. Ровно посередине — меньшая ступень:
        /// текст, приведённый к шкале, не должен вылезать из своей рамки.
        /// </summary>
        public static TextStep NearestStep(float size)
        {
            TextStep best = TextStep.Caption;
            float bestGap = float.MaxValue;
            // От мелкой к крупной: при равном расстоянии остаётся меньшая.
            for (int i = (int)TextStep.Caption; i >= (int)TextStep.Display; i--)
            {
                float gap = Math.Abs(DefaultSize((TextStep)i) - size);
                if (gap < bestGap - .001f)
                {
                    bestGap = gap;
                    best = (TextStep)i;
                }
            }
            return best;
        }

        /// <summary>Размер ровно на шкале листа 5.</summary>
        public static bool OnScale(float size) => Math.Abs(DefaultSize(NearestStep(size)) - size) < .01f;

        // ---------------------------------------------------------------- клавиша

        /// <summary>Кейкап листа 5 (экран награды, итоги, подсказки окон).</summary>
        public const float KeycapSizeDefault = 38f;
        /// <summary>Малый кейкап: в строке подсказки, на кнопке футера, в углу плитки.</summary>
        public const float KeycapSmallDefault = 30f;
        /// <summary>Скругление кейкапа на высоту 38 — 8 единиц; у меньшего и большего — пропорционально.</summary>
        public const float KeycapCornerRatio = 8f / 38f;
        /// <summary>Буква кейкапа — Nunito в половину высоты (на 38 — 19).</summary>
        public const float KeycapLetterRatio = .5f;
        /// <summary>Длинная подпись («Esc», «Space», «ЛКМ»): поля вокруг текста, в высотах клавиши.</summary>
        public const float KeycapPadRatio = .5f;
        /// <summary>Самая широкая клавиша, в высотах: дальше подпись ужимается сама.</summary>
        public const float KeycapMaxWidthRatio = 4.5f;
        /// <summary>Прозрачность тонкой кромки кейкапа (цвет — PanelLine темы).</summary>
        public const float KeycapEdgeAlpha = .4f;
        /// <summary>Прозрачность тёмной подложки кейкапа (цвет — SmokeDeep темы).</summary>
        public const float KeycapFillAlpha = .9f;

        /// <summary>Скругление кейкапа высотой <paramref name="height"/>, не меньше 4 единиц.</summary>
        public static float KeycapCorner(float height) => Math.Max(4f, height * KeycapCornerRatio);

        /// <summary>
        /// Ширина кейкапа: одна буква — квадрат; длинная подпись — текст и поля, но не уже квадрата и не шире
        /// <see cref="KeycapMaxWidthRatio"/> высот. <paramref name="textWidth"/> — ширина самой подписи, без полей.
        /// </summary>
        public static float KeycapWidth(float textWidth, float height, int length, float maxRatio = KeycapMaxWidthRatio)
        {
            if (height <= 0f) return 0f;
            if (length < 2) return height;
            float width = textWidth + height * KeycapPadRatio;
            return Math.Min(Math.Max(width, height), height * Math.Max(1f, maxRatio));
        }

        // ---------------------------------------------------------------- кнопки и отступы

        /// <summary>Основная и вторичная кнопка листа 5 — большая и малая.</summary>
        public const float ButtonHeightDefault = 56f;
        public const float ButtonHeightSmallDefault = 44f;

        public const float SpaceXSDefault = 4f;
        public const float SpaceSDefault = 8f;
        public const float SpaceMDefault = 16f;
        public const float SpaceLDefault = 24f;
        public const float SpaceXLDefault = 32f;

        // ---------------------------------------------------------------- цвета по умолчанию (RRGGBB)
        // Поля темы берут умолчания отсюда; живые цвета — в ассете (Resources/UI/UiTheme.asset). Акцент #FD7442
        // снят пипеткой с листа набора 22.09 (палитра P4 записана как #ff8a4c) — один акцент на весь интерфейс.

        public const string AccentHex = "FD7442";
        public const string AccentHoverHex = "FF8C5E";
        public const string AccentPressedHex = "DF5E30";
        public const string TextHex = "F4F7FB";
        public const string TextMutedHex = "93A2BC";
        public const string TextOnAccentHex = "FFF6EE";
        public const string CommonHex = "A6B3C8";
        public const string RareHex = "3BF0F5";
        public const string EpicHex = "A765FF";
        public const string UniqueHex = "FF5236";
        public const string LavidiumHex = "FA883C";
        public const string HealthHex = "F34F37";
        public const string CoinsHex = "D98A5A";
        public const string GoodHex = "8FE3A8";
        public const string BadHex = "FF6A5A";
    }
}
