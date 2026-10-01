using System;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Все ручки комикс-рисовки в одном месте: живут в ассете PC_Renderer (фича ComicStyle) и правятся в
    /// его инспекторе. Умолчания и пределы — ComicStyleRules; Sanitize не даёт шейдеру NaN и крайностей.
    /// </summary>
    [Serializable]
    public sealed class ComicStyleSettings
    {
        [Header("Тушь")]
        [Tooltip("Цвет контура: тёплый коричнево-чёрный #2A1B12, не серый.")]
        public Color InkColor = new Color(ComicStyleRules.InkR, ComicStyleRules.InkG, ComicStyleRules.InkB, 1f);
        [Range(0f, 1f)] public float InkOpacity = ComicStyleRules.DefaultInkOpacity;
        [Tooltip("Толщина линии в пикселях при 1080p; на других разрешениях масштабируется.")]
        [Range(0f, 4f)] public float OutlineThickness = ComicStyleRules.DefaultOutlineThickness;
        [Tooltip("Разрыв глубины (метры), с которого рисуется контур. Трава до ~0,75 м не обводится.")]
        [Range(.05f, 3f)] public float DepthThreshold = ComicStyleRules.DefaultDepthThreshold;
        [Range(.01f, 2f)] public float DepthSoftness = ComicStyleRules.DefaultDepthSoftness;
        [Tooltip("Малый разрыв глубины (метры): грибы, кусты, мелкие камни. Обводится, только если рядом меняется цвет.")]
        [Range(.02f, 3f)] public float SmallDepthThreshold = ComicStyleRules.DefaultSmallDepthThreshold;
        [Range(.01f, 2f)] public float SmallDepthSoftness = ComicStyleRules.DefaultSmallDepthSoftness;
        [Tooltip("Излом поверхности (градусы), с которого рисуется контур: подошва камня, ноги на земле.")]
        [Range(5f, 90f)] public float NormalThreshold = ComicStyleRules.DefaultNormalThreshold;
        [Range(1f, 45f)] public float NormalSoftness = ComicStyleRules.DefaultNormalSoftness;
        [Tooltip("Излом рисуется, только если рядом меняется цвет: трава на траве чистая.")]
        [Range(0f, 1f)] public float ColourGateLow = ComicStyleRules.DefaultColourGateLow;
        [Range(0f, 1f)] public float ColourGateHigh = ComicStyleRules.DefaultColourGateHigh;

        [Header("Тон")]
        [Tooltip("Насколько тон стягивается к ступеням (0 — без ступеней).")]
        [Range(0f, 1f)] public float ToneStrength = ComicStyleRules.DefaultToneStrength;
        [Tooltip("Ширина ступени в стопах: 1,25 — на кадре 2–3 тона.")]
        [Range(.25f, 4f)] public float ToneBandStops = ComicStyleRules.DefaultToneBandStops;
        [Tooltip("Середина основной ступени по кадру: средняя яркость мира × множитель. Выключить — ручная яркость ниже.")]
        public bool ToneAutoPivot = ComicStyleRules.DefaultToneAutoPivot;
        [Range(.25f, 4f)] public float TonePivotScale = ComicStyleRules.DefaultTonePivotScale;
        [Tooltip("Ручная середина основной ступени (линейная, до тонмаппинга), если авто выключено.")]
        [Range(.005f, 4f)] public float TonePivot = ComicStyleRules.DefaultTonePivot;
        [Range(0f, 1f)] public float ToneHardness = ComicStyleRules.DefaultToneHardness;
        [Tooltip("Тёплый сдвиг теней: тень коричнево-оранжевая, не серая.")]
        [Range(0f, 1f)] public float ShadowWarmth = ComicStyleRules.DefaultShadowWarmth;
        public Color ShadowTint = new Color(ComicStyleRules.ShadowTintR, ComicStyleRules.ShadowTintG, ComicStyleRules.ShadowTintB, 1f);
        [Range(0f, 1f)] public float ShadowSaturation = ComicStyleRules.DefaultShadowSaturation;
        [Range(0f, 2f)] public float Saturation = ComicStyleRules.DefaultSaturation;
        [Tooltip("Общая яркость мира. 1 — как есть; выше 1 — с плечом: свет выше колена сворачивается к порогу Bloom, герои не выгорают.")]
        [Range(.5f, 3f)] public float Brightness = ComicStyleRules.DefaultBrightness;
        [Tooltip("Колено плеча яркости: с какой яркости канала подъём начинает сворачиваться.")]
        [Range(.3f, 1f)] public float HighlightKnee = ComicStyleRules.DefaultHighlightKnee;

        [Header("Кисть (живописное сглаживание)")]
        [Tooltip("Радиус кисти в пикселях при 1080p. 0 — выключить.")]
        [Range(0f, 8f)] public float PainterlyRadius = ComicStyleRules.DefaultPainterlyRadius;
        [Range(0f, 1f)] public float PainterlyStrength = ComicStyleRules.DefaultPainterlyStrength;
        [Range(1f, 18f)] public float PainterlySharpness = ComicStyleRules.DefaultPainterlySharpness;
        [Range(1f, 100f)] public float PainterlyHardness = ComicStyleRules.DefaultPainterlyHardness;
        [Tooltip("Считать кисть в половинном разрешении (дешевле, мягче). Крупный радиус уходит туда сам.")]
        public bool PainterlyHalfResolution;

        [Header("Диорама (tilt-shift)")]
        public bool TiltShift = true;
        [Tooltip("Доля высоты кадра по центру, которая остаётся резкой.")]
        [Range(0f, .95f)] public float TiltSharpBand = ComicStyleRules.DefaultTiltSharpBand;
        [Range(0f, 1f)] public float TiltStrength = ComicStyleRules.DefaultTiltStrength;
        [Tooltip("Размытие у кромки в пикселях при 1080p.")]
        [Range(0f, 32f)] public float TiltBlur = ComicStyleRules.DefaultTiltBlur;

        [Header("Камеры")]
        [Tooltip("Показывать рисовку и в окне Scene (только когда она включена).")]
        public bool ApplyInSceneView;

        /// <summary>Приводит значения к допустимым: NaN → умолчание, выход за пределы → граница.</summary>
        public void Sanitize()
        {
            InkOpacity = ComicStyleRules.Clamp01(InkOpacity, ComicStyleRules.DefaultInkOpacity);
            OutlineThickness = ComicStyleRules.Clamp(OutlineThickness, ComicStyleRules.MinOutlineThickness,
                ComicStyleRules.MaxOutlineThickness, ComicStyleRules.DefaultOutlineThickness);
            DepthThreshold = ComicStyleRules.Clamp(DepthThreshold, .05f, 3f, ComicStyleRules.DefaultDepthThreshold);
            DepthSoftness = ComicStyleRules.Clamp(DepthSoftness, .01f, 2f, ComicStyleRules.DefaultDepthSoftness);
            SmallDepthThreshold = ComicStyleRules.Clamp(SmallDepthThreshold, .02f, 3f, ComicStyleRules.DefaultSmallDepthThreshold);
            SmallDepthSoftness = ComicStyleRules.Clamp(SmallDepthSoftness, .01f, 2f, ComicStyleRules.DefaultSmallDepthSoftness);
            NormalThreshold = ComicStyleRules.Clamp(NormalThreshold, 5f, 90f, ComicStyleRules.DefaultNormalThreshold);
            NormalSoftness = ComicStyleRules.Clamp(NormalSoftness, 1f, 45f, ComicStyleRules.DefaultNormalSoftness);
            ColourGateLow = ComicStyleRules.Clamp01(ColourGateLow, ComicStyleRules.DefaultColourGateLow);
            ColourGateHigh = ComicStyleRules.Clamp(ColourGateHigh, ColourGateLow + .01f, 1.01f, ComicStyleRules.DefaultColourGateHigh);
            ToneStrength = ComicStyleRules.Clamp01(ToneStrength, ComicStyleRules.DefaultToneStrength);
            ToneBandStops = ComicStyleRules.Clamp(ToneBandStops, ComicStyleRules.MinToneBandStops,
                ComicStyleRules.MaxToneBandStops, ComicStyleRules.DefaultToneBandStops);
            TonePivot = ComicStyleRules.Clamp(TonePivot, ComicStyleRules.MinTonePivot, ComicStyleRules.MaxTonePivot,
                ComicStyleRules.DefaultTonePivot);
            TonePivotScale = ComicStyleRules.Clamp(TonePivotScale, ComicStyleRules.MinTonePivotScale,
                ComicStyleRules.MaxTonePivotScale, ComicStyleRules.DefaultTonePivotScale);
            ToneHardness = ComicStyleRules.Clamp01(ToneHardness, ComicStyleRules.DefaultToneHardness);
            ShadowWarmth = ComicStyleRules.Clamp01(ShadowWarmth, ComicStyleRules.DefaultShadowWarmth);
            ShadowSaturation = ComicStyleRules.Clamp01(ShadowSaturation, ComicStyleRules.DefaultShadowSaturation);
            Saturation = ComicStyleRules.Clamp(Saturation, 0f, ComicStyleRules.MaxSaturation, ComicStyleRules.DefaultSaturation);
            Brightness = ComicStyleRules.Clamp(Brightness, ComicStyleRules.MinBrightness, ComicStyleRules.MaxBrightness,
                ComicStyleRules.DefaultBrightness);
            HighlightKnee = ComicStyleRules.Clamp(HighlightKnee, ComicStyleRules.MinHighlightKnee, ComicStyleRules.MaxHighlightKnee,
                ComicStyleRules.DefaultHighlightKnee);
            PainterlyRadius = ComicStyleRules.Clamp(PainterlyRadius, 0f, ComicStyleRules.MaxPainterlyRadius,
                ComicStyleRules.DefaultPainterlyRadius);
            PainterlyStrength = ComicStyleRules.Clamp01(PainterlyStrength, ComicStyleRules.DefaultPainterlyStrength);
            PainterlySharpness = ComicStyleRules.Clamp(PainterlySharpness, ComicStyleRules.MinPainterlySharpness,
                ComicStyleRules.MaxPainterlySharpness, ComicStyleRules.DefaultPainterlySharpness);
            PainterlyHardness = ComicStyleRules.Clamp(PainterlyHardness, ComicStyleRules.MinPainterlyHardness,
                ComicStyleRules.MaxPainterlyHardness, ComicStyleRules.DefaultPainterlyHardness);
            TiltSharpBand = ComicStyleRules.Clamp(TiltSharpBand, 0f, ComicStyleRules.MaxTiltSharpBand,
                ComicStyleRules.DefaultTiltSharpBand);
            TiltStrength = ComicStyleRules.Clamp01(TiltStrength, ComicStyleRules.DefaultTiltStrength);
            TiltBlur = ComicStyleRules.Clamp(TiltBlur, 0f, ComicStyleRules.MaxTiltBlur, ComicStyleRules.DefaultTiltBlur);
        }
    }
}
