using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>
    /// ЗВЕНЬЯ НАД ГЕРОЕМ НА УДАРАХ КРУШЕНИЯ (06.10, нижний ряд целевого кадра ART/characters/pelag/
    /// wreck-look-2026-10-06/chatgpt-results/series-ui.png; вторая половина индикатора серии — первая на плитке HUD).
    /// На удар N над головой Пелага вскакивает N маленьких светящихся звеньев цвета формы и гаснет за полсекунды;
    /// последний удар — разлёт сходится в короткую цепь и лопается лучами. Числа — PelagWreckSeriesMarkRules.
    ///
    /// Рождается от события Sim WreckStage (Amount — этап, Flag — последний удар), не опросом снимка; возраст — от
    /// тика события с долей кадра (тик − 1 + Alpha): пауза держит кадр. Рисунки — те же звенья, что на плитке
    /// (Resources/UI/HUD/WreckSeries, тёмное железо с контуром + белый свет, красится формой). Части — спрайты лицом к
    /// камере поверх мира, как полоски здоровья (UI/Default с ZTest Always; свет — Razlom/UI Additive), чтобы не тонуть
    /// в кронах. Героя не высветляет (razlom-no-character-whitening): только звенья над головой.
    /// Ставится на объект арены одной строкой в ArenaView (EnsureOn).
    /// </summary>
    [DefaultExecutionOrder(1100)]
    [RequireComponent(typeof(TickDriver))]
    public sealed class PelagWreckSeriesView : MonoBehaviour
    {
        private const string Folder = "UI/HUD/WreckSeries/";
        private const string HeadBone = "mixamorig:Head";
        private const int Never = int.MinValue / 2, Order = 5150;
        private static readonly int ZTestId = Shader.PropertyToID("unity_GUIZTestMode");
        private static readonly int TextureSampleAddId = Shader.PropertyToID("_TextureSampleAdd");

        private TickDriver _driver;
        private ArenaView _arena;
        private Transform _camera, _root, _heroView, _head;
        private readonly Transform[] _links = new Transform[HudWreckSeriesRules.MaxLinks];
        private readonly SpriteRenderer[] _irons = new SpriteRenderer[HudWreckSeriesRules.MaxLinks];
        private readonly SpriteRenderer[] _glows = new SpriteRenderer[HudWreckSeriesRules.MaxLinks];
        private SpriteRenderer _burst;
        private float _linkScale, _burstScale;
        private bool _ready;

        private Simulation _shown;
        private int _generation = -1, _depth = -1, _strikeTick = Never, _count;
        private bool _final;
        private PelagForm _form;

        public static PelagWreckSeriesView EnsureOn(GameObject host)
        {
            if (host == null) return null;
            var view = host.GetComponent<PelagWreckSeriesView>();
            return view != null ? view : host.AddComponent<PelagWreckSeriesView>();
        }

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _arena = GetComponent<ArenaView>();
            Build();
        }

        private void Build()
        {
            Sprite iron = Resources.Load<Sprite>(Folder + "Wreck_Link_Iron");
            Sprite glow = Resources.Load<Sprite>(Folder + "Wreck_Link_Glow");
            Sprite burst = Resources.Load<Sprite>(Folder + "Wreck_Link_Burst");
            Shader plain = Shader.Find("UI/Default"), light = Shader.Find("Razlom/UI Additive");
            if (iron == null || glow == null || burst == null || plain == null || light == null)
            {
                Debug.LogWarning("[Разлом] Звенья серии Крушения над героем: нет рисунков «Resources/" + Folder + "» или шейдеров UI/Default, Razlom/UI Additive — звеньев не будет.");
                return;
            }
            Material ironMaterial = Overlay(plain, "Крушение: звенья над героем · железо");
            Material glowMaterial = Overlay(light, "Крушение: звенья над героем · свет");
            _root = new GameObject("Крушение: звенья над героем").transform;
            _root.SetParent(transform, false);
            // Рисунок 128 px при 100 px на метр — 1,28 м: звено ужимается до ширины из правил.
            _linkScale = PelagWreckSeriesMarkRules.LinkWidth / iron.bounds.size.x;
            for (int i = 0; i < _links.Length; i++)
            {
                _links[i] = new GameObject("Звено " + (i + 1)).transform;
                _links[i].SetParent(_root, false);
                _irons[i] = Part(_links[i], "Железо", iron, ironMaterial, Order);
                _glows[i] = Part(_links[i], "Свет", glow, glowMaterial, Order + 1);
            }
            // Лучи — шире цепи из трёх звеньев в полтора раза (рисунок 256 px).
            float chain = (Simulation.WreckStages - 1) * PelagWreckSeriesMarkRules.ChainPitch + PelagWreckSeriesMarkRules.LinkWidth;
            _burstScale = chain * 1.6f / burst.bounds.size.x;
            _burst = Part(_root, "Лучи", burst, glowMaterial, Order + 2);
            _root.gameObject.SetActive(false);
            _ready = true;
        }

        private static Material Overlay(Shader shader, string name)
        {
            var material = new Material(shader) { name = name };
            material.SetFloat(ZTestId, (float)CompareFunction.Always);
            material.SetVector(TextureSampleAddId, Vector4.zero);
            return material;
        }

        private static SpriteRenderer Part(Transform parent, string name, Sprite sprite, Material material, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private void LateUpdate()
        {
            Simulation sim = _driver.Sim;
            int depth = _driver.Run != null ? _driver.Run.Depth : -1;
            // Смена симуляции, новый Разлом или общий сброс: тики начинаются заново (как HeroControlView).
            if (!ReferenceEquals(sim, _shown) || _generation != _driver.Generation || depth != _depth)
            {
                _shown = sim;
                _generation = _driver.Generation;
                _depth = depth;
                _strikeTick = Never;
            }
            if (!_ready || sim == null) { Hide(); return; }
            ConsumeEvents(sim);
            float age = (sim.Tick - 1 + _driver.Alpha - _strikeTick) / Simulation.TicksPerSecond;
            float alpha = PelagWreckSeriesMarkRules.Alpha(age);
            if (alpha <= 0f || CaptureRig.NoVfx || !sim.Entities.Alive[Simulation.PlayerId] || !Place(sim))
            {
                Hide();
                return;
            }
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            Paint(age, alpha);
        }

        private void ConsumeEvents(Simulation sim)
        {
            var contexts = _driver.FrameEventContexts;
            for (int i = 0; i < contexts.Count; i++)
            {
                SimEvent e = contexts[i].Event;
                if (e.Type == SimEventType.WreckStage && e.Source == Simulation.PlayerId)
                {
                    _strikeTick = contexts[i].SimulationTick - 1;
                    _count = PelagWreckSeriesMarkRules.Links(e.Amount + 1);
                    _final = e.Flag;
                    int slot = sim.Wreck.Slot;
                    _form = (uint)slot < (uint)Simulation.AbilitySlots ? sim.FormAt(slot) : PelagForm.None;
                }
                else if (e.Type == SimEventType.Death && e.Target == Simulation.PlayerId) _strikeTick = Never;
            }
        }

        /// <summary>Над костью головы (идёт за покачиванием тела); без кости — над точкой героя. Лицом к камере.</summary>
        private bool Place(Simulation sim)
        {
            if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
            if (_camera == null) return false;
            Vector3 at;
            if (_arena != null && _arena.TryGetEntityView(Simulation.PlayerId, out Transform view) && view != null)
            {
                if (view != _heroView) { _heroView = view; _head = FindHead(view); }
                at = _head != null
                    ? new Vector3(view.position.x, _head.position.y + PelagWreckSeriesMarkRules.HeadLift, view.position.z)
                    : view.position + Vector3.up * PelagWreckSeriesMarkRules.FallbackHeight;
            }
            else at = _driver.GetRenderPosition(Simulation.PlayerId) + Vector3.up * PelagWreckSeriesMarkRules.FallbackHeight;
            _root.SetPositionAndRotation(at, _camera.rotation);
            return true;
        }

        private static Transform FindHead(Transform root)
        {
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
                if (bone.name == HeadBone) return bone;
            return null;
        }

        private void Paint(float age, float alpha)
        {
            for (int i = 0; i < _links.Length; i++)
            {
                bool on = i < _count;
                if (_links[i].gameObject.activeSelf != on) _links[i].gameObject.SetActive(on);
                if (!on) continue;
                float scale = PelagWreckSeriesMarkRules.Scale(i, _count, age, _final) * _linkScale;
                _links[i].localPosition = new Vector3(PelagWreckSeriesMarkRules.Offset(i, _count, age, _final), 0f, 0f);
                _links[i].localScale = new Vector3(scale, scale, 1f);
                _irons[i].color = new Color(1f, 1f, 1f, alpha);
                _glows[i].color = Tint(PelagWreckSeriesMarkRules.Hot(i, _count, age, _final), alpha);
            }
            float burst = PelagWreckSeriesMarkRules.BurstAlpha(age, _final);
            if (_burst.enabled != burst > .001f) _burst.enabled = burst > .001f;
            if (burst <= .001f) return;
            float k = PelagWreckSeriesMarkRules.BurstScale(age) * _burstScale;
            _burst.transform.localScale = new Vector3(k, k, 1f);
            _burst.color = Tint(.35f, burst);
        }

        private Color Tint(float hot, float alpha)
        {
            HudWreckSeriesRules.FormColour(_form, hot, out float r, out float g, out float b);
            return new Color(r, g, b, Mathf.Clamp01(alpha));
        }

        private void Hide()
        {
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        private void OnDisable() => Hide();
    }
}
