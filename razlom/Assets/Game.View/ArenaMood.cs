using System;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Свет арены этой минуты — смесь пресетов профиля по оси (см. <see cref="ArenaMoodRules"/>). Числа «жизни в
    /// кадре» (дымка, лучи, пыльца, светлячки, фонари, огоньки) — для эффектов: их строят другие компоненты,
    /// а здесь только сколько и какого цвета.
    /// </summary>
    public struct ArenaMoodState
    {
        /// <summary>Номер арены с единицы и арена босса (по забегу, не по выбору F8).</summary>
        public int Depth;
        public bool Boss;
        /// <summary>Акцент босса включён (арена босса в «авто» или режим Boss).</summary>
        public bool BossAccent;
        public ArenaMoodMode Mode;
        /// <summary>Ось «день 0 → туман 1 → сумерки 2» и веса пресетов.</summary>
        public float Axis;
        public ArenaMoodWeights Weights;
        public bool IsDusk => Weights.IsDusk;

        // Свет
        public Color SunColor;
        public float SunIntensityScale, SunPitch, SunShadowStrength;
        public Color FillColor;
        public float FillIntensity;
        public Color AmbientSky, AmbientEquator, AmbientGround;
        public float SpotStrength, DappleStrength, DappleInside, EdgeShade;

        // Цветокоррекция
        public float Exposure, Contrast, Saturation, Temperature, Tint;
        public Vector4 ShadowsTone, HighlightsTone;
        public float BloomIntensity, BloomThreshold;
        public Color VignetteColor;
        public float VignetteIntensity;

        // Комикс: доля перекрытия и цели
        public float ComicAmount;
        public Color ComicInk, ComicShadowTint;
        public float ComicInkOpacity, ComicShadowWarmth, ComicTonePivotScale, ComicTiltStrength;

        // Жизнь в кадре
        public float HazeDensity, HazeDrift;
        public Color HazeColor;
        public int BeamCount;
        public float BeamIntensity;
        public int PollenCount;
        public Color PollenColor;
        public int FireflyCount;
        public Color FireflyColor;
        public int LanternCount;
        public Color LanternColor;
        public float LanternIntensity, LanternRange;
        public Color WispColor;
        public float WispAlpha;
    }

    /// <summary>
    /// «Свет арены по глубине» (световая арка акта I, план ART/UI/concepts-2026-10-01-style-shift/light-arc-plan.md).
    /// По умолчанию ВЫКЛЮЧЕН: обычная игра владельца и плейтесты Кости выглядят как сегодня, пока его не
    /// включат в F8 («Визуал · проба рисовки» → «Свет арены по глубине»; выбор в PlayerPrefs). Принудительный
    /// пресет F8 (авто / день / туман / сумерки / босс) — только до выхода. В изолированной съёмке —
    /// ключ -capture-mood off|auto|day|mist|dusk|boss|&lt;ось 0…2&gt;: только на запуск; без ключа — выключен.
    ///
    /// Для эффектов (дымка, лучи, пыльца, светлячки, фонари) — состояние применённого света: <see cref="Active"/>,
    /// <see cref="Current"/> (веса, «сумерки», числа), поляна (<see cref="Glade"/>, центр, радиусы, полярный
    /// контур, кольцо кромки) и <see cref="Version"/> / <see cref="StateChanged"/> — сменилось. Применяет и
    /// откатывает свет <see cref="ArenaMoodView"/>; всё ставится, пока экран закрыт дымом перехода.
    /// </summary>
    public static class ArenaMood
    {
        public const string PrefsKey = "razlom.visual.arena-mood";
        public const string CaptureFlag = "-capture-mood";

        static bool _loaded, _enabled;
        static ArenaMoodMode _mode = ArenaMoodMode.Auto;
        static float _axis = ArenaMoodRules.MistAxis;
        static volatile float _edgeShade = 1f;
        static readonly float[] _contour = new float[ArenaMoodRules.ContourSamples];

        // ---- Переключатель -------------------------------------------------------------------------

        /// <summary>Свет арены включён. Дёшево после первого чтения.</summary>
        public static bool Enabled
        {
            get
            {
                if (!_loaded) Load();
                return _enabled;
            }
        }

        /// <summary>Какой свет при включённом: авто по номеру арены или принудительный пресет.</summary>
        public static ArenaMoodMode Mode => _mode == ArenaMoodMode.Off ? ArenaMoodMode.Auto : _mode;

        /// <summary>Своё число оси для режима <see cref="ArenaMoodMode.Axis"/>.</summary>
        public static float AxisOverride => _axis;

        /// <summary>Сменились переключатель или режим (F8, съёмка).</summary>
        public static event Action SettingsChanged;

        /// <summary>Включить или выключить и запомнить выбор.</summary>
        public static void SetEnabled(bool enabled)
        {
            if (!_loaded) Load();
            bool changed = _enabled != enabled;
            _enabled = enabled;
            try
            {
                PlayerPrefs.SetInt(PrefsKey, enabled ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning("[arena-mood] не удалось сохранить выбор: " + e.Message); }
            if (changed) SettingsChanged?.Invoke();
        }

        /// <summary>Принудительный пресет до выхода из игры (F8). Off здесь — то же, что авто.</summary>
        public static void SetMode(ArenaMoodMode mode, float axis = ArenaMoodRules.MistAxis)
        {
            if (mode == ArenaMoodMode.Off) mode = ArenaMoodMode.Auto;
            bool changed = _mode != mode || (mode == ArenaMoodMode.Axis && _axis != axis);
            _mode = mode;
            _axis = ArenaMoodRules.ClampAxis(axis);
            if (changed) SettingsChanged?.Invoke();
        }

        /// <summary>
        /// Только на этот запуск, без записи в настройки — для съёмки «было → стало». Off выключает свет,
        /// любой другой режим включает его этим режимом.
        /// </summary>
        public static void OverrideForSession(ArenaMoodMode mode, float axis = 0f)
        {
            bool enabled = mode != ArenaMoodMode.Off;
            _loaded = true;
            _enabled = enabled;
            _mode = enabled ? mode : ArenaMoodMode.Auto;
            _axis = ArenaMoodRules.ClampAxis(axis);
            Debug.Log("[arena-mood] на этот запуск: " + (enabled
                ? "свет арены " + ArenaMoodRules.Name(_mode) + (_mode == ArenaMoodMode.Axis ? " " + _axis.ToString("0.00") : "")
                : "как сегодня"));
            SettingsChanged?.Invoke();
        }

        /// <summary>Ключ съёмки: значение -capture-mood. Нет ключа или не разобрали — выключен.</summary>
        public static void ApplyCaptureValue(string value)
        {
            if (value != null && !ArenaMoodRules.TryParse(value, out _, out _))
                Debug.LogWarning("[arena-mood] не понял " + CaptureFlag + " " + value + " — свет как сегодня.");
            ArenaMoodRules.TryParse(value, out ArenaMoodMode mode, out float axis);
            OverrideForSession(mode, axis);
        }

        // ---- Состояние для эффектов ----------------------------------------------------------------

        /// <summary>Свет арены применён к показанной арене прямо сейчас.</summary>
        public static bool Active { get; private set; }

        /// <summary>
        /// Смесь пресетов для арены: ставится, как только известна новая арена (ещё под завесой, до сборки —
        /// её может читать расстановка), и остаётся, пока свет применён. Без настроения — пустое (Depth 0).
        /// </summary>
        public static ArenaMoodState Current { get; private set; }

        /// <summary>
        /// Для новой арены уже посчитано <see cref="Current"/> (свет ещё может ждать конца сборки). false — на
        /// этой арене настроения нет: эффекты ничего не добавляют.
        /// </summary>
        public static bool Prepared { get; private set; }

        /// <summary>Растёт при каждой смене состояния (применили, откатили, новая арена) — для опроса без событий.</summary>
        public static int Version { get; private set; }

        /// <summary>Применили или откатили свет арены.</summary>
        public static event Action StateChanged;

        /// <summary>Сумерки берут верх (вес ≥ ½) и свет применён.</summary>
        public static bool IsDusk => Active && Current.IsDusk;

        /// <summary>Веса пресетов применённого света; без света — (1, 0, 0), чистый день.</summary>
        public static ArenaMoodWeights Weights => Active ? Current.Weights : new ArenaMoodWeights(1f, 0f, 0f);

        /// <summary>
        /// Множитель глубины лесной подстилки за краем пола (маска земли, LayoutView.CampSurface): 1 — как
        /// сегодня. Читается на рабочих потоках сборки, поэтому простое число.
        /// </summary>
        public static float EdgeShade => _edgeShade;

        // ---- Поляна --------------------------------------------------------------------------------

        /// <summary>У применённого света есть поляна (форма из симуляции).</summary>
        public static bool HasGlade { get; private set; }

        /// <summary>Поляна арены из симуляции: <see cref="GladeRegion.Field"/> ≤ 1 — внутри.</summary>
        public static GladeRegion Glade { get; private set; }

        /// <summary>Центр поляны на земле (y = 0) и её радиусы по x и z мира, метры.</summary>
        public static Vector3 GladeCenter { get; private set; }
        public static Vector2 GladeRadii { get; private set; }

        /// <summary>Солнце арены, на которое лёг свет (Key Light лагеря на лугу) — для лучей. null — света нет.</summary>
        public static Light Sun { get; private set; }

        /// <summary>Кольцо кромки для дымки и светлячков: от края поляны наружу, метры.</summary>
        public static float EdgeRingInner { get; private set; } = 1f;
        public static float EdgeRingOuter { get; private set; } = 18f;

        /// <summary>Гладкое поле поляны в точке мира (≤ 1 — внутри, за рамкой растёт плавно). Не для каждого кадра.</summary>
        public static float GladeField(Vector3 world) =>
            HasGlade ? ArenaMoodRules.GladeField(Glade, world.x, world.z) : float.PositiveInfinity;

        /// <summary>Расстояние от центра до края поляны по направлению (радианы; 0 — +x мира, π/2 — +z).</summary>
        public static float ContourRadius(float angle) => HasGlade ? ArenaMoodRules.ContourRadius(_contour, angle) : 0f;

        /// <summary>Точка на земле: край поляны по направлению <paramref name="angle"/> плюс <paramref name="beyond"/> метров наружу.</summary>
        public static Vector3 EdgePoint(float angle, float beyond)
        {
            float r = ContourRadius(angle) + beyond;
            return GladeCenter + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
        }

        // ---- Для ArenaMoodView -----------------------------------------------------------------------

        internal static void SetPrepared(in ArenaMoodState state)
        {
            Current = state;
            Prepared = true;
            _edgeShade = state.EdgeShade > 0f ? state.EdgeShade : 1f;
            Version++;
            StateChanged?.Invoke();
        }

        internal static void ClearPrepared()
        {
            if (!Prepared && !Active && _edgeShade == 1f) return;
            Prepared = false;
            Active = false;
            HasGlade = false;
            Sun = null;
            Current = default;
            _edgeShade = 1f;
            Version++;
            StateChanged?.Invoke();
        }

        internal static void SetActive(GladeRegion glade, float[] contour, float ringInner, float ringOuter, Light sun)
        {
            Sun = sun;
            Glade = glade;
            HasGlade = contour != null;
            GladeCenter = new Vector3(glade.Center.X.ToFloat(), 0f, glade.Center.Y.ToFloat());
            GladeRadii = new Vector2(glade.Radii.X.ToFloat(), glade.Radii.Y.ToFloat());
            if (contour != null) Array.Copy(contour, _contour, Math.Min(contour.Length, _contour.Length));
            EdgeRingInner = ringInner;
            EdgeRingOuter = ringOuter;
            Active = true;
            Version++;
            StateChanged?.Invoke();
        }

        internal static void ClearActive()
        {
            if (!Active) return;
            Active = false;
            HasGlade = false;
            Sun = null;
            Version++;
            StateChanged?.Invoke();
        }

        static void Load()
        {
            _loaded = true;
            try { _enabled = PlayerPrefs.GetInt(PrefsKey, 0) == 1; }
            catch (Exception) { _enabled = false; }
        }

        // Без перезагрузки домена (Enter Play Mode Options) статика пережила бы выход из Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            _loaded = false;
            _mode = ArenaMoodMode.Auto;
            _axis = ArenaMoodRules.MistAxis;
            _edgeShade = 1f;
            Prepared = Active = HasGlade = false;
            Sun = null;
            Current = default;
            SettingsChanged = null;
            StateChanged = null;
        }
    }
}
