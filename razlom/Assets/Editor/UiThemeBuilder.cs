using System.IO;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Создаёт тему UI «Ночная акварель» (Resources/UI/UiTheme.asset) и её шрифты.
    ///
    /// Правки владельца не трогает: заполняет только пустые поля. Если спрайт
    /// или шрифт в теме заменён руками — он остаётся. Чтобы вернуть значение
    /// по умолчанию, достаточно очистить поле: следующий запуск заполнит его.
    ///
    /// Спрайты — Assets/UI/Kit/Watercolor (tools/ui-kit/build_watercolor.py).
    /// Шрифты — Philosopher (заголовки) и Nunito (текст), выбор владельца 22 сентября.
    /// </summary>
    [InitializeOnLoad]
    public static class UiThemeBuilder
    {
        public const string ThemePath = "Assets/Resources/UI/UiTheme.asset";
        const string KitFolder = UiKitImport.KitRoot + "/Watercolor";
        public const string HeadingFamily = "Philosopher";
        public const string BodyFamily = "Nunito";

        static UiThemeBuilder() => EditorApplication.delayCall += () => Ensure(false);

        [MenuItem("Разлом/UI/Тема «Ночная акварель» — заполнить пустое")]
        static void EnsureFromMenu() => Ensure(true);

        public static UiTheme Ensure(bool log)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return null;
            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);
            bool created = theme == null;
            if (created)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ThemePath));
                theme = ScriptableObject.CreateInstance<UiTheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
            }

            int filled = 0;
            Sprite S(Sprite current, string name)
            {
                if (current != null) return current;
                string path = KitFolder + "/" + name + ".png";
                if (!File.Exists(path)) return null;
                UiKitImport.Ensure(path);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) filled++;
                return sprite;
            }

            theme.Fill = S(theme.Fill, "wc_fill");
            theme.Frame = S(theme.Frame, "wc_frame");
            theme.FrameBold = S(theme.FrameBold, "wc_frame_bold");
            theme.HighlightSprite = S(theme.HighlightSprite, "wc_highlight");
            theme.ShadeSprite = S(theme.ShadeSprite, "wc_shade");
            theme.FillSmall = S(theme.FillSmall, "wc_fill_s");
            theme.FrameSmall = S(theme.FrameSmall, "wc_frame_s");
            theme.FrameBoldSmall = S(theme.FrameBoldSmall, "wc_frame_bold_s");
            theme.HighlightSmall = S(theme.HighlightSmall, "wc_highlight_s");
            theme.ShadeSmall = S(theme.ShadeSmall, "wc_shade_s");
            theme.GlowSmall = S(theme.GlowSmall, "wc_glow_s");
            theme.InnerGlowSmall = S(theme.InnerGlowSmall, "wc_inner_glow_s");
            theme.Glow = S(theme.Glow, "wc_glow");
            theme.InnerGlow = S(theme.InnerGlow, "wc_inner_glow");
            theme.ButtonGlow = S(theme.ButtonGlow, "wc_button_glow");
            theme.FrameDashed = S(theme.FrameDashed, "wc_frame_dashed");
            theme.ButtonFill = S(theme.ButtonFill, "wc_button_fill");
            theme.ButtonFrame = S(theme.ButtonFrame, "wc_button_frame");
            theme.TagFill = S(theme.TagFill, "wc_tag_fill");
            theme.TagFrame = S(theme.TagFrame, "wc_tag_frame");
            theme.PillFill = S(theme.PillFill, "wc_pill_fill");
            theme.PillFrame = S(theme.PillFrame, "wc_pill_frame");
            theme.BarFill = S(theme.BarFill, "wc_bar_fill");
            theme.BarFrame = S(theme.BarFrame, "wc_bar_frame");
            theme.CircleFill = S(theme.CircleFill, "wc_circle_fill");
            theme.CircleFrame = S(theme.CircleFrame, "wc_circle_frame");
            theme.CircleFrameBold = S(theme.CircleFrameBold, "wc_circle_frame_bold");
            theme.CircleRing = S(theme.CircleRing, "wc_circle_ring");
            theme.DiamondSmall = S(theme.DiamondSmall, "wc_diamond_s");
            theme.DiamondMedium = S(theme.DiamondMedium, "wc_diamond_m");
            theme.DiamondLarge = S(theme.DiamondLarge, "wc_diamond_l");
            theme.DiamondFrame = S(theme.DiamondFrame, "wc_diamond_frame");
            theme.DiamondFrameSmall = S(theme.DiamondFrameSmall, "wc_diamond_frame_s");
            theme.DiamondFill = S(theme.DiamondFill, "wc_diamond_fill");
            theme.Gem = S(theme.Gem, "wc_gem");
            theme.Cross = S(theme.Cross, "wc_cross");
            theme.Check = S(theme.Check, "wc_check");
            theme.Plus = S(theme.Plus, "wc_plus");
            theme.ChevronDown = S(theme.ChevronDown, "wc_chevron_down");
            theme.ChevronRight = S(theme.ChevronRight, "wc_chevron_right");
            theme.TriangleUp = S(theme.TriangleUp, "wc_tri_up");
            theme.TriangleDown = S(theme.TriangleDown, "wc_tri_down");
            theme.VeilRadial = S(theme.VeilRadial, "wc_veil_radial");
            theme.VeilLinear = S(theme.VeilLinear, "wc_veil_linear");
            theme.Pixel = S(theme.Pixel, "wc_px");
            theme.Grain = S(theme.Grain, "wc_grain");
            theme.Knob = S(theme.Knob, "wc_knob");
            theme.BarFillLarge = S(theme.BarFillLarge, "wc_bar_fill_l");
            theme.BarFrameLarge = S(theme.BarFrameLarge, "wc_bar_frame_l");
            theme.CircleFrameLarge = S(theme.CircleFrameLarge, "wc_circle_frame_l");
            theme.FrameOpenTop = S(theme.FrameOpenTop, "wc_frame_open");
            theme.RingTicks = S(theme.RingTicks, "wc_ring_ticks");
            theme.RingGlow = S(theme.RingGlow, "wc_ring_glow");
            theme.Blob = S(theme.Blob, "wc_blob");
            theme.Haze = S(theme.Haze, "wc_haze");
            theme.Spark = S(theme.Spark, "wc_spark");
            theme.Arrow = S(theme.Arrow, "wc_arrow");
            theme.Pointer = S(theme.Pointer, "wc_pointer");
            theme.Mouse = S(theme.Mouse, "wc_mouse");

            // Шрифты создаются только при готовом TMP (как у боевого HUD).
            if ((theme.Heading == null || theme.Body == null) && CombatHudBuilder.EnsureEssentials())
            {
                if (theme.Heading == null && (theme.Heading = CombatHudBuilder.EnsureFont(HeadingFamily, "Regular")) != null) filled++;
                if (theme.Body == null && (theme.Body = CombatHudBuilder.EnsureFont(BodyFamily, "Regular")) != null) filled++;
                // Остальные начертания — для окон, где нужен тонкий или жирный вариант.
                CombatHudBuilder.EnsureFont(HeadingFamily, "Bold");
                CombatHudBuilder.EnsureFont(BodyFamily, "SemiBold");
                CombatHudBuilder.EnsureFont(BodyFamily, "Bold");
            }
            if (theme.Numbers == null && CombatHudBuilder.EnsureEssentials())
            {
                if ((theme.Numbers = CombatHudBuilder.EnsureFont(BodyFamily, "Bold")) != null) filled++;
            }

            if (created || filled > 0)
            {
                EditorUtility.SetDirty(theme);
                AssetDatabase.SaveAssets();
                Debug.Log("[ui-kit] Тема «Ночная акварель»: " + (created ? "создана" : "дополнена") + ", заполнено полей: " + filled);
            }
            else if (log) Debug.Log("[ui-kit] Тема «Ночная акварель»: пустых полей нет, правки не тронуты.");
            return theme;
        }
    }
}
