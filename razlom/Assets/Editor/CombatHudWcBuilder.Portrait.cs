using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Портрет Пелага (владелец 29.09: «кайма вокруг волос, рваный край круга, шов посередине» —
    /// чинить все три; анимация — «рисованный + реакции»).
    ///
    /// Было: тёмный диск — стенсил-маска (край без сглаживания, «лесенка»), в его нижней половине
    /// вырез под RectMask2D, выше середины круга — вторая копия того же выреза под своей
    /// RectMask2D (стык двух копий на высоте челюсти — «шов»). Стало: одна копия выреза с мягкой
    /// маской шейдера «Дыма и света» (Razlom/UI Ink, INK_SHAPE): маска PelagPortraitMask в
    /// координатах прямоугольника рисунка — ниже середины круга сглаженный круг, выше — всё.
    /// Кайму (цвет старого тёмно-синего фона и красного ореола в полупрозрачном крае волос) снял
    /// tools/ui-kit/make-hud-portrait.py, он же рисует маску. Импорт портрета и маски —
    /// HudPortraitImporter (без Kaiser и отрицательного bias: звон по краю).
    /// </summary>
    public static partial class CombatHudWcBuilder
    {
        const string PortraitCutoutPath = "Assets/Resources/UI/HUD/PelagPortraitPaintedCutout.png";
        const string PortraitMaskPath = "Assets/Resources/UI/HUD/PelagPortraitMask.png";
        const string PortraitMaterialPath = "Assets/UI/Shaders/UiInkPortrait.mat";
        const string BackLightName = "Свет за головой";
        const string WarmLightName = "Тёплый свет";

        /// <summary>
        /// Вырез портрета крупнее круга и приподнят: голова пересекает верхнюю кромку. Маска
        /// PelagPortraitMask нарисована ровно под эту раскладку (ART_*, CIRCLE_* в
        /// tools/ui-kit/make-hud-portrait.py): поменялась раскладка — перезапустить скрипт.
        /// </summary>
        const float PortraitArtScale = 1.18f, PortraitArtLift = 16f;
        const float PortraitArtSize = Portrait * PortraitArtScale;
        static readonly Vector2 PortraitArtCentre = new Vector2(Portrait * .5f, Portrait * .5f + PortraitArtLift);
        /// <summary>Тёплый свет лечения и уровня — во сколько раз шире круга портрета.</summary>
        const float WarmLightScale = 1.9f;

        /// <summary>
        /// Рисунок портрета с мягкой маской: материал UiInkPortrait (как UiInkArt — без течения и
        /// дымки, только проявление с кромкой — плюс форма PelagPortraitMask) и данные элемента
        /// (UiInkReveal): без uv2 шейдер не знает, где маска, и рисунок пропал бы целиком.
        /// </summary>
        static RawImage SoftPortrait(RawImage art)
        {
            if (art.texture == null) art.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitCutoutPath);
            art.material = PortraitMaterial();
            art.color = Color.white;
            art.uvRect = new Rect(0f, 0f, 1f, 1f);
            art.raycastTarget = false;
            // Чернила растекаются от груди — голова проявляется последней, вслед за диском.
            if (art.GetComponent<UiInkReveal>() == null) UiInkKit.Inked(art, new Vector2(.5f, .35f), .12f);
            return art;
        }

        /// <summary>
        /// Материал портрета. Заодно доводит импорт выреза и маски до настроек HudPortraitImporter:
        /// смена кода импортёра сама уже импортированные картинки не переимпортирует.
        /// </summary>
        static Material PortraitMaterial()
        {
            HudPortraitImporter.EnsurePortraitImport(PortraitCutoutPath);
            HudPortraitImporter.EnsurePortraitImport(PortraitMaskPath);
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitMaskPath);
            if (mask == null)
                Debug.LogError("[ui-kit] Нет маски портрета " + PortraitMaskPath + " (tools/ui-kit/make-hud-portrait.py): портрет будет без обрезки кругом");
            return InkMaterial(UiInkKit.Art, PortraitMaterialPath, "UiInkPortrait", m =>
            {
                m.SetTexture("_ShapeTex", mask);
                m.SetFloat("_UseShape", 1f);
                m.EnableKeyword("INK_SHAPE");
            });
        }

        /// <summary>
        /// Свой материал «Дыма и света» на основе готового (<paramref name="source"/>): числа берутся
        /// у него при каждой сборке и миграции, поверх — <paramref name="setup"/>. Лежит рядом с
        /// материалами UiInkKit (Assets/UI/Shaders).
        /// </summary>
        static Material InkMaterial(Material source, string path, string name, System.Action<Material> setup)
        {
            if (source == null)
            {
                Debug.LogError("[ui-kit] Нет материала-основы для " + name + ": шейдер Razlom/UI Ink не найден");
                return null;
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = mat == null;
            if (created) mat = new Material(source) { name = name };
            else
            {
                mat.shader = source.shader;
                mat.CopyPropertiesFromMaterial(source);
            }
            setup(mat);
            if (created) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Живой портрет (<see cref="HudPortraitMotion"/>) на узле «Портрет»: он же вздрагивает от удара
        /// целиком. Тёплый свет лечения и уровня — за диском: виден кольцом вокруг круга и не
        /// высветляет самого Пелага (владелец 24.09: «ужасно»). Свет за головой разгорается вместе
        /// с ним. Уже собранное (повторный вызов) не дублируется.
        /// </summary>
        static HudPortraitMotion LivePortrait(RectTransform portrait, RectTransform disk, RawImage art, HudPulse danger)
        {
            Transform found = portrait.Find(WarmLightName);
            Image warm = found != null ? found.GetComponent<Image>() : null;
            if (warm == null)
            {
                warm = Mark(portrait, WarmLightName, Kit("wc_fx_glow"), Role.Text, 0f, new Vector2(.5f, .5f), Vector2.zero,
                    Portrait * WarmLightScale);
                Additive(warm, new Color(1f, .7f, .38f, 0f));
                warm.enabled = false;
            }
            if (disk != null) warm.transform.SetSiblingIndex(disk.GetSiblingIndex());

            var motion = portrait.GetComponent<HudPortraitMotion>();
            if (motion == null) motion = portrait.gameObject.AddComponent<HudPortraitMotion>();
            motion.Art = art;
            motion.Body = portrait;
            Transform light = disk != null ? disk.Find(BackLightName) : null;
            motion.BackLight = light != null ? light.GetComponent<Graphic>() : null;
            motion.Warm = warm;
            motion.Danger = danger;
            return motion;
        }

        /// <summary>
        /// Маска нарисована под раскладку сборщика: круг 132 от угла узла «Портрет», рисунок 155,76 с
        /// центром (66; 82). Владелец сдвинул или растянул портрет — маска разъедется с кругом:
        /// предупредить, что скрипт маски надо перезапустить с новыми числами.
        /// </summary>
        static void CheckPortraitLayout(RectTransform portrait, RectTransform art)
        {
            Vector2 centre = (Vector2)art.localPosition + Vector2.Scale(new Vector2(.5f, .5f) - art.pivot, art.rect.size) - portrait.rect.min;
            bool same = Mathf.Abs(portrait.rect.width - Portrait) < .5f && Mathf.Abs(portrait.rect.height - Portrait) < .5f
                        && (centre - PortraitArtCentre).magnitude < .5f
                        && Mathf.Abs(art.rect.width - PortraitArtSize) < .5f && Mathf.Abs(art.rect.height - PortraitArtSize) < .5f;
            if (same) return;
            Debug.LogWarning("[ui-kit] Портрет боевого HUD не в раскладке маски: круг " + portrait.rect.size + ", рисунок " + art.rect.size
                             + " с центром " + centre + " (маска — под круг " + Portrait + " и рисунок " + PortraitArtSize + " с центром "
                             + PortraitArtCentre + "). Перезапустить tools/ui-kit/make-hud-portrait.py с новыми ART_*/CIRCLE_*.");
        }
    }
}
