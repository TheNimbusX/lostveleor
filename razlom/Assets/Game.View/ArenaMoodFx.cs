using System;
using System.Globalization;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// «Жизнь в кадре» света арены по глубине (световая арка акта I, план
    /// ART/UI/concepts-2026-10-01-style-shift/light-arc-plan.md, разделы 3.5–3.6): поверх света
    /// <see cref="ArenaMoodView"/> — дымка чащи кольцами вокруг поляны, лучи в тумане, пыльца днём, искры в
    /// лучах, светлячки и фонари на столбах в сумерках, тёплые огоньки поляны и фонари порталов в сумерках.
    /// Сколько и какого цвета — из <see cref="ArenaMood.Current"/>, всё взвешено настроением.
    ///
    /// Только пока свет арены применён (<see cref="ArenaMood.Active"/>): по умолчанию он выключен, в лагере и в
    /// меню эффектов нет, игра как сегодня. Ставится в кадре, где ArenaMoodView применил свет (конец сборки
    /// арены, дым перехода ещё держит экран), убирается и возвращает чужое (огоньки и фонари порталов Кости) в
    /// момент отката — до сборки следующей арены. Всё создаётся один раз и переиспользуется; в кадре — только
    /// сдвиг лучей за камерой, рождение частиц и мерцание фонарей, без выделений. Коллайдеров нет, оранжевого
    /// нет (оранжевое — метка атаки врага), звуков нет.
    /// </summary>
    [DefaultExecutionOrder(160)]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed partial class ArenaMoodFx : MonoBehaviour
    {
        private TickDriver _driver;
        private LayoutView _layout;
        private Transform _root;
        private Camera _camera;
        private CameraFollow _follow;
        private bool _dirty, _shown;
        private ArenaMoodState _state;
        private uint _seed;

        // Проходы к порталам: дымка, светлячки и фонари не закрывают тропу (по 4 числа: центр поляны → портал).
        private readonly float[] _portalSegments = new float[4 * 8];
        private int _portalCount;

        // Полярный контур поляны (как у ArenaMood, 72 направления) — для колец дымки.
        private readonly float[] _contour = new float[ArenaMoodRules.ContourSamples];

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _layout = GetComponent<LayoutView>();
            // Съёмка: -capture-mood-set "Fx.…" — ручки эффектов на запуск; в игре ничего.
            ArenaMoodCaptureTuning.ApplyTo(this);
        }

        private void OnEnable()
        {
            ArenaMood.StateChanged += OnMoodChanged;
            _dirty = ArenaMood.Active;
        }

        private void OnDisable()
        {
            ArenaMood.StateChanged -= OnMoodChanged;
            HideAll();
        }

        private void OnDestroy()
        {
            HideAll();
            DestroyHaze();
            DestroyBeams();
            DestroyMotes();
            DestroyLanterns();
            if (_root != null) Destroy(_root.gameObject);
        }

        // Откат света (новая арена, лагерь, F8) — сразу, синхронно: огоньки и фонари порталов Кости
        // возвращаются до того, как сборка следующей арены возьмёт их из пула.
        private void OnMoodChanged()
        {
            try
            {
                if (!ArenaMood.Active)
                {
                    _dirty = false;
                    if (_shown) HideAll();
                    return;
                }
                _dirty = true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void LateUpdate()
        {
            if (_dirty)
            {
                _dirty = false;
                if (ArenaMood.Active)
                {
                    try
                    {
                        Show();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        HideAll();
                    }
                }
            }
            if (!_shown) return;
            float time = Time.time, delta = Time.deltaTime;
            TickBeams(time);
            TickMotes(delta);
            TickLanterns(time);
        }

        private void Show()
        {
            if (_shown) HideAll();
            RiftRun run = _driver != null ? _driver.Run : null;
            LayoutMap map = run != null ? run.Map : null;
            if (map == null || !ArenaMood.HasGlade) return;
            // С этой строки откат возможен: ошибка посреди показа не оставит чужие огоньки и фонари перекрашенными.
            _shown = true;
            _state = ArenaMood.Current;
            _seed = ArenaMoodRandom.Mix(run.LayoutSeed, _state.Depth, (int)_state.Mode);
            EnsureRoot();
            FindCamera();
            ReadContour();
            ReadPortals(map);

            int haze = ShowHaze();
            int beams = ShowBeams();
            ShowMotes(out int pollen, out int sparks, out int fireflies);
            int lanterns = ShowLanterns();
            int portalLights = TintPortalLanterns();
            bool wisps = TintWisps();
            Debug.Log("[arena-mood] fx: haze=" + (haze > 0 ? _state.HazeDensity.ToString("0.##", Invariant) + "×" + haze : "нет")
                      + " beams=" + beams + " sparks=" + sparks + " pollen=" + pollen + " fireflies=" + fireflies
                      + " lanterns=" + lanterns + "/" + _state.LanternCount + " portal-lights=" + portalLights
                      + " wisps=" + (wisps ? "#" + ColorUtility.ToHtmlStringRGB(_state.WispColor) : "как есть"));
        }

        private void HideAll()
        {
            if (!_shown) return;
            _shown = false;
            HideHaze();
            HideBeams();
            HideMotes();
            HideLanterns();
            RestorePortalLanterns();
            RestoreWisps();
        }

        private void EnsureRoot()
        {
            if (_root != null) return;
            var root = new GameObject("Свет арены — жизнь в кадре");
            root.transform.SetParent(transform, false);
            _root = root.transform;
        }

        private void FindCamera()
        {
            if (_follow == null) _follow = FindAnyObjectByType<CameraFollow>();
            Camera camera = _follow != null ? _follow.GetComponent<Camera>() : null;
            _camera = camera != null ? camera : Camera.main;
        }

        /// <summary>Куда камера смотрит по земле (единичный вектор) и её наклон, градусы.</summary>
        private void CameraGround(out float forwardX, out float forwardZ, out float pitch)
        {
            Vector3 forward = _camera != null ? _camera.transform.forward : new Vector3(.29f, -.74f, .6f);
            var flat = new Vector2(forward.x, forward.z);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector2.up;
            flat.Normalize();
            forwardX = flat.x;
            forwardZ = flat.y;
            pitch = Mathf.Asin(Mathf.Clamp(-forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private void ReadContour()
        {
            for (int a = 0; a < _contour.Length; a++)
                _contour[a] = ArenaMood.ContourRadius(a * 2f * Mathf.PI / _contour.Length);
        }

        private void ReadPortals(LayoutMap map)
        {
            _portalCount = 0;
            Vector3 centre = ArenaMood.GladeCenter;
            AddPortal(centre, map.EntryPoint);
            for (int e = 0; e < map.ExitCount; e++) AddPortal(centre, map.ExitPoint(e));
        }

        private void AddPortal(Vector3 centre, FixVec2 point)
        {
            if (_portalCount * 4 + 4 > _portalSegments.Length) return;
            float px = point.X.ToFloat(), pz = point.Y.ToFloat();
            var along = new Vector2(px - centre.x, pz - centre.z);
            // Проход тянется и за портал: там тропа и столбы у входа.
            Vector2 end = along.sqrMagnitude > 1e-4f ? new Vector2(px, pz) + along.normalized * 4f : new Vector2(px, pz);
            int i = _portalCount * 4;
            _portalSegments[i] = centre.x;
            _portalSegments[i + 1] = centre.z;
            _portalSegments[i + 2] = end.x;
            _portalSegments[i + 3] = end.y;
            _portalCount++;
        }

        /// <summary>Точка ближе к проходу к порталу, чем <paramref name="clear"/> метров.</summary>
        private bool NearPortalPath(float x, float z, float clear)
        {
            for (int i = 0; i < _portalCount; i++)
            {
                int k = i * 4;
                if (ArenaMoodFxRules.SegmentDistance(x, z, _portalSegments[k], _portalSegments[k + 1],
                        _portalSegments[k + 2], _portalSegments[k + 3]) < clear) return true;
            }
            return false;
        }

        private static Material LoadShaderMaterial(string resource, string name)
        {
            Shader shader = Resources.Load<Shader>(resource);
            if (shader == null) return null;
            return new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        /// <summary>Цвет как в инспекторе (sRGB) × доля яркости, без альфы.</summary>
        private static Color Scaled(Color colour, float scale) =>
            new Color(colour.r * scale, colour.g * scale, colour.b * scale, 1f);
    }
}
