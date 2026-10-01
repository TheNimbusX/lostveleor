using System;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Один свет арены — день, туман или сумерки (план light-arc-plan.md, раздел 3.3). Цвета — как в
    /// инспекторе (sRGB), смешиваются в линейном пространстве. Модели и материалы персонажей не трогаются:
    /// мир светлеет и темнеет солнцем, небом и пятном поляны, а не высветлением героев (ревью 24.09).
    /// </summary>
    [Serializable]
    public sealed class ArenaMoodPreset
    {
        [Header("Солнце и заливка")]
        [Tooltip("Цвет солнца. Тун-шейдер героев берёт у солнца только оттенок (на 78 %): насыщенный оранжевый " +
                 "окрасил бы героя — сумерки держим янтарными, синеву даёт небо.")]
        public Color SunColor = new Color(1f, .9f, .68f);
        [Tooltip("Яркость солнца — доля от света арены без настроения (сегодня ≈ 1,25). Героев не высветляет: " +
                 "тун-шейдер силу солнца не учитывает.")]
        [Range(0f, 2f)] public float SunIntensityScale = 1.4f;
        [Tooltip("Высота солнца, градусы; 0 — как сейчас (сторона света не меняется никогда).")]
        [Range(0f, 80f)] public float SunPitch;
        [Range(0f, 1f)] public float SunShadowStrength = .8f;
        public Color FillColor = new Color(.55f, .62f, .80f);
        [Min(0f)] public float FillIntensity = .1f;

        [Header("Небо (ambient, три цвета)")]
        public Color AmbientSky = new Color(.52f, .60f, .62f);
        public Color AmbientEquator = new Color(.42f, .45f, .36f);
        public Color AmbientGround = new Color(.20f, .19f, .12f);

        [Header("Пятно поляны и кружево листвы (cookie на солнце)")]
        [Tooltip("Глубина тени за краем поляны: 0 — без пятна, 1 — прямого солнца за краем нет совсем.")]
        [Range(0f, 1f)] public float SpotStrength = .15f;
        [Tooltip("Сила кружева листвы: на сколько гаснет солнце в тени кроны.")]
        [Range(0f, 1f)] public float DappleStrength = .35f;
        [Tooltip("Доля кружева на самой поляне (пол боя): 0 — пол без пятен, 1 — как в лесу.")]
        [Range(0f, 1f)] public float DappleInside = .25f;
        [Tooltip("Глубина лесной подстилки за краем пола (маска земли Кости, канал тени): 1 — как сейчас. " +
                 "Подстилка гасит только рассеянный свет; меняется со следующей собранной арены.")]
        [Range(.5f, 2.2f)] public float EdgeShade = 1f;

        [Header("Цветокоррекция (свои Volume поверх лагерного)")]
        [Tooltip("Экспозиция светлит и героев — день светлее за счёт солнца и неба, не экспозиции.")]
        [Range(-2f, 2f)] public float Exposure = .25f;
        [Range(-50f, 50f)] public float Contrast = 10f;
        [Tooltip("Насыщенность и тепло дня: с +12 / +22 бурая земля арены 1 уходила в горчичный (съёмка 01.10).")]
        [Range(-50f, 50f)] public float Saturation;
        [Range(-100f, 100f)] public float Temperature = 10f;
        [Range(-100f, 100f)] public float Tint = 3f;
        [Tooltip("Тени / блики Shadows-Midtones-Highlights: rgb — цвет, w — сдвиг.")]
        public Vector4 ShadowsTone = new Vector4(1f, 1f, 1f, 0f);
        public Vector4 HighlightsTone = new Vector4(1f, .94f, .82f, .02f);
        [Range(0f, 2f)] public float BloomIntensity = .35f;
        [Range(0f, 2f)] public float BloomThreshold = 1f;
        public Color VignetteColor = new Color(.22f, .14f, .06f);
        [Range(0f, 1f)] public float VignetteIntensity = .1f;

        [Header("Комикс-рисовка (если включена)")]
        [Tooltip("Насколько этот свет перекрывает ручки рисовки ниже: 0 — рисовка как в своих настройках.")]
        [Range(0f, 1f)] public float ComicAmount;
        public Color ComicInk = new Color(42f / 255f, 27f / 255f, 18f / 255f);
        [Range(0f, 1f)] public float ComicInkOpacity = 1f;
        public Color ComicShadowTint = new Color(1f, .78f, .6f);
        [Range(0f, 1f)] public float ComicShadowWarmth = .15f;
        [Range(.25f, 4f)] public float ComicTonePivotScale = 1f;
        [Range(0f, 1f)] public float ComicTiltStrength = .9f;

        [Header("Жизнь в кадре — числа для эффектов (редко и по краю)")]
        [Tooltip("Плотность дымки чащи у кромки (0 — нет).")]
        [Range(0f, 1f)] public float HazeDensity;
        public Color HazeColor = new Color(.95f, .86f, .62f);
        [Tooltip("Снос дымки, м/с.")]
        [Range(0f, .3f)] public float HazeDrift;
        [Range(0, 6)] public int BeamCount;
        [Range(0f, .3f)] public float BeamIntensity;
        [Tooltip("Пыльца днём / искры в лучах в тумане. «Конфетти» в лагере владелец отверг; на аренах 01.10 — «маловато», ×2, но у кромки.")]
        [Range(0, 160)] public int PollenCount = 80;
        [Tooltip("Строго меньше 1 — без блума.")]
        public Color PollenColor = new Color(1f, .93f, .70f);
        [Range(0, 80)] public int FireflyCount;
        [ColorUsage(false, true)] public Color FireflyColor = new Color(1.6f, 1.4f, .5f);
        [Tooltip("Фонари на столбах по краю поляны, за полом.")]
        [Range(0, 8)] public int LanternCount;
        [Tooltip("Жёлтый, не оранжевый: оранжевый — метка атаки врага.")]
        public Color LanternColor = new Color(1f, .78f, .45f);
        [Range(0f, 6f)] public float LanternIntensity = 2.5f;
        [Range(0f, 12f)] public float LanternRange = 5.5f;
        [Tooltip("Огоньки поляны Кости (сегодня голубые (.5, .9, 1)).")]
        public Color WispColor = new Color(.5f, .9f, 1f);
        [Range(0f, 1f)] public float WispAlpha = 1f;
    }

    /// <summary>Поправки поверх сумерек на арене босса (выбор по умолчанию — чуть сильнее кромка).</summary>
    [Serializable]
    public sealed class ArenaMoodAccent
    {
        [Range(-.5f, .5f)] public float SpotStrength = .06f;
        [Range(-1f, 1f)] public float EdgeShade = .1f;
        [Range(-.5f, .5f)] public float VignetteIntensity = .03f;
        [Range(-1f, 1f)] public float HazeDensity = .05f;
        [Range(-4, 4)] public int LanternCount = 1;
    }

    /// <summary>
    /// Свет арены по глубине для одной локации: три пресета, ось по аренам и запекание пятна поляны.
    /// Ассет — Resources/Locations/&lt;локация&gt;Mood (для луга — MeadowMood), рядом с оформлением Кости;
    /// LocationTheme не меняется. Нет ассета — у локации нет настроения, свет как сегодня.
    /// </summary>
    [CreateAssetMenu(fileName = "MeadowMood", menuName = "Разлом/Локации/Свет арены по глубине")]
    public sealed class ArenaMoodProfile : ScriptableObject
    {
        public const string ResourceFolder = "Locations/";
        public const string ResourceSuffix = "Mood";

        public ArenaMoodPreset Day = DefaultDay();
        public ArenaMoodPreset Mist = DefaultMist();
        public ArenaMoodPreset Dusk = DefaultDusk();

        [Tooltip("Ось «день 0 → туман 1 → сумерки 2» для арен 1, 2, 3…; последняя строка — арена босса.")]
        public float[] DepthBlend = (float[])ArenaMoodRules.DefaultDepthBlend.Clone();
        [Tooltip("Поверх сумерек на арене босса.")]
        public ArenaMoodAccent BossAccent = new ArenaMoodAccent();

        [Header("Пятно поляны")]
        [Tooltip("Поле поляны (≤ 1 — внутри; поле квадратичное), с которого свет начинает гаснуть к краю, и где " +
                 "тень края полная. ,45 / 1,05: гаснет уже травяная кайма за полом, а не только лес — с ,7 / 1,45 " +
                 "пятно на съёмке 01.10 не было видно даже в широком кадре.")]
        [Range(.2f, 1.2f)] public float SpotInnerField = .45f;
        [Range(1f, 3f)] public float SpotOuterField = 1.05f;
        [Tooltip("Размер пятна кружева листвы, метры.")]
        [Range(.5f, 10f)] public float DappleScale = 3.2f;
        [Tooltip("Запас текстуры за рамкой поляны, метры: видимый край кадра и кроны на высоте.")]
        [Range(10f, 60f)] public float CookieMargin = 30f;

        [Header("Кольцо кромки для эффектов")]
        [Tooltip("Кольцо дымки: от края поляны наружу, метры (меньше нуля — начинается внутри контура, над " +
                 "травяной каймой; на ближней к камере стороне дымку всё равно отодвигает ArenaMoodFxRules).")]
        public float EdgeRingInner = -2f;
        public float EdgeRingOuter = 18f;

        [Header("Volume")]
        [Tooltip("Приоритет нижнего из трёх Volume; лагерный профиль арены — 20.")]
        public float VolumePriority = 25f;

        public static ArenaMoodProfile LoadFor(LocationTheme theme)
        {
            if (theme == null) return null;
            return Resources.Load<ArenaMoodProfile>(ResourceFolder + theme.name + ResourceSuffix);
        }

        /// <summary>Тёплый золотой день (арены 1–3, панель C): светлее сегодняшнего за счёт солнца и неба.</summary>
        public static ArenaMoodPreset DefaultDay() => new ArenaMoodPreset();

        /// <summary>Туман сгущается (арены 4–6, панель E): поляна под прожектором, края в бирюзовой дымке.</summary>
        public static ArenaMoodPreset DefaultMist() => new ArenaMoodPreset
        {
            // Поляна под прожектором светла как днём (реф: середина ≈ ,40), темнеют кайма и лес — пятном и дымкой,
            // а не общей яркостью (с солнцем ×,85 середина кадра была ,28). Экспозиция не выше сегодняшней (+,25).
            SunColor = new Color(1f, .92f, .74f), SunIntensityScale = 1.3f, SunShadowStrength = .85f,
            FillColor = new Color(.45f, .62f, .70f), FillIntensity = .16f,
            AmbientSky = new Color(.32f, .44f, .48f), AmbientEquator = new Color(.18f, .27f, .28f),
            AmbientGround = new Color(.07f, .10f, .10f),
            SpotStrength = .75f, DappleStrength = .45f, DappleInside = .3f, EdgeShade = 1.5f,
            Exposure = .25f, Contrast = 12f, Saturation = 0f, Temperature = -2f, Tint = -4f,
            ShadowsTone = new Vector4(.66f, .90f, 1f, 0f), HighlightsTone = new Vector4(1f, .95f, .82f, .02f),
            BloomIntensity = .3f, BloomThreshold = 1.02f,
            VignetteColor = new Color(.06f, .16f, .16f), VignetteIntensity = .22f,
            ComicAmount = 1f, ComicInk = new Color(23f / 255f, 35f / 255f, 34f / 255f), ComicInkOpacity = 1f,
            ComicShadowTint = new Color(.75f, .90f, 1f), ComicShadowWarmth = .18f, ComicTonePivotScale = 1f,
            ComicTiltStrength = .7f,
            HazeDensity = .85f, HazeColor = new Color(.55f, .72f, .74f), HazeDrift = .06f,
            BeamCount = 3, BeamIntensity = .1f,
            PollenCount = 60, PollenColor = new Color(.98f, .92f, .70f),
            FireflyCount = 0, LanternCount = 0,
            WispColor = new Color(.6f, .9f, .95f), WispAlpha = .8f,
        };

        /// <summary>Сумерки (арены 7–8 и босс, панель F): синие тени, жёлтые фонари, светлячки.</summary>
        public static ArenaMoodPreset DefaultDusk() => new ArenaMoodPreset
        {
            // Янтарный «прожектор» над поляной (пол реф. сумерек ≈ ,30), синева — небом и тенью за краем пятна.
            // С солнцем ×,45 и экспозицией 0 кадр был ,09 при цели ,19; тинт −12 гасит лиловый на бурой земле.
            SunColor = new Color(1f, .72f, .50f), SunIntensityScale = 1.25f, SunShadowStrength = .7f,
            FillColor = new Color(.38f, .52f, .95f), FillIntensity = .25f,
            AmbientSky = new Color(.20f, .30f, .50f), AmbientEquator = new Color(.10f, .18f, .30f),
            AmbientGround = new Color(.04f, .07f, .10f),
            SpotStrength = .9f, DappleStrength = .2f, DappleInside = .2f, EdgeShade = 1.7f,
            Exposure = .25f, Contrast = 8f, Saturation = 5f, Temperature = -8f, Tint = -12f,
            ShadowsTone = new Vector4(.56f, .76f, 1f, -.02f), HighlightsTone = new Vector4(1f, .85f, .62f, .03f),
            BloomIntensity = .45f, BloomThreshold = 1f,
            VignetteColor = new Color(.04f, .06f, .14f), VignetteIntensity = .26f,
            ComicAmount = 1f, ComicInk = new Color(18f / 255f, 24f / 255f, 38f / 255f), ComicInkOpacity = 1f,
            ComicShadowTint = new Color(.60f, .68f, 1f), ComicShadowWarmth = .22f, ComicTonePivotScale = 1.15f,
            ComicTiltStrength = .8f,
            HazeDensity = .45f, HazeColor = new Color(.16f, .22f, .32f), HazeDrift = .04f,
            BeamCount = 0, BeamIntensity = 0f,
            PollenCount = 0, FireflyCount = 50, FireflyColor = new Color(1.6f, 1.4f, .5f),
            // Тинт −12 сумерек зеленил фонари (кадры 01.10); янтарнее на входе — тёплый жёлтый на экране.
            LanternCount = 5, LanternColor = new Color(1f, .66f, .30f),
            WispColor = new Color(1f, .85f, .45f), WispAlpha = .6f,
        };

        /// <summary>Пустые поля после старой версии ассета — умолчания, NaN и крайности — в пределы.</summary>
        public void Sanitize()
        {
            if (Day == null) Day = DefaultDay();
            if (Mist == null) Mist = DefaultMist();
            if (Dusk == null) Dusk = DefaultDusk();
            if (BossAccent == null) BossAccent = new ArenaMoodAccent();
            if (DepthBlend == null || DepthBlend.Length == 0) DepthBlend = (float[])ArenaMoodRules.DefaultDepthBlend.Clone();
            if (!(SpotOuterField > SpotInnerField)) SpotOuterField = SpotInnerField + .5f;
            if (!(DappleScale > .05f)) DappleScale = 3.2f;
            if (!(CookieMargin >= 10f)) CookieMargin = 30f;
            if (!(EdgeRingOuter > EdgeRingInner)) EdgeRingOuter = EdgeRingInner + 1f;
        }

        void OnValidate() => Sanitize();
    }
}
