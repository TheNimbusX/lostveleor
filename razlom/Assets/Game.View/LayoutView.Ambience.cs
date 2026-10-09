using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    // Жизнь воздуха арены (8 октября): тени облаков плывут по земле по ветру листвы (_CampBreeze), над
    // поляной редкая тёплая пыльца. Облака — cookie солнца, поэтому при включённом свете арены (F8,
    // ArenaMoodView кладёт свой cookie) отступают. Пыльца редкая: «конфетти» лагеря владелец отверг 16.09.
    public sealed partial class LayoutView
    {
        private const int CloudResolution = 128;
        private const float CloudMeters = 64f, CloudSpeed = .55f;
        private static Texture2D _cloudCookie;
        private Light _cloudSun;
        private UniversalAdditionalLightData _cloudSunData;
        private Texture _cloudSavedCookie;
        private Vector2 _cloudSavedSize, _cloudSavedOffset, _cloudOffset;
        private ParticleSystem _pollen;
        private Material _pollenMaterial;

        private void UpdateAmbience()
        {
            bool arena = Application.isPlaying && _shownMap != null && _shownMap.IsArena && _style.UseCampLighting;
            if (arena && !ArenaMood.Enabled) DriftClouds();
            else RestoreClouds();
            UpdatePollen(arena);
        }

        private void DriftClouds()
        {
            var sun = RenderSettings.sun;
            if (sun == null) return;
            if (_cloudSun != sun)
            {
                RestoreClouds();
                if (!sun.TryGetComponent(out _cloudSunData)) _cloudSunData = sun.gameObject.AddComponent<UniversalAdditionalLightData>();
                _cloudSun = sun;
                _cloudSavedCookie = sun.cookie;
                _cloudSavedSize = _cloudSunData.lightCookieSize; _cloudSavedOffset = _cloudSunData.lightCookieOffset;
            }
            if (_cloudCookie == null) _cloudCookie = BuildCloudCookie();
            var wind = new Vector2(.788f, .616f);
            _cloudOffset += wind * (CloudSpeed * Time.deltaTime);
            // Смещение в плоскости света: ветер по земле переводится в её оси, чтобы тень шла туда же, куда листва.
            var local = sun.transform.InverseTransformDirection(new Vector3(wind.x, 0, wind.y));
            var drift = new Vector2(local.x, local.y).normalized * _cloudOffset.magnitude;
            sun.cookie = _cloudCookie;
            _cloudSunData.lightCookieSize = new Vector2(CloudMeters, CloudMeters);
            _cloudSunData.lightCookieOffset = new Vector2(Mathf.Repeat(drift.x, CloudMeters), Mathf.Repeat(drift.y, CloudMeters));
        }

        private void RestoreClouds()
        {
            if (_cloudSun == null) return;
            // Чужой cookie (свет арены успел положить свой) не трогаем — возвращаем только своё.
            if (_cloudSun.cookie == _cloudCookie)
            {
                _cloudSun.cookie = _cloudSavedCookie;
                if (_cloudSunData != null)
                {
                    _cloudSunData.lightCookieSize = _cloudSavedSize;
                    _cloudSunData.lightCookieOffset = _cloudSavedOffset;
                }
            }
            _cloudSun = null; _cloudSunData = null; _cloudSavedCookie = null;
        }

        // Бесшовный шум из трёх октав: мягкие пятна облаков, тень не темнее 0,58 солнца.
        private static Texture2D BuildCloudCookie()
        {
            const int n = CloudResolution;
            var bytes = new byte[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (float)x / n, v = (float)y / n;
                    float value = .55f * TiledNoise(u, v, 3, 11) + .3f * TiledNoise(u, v, 6, 29) + .15f * TiledNoise(u, v, 12, 47);
                    float cloud = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, .68f, value));
                    bytes[y * n + x] = (byte)(255 * (1 - .42f * cloud));
                }
            var texture = new Texture2D(n, n, TextureFormat.R8, false, true)
            {
                name = "Тени облаков", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            texture.LoadRawTextureData(bytes);
            texture.Apply(false, true);
            return texture;
        }

        private static float TiledNoise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float Corner(int cx, int cy)
            {
                uint h = (uint)(((cx % period + period) % period) * 73856093) ^ (uint)(((cy % period + period) % period) * 19349663) ^ (uint)(seed * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xffff) / 65535f;
            }
            return Mathf.Lerp(Mathf.Lerp(Corner(x0, y0), Corner(x0 + 1, y0), fx), Mathf.Lerp(Corner(x0, y0 + 1), Corner(x0 + 1, y0 + 1), fx), fy);
        }

        private void UpdatePollen(bool arena)
        {
            var camera = Camera.main;
            if (!arena || camera == null)
            {
                if (_pollen != null) _pollen.gameObject.SetActive(false);
                return;
            }
            if (_pollen == null && !BuildPollen()) return;
            if (!_pollen.gameObject.activeSelf) _pollen.gameObject.SetActive(true);
            // Облако пыльцы следует за точкой, куда смотрит камера; частицы живут в мире и не едут вместе с ней.
            var ray = new Ray(camera.transform.position, camera.transform.forward);
            float ground = FloorLevel(camera.transform.position.x, camera.transform.position.z);
            if (Mathf.Abs(ray.direction.y) > .05f)
            {
                float distance = (ground - ray.origin.y) / ray.direction.y;
                if (distance > 0) _pollen.transform.position = ray.GetPoint(distance) + Vector3.up * 1.6f;
            }
            // Запуск после первой расстановки: prewarm наполняет воздух там, где камера, а не в начале координат.
            if (!_pollen.isPlaying) _pollen.Play();
        }

        private bool BuildPollen()
        {
            var shader = Resources.Load<Shader>("Shaders/CampBiomeParticles");
            if (shader == null) return false;
            _pollenMaterial = new Material(shader) { name = "Пыльца арены", hideFlags = HideFlags.DontSave };
            var host = new GameObject("Пыльца арены");
            host.transform.SetParent(transform, false);
            _pollen = host.AddComponent<ParticleSystem>();
            _pollen.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _pollen.main;
            main.loop = true; main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 12f);
            main.startSize = new ParticleSystem.MinMaxCurve(.07f, .12f);
            main.startColor = new Color(1f, .93f, .72f, .65f);
            main.maxParticles = 48;
            var emission = _pollen.emission;
            emission.rateOverTime = 4f;
            var shape = _pollen.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(34f, 2.4f, 24f);
            var velocity = _pollen.velocityOverLifetime;
            velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(.08f, .2f);
            velocity.y = new ParticleSystem.MinMaxCurve(-.02f, .05f);
            velocity.z = new ParticleSystem.MinMaxCurve(.06f, .16f);
            var noise = _pollen.noise;
            noise.enabled = true; noise.strength = .18f; noise.frequency = .4f; noise.scrollSpeed = .2f;
            var fade = _pollen.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(1, .8f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _pollenMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return true;
        }

        private void DisposeAmbience()
        {
            RestoreClouds();
            if (_pollen != null) DestroyOwned(_pollen.gameObject);
            DestroyOwned(_pollenMaterial);
            _pollen = null; _pollenMaterial = null;
        }
    }
}
