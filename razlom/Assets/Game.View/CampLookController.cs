using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    public enum CampLookStyle
    {
        [InspectorName("Исходный вид")] Original,
        [InspectorName("Чистый атмосферный")] Clean,
        [InspectorName("Мягкий живописный")] Painterly,
        [InspectorName("Лёгкий плёночный")] Film,
        [InspectorName("Сравнение ACES")] Aces,
        // Только в конец: номер стиля хранится в сцене.
        [InspectorName("Янтарный закат")] GoldenEvening
    }

    // Корень лагеря выключается при выходе: общие источники возвращаются к состоянию до входа.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class CampLookController : MonoBehaviour
    {
        public CampLookStyle Style = CampLookStyle.Clean;
        public Volume Volume;
        public VolumeProfile Original, Clean, Painterly, Film, Aces, GoldenEvening;
        public Light Sun, Fill;
        public Color SunColor = new Color(1f, .93f, .83f);
        [Range(0, 1)] public float ShadowStrength = .82f;
        [Min(0)] public float FillIntensity = .085f;

        [Header("Янтарный закат — выбор владельца 16 сентября")]
        [Tooltip("Ветер и огни лагеря: вечером огонь ярче относительно низкого солнца")]
        public CampAmbience Ambience;
        public Color EveningSunColor = new Color(1f, .74f, .48f);
        [Tooltip("Высота солнца над горизонтом, градусы. Сторона света остаётся авторской — тени лягут туда же, куда и сейчас, но длиннее. " +
                 "Ниже 20° камера сверху почти не видит освещённой земли, и лагерь уходит в ночь")]
        [Range(6f, 40f)] public float EveningSunPitch = 25f;
        [Tooltip("Доля от авторской яркости солнца: 1 — как в сцене")]
        [Range(.3f, 1.5f)] public float EveningSunScale = 1f;
        [Range(0, 1)] public float EveningShadowStrength = .9f;
        [Tooltip("Заполнение холоднее: тени уходят в сине-фиолетовый")]
        public Color EveningFillColor = new Color(.44f, .56f, .88f);
        [Min(0f)] public float EveningFillIntensity = .2f;
        [Range(.5f, 3f)] public float EveningFireBoost = 1.45f;
        [Range(.5f, 3f)] public float EveningLampBoost = 1.7f;

        [Header("Обработка вечера — пока нет ассета профиля")]
        [Tooltip("Эти числа повторяют ассет CampGoldenEvening: они работают до того, как владелец выполнит пункт меню")]
        [Range(-50f, 50f)] public float EveningContrast = 10f;
        [Range(-50f, 50f)] public float EveningSaturation = 8f;
        [Tooltip("Теплее — ближе к закатному солнцу")]
        [Range(-100f, 100f)] public float EveningTemperature = 20f;
        [Tooltip("Косой свет сам по себе затемняет кадр: экспозиция возвращает закату яркость. " +
                 "16 сентября владелец попросил «чуть-чуть светлее»: 0.25 → 0.42")]
        [Range(-2f, 2f)] public float EveningExposure = .42f;
        [Range(0f, 1f)] public float EveningBloom = .3f;

        [Header("Дымка")]
        public bool EveningFog = true;
        public Color EveningFogColor = new Color(1f, .78f, .60f);
        [Tooltip("Полоса тумана считается от глубины лагеря под камерой. Отрицательное — начинать ближе камеры, чем лагерь. " +
                 "Широкая полоса (−10…26) давала у лагеря лишь четверть силы, и дымка была не видна")]
        public float FogNear = -6f;
        [Tooltip("Дальше лагеря на столько метров дымка набирает полную силу")]
        public float FogFar = 18f;

        [Header("Лучи солнца и живность")]
        [Tooltip("Пусто — объект с лучами создаётся в игре рядом с лагерем при вечернем стиле")]
        public CampSunbeams Sunbeams;
        [Tooltip("Пусто — бабочки, листья и светлячки создаются в игре при вечернем стиле")]
        public CampWildlife Wildlife;
        [Header("Локальная атмосфера — объекты в SampleScene")]
        [Tooltip("Корень с дымкой и перегонкой; все дочерние объекты можно менять в сцене")]
        public CampAtmosphereDetails AtmosphereDetails;

        Color _sunColor, _fillColor;
        float _shadowStrength, _fillIntensity, _sunIntensity;
        Quaternion _sunRotation;
        float _fireBoost = 1f, _lampBoost = 1f;
        bool _fogEnabled;
        FogMode _fogMode;
        Color _fogColor;
        float _fogStart, _fogEnd;
        VolumeProfile _previousProfile;
        CampLookStyle _applied;
        bool _captured;
        public bool HasCaptured => _captured;
        // MeadowLighting reads the active camp grade while the camp root is disabled.
        // Keep this selection in one place so the rift gets the same Golden Evening fallback.
        public VolumeProfile SelectedProfile => Style switch
        {
            CampLookStyle.Original => Original,
            CampLookStyle.Painterly => Painterly,
            CampLookStyle.Film => Film,
            CampLookStyle.Aces => Aces,
            CampLookStyle.GoldenEvening => GoldenEvening != null ? GoldenEvening : RuntimeEvening(),
            _ => Clean
        };

        void Capture()
        {
            if (Volume == null || Sun == null || Fill == null) return;
            _sunColor = Sun.color;
            _shadowStrength = Sun.shadowStrength;
            _fillIntensity = Fill.intensity;
            _previousProfile = Volume.sharedProfile;
            // Вечер меняет ещё и наклон солнца, цвет заполнения, туман и яркость огней —
            // всё авторское запоминается здесь и возвращается в Restore.
            _sunRotation = Sun.transform.rotation;
            _sunIntensity = Sun.intensity;
            _fillColor = Fill.color;
            _fogEnabled = RenderSettings.fog;
            _fogMode = RenderSettings.fogMode;
            _fogColor = RenderSettings.fogColor;
            _fogStart = RenderSettings.fogStartDistance;
            _fogEnd = RenderSettings.fogEndDistance;
            if (Ambience != null) { _fireBoost = Ambience.FireBoost; _lampBoost = Ambience.LampBoost; }
            _captured = true;
            Apply();
        }

        void LateUpdate()
        {
            // При возвращении сначала LayoutView восстанавливает прежний свет Разлома.
            if (!_captured) Capture();
            else if (_applied != Style) Apply();
            if (_applied == CampLookStyle.GoldenEvening && EveningFog) UpdateFogDistance();
        }

        /// <summary>
        /// Туман считается по глубине от камеры, а камера лагеря стоит далеко и
        /// ортографически: фиксированные дистанции дали бы ровную пелену на весь кадр.
        /// </summary>
        void UpdateFogDistance()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            float depth = Vector3.Dot(transform.position - camera.transform.position, camera.transform.forward);
            RenderSettings.fogStartDistance = depth + FogNear;
            RenderSettings.fogEndDistance = depth + FogFar;
        }

        public void SetStyle(CampLookStyle style)
        {
            Style = style;
            if (_captured && isActiveAndEnabled) Apply();
        }

        void Apply()
        {
            VolumeProfile profile = Style switch
            {
                CampLookStyle.Original => Original,
                CampLookStyle.Painterly => Painterly,
                CampLookStyle.Film => Film,
                CampLookStyle.Aces => Aces,
                // Ассет профиля появляется, когда владелец выполнит пункт меню; до этого
                // вечер собирается в игре, чтобы кадры и съёмка уже показывали настоящий вид.
                CampLookStyle.GoldenEvening => GoldenEvening != null ? GoldenEvening : RuntimeEvening(),
                _ => Clean
            };
            if (profile == null) return;
            bool evening = Style == CampLookStyle.GoldenEvening;
            ApplyDirectionalLighting(Sun, Fill);
            if (Ambience != null)
            {
                Ambience.FireBoost = evening ? EveningFireBoost : _fireBoost;
                Ambience.LampBoost = evening ? EveningLampBoost : _lampBoost;
            }
            ApplyFog(evening && EveningFog);
            EnsureSunbeams(evening);
            EnsureWildlife(evening);
            EnsureAtmosphereDetails(evening);
            Volume.sharedProfile = profile;
            _applied = Style;
        }

        // Один рецепт для лагеря и разлома, в том числе когда корень лагеря выключен.
        public void ApplyDirectionalLighting(Light sun, Light fill)
        {
            bool original = Style == CampLookStyle.Original;
            bool evening = Style == CampLookStyle.GoldenEvening;
            var rotation = _captured ? _sunRotation : Sun.transform.rotation;
            float intensity = _captured ? _sunIntensity : Sun.intensity;
            sun.color = original ? (_captured ? _sunColor : Sun.color) : evening ? EveningSunColor : SunColor;
            sun.shadowStrength = original ? (_captured ? _shadowStrength : Sun.shadowStrength)
                : evening ? EveningShadowStrength : ShadowStrength;
            fill.intensity = original ? (_captured ? _fillIntensity : Fill.intensity)
                : evening ? EveningFillIntensity : FillIntensity;
            fill.color = evening ? EveningFillColor : (_captured ? _fillColor : Fill.color);
            var angles = rotation.eulerAngles;
            sun.transform.rotation = evening ? Quaternion.Euler(EveningSunPitch, angles.y, angles.z) : rotation;
            sun.intensity = evening ? intensity * EveningSunScale : intensity;
        }

        /// <summary>Лучи живут только в вечернем стиле; в сцене владельца объект не заводится.</summary>
        void EnsureSunbeams(bool evening)
        {
            if (Sunbeams == null)
            {
                if (!evening || !Application.isPlaying) return;
                var host = new GameObject("Лучи солнца");
                host.transform.SetParent(transform.parent != null ? transform.parent : transform, false);
                Sunbeams = host.AddComponent<CampSunbeams>();
                Sunbeams.Sun = Sun;
            }
            Sunbeams.gameObject.SetActive(evening);
        }

        /// <summary>Бабочки, листья и светлячки — так же, как лучи: объект создаётся в игре.</summary>
        void EnsureWildlife(bool evening)
        {
            if (Wildlife == null)
            {
                if (!evening || !Application.isPlaying) return;
                var host = new GameObject("Живность лагеря");
                host.transform.SetParent(transform.parent != null ? transform.parent : transform, false);
                Wildlife = host.AddComponent<CampWildlife>();
                Wildlife.Ambience = Ambience;
            }
            Wildlife.gameObject.SetActive(evening);
        }

        void EnsureAtmosphereDetails(bool evening)
        {
            // Объекты уже размещены в SampleScene; в игре меняется только их активность.
            if (!Application.isPlaying) return;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-no-camp-atmosphere") >= 0)
                evening = false;
            if (AtmosphereDetails == null) AtmosphereDetails = GetComponentInChildren<CampAtmosphereDetails>(true);
            if (AtmosphereDetails != null) AtmosphereDetails.gameObject.SetActive(evening);
        }

        VolumeProfile _runtimeEvening;

        /// <summary>Профиль вечера, собранный в игре из полей инспектора. В редакторе не создаётся: объект пережил бы выход из Play.</summary>
        VolumeProfile RuntimeEvening()
        {
            if (!Application.isPlaying) return Clean;
            if (_runtimeEvening != null) return _runtimeEvening;
            _runtimeEvening = ScriptableObject.CreateInstance<VolumeProfile>();
            _runtimeEvening.name = "CampGoldenEvening (в игре)";
            _runtimeEvening.hideFlags = HideFlags.HideAndDontSave;
            var color = _runtimeEvening.Add<ColorAdjustments>(true);
            color.contrast.Override(EveningContrast);
            color.saturation.Override(EveningSaturation);
            color.postExposure.Override(EveningExposure);
            var balance = _runtimeEvening.Add<WhiteBalance>(true);
            balance.temperature.Override(EveningTemperature);
            balance.tint.Override(3f);
            var tones = _runtimeEvening.Add<ShadowsMidtonesHighlights>(true);
            tones.shadows.Override(new Vector4(.70f, .82f, 1f, -.03f));
            tones.highlights.Override(new Vector4(1f, .84f, .62f, .04f));
            var bloom = _runtimeEvening.Add<Bloom>(true);
            bloom.intensity.Override(EveningBloom);
            bloom.threshold.Override(1.02f);
            bloom.scatter.Override(.62f);
            return _runtimeEvening;
        }

        void OnDestroy()
        {
            if (_runtimeEvening == null) return;
            Destroy(_runtimeEvening);
            _runtimeEvening = null;
        }

        void ApplyFog(bool on)
        {
            if (on)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = EveningFogColor;
                UpdateFogDistance();
                return;
            }
            RenderSettings.fog = _fogEnabled;
            RenderSettings.fogMode = _fogMode;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.fogStartDistance = _fogStart;
            RenderSettings.fogEndDistance = _fogEnd;
        }

        void OnDisable() => Restore();

        // Редактор применяет настройки только на время отрисовки камеры, не сохраняя их в сцену.
        public void BeginEditorPreview() { if (!Application.isPlaying) { Capture(); } }
        public void EndEditorPreview() { if (!Application.isPlaying) Restore(); }

        void Restore()
        {
            if (!_captured) return;
            if (Sun != null)
            {
                Sun.color = _sunColor; Sun.shadowStrength = _shadowStrength;
                Sun.intensity = _sunIntensity; Sun.transform.rotation = _sunRotation;
            }
            if (Fill != null) { Fill.intensity = _fillIntensity; Fill.color = _fillColor; }
            if (Ambience != null) { Ambience.FireBoost = _fireBoost; Ambience.LampBoost = _lampBoost; }
            ApplyFog(false);
            if (Volume != null) Volume.sharedProfile = _previousProfile;
            _captured = false;
        }
    }
}
