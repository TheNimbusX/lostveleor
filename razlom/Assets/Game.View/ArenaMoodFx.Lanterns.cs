using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    // Фонари сумерек: столбы Кости (MeadowLanternPost) по краю поляны за полом боя, огонь развёрнут к поляне,
    // жёлтый свет (не оранжевый — оранжевое у метки атаки) с мерцанием и мягким ореолом. Стоят на дальней от
    // камеры дуге и по бокам: на ближней стороне столб закрыл бы пол. Дешёвые: точечный свет без теней
    // (Forward+, тени доп. источников выключены), до восьми штук. Фонари у порталов в сумерках теплеют и
    // разгораются так же; их числа возвращаются при откате.
    public sealed partial class ArenaMoodFx
    {
        private const int MaxLanterns = 8;
        private const string LanternPrefab = "Environment/Meadow/MeadowLanternPost", LanternLightName = "Свет фонаря";

        [Header("Фонари сумерек")]
        [Tooltip("Ореол вокруг стекла: доля цвета фонаря (кадр — мягкое пятно, а не вторая лампа).")]
        [Range(0f, 1f)] public float LanternHalo = .5f;
        [Range(.2f, 3f)] public float LanternHaloSize = 1.1f;
        [Tooltip("Мерцание огня ± доля яркости.")]
        [Range(0f, .3f)] public float LanternFlicker = .1f;
        [Tooltip("Между фонарями не меньше, метры.")]
        public float LanternSpacing = 4.5f;

        private sealed class Lantern
        {
            public GameObject Root;
            public Light Light;
            public Transform Halo;
            public float Phase, Intensity;
        }

        private readonly List<Lantern> _lanterns = new List<Lantern>(MaxLanterns);
        private readonly List<Vector2> _lanternSpots = new List<Vector2>(MaxLanterns);
        private int _lanternsShown;
        private Transform _lanternRoot;
        private GameObject _lanternPrefab;
        private bool _lanternLooked;
        private Mesh _haloMesh;
        private Material _haloMaterial;
        private readonly Color32[] _haloColours = new Color32[4];

        // Фонари у порталов (Костины, из пула декора): что было и что поставили.
        private readonly List<Light> _portalScan = new List<Light>(16);
        private readonly List<Light> _portalLights = new List<Light>(8);
        private readonly List<Color> _portalColourBase = new List<Color>(8);
        private readonly List<float> _portalIntensityBase = new List<float>(8);
        private readonly List<float> _portalRangeBase = new List<float>(8);
        private readonly List<float> _portalIntensitySet = new List<float>(8);

        private int ShowLanterns()
        {
            ArenaMoodState s = _state;
            _lanternsShown = 0;
            int wanted = Mathf.Clamp(s.LanternCount, 0, MaxLanterns);
            if (wanted == 0 || _layout == null || !EnsureLanterns()) return 0;
            CameraGround(out float forwardX, out float forwardZ, out _);
            float far = Mathf.Atan2(forwardZ, forwardX);
            Vector3 centre = ArenaMood.GladeCenter;
            var random = new ArenaMoodRandom(ArenaMoodRandom.Mix(_seed, 0x6C616D));
            _lanternSpots.Clear();
            SetHaloColour(s.LanternColor);
            Quaternion facing = _camera != null ? _camera.transform.rotation : Quaternion.identity;
            for (int slot = 0; slot < wanted; slot++)
            {
                float jitter = random.Range(-1f, 1f);
                for (int attempt = 0; attempt < ArenaMoodFxRules.LanternAttempts; attempt++)
                {
                    float angle = ArenaMoodFxRules.LanternAngle(slot, wanted, attempt, far, jitter);
                    Vector3 point = ArenaMood.EdgePoint(angle, ArenaMoodFxRules.LanternDistance(attempt));
                    if (!FreeForLantern(point.x, point.z)) continue;
                    Place(GetLantern(_lanternsShown), point, centre, facing, s, random.Range(0f, 6.283f));
                    _lanternSpots.Add(new Vector2(point.x, point.z));
                    _lanternsShown++;
                    break;
                }
            }
            if (_lanternsShown > 0) _lanternRoot.gameObject.SetActive(true);
            return _lanternsShown;
        }

        private bool FreeForLantern(float x, float z)
        {
            if (NearPortalPath(x, z, 3.5f)) return false;
            foreach (Vector2 spot in _lanternSpots)
                if ((spot - new Vector2(x, z)).sqrMagnitude < LanternSpacing * LanternSpacing) return false;
            // Пол, вода, стволы и камни, ориентиры — проверки расстановки Кости.
            return _layout.FreeForProp(x, z, .6f);
        }

        private void Place(Lantern lantern, Vector3 point, Vector3 centre, Quaternion facing, in ArenaMoodState s, float phase)
        {
            float y = _layout.WeaponGroundHeight(point.x, point.z) - .03f;
            var toward = new Vector2(centre.x - point.x, centre.z - point.z);
            // Кронштейн модели смотрит вдоль её оси X: огонь висит в сторону поляны (как у фонарей порталов).
            lantern.Root.transform.SetPositionAndRotation(new Vector3(point.x, y, point.z),
                Quaternion.Euler(0f, Mathf.Atan2(-toward.y, toward.x) * Mathf.Rad2Deg, 0f));
            lantern.Phase = phase;
            lantern.Intensity = Mathf.Max(0f, s.LanternIntensity);
            if (lantern.Light != null)
            {
                lantern.Light.color = s.LanternColor;
                lantern.Light.intensity = lantern.Intensity;
                lantern.Light.range = Mathf.Max(.5f, s.LanternRange);
                lantern.Light.shadows = LightShadows.None;
            }
            if (lantern.Halo != null)
            {
                lantern.Halo.rotation = facing;
                lantern.Halo.localScale = Vector3.one * LanternHaloSize;
            }
            lantern.Root.SetActive(true);
        }

        private void TickLanterns(float time)
        {
            for (int i = 0; i < _lanternsShown && i < _lanterns.Count; i++)
            {
                Lantern lantern = _lanterns[i];
                float flicker = ArenaMoodFxRules.Flicker(time, lantern.Phase, LanternFlicker);
                if (lantern.Light != null) lantern.Light.intensity = lantern.Intensity * flicker;
                if (lantern.Halo != null) lantern.Halo.localScale = Vector3.one * (LanternHaloSize * (1f + (flicker - 1f) * .5f));
            }
            for (int i = 0; i < _portalLights.Count; i++)
            {
                Light light = _portalLights[i];
                if (light == null) continue;
                float intensity = _portalIntensitySet[i] * ArenaMoodFxRules.Flicker(time, i * 1.9f, LanternFlicker);
                light.intensity = intensity;
            }
        }

        private Lantern GetLantern(int index)
        {
            while (_lanterns.Count <= index)
            {
                GameObject root = Instantiate(_lanternPrefab, _lanternRoot, false);
                root.name = "Фонарь сумерек " + (_lanterns.Count + 1);
                root.SetActive(false);
                var lantern = new Lantern { Root = root };
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                    if (light.type == LightType.Point) { lantern.Light = light; break; }
                // Без коллайдеров: столб — декор, бой и движение он не трогает.
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) Destroy(collider);
                if (_haloMesh != null && _haloMaterial != null)
                {
                    var halo = new GameObject("Ореол");
                    halo.transform.SetParent(root.transform, false);
                    halo.transform.localPosition = lantern.Light != null
                        ? root.transform.InverseTransformPoint(lantern.Light.transform.position)
                        : new Vector3(.18f, .98f, 0f);
                    halo.AddComponent<MeshFilter>().sharedMesh = _haloMesh;
                    var renderer = halo.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = _haloMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    lantern.Halo = halo.transform;
                }
                _lanterns.Add(lantern);
            }
            return _lanterns[index];
        }

        private bool EnsureLanterns()
        {
            if (!_lanternLooked)
            {
                _lanternLooked = true;
                _lanternPrefab = Resources.Load<GameObject>(LanternPrefab);
                if (_lanternPrefab == null) Debug.LogWarning("[arena-mood] нет столба фонаря " + LanternPrefab + " — фонарей сумерек не будет.");
            }
            if (_lanternPrefab == null) return false;
            if (_lanternRoot == null)
            {
                var root = new GameObject("Фонари сумерек");
                root.transform.SetParent(_root, false);
                root.SetActive(false);
                _lanternRoot = root.transform;
            }
            if (_haloMaterial == null)
            {
                _haloMaterial = LoadShaderMaterial("Shaders/CampMagicMotes", "Свет арены: ореол фонаря");
                if (_haloMaterial != null) _haloMaterial.SetFloat(StyleId, 1f);
            }
            if (_haloMesh == null) _haloMesh = HaloMesh();
            return true;
        }

        private void SetHaloColour(Color colour)
        {
            if (_haloMesh == null) return;
            // 8 бит на канал: цвета меша во float ломают цвет частиц и квадов (ловушка 27.09).
            Color32 halo = Scaled(colour, LanternHalo);
            for (int i = 0; i < _haloColours.Length; i++) _haloColours[i] = halo;
            _haloMesh.colors32 = _haloColours;
        }

        private static Mesh HaloMesh()
        {
            var mesh = new Mesh { name = "Свет арены: ореол фонаря", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[] { new Vector3(-.5f, -.5f, 0f), new Vector3(.5f, -.5f, 0f), new Vector3(.5f, .5f, 0f), new Vector3(-.5f, .5f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void HideLanterns()
        {
            for (int i = 0; i < _lanterns.Count; i++)
                if (_lanterns[i].Root != null) _lanterns[i].Root.SetActive(false);
            if (_lanternRoot != null) _lanternRoot.gameObject.SetActive(false);
            _lanternsShown = 0;
        }

        private void DestroyLanterns()
        {
            DestroyOwned(_haloMesh);
            DestroyOwned(_haloMaterial);
            _haloMesh = null;
            _haloMaterial = null;
            _lanterns.Clear();
            _lanternRoot = null;
        }

        /// <summary>Фонари у порталов в сумерках: теплее и ярче долей сумерек. Сколько нашли.</summary>
        private int TintPortalLanterns()
        {
            RestorePortalLanterns();
            float dusk = _state.Weights.Dusk;
            if (dusk <= .01f || _layout == null) return 0;
            _layout.GetComponentsInChildren(false, _portalScan);
            foreach (Light light in _portalScan)
            {
                if (light == null || light.type != LightType.Point || light.name != LanternLightName) continue;
                if (_root != null && light.transform.IsChildOf(_root)) continue;
                _portalLights.Add(light);
                _portalColourBase.Add(light.color);
                _portalIntensityBase.Add(light.intensity);
                _portalRangeBase.Add(light.range);
                float intensity = Mathf.Lerp(light.intensity, Mathf.Max(light.intensity, _state.LanternIntensity * .8f), dusk);
                _portalIntensitySet.Add(intensity);
                light.color = Color.Lerp(light.color, _state.LanternColor, dusk);
                light.range = Mathf.Lerp(light.range, Mathf.Max(light.range, _state.LanternRange * .85f), dusk);
                light.intensity = intensity;
            }
            _portalScan.Clear();
            return _portalLights.Count;
        }

        private void RestorePortalLanterns()
        {
            for (int i = 0; i < _portalLights.Count; i++)
            {
                Light light = _portalLights[i];
                if (light == null) continue;
                light.color = _portalColourBase[i];
                light.intensity = _portalIntensityBase[i];
                light.range = _portalRangeBase[i];
            }
            _portalLights.Clear();
            _portalColourBase.Clear();
            _portalIntensityBase.Clear();
            _portalRangeBase.Clear();
            _portalIntensitySet.Clear();
        }
    }
}
