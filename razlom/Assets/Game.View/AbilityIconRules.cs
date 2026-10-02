using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// ПРАВИЛО ИКОНКИ СПОСОБНОСТИ — ОДНО МЕСТО (план форм 02.10, §7): (DefinitionId, форма) → файл в
    /// Resources/UI/Abilities. У формы тот же DefinitionId, что у навыка, поэтому каждый кеш иконок
    /// (плитки HUD, подсказка, экраны забега, мини-меню добычи, IMGUI) ключуется ещё и формой —
    /// <see cref="CacheKey"/>; сами картинки собирает <see cref="AbilityIcons"/>.
    ///
    /// Пока своего арта форм нет (арт — отдельно с владельцем, без генерации), иконка формы — базовая
    /// иконка навыка плюс маленькая метка формы в стиле набора «Дым и свет»: тёмный круг, тонкое кольцо
    /// единственного оранжевого акцента и огонёк в середине (как огонёк усилений на плитке HUD). Метка
    /// стоит справа внизу ВНУТРИ круга, который видно и на плитке HUD (там иконка обрезана на
    /// <see cref="HudIconCrop"/> с каждой стороны и спрятана под круглую маску), и в медальоне карточки.
    ///
    /// Без Unity: правило и геометрию метки проверяют тесты представления (AbilityIconRulesTests).
    /// </summary>
    public static class AbilityIconRules
    {
        /// <summary>Папка иконок способностей в Resources.</summary>
        public const string ResourceFolder = "UI/Abilities/";

        /// <summary>Обрезка иконки на плитке HUD (CombatHudView.IconCrop по умолчанию): метка обязана быть видна и там.</summary>
        public const float HudIconCrop = .12f;

        /// <summary>Радиус метки, доля стороны иконки.</summary>
        public const float MarkRadius = .09f;

        /// <summary>Центр метки от центра иконки, доля стороны, и угол (градусы от «вправо», против часовой; минус — вниз).</summary>
        public const float MarkDistance = .25f, MarkAngle = -35f;

        /// <summary>Сторона иконки формы, пикселей: карточка награды рисует медальон в 140 единиц, запас на 4K.</summary>
        public const int MarkedIconSize = 512;

        /// <summary>Файл базовой иконки способности (без папки и расширения); null — своей иконки нет.</summary>
        public static string BaseFile(int definitionId)
        {
            if (definitionId == AbilityDefinition.SkewerId) return "Icon_Skewer";
            if (definitionId == AbilityDefinition.BackblastId) return "Icon_Backblast";
            if (definitionId == AbilityDefinition.AnchorSlamId) return "Icon_AnchorSweep";
            if (definitionId == AbilityDefinition.WreckId) return "Icon_Wreck";
            if (definitionId == AbilityDefinition.FireFlaskId) return "Icon_FireFlask";
            if (definitionId == AbilityDefinition.CleaveId) return "Icon_Cleave";
            if (definitionId == AbilityDefinition.DashId) return "Icon_Dash";
            if (definitionId == AbilityDefinition.WhirlwindId) return "Icon_Whirlwind";
            if (definitionId == AbilityDefinition.AnchorLeapId) return "Icon_AnchorLeap";
            if (definitionId == AbilityDefinition.ChainStepId) return "Icon_Squall";
            if (definitionId == AbilityDefinition.BlazeId) return "Icon_Blaze";
            return null;
        }

        /// <summary>Хвост имени файла формы: Icon_Whirlwind + «_Storm». Новая форма — новая строка здесь.</summary>
        public static string FormSuffix(PelagForm form)
        {
            switch (form)
            {
                case PelagForm.WhirlwindStorm: return "Storm";
                case PelagForm.WhirlwindMaelstrom: return "Maelstrom";
                case PelagForm.WhirlwindFoamWaves: return "FoamWaves";
                case PelagForm.WhirlwindOnTheMove: return "OnTheMove";
                case PelagForm.None: return null;
                // Номер формы без своей строки — всё равно отдельный файл: две формы не делят одну картинку.
                default: return "Form" + ((int)form).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Файл своего арта формы: «Icon_Whirlwind_Storm». Его может ещё не быть — тогда
        /// <see cref="AbilityIcons"/> берёт базовую иконку с меткой формы. Без формы — null.
        /// </summary>
        public static string FormFile(int definitionId, PelagForm form)
        {
            string suffix = FormSuffix(form);
            string file = BaseFile(definitionId);
            return suffix == null || file == null ? null : file + "_" + suffix;
        }

        /// <summary>Ключ кешей иконок: способность И форма (у формы DefinitionId прежний).</summary>
        public static long CacheKey(int definitionId, PelagForm form) => ((long)definitionId << 8) | (byte)form;

        // ---------------------------------------------------------------- метка формы

        /// <summary>Центр метки в долях иконки; v — снизу вверх, как строки Texture2D.</summary>
        public static float MarkCenterU => .5f + MarkDistance * (float)Math.Cos(MarkAngle * Math.PI / 180.0);
        public static float MarkCenterV => .5f + MarkDistance * (float)Math.Sin(MarkAngle * Math.PI / 180.0);

        /// <summary>Сколько каждого слоя метки в точке: тень, тёмный круг, кольцо, огонёк — 0…1, мягкий край в пиксель.</summary>
        public struct MarkCover
        {
            public float Shadow, Disc, Ring, Core;
            public bool Any => Shadow > 0f || Disc > 0f || Ring > 0f || Core > 0f;
        }

        // Доли радиуса метки: кольцо — тонкая линия у кромки, огонёк — треть радиуса, тень — полтора радиуса.
        const float RingAt = .8f, RingHalfWidth = .085f, CoreRadius = .34f, ShadowRadius = 1.55f, ShadowStrength = .6f;

        /// <summary>Слои метки в точке (u, v) иконки; <paramref name="pixel"/> — размер пикселя в долях стороны.</summary>
        public static MarkCover Mark(float u, float v, float pixel)
        {
            float du = u - MarkCenterU, dv = v - MarkCenterV;
            float d = (float)Math.Sqrt(du * du + dv * dv);
            float r = MarkRadius, edge = Math.Max(pixel, 1e-5f);
            var cover = new MarkCover();
            if (d >= r * ShadowRadius) return cover;
            // Тень — мягкое тёмное пятно вокруг круга: метка читается и на светлом огне иконки.
            float shadow = 1f - Smooth(r * .9f, r * ShadowRadius, d);
            cover.Shadow = ShadowStrength * shadow;
            cover.Disc = Clamp01((r - d) / edge + .5f);
            cover.Ring = Clamp01((r * RingHalfWidth - Math.Abs(d - r * RingAt)) / edge + .5f);
            cover.Core = Clamp01((r * CoreRadius - d) / edge + .5f);
            return cover;
        }

        // Цвета метки — набор «Дым и свет»: круг — дым темы (Smoke 121923), кольцо и огонёк — акцент темы
        // (UiTheme.AccentHex), в середине огонька — тёплый светлый (как светлая голова заливки у полосы босса).
        static readonly byte[] DiscColour = { 0x12, 0x19, 0x23 };
        const float DiscAlpha = .94f;
        static readonly byte[] AccentColour = Rgb(UiTheme.AccentHex);
        static readonly byte[] CoreLight = { 0xFF, 0xD9, 0xBC };

        /// <summary>
        /// Пиксель иконки под меткой: поверх — тень, тёмный круг, кольцо акцента и огонёк (светлее к середине).
        /// Вне метки пиксель не меняется.
        /// </summary>
        public static void Paint(ref byte r, ref byte g, ref byte b, float u, float v, float pixel)
        {
            MarkCover cover = Mark(u, v, pixel);
            if (!cover.Any) return;
            float fr = r, fg = g, fb = b;
            Over(ref fr, ref fg, ref fb, 0f, 0f, 0f, cover.Shadow);
            Over(ref fr, ref fg, ref fb, DiscColour[0], DiscColour[1], DiscColour[2], cover.Disc * DiscAlpha);
            Over(ref fr, ref fg, ref fb, AccentColour[0], AccentColour[1], AccentColour[2], cover.Ring);
            if (cover.Core > 0f)
            {
                float du = u - MarkCenterU, dv = v - MarkCenterV;
                float k = Clamp01(1f - (float)Math.Sqrt(du * du + dv * dv) / (MarkRadius * CoreRadius));
                float cr = Lerp(AccentColour[0], CoreLight[0], k * .8f);
                float cg = Lerp(AccentColour[1], CoreLight[1], k * .8f);
                float cb = Lerp(AccentColour[2], CoreLight[2], k * .8f);
                Over(ref fr, ref fg, ref fb, cr, cg, cb, cover.Core);
            }
            r = ToByte(fr);
            g = ToByte(fg);
            b = ToByte(fb);
        }

        /// <summary>Пиксели метки вместе с тенью на иконке стороной <paramref name="size"/>: [x0, x1) × [y0, y1).</summary>
        public static void MarkPixels(int size, out int x0, out int y0, out int x1, out int y1)
        {
            float reach = MarkRadius * ShadowRadius;
            x0 = Math.Max(0, (int)Math.Floor((MarkCenterU - reach) * size) - 1);
            y0 = Math.Max(0, (int)Math.Floor((MarkCenterV - reach) * size) - 1);
            x1 = Math.Min(size, (int)Math.Ceiling((MarkCenterU + reach) * size) + 1);
            y1 = Math.Min(size, (int)Math.Ceiling((MarkCenterV + reach) * size) + 1);
        }

        /// <summary>Самая дальняя от центра точка круга метки (без тени), доля стороны: метка целиком внутри круга HUD.</summary>
        public static float MarkFarthest => MarkDistance + MarkRadius;

        /// <summary>Радиус круга, который видно на плитке HUD, в долях стороны иконки (без отступа маски).</summary>
        public static float HudVisibleRadius => .5f - HudIconCrop;

        static byte[] Rgb(string hex)
            => new[]
            {
                Convert.ToByte(hex.Substring(0, 2), 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16),
            };

        static void Over(ref float r, ref float g, ref float b, float cr, float cg, float cb, float a)
        {
            if (a <= 0f) return;
            r += (cr - r) * a;
            g += (cg - g) * a;
            b += (cb - b) * a;
        }

        static float Smooth(float from, float to, float x)
        {
            float t = Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        static byte ToByte(float x) => (byte)(x <= 0f ? 0 : x >= 255f ? 255 : (int)(x + .5f));
    }
}
