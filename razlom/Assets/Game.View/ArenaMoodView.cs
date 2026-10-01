using System;
using System.Globalization;
using System.Threading.Tasks;
using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// Свет арены по глубине (световая арка акта I, план ART/UI/concepts-2026-10-01-style-shift/light-arc-plan.md):
    /// арены 1–3 — тёплый золотой день, 4–6 — туман сгущается, 7–8 и босс — сумерки. Только на аренах и только
    /// при включённом <see cref="ArenaMood.Enabled"/> (по умолчанию выключен — игра как сегодня).
    ///
    /// Слой кладётся ПОВЕРХ света арены от MeadowLighting (Костин, общий с лагерем Key Light и Fill):
    /// солнце и заливка (цвет, доля яркости, тень), небо (ambient Trilight), «прожектор» поляны — cookie на
    /// солнце по настоящей форме поляны с кружевом листвы, три своих Volume (день / туман / сумерки) выше
    /// лагерного профиля арены, ручки комикс-рисовки (<see cref="ComicStyleMood"/>) и числа для эффектов
    /// (<see cref="ArenaMood.Current"/>). Персонажей свет не высветляет: тун-шейдер берёт у солнца только
    /// оттенок, cookie и фонари его не видят.
    ///
    /// Когда: числа новой арены и запекание пятна — в <see cref="ArenaMoodClock"/> (раньше сборки), сам свет —
    /// здесь, в кадре конца сборки, пока дым перехода ещё держит экран. Откат — в тот же ранний шаг (новая
    /// арена, лагерь, меню, выключатель F8), до того как MeadowLighting вернёт солнце лагерю. Возвращается только
    /// то, что ещё держит записанное нами значение: свойство, которое успел вернуть MeadowLighting, не трогаем —
    /// иначе свет арены протёк бы в лагерь. Журнал: «[arena-mood] …» при применении и «[arena-mood] restored».
    /// </summary>
    [DefaultExecutionOrder(150)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TickDriver))]
    public sealed class ArenaMoodView : MonoBehaviour
    {
        private static readonly string[] VolumeNames = { "день", "туман", "сумерки" };

        private TickDriver _driver;
        private LayoutView _layout;
        private ArenaMoodClock _clock;

        // Профиль локации (Resources/Locations/<локация>Mood).
        private LocationTheme _profileTheme;
        private ArenaMoodProfile _profile;
        private bool _profileLooked;

        // Подготовка: какая арена и какой выбор посчитаны.
        private bool _prepared;
        private int _generation = -1, _depth = -1;
        private ArenaMoodMode _mode;
        private float _axisOverride;
        private ArenaMoodProfile _preparedProfile;
        private ArenaMoodState _state;
        private bool _hasGlade;
        private GladeRegion _glade;
        private float _gladeReach;

        // Пятно поляны: мировая сетка света (рабочий поток) → cookie в плоскости солнца (главный поток).
        private Task _bake;
        private float[] _ground;
        private int _groundN;
        private double _groundMinX, _groundMinZ, _groundStep;
        private float _outside;
        private readonly float[] _contour = new float[ArenaMoodRules.ContourSamples];
        private readonly float[] _lightToWorld = new float[12];
        private Texture2D _cookie;
        private byte[] _cookieBytes;
        private int _cookieResolution;
        private double _cookieSize;

        // Применённое: исходное значение и записанное нами — откат только того, что ещё наше.
        private bool _applied;
        private Light _sun, _fill;
        private Color _sunColorBase, _sunColorSet, _fillColorBase, _fillColorSet;
        private float _sunIntensityBase, _sunIntensitySet, _shadowBase, _shadowSet, _fillIntensityBase, _fillIntensitySet;
        private Quaternion _rotationBase, _rotationSet;
        private bool _rotated, _sunSet, _fillSet;
        private AmbientMode _ambientModeBase;
        private Color _skyBase, _equatorBase, _groundBase, _skySet, _equatorSet, _groundSet;
        private bool _ambientSet;
        private UniversalAdditionalLightData _sunData;
        private Texture _cookieBase;
        private Vector2 _cookieSizeBase, _cookieOffsetBase;
        private bool _cookieSet;

        // Свои Volume: день / туман / сумерки, приоритеты 25 / 26 / 27 — выше лагерного профиля арены (20).
        private Volume[] _volumes;
        private VolumeProfile[] _volumeProfiles;
        private ColorAdjustments[] _colour;
        private WhiteBalance[] _balance;
        private ShadowsMidtonesHighlights[] _tones;
        private Bloom[] _bloom;
        private Vignette[] _vignette;
        private readonly float[] _volumeWeights = new float[3];

        private CampLookController _look;
        private bool _lookSearched;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _layout = GetComponent<LayoutView>();
            _clock = GetComponent<ArenaMoodClock>();
            if (_clock == null) _clock = gameObject.AddComponent<ArenaMoodClock>();
            _clock.View = this;
            // Дымка, лучи, пыльца, светлячки, фонари — только пока свет применён.
            if (GetComponent<ArenaMoodFx>() == null) gameObject.AddComponent<ArenaMoodFx>();
        }

        private void OnDisable()
        {
            Unapply();
            if (_prepared)
            {
                _prepared = false;
                ArenaMood.ClearPrepared();
            }
        }

        private void OnDestroy()
        {
            Unapply();
            WaitBake();
            if (_prepared) ArenaMood.ClearPrepared();
            _prepared = false;
            DestroyOwned(_cookie);
            _cookie = null;
            if (_volumeProfiles != null)
                foreach (var profile in _volumeProfiles) DestroyOwned(profile);
            _volumeProfiles = null;
            _volumes = null;
        }

        // ---------------------------------------------------------------------------------------------
        // Ранний шаг (ArenaMoodClock): новая арена или другой выбор — откат прежнего, числа новой, запекание.
        // ---------------------------------------------------------------------------------------------

        internal void Prepare()
        {
            RiftRun run = _driver != null ? _driver.Run : null;
            LayoutMap map = run != null && _driver.Sim != null ? run.Map : null;
            ArenaMoodProfile profile = ArenaMood.Enabled && map != null && map.PlacedCount > 0
                                       && _layout != null && _layout.isActiveAndEnabled
                ? ProfileFor(_layout.Profile) : null;
            if (profile == null)
            {
                if (_applied) Unapply();
                if (_prepared)
                {
                    _prepared = false;
                    ArenaMood.ClearPrepared();
                }
                return;
            }

            int generation = _driver.Generation, depth = run.Depth;
            ArenaMoodMode mode = ArenaMood.Mode;
            float axis = ArenaMood.AxisOverride;
            if (_prepared && generation == _generation && depth == _depth && mode == _mode && axis == _axisOverride
                && profile == _preparedProfile) return;

            // Прежний свет — сразу: MeadowLighting ещё держит свет прежней арены, и откат вернёт ровно его.
            Unapply();
            _generation = generation;
            _depth = depth;
            _mode = mode;
            _axisOverride = axis;
            _preparedProfile = profile;
            _state = Evaluate(profile, mode, axis, depth, run.LevelSettings.Boss);
            _hasGlade = map.GladeCount > 0;
            if (_hasGlade)
            {
                _glade = map.GetGlade(0);
                StartBake(profile, run.LayoutSeed);
            }
            _prepared = true;
            ArenaMood.SetPrepared(_state);
        }

        /// <summary>Смесь пресетов профиля для арены: ось, веса, акцент босса.</summary>
        public static ArenaMoodState Evaluate(ArenaMoodProfile profile, ArenaMoodMode mode, float axisOverride, int depth, bool boss)
        {
            profile.Sanitize();
            float axis = ArenaMoodRules.AxisFor(mode, axisOverride, depth, boss, profile.DepthBlend);
            ArenaMoodWeights w = ArenaMoodRules.Weights(axis);
            ArenaMoodPreset d = profile.Day, m = profile.Mist, k = profile.Dusk;
            var s = new ArenaMoodState
            {
                Depth = depth,
                Boss = boss,
                BossAccent = ArenaMoodRules.BossAccentFor(mode, boss),
                Mode = mode,
                Axis = axis,
                Weights = w,
                SunColor = Mix(w, d.SunColor, m.SunColor, k.SunColor),
                SunIntensityScale = w.Blend(d.SunIntensityScale, m.SunIntensityScale, k.SunIntensityScale),
                SunShadowStrength = w.Blend(d.SunShadowStrength, m.SunShadowStrength, k.SunShadowStrength),
                FillColor = Mix(w, d.FillColor, m.FillColor, k.FillColor),
                FillIntensity = w.Blend(d.FillIntensity, m.FillIntensity, k.FillIntensity),
                AmbientSky = Mix(w, d.AmbientSky, m.AmbientSky, k.AmbientSky),
                AmbientEquator = Mix(w, d.AmbientEquator, m.AmbientEquator, k.AmbientEquator),
                AmbientGround = Mix(w, d.AmbientGround, m.AmbientGround, k.AmbientGround),
                SpotStrength = w.Blend(d.SpotStrength, m.SpotStrength, k.SpotStrength),
                DappleStrength = w.Blend(d.DappleStrength, m.DappleStrength, k.DappleStrength),
                DappleInside = w.Blend(d.DappleInside, m.DappleInside, k.DappleInside),
                EdgeShade = w.Blend(d.EdgeShade, m.EdgeShade, k.EdgeShade),
                Exposure = w.Blend(d.Exposure, m.Exposure, k.Exposure),
                Contrast = w.Blend(d.Contrast, m.Contrast, k.Contrast),
                Saturation = w.Blend(d.Saturation, m.Saturation, k.Saturation),
                Temperature = w.Blend(d.Temperature, m.Temperature, k.Temperature),
                Tint = w.Blend(d.Tint, m.Tint, k.Tint),
                ShadowsTone = Mix(w, d.ShadowsTone, m.ShadowsTone, k.ShadowsTone),
                HighlightsTone = Mix(w, d.HighlightsTone, m.HighlightsTone, k.HighlightsTone),
                BloomIntensity = w.Blend(d.BloomIntensity, m.BloomIntensity, k.BloomIntensity),
                BloomThreshold = w.Blend(d.BloomThreshold, m.BloomThreshold, k.BloomThreshold),
                VignetteColor = Mix(w, d.VignetteColor, m.VignetteColor, k.VignetteColor),
                VignetteIntensity = w.Blend(d.VignetteIntensity, m.VignetteIntensity, k.VignetteIntensity),
                HazeDensity = w.Blend(d.HazeDensity, m.HazeDensity, k.HazeDensity),
                HazeColor = Mix(w, d.HazeColor, m.HazeColor, k.HazeColor),
                HazeDrift = w.Blend(d.HazeDrift, m.HazeDrift, k.HazeDrift),
                BeamCount = ArenaMoodRules.BlendCount(w, d.BeamCount, m.BeamCount, k.BeamCount),
                BeamIntensity = w.Blend(d.BeamIntensity, m.BeamIntensity, k.BeamIntensity),
                PollenCount = ArenaMoodRules.BlendCount(w, d.PollenCount, m.PollenCount, k.PollenCount),
                PollenColor = Mix(w, d.PollenColor, m.PollenColor, k.PollenColor),
                FireflyCount = ArenaMoodRules.BlendCount(w, d.FireflyCount, m.FireflyCount, k.FireflyCount),
                FireflyColor = Mix(w, d.FireflyColor, m.FireflyColor, k.FireflyColor),
                LanternCount = ArenaMoodRules.BlendCount(w, d.LanternCount, m.LanternCount, k.LanternCount),
                LanternColor = Mix(w, d.LanternColor, m.LanternColor, k.LanternColor),
                LanternIntensity = w.Blend(d.LanternIntensity, m.LanternIntensity, k.LanternIntensity),
                LanternRange = w.Blend(d.LanternRange, m.LanternRange, k.LanternRange),
                WispColor = Mix(w, d.WispColor, m.WispColor, k.WispColor),
                WispAlpha = w.Blend(d.WispAlpha, m.WispAlpha, k.WispAlpha),
            };

            // Комикс: доля перекрытия и цели, взвешенные долями пресетов (день с долей 0 — рисовка как есть).
            float da = d.ComicAmount, ma = m.ComicAmount, ka = k.ComicAmount;
            s.ComicAmount = ArenaMoodRules.OverlayAmount(w, da, ma, ka);
            s.ComicInk = OverlayColour(w, da, ma, ka, d.ComicInk, m.ComicInk, k.ComicInk);
            s.ComicShadowTint = OverlayColour(w, da, ma, ka, d.ComicShadowTint, m.ComicShadowTint, k.ComicShadowTint);
            s.ComicInkOpacity = ArenaMoodRules.OverlayTarget(w, da, ma, ka, d.ComicInkOpacity, m.ComicInkOpacity,
                k.ComicInkOpacity, ComicStyleRules.DefaultInkOpacity);
            s.ComicShadowWarmth = ArenaMoodRules.OverlayTarget(w, da, ma, ka, d.ComicShadowWarmth, m.ComicShadowWarmth,
                k.ComicShadowWarmth, ComicStyleRules.DefaultShadowWarmth);
            s.ComicTonePivotScale = ArenaMoodRules.OverlayTarget(w, da, ma, ka, d.ComicTonePivotScale, m.ComicTonePivotScale,
                k.ComicTonePivotScale, ComicStyleRules.DefaultTonePivotScale);
            s.ComicTiltStrength = ArenaMoodRules.OverlayTarget(w, da, ma, ka, d.ComicTiltStrength, m.ComicTiltStrength,
                k.ComicTiltStrength, ComicStyleRules.DefaultTiltStrength);

            if (s.BossAccent && profile.BossAccent != null)
            {
                ArenaMoodAccent a = profile.BossAccent;
                s.SpotStrength = Mathf.Clamp01(s.SpotStrength + a.SpotStrength);
                s.EdgeShade = Mathf.Clamp(s.EdgeShade + a.EdgeShade, .5f, 2.2f);
                s.VignetteIntensity = Mathf.Clamp01(s.VignetteIntensity + a.VignetteIntensity);
                s.HazeDensity = Mathf.Clamp01(s.HazeDensity + a.HazeDensity);
                s.LanternCount = Mathf.Max(0, s.LanternCount + a.LanternCount);
            }
            s.EdgeShade = Mathf.Clamp(s.EdgeShade, .5f, 2.2f);
            return s;
        }

        private ArenaMoodProfile ProfileFor(LocationTheme theme)
        {
            if (_profileLooked && theme == _profileTheme) return _profile;
            _profileLooked = true;
            _profileTheme = theme;
            _profile = ArenaMoodProfile.LoadFor(theme);
            // Съёмка: -capture-mood-set — ручки пресетов на запуск (подбор без пересборки); в игре ничего.
            ArenaMoodCaptureTuning.ApplyTo(_profile);
            if (_profile != null) _profile.Sanitize();
            return _profile;
        }

        // ---------------------------------------------------------------------------------------------
        // Пятно поляны: сетка света по земле на рабочем потоке. От солнца не зависит — солнце арены
        // ставит MeadowLighting только посреди сборки, поэтому плоскость света — уже в Apply.
        // ---------------------------------------------------------------------------------------------

        private void StartBake(ArenaMoodProfile profile, ulong seed)
        {
            WaitBake();
            GladeRegion glade = _glade;
            float rx = glade.Radii.X.ToFloat(), rz = glade.Radii.Y.ToFloat();
            _gladeReach = Mathf.Max(rx, rz);
            double half = _gladeReach + profile.CookieMargin;
            double step = Math.Max(.5, 2 * half / 320);
            int n = (int)Math.Ceiling(2 * half / step) + 1;
            if (_ground == null || _ground.Length < n * n) _ground = new float[n * n];
            _groundN = n;
            _groundStep = step;
            _groundMinX = glade.Center.X.ToDouble() - half;
            _groundMinZ = glade.Center.Y.ToDouble() - half;
            var light = new ArenaGladeLightParams
            {
                SpotStrength = _state.SpotStrength,
                DappleStrength = _state.DappleStrength,
                DappleInside = _state.DappleInside,
                DappleScale = profile.DappleScale,
                SpotInnerField = profile.SpotInnerField,
                SpotOuterField = profile.SpotOuterField,
                Seed = unchecked((uint)seed ^ (uint)(seed >> 32)),
            };
            _outside = ArenaMoodRules.OutsideLight(light);
            float[] ground = _ground, contour = _contour;
            double minX = _groundMinX, minZ = _groundMinZ;
            _bake = Task.Run(() =>
            {
                ArenaMoodRules.BakeGround(ground, n, minX, minZ, step, glade, light);
                ArenaMoodRules.Contour(contour, glade);
            });
        }

        /// <summary>Дождаться запекания: оно пишет в общие массивы. Обычно давно готово — арена собиралась дольше.</summary>
        private bool WaitBake()
        {
            if (_bake == null) return true;
            Task bake = _bake;
            _bake = null;
            try
            {
                if (!bake.IsCompleted) bake.Wait();
                return !bake.IsFaulted;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Применение: после сборки арены, поверх MeadowLighting, пока экран под дымом.
        // ---------------------------------------------------------------------------------------------

        private void LateUpdate()
        {
            if (!_prepared || _applied || _layout == null) return;
            // Арена ещё собирается по кадрам под завесой: MeadowLighting ставит свет посреди сборки.
            if (_layout.Building) return;
            Apply();
        }

        private void Apply()
        {
            ArenaMoodProfile profile = _preparedProfile;
            if (profile == null) return;
            bool baked = _hasGlade && WaitBake();
            ArenaMoodState s = _state;
            FindLights();
            // С этой строки откат возможен: ошибка посреди применения не оставит половину света навсегда.
            _applied = true;
            _sunSet = _fillSet = _ambientSet = _cookieSet = _rotated = false;
            bool cookie = false;
            int volumes = 0;
            try
            {
                if (_sun != null)
                {
                    _sunColorBase = _sun.color;
                    _sunIntensityBase = _sun.intensity;
                    _shadowBase = _sun.shadowStrength;
                    _rotationBase = _sun.transform.rotation;
                    _sun.color = s.SunColor;
                    _sun.intensity = _sunIntensityBase * Mathf.Max(0f, s.SunIntensityScale);
                    _sun.shadowStrength = Mathf.Clamp01(s.SunShadowStrength);
                    float pitch = SunPitch(profile, s.Weights, _rotationBase.eulerAngles.x);
                    _rotated = Mathf.Abs(Mathf.DeltaAngle(pitch, _rotationBase.eulerAngles.x)) > .01f;
                    if (_rotated)
                    {
                        // Сторона света остаётся прежней — меняется только высота солнца.
                        Vector3 angles = _rotationBase.eulerAngles;
                        _sun.transform.rotation = Quaternion.Euler(pitch, angles.y, angles.z);
                    }
                    _sunColorSet = _sun.color;
                    _sunIntensitySet = _sun.intensity;
                    _shadowSet = _sun.shadowStrength;
                    _rotationSet = _sun.transform.rotation;
                    _sunSet = true;
                }
                if (_fill != null)
                {
                    _fillColorBase = _fill.color;
                    _fillIntensityBase = _fill.intensity;
                    _fill.color = s.FillColor;
                    _fill.intensity = Mathf.Max(0f, s.FillIntensity);
                    _fillColorSet = _fill.color;
                    _fillIntensitySet = _fill.intensity;
                    _fillSet = true;
                }

                _ambientModeBase = RenderSettings.ambientMode;
                _skyBase = RenderSettings.ambientSkyColor;
                _equatorBase = RenderSettings.ambientEquatorColor;
                _groundBase = RenderSettings.ambientGroundColor;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = s.AmbientSky;
                RenderSettings.ambientEquatorColor = s.AmbientEquator;
                RenderSettings.ambientGroundColor = s.AmbientGround;
                _skySet = RenderSettings.ambientSkyColor;
                _equatorSet = RenderSettings.ambientEquatorColor;
                _groundSet = RenderSettings.ambientGroundColor;
                _ambientSet = true;
                // Сферические гармоники неба пересчитываются сразу: иначе герои и мобы первый кадр в прежнем небе.
                DynamicGI.UpdateEnvironment();

                cookie = baked && ApplyCookie(profile);
                volumes = ApplyVolumes(profile, s);
                ComicStyleMood.Set(s.ComicAmount, s.ComicInk, s.ComicInkOpacity, s.ComicShadowTint, s.ComicShadowWarmth,
                    s.ComicTonePivotScale, s.ComicTiltStrength);
            }
            catch (Exception e)
            {
                // Остаётся то, что успели поставить; откат при выходе вернёт и его. Повторять каждый кадр не будем.
                Debug.LogException(e);
            }
            ArenaMood.SetActive(_glade, _hasGlade && baked ? _contour : null, profile.EdgeRingInner, profile.EdgeRingOuter, _sun);
            Debug.Log(Describe(s, cookie, volumes));
        }

        /// <summary>Высота солнца: смесь высот пресетов, у которых она задана; 0 в пресете — нынешняя высота.</summary>
        private static float SunPitch(ArenaMoodProfile profile, ArenaMoodWeights w, float current)
        {
            current = Unwrap(current);
            float day = profile.Day.SunPitch > 0f ? profile.Day.SunPitch : current;
            float mist = profile.Mist.SunPitch > 0f ? profile.Mist.SunPitch : current;
            float dusk = profile.Dusk.SunPitch > 0f ? profile.Dusk.SunPitch : current;
            return w.Blend(day, mist, dusk);
        }

        private static float Unwrap(float pitch) => pitch > 180f ? pitch - 360f : pitch;

        private void FindLights()
        {
            CampLookController look = Look();
            LocationTheme theme = _layout != null ? _layout.Profile : null;
            // Те же условия, что у MeadowLighting.ApplyCamp: тогда арена светится Key Light и Fill лагеря.
            bool camp = theme != null && theme.Style != null && theme.Style.UseCampLighting && look != null
                        && look.Sun != null && look.Fill != null && look.Volume != null && look.SelectedProfile != null;
            _sun = camp ? look.Sun : RenderSettings.sun;
            _fill = camp ? look.Fill : null;
            if (_sun == null)
            {
                foreach (var light in FindObjectsByType<Light>())
                    if (light.isActiveAndEnabled && light.type == LightType.Directional && (_sun == null || light.intensity > _sun.intensity))
                        _sun = light;
            }
        }

        private CampLookController Look()
        {
            if (_look != null || _lookSearched) return _look;
            _lookSearched = true;
            var world = FindAnyObjectByType<SceneWorldView>();
            _look = world != null && world.CampRoot != null ? world.CampRoot.GetComponentInChildren<CampLookController>(true) : null;
            return _look;
        }

        private bool ApplyCookie(ArenaMoodProfile profile)
        {
            if (_sun == null || !_hasGlade || _ground == null) return false;
            if (_state.SpotStrength <= 0f && _state.DappleStrength <= 0f) return false;
            double reach = _gladeReach + profile.CookieMargin;
            double size = 2.4 * reach;
            int resolution = size > 120 ? 384 : 256;
            if (_cookie == null || _cookieResolution != resolution)
            {
                DestroyOwned(_cookie);
                _cookie = new Texture2D(resolution, resolution, TextureFormat.R8, false, true)
                {
                    name = "Свет арены: пятно поляны",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
                _cookieBytes = new byte[resolution * resolution];
                _cookieResolution = resolution;
            }
            Transform t = _sun.transform;
            Matrix4x4 lightToWorld = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 4; column++)
                    _lightToWorld[row * 4 + column] = lightToWorld[row, column];
            Vector3 centre = new Vector3(_glade.Center.X.ToFloat(), 0f, _glade.Center.Y.ToFloat());
            Vector3 local = lightToWorld.inverse.MultiplyPoint3x4(centre);
            ArenaMoodRules.BakeCookie(_cookieBytes, resolution, _lightToWorld, local.x, local.y, size,
                _ground, _groundN, _groundMinX, _groundMinZ, _groundStep, _outside);
            _cookie.LoadRawTextureData(_cookieBytes);
            _cookie.Apply(false, false);
            _cookieSize = size;

            if (!_sun.TryGetComponent(out _sunData)) _sunData = _sun.gameObject.AddComponent<UniversalAdditionalLightData>();
            _cookieBase = _sun.cookie;
            _cookieSizeBase = _sunData.lightCookieSize;
            _cookieOffsetBase = _sunData.lightCookieOffset;
            _sun.cookie = _cookie;
            // URP: uv = (точка в плоскости света − offset) / size + ½ — центр поляны в середине текстуры.
            _sunData.lightCookieSize = new Vector2((float)size, (float)size);
            _sunData.lightCookieOffset = new Vector2(local.x, local.y);
            _cookieSet = true;
            return true;
        }

        private int ApplyVolumes(ArenaMoodProfile profile, in ArenaMoodState s)
        {
            EnsureVolumes(profile);
            WriteVolume(0, profile.Day, null);
            WriteVolume(1, profile.Mist, null);
            WriteVolume(2, profile.Dusk, s.BossAccent ? profile.BossAccent : null);
            // Смесь двух соседних пресетов — их же весами: нижний Volume целиком (вес 1), верхний — долей.
            // Три веса по одному приоритету смешивались бы последовательно и не дали бы ровной смеси.
            float axis = s.Axis;
            _volumeWeights[0] = axis < ArenaMoodRules.MistAxis ? 1f : 0f;
            _volumeWeights[1] = axis < ArenaMoodRules.MistAxis ? axis : 1f;
            _volumeWeights[2] = axis <= ArenaMoodRules.MistAxis ? 0f : axis - ArenaMoodRules.MistAxis;
            int layer = Look() != null && _look.Volume != null ? _look.Volume.gameObject.layer : gameObject.layer;
            int active = 0;
            for (int i = 0; i < 3; i++)
            {
                float weight = Mathf.Clamp01(_volumeWeights[i]);
                _volumes[i].gameObject.layer = layer;
                _volumes[i].priority = profile.VolumePriority + i;
                _volumes[i].weight = weight;
                _volumes[i].gameObject.SetActive(weight > 0f);
                if (weight > 0f) active++;
            }
            return active;
        }

        private void EnsureVolumes(ArenaMoodProfile profile)
        {
            if (_volumes != null) return;
            _volumes = new Volume[3];
            _volumeProfiles = new VolumeProfile[3];
            _colour = new ColorAdjustments[3];
            _balance = new WhiteBalance[3];
            _tones = new ShadowsMidtonesHighlights[3];
            _bloom = new Bloom[3];
            _vignette = new Vignette[3];
            for (int i = 0; i < 3; i++)
            {
                var host = new GameObject("Свет арены — " + VolumeNames[i]);
                host.transform.SetParent(transform, false);
                host.SetActive(false);
                var volume = host.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = profile.VolumePriority + i;
                volume.weight = 0f;
                // Профиль собирается в игре: ассеты PC_Renderer и лагеря не меняются; что не задано — от лагерного.
                var volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                volumeProfile.name = "Свет арены (" + VolumeNames[i] + ", в игре)";
                volumeProfile.hideFlags = HideFlags.HideAndDontSave;
                _colour[i] = volumeProfile.Add<ColorAdjustments>();
                _balance[i] = volumeProfile.Add<WhiteBalance>();
                _tones[i] = volumeProfile.Add<ShadowsMidtonesHighlights>();
                _bloom[i] = volumeProfile.Add<Bloom>();
                _vignette[i] = volumeProfile.Add<Vignette>();
                volume.sharedProfile = volumeProfile;
                _volumes[i] = volume;
                _volumeProfiles[i] = volumeProfile;
            }
        }

        private void WriteVolume(int i, ArenaMoodPreset p, ArenaMoodAccent accent)
        {
            _colour[i].postExposure.Override(p.Exposure);
            _colour[i].contrast.Override(p.Contrast);
            _colour[i].saturation.Override(p.Saturation);
            _balance[i].temperature.Override(p.Temperature);
            _balance[i].tint.Override(p.Tint);
            _tones[i].shadows.Override(p.ShadowsTone);
            _tones[i].highlights.Override(p.HighlightsTone);
            _bloom[i].intensity.Override(p.BloomIntensity);
            _bloom[i].threshold.Override(p.BloomThreshold);
            _vignette[i].color.Override(p.VignetteColor);
            _vignette[i].intensity.Override(Mathf.Clamp01(p.VignetteIntensity + (accent != null ? accent.VignetteIntensity : 0f)));
        }

        // ---------------------------------------------------------------------------------------------
        // Откат: только то, что ещё держит записанное нами значение.
        // ---------------------------------------------------------------------------------------------

        private void Unapply()
        {
            if (!_applied) return;
            _applied = false;
            int kept = 0;
            if (_sun != null && _sunSet)
            {
                if (_sun.color == _sunColorSet) _sun.color = _sunColorBase; else kept++;
                if (_sun.intensity == _sunIntensitySet) _sun.intensity = _sunIntensityBase; else kept++;
                if (_sun.shadowStrength == _shadowSet) _sun.shadowStrength = _shadowBase; else kept++;
                if (_rotated && _sun.transform.rotation == _rotationSet) _sun.transform.rotation = _rotationBase;
            }
            if (_sun != null && _cookieSet && _sun.cookie == _cookie)
            {
                _sun.cookie = _cookieBase;
                if (_sunData != null)
                {
                    _sunData.lightCookieSize = _cookieSizeBase;
                    _sunData.lightCookieOffset = _cookieOffsetBase;
                }
            }
            _cookieSet = _rotated = _sunSet = false;
            if (_fill != null && _fillSet)
            {
                if (_fill.color == _fillColorSet) _fill.color = _fillColorBase;
                if (_fill.intensity == _fillIntensitySet) _fill.intensity = _fillIntensityBase;
            }
            _fillSet = false;
            if (_ambientSet)
            {
                _ambientSet = false;
                if (RenderSettings.ambientSkyColor == _skySet && RenderSettings.ambientEquatorColor == _equatorSet
                    && RenderSettings.ambientGroundColor == _groundSet)
                {
                    RenderSettings.ambientMode = _ambientModeBase;
                    RenderSettings.ambientSkyColor = _skyBase;
                    RenderSettings.ambientEquatorColor = _equatorBase;
                    RenderSettings.ambientGroundColor = _groundBase;
                    DynamicGI.UpdateEnvironment();
                }
            }
            if (_volumes != null)
                foreach (var volume in _volumes)
                    if (volume != null)
                    {
                        volume.weight = 0f;
                        volume.gameObject.SetActive(false);
                    }
            ComicStyleMood.Clear();
            ArenaMood.ClearActive();
            _sun = _fill = null;
            _sunData = null;
            // kept — свойства солнца, которые уже вернул MeadowLighting (новая сборка, лагерь): не трогали.
            Debug.Log("[arena-mood] restored" + (kept > 0 ? " (солнце уже вернул свет арены: " + kept + ")" : ""));
        }

        // ---------------------------------------------------------------------------------------------

        private static Color Mix(ArenaMoodWeights w, Color day, Color mist, Color dusk)
        {
            // Цвета смешиваются в линейном пространстве, отдаются как в инспекторе (sRGB).
            Color a = day.linear, b = mist.linear, c = dusk.linear;
            var linear = new Color(w.Blend(a.r, b.r, c.r), w.Blend(a.g, b.g, c.g), w.Blend(a.b, b.b, c.b), 1f);
            return linear.gamma;
        }

        private static Vector4 Mix(ArenaMoodWeights w, Vector4 day, Vector4 mist, Vector4 dusk) =>
            new Vector4(w.Blend(day.x, mist.x, dusk.x), w.Blend(day.y, mist.y, dusk.y),
                w.Blend(day.z, mist.z, dusk.z), w.Blend(day.w, mist.w, dusk.w));

        private static Color OverlayColour(ArenaMoodWeights w, float da, float ma, float ka, Color day, Color mist, Color dusk)
        {
            return new Color(
                ArenaMoodRules.OverlayTarget(w, da, ma, ka, day.r, mist.r, dusk.r, day.r),
                ArenaMoodRules.OverlayTarget(w, da, ma, ka, day.g, mist.g, dusk.g, day.g),
                ArenaMoodRules.OverlayTarget(w, da, ma, ka, day.b, mist.b, dusk.b, day.b), 1f);
        }

        private string Describe(in ArenaMoodState s, bool cookie, int volumes)
        {
            var c = CultureInfo.InvariantCulture;
            string F(float v) => v.ToString("0.##", c);
            string C(Color v) => "(" + F(v.r) + "," + F(v.g) + "," + F(v.b) + ")";
            return "[arena-mood] depth=" + s.Depth + (s.Boss ? " boss" : "") + (s.BossAccent ? " accent" : "")
                   + " mode=" + ArenaMoodRules.Name(s.Mode) + " axis=" + s.Axis.ToString("0.00", c) + " w=" + F(s.Weights.Day) + "/"
                   + F(s.Weights.Mist) + "/" + F(s.Weights.Dusk)
                   + " sun=" + (_sun != null ? _sun.name + C(_sun.color) + "×" + F(_sun.intensity) + " pitch="
                                               + F(Unwrap(_sun.transform.rotation.eulerAngles.x)) : "нет")
                   + " fill=" + (_fill != null ? C(_fill.color) + "×" + F(_fill.intensity) : "нет")
                   + " ambient=" + C(s.AmbientSky) + C(s.AmbientEquator) + C(s.AmbientGround)
                   + " spot=" + F(s.SpotStrength) + " dapple=" + F(s.DappleStrength) + " edge=" + F(s.EdgeShade)
                   + " cookie=" + (cookie ? _cookieResolution + "px/" + F((float)_cookieSize) + "m" : "нет")
                   + " volumes=" + volumes + " exposure=" + F(s.Exposure) + " comic=" + F(s.ComicAmount)
                   + " ink=#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(new Color(ComicStyleRules.InkR, ComicStyleRules.InkG,
                       ComicStyleRules.InkB), s.ComicInk, s.ComicAmount))
                   + " haze=" + F(s.HazeDensity) + " beams=" + s.BeamCount + " pollen=" + s.PollenCount
                   + " fireflies=" + s.FireflyCount + " lanterns=" + s.LanternCount;
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
