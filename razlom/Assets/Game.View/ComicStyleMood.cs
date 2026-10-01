using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Ручки комикс-рисовки, которые меняет свет арены (<see cref="ArenaMood"/>): тушь, тёплый сдвиг теней,
    /// множитель середины тона и сила диорамы. Тёплая коричневая тушь на тёмно-синей земле сумерек даёт бурые
    /// ореолы, тёплые тени спорят с бирюзовым туманом (план light-arc-plan.md, раздел 4).
    ///
    /// Настройки рисовки (ассет PC_Renderer) и их умолчания не меняются: проходы ComicStyleFeature берут
    /// значение настроек и, пока свет арены применён, смешивают его с целью на долю <see cref="Amount"/>.
    /// Без света арены (по умолчанию) — ровно значения настроек.
    /// </summary>
    public static class ComicStyleMood
    {
        static float _amount;
        static Color _ink, _shadowTint;
        static float _inkOpacity, _shadowWarmth, _tonePivotScale, _tiltStrength;

        /// <summary>Доля перекрытия 0…1; 0 — рисовка как в своих настройках.</summary>
        public static float Amount => _amount;

        /// <summary>Свет арены перекрывает ручки рисовки.</summary>
        public static bool Active => _amount > 0f;

        /// <summary>Задать цели и долю перекрытия (свет арены, раз на арену).</summary>
        public static void Set(float amount, Color ink, float inkOpacity, Color shadowTint, float shadowWarmth,
            float tonePivotScale, float tiltStrength)
        {
            _amount = ComicStyleRules.Clamp01(amount, 0f);
            _ink = ink;
            _inkOpacity = ComicStyleRules.Clamp01(inkOpacity, ComicStyleRules.DefaultInkOpacity);
            _shadowTint = shadowTint;
            _shadowWarmth = ComicStyleRules.Clamp01(shadowWarmth, ComicStyleRules.DefaultShadowWarmth);
            _tonePivotScale = ComicStyleRules.Clamp(tonePivotScale, ComicStyleRules.MinTonePivotScale,
                ComicStyleRules.MaxTonePivotScale, ComicStyleRules.DefaultTonePivotScale);
            _tiltStrength = ComicStyleRules.Clamp01(tiltStrength, ComicStyleRules.DefaultTiltStrength);
        }

        /// <summary>Рисовка снова как в своих настройках (лагерь, меню, свет арены выключен).</summary>
        public static void Clear() => _amount = 0f;

        public static Color InkColor(Color settings) => _amount > 0f ? Color.Lerp(settings, _ink, _amount) : settings;
        public static float InkOpacity(float settings) => _amount > 0f ? Mathf.Lerp(settings, _inkOpacity, _amount) : settings;
        public static Color ShadowTint(Color settings) => _amount > 0f ? Color.Lerp(settings, _shadowTint, _amount) : settings;
        public static float ShadowWarmth(float settings) => _amount > 0f ? Mathf.Lerp(settings, _shadowWarmth, _amount) : settings;
        public static float TonePivotScale(float settings) => _amount > 0f ? Mathf.Lerp(settings, _tonePivotScale, _amount) : settings;
        public static float TiltStrength(float settings) => _amount > 0f ? Mathf.Lerp(settings, _tiltStrength, _amount) : settings;

        // Без перезагрузки домена статика пережила бы выход из Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => _amount = 0f;
    }
}
