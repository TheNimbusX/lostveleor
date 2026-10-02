using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — ОДЕЖДА ФАЗ (02.10, «босс по фазам не меняется вообще»; план
    /// artifacts/tools/wf/boss-vfx-plan.md §4). Стоит на теле (ThicketMaster_Runtime, рядом с
    /// ThicketMasterAnimatorView); на префаб его ставит ThicketMasterDressingSetup (или строка
    /// в ThicketMasterBuilder.Build). Сериализованных ссылок нет: кости — по имени, накладки
    /// и карта Ф3 — из Resources.
    ///
    /// Что меняется (уровень — <see cref="ThicketMasterPhaseRules"/>, смена — на УДАРЕ рёва):
    /// • Ф1 — руны тёмные, листва зелёная, куст как в текстуре (редкие ягоды);
    /// • Ф2 (рёв 66) — ОСЕНЬ: листва багряная/рыжая/золотая (карта цвета F2), руны, глаза и
    ///   трещины спины горят янтарём и пульсируют, ягоды куста краснеют и набухают, со спины
    ///   и кроны всё время поднимаются угли; всплеск — листопад, угли, ягоды. Ярость (рёв 50) —
    ///   руны ярче, углей больше, искры рун и листья;
    /// • Ф3 (рёв 33) — ЦВЕТЕНИЕ: верх кроны розовый (карта цвета F3), руны ярче со смоляными
    ///   прожилками (карта эмиссии F3), из куста и на кроне распускаются цветы, с кроны всё время
    ///   падают лепестки и плывёт розовый свет; глаза — розовым золотом; всплеск лепестков.
    /// Ревью 02.10 «внешний вид фазы 2 не особо отличается»: камера 48° видит спину и крону,
    /// поэтому главное — цвет листвы и аура над спиной, а не руны на груди.
    /// Светятся только руны, трещины и глаза — по маске эмиссии; тело не высветляется (_BaseColor
    /// белый; у карт листвы яркость та же, другой оттенок). Эмиссия и карта цвета — блоком свойств
    /// поверх блока ArenaView: тот каждый LateUpdate перезаписывает блоки тела по слотам, поэтому
    /// вид идёт после него (порядок 670) и сливает свои значения в его блок каждый кадр.
    /// Материал не копируется.
    ///
    /// Накладки — дети костей (bush, crown_L/R, head): едут с позой и ростом тела, прячутся
    /// в нырке вместе с телом (SetHidden вида тела), уходят в землю со смертью. Вид не трогает
    /// renderer.enabled и не выключает накладки: «не видно» — масштаб 0 или нет частиц.
    /// Всё создаётся в Awake один раз на тело пула; в кадре — без аллокаций. Возраст —
    /// от тика Sim (Tick − 1 + Alpha): пауза и съёмка держат кадр.
    /// Привязка посреди боя (съёмка с середины, пересборка пула) — сразу нужный уровень, без
    /// вспышки и всплесков. Звука нет.
    /// </summary>
    [DefaultExecutionOrder(670)]
    [DisallowMultipleComponent]
    public sealed class ThicketMasterPhaseDressing : MonoBehaviour
    {
        /// <summary>Тело +15 % (4,14 / 3,6 м, решение 02.10). Накладки масштабирует сам узел тела — справочно.</summary>
        public const float BodyScale = 1.15f;

        public const string PrefabFolder = "VFX/ThicketMaster/Phases/Prefabs/";
        public const string BerriesName = "VFX_ThicketPhase_Berries";
        public const string BushBloomName = "VFX_ThicketPhase_BushBloom";
        public const string CrownBloomLeftName = "VFX_ThicketPhase_CrownBloom_L";
        public const string CrownBloomRightName = "VFX_ThicketPhase_CrownBloom_R";
        public const string EyeGlowName = "VFX_ThicketPhase_EyeGlow";
        /// <summary>Аура Ф2 на груди: угли со спины и кроны; всплеск рёва 66 — листопад и угли.</summary>
        public const string EmbersName = "VFX_ThicketPhase_Embers";
        /// <summary>Аура Ф3 на груди: розовый свет над кроной; всплеск рёва 33 — светящиеся лепестки.</summary>
        public const string BloomAuraName = "VFX_ThicketPhase_BloomAura";

        /// <summary>Растущая часть накладки: «Bud|задержка мс|подпись» — MeshRenderer, масштаб префаба = вырос.</summary>
        public const string BudPrefix = "Bud|";
        public const char BudSeparator = '|';

        /// <summary>Контейнер всплеска на рёве (системы частиц с одним залпом).</summary>
        public const string BurstsName = "Bursts";

        /// <summary>Петля падающих лепестков на кроне (Ф3).</summary>
        public const string PetalLoopName = "Petal Fall";

        /// <summary>Петля ауры фазы (угли Ф2, розовый свет Ф3): шагает так же, как лепестки кроны.</summary>
        public const string AuraName = "Aura";

        /// <summary>Ореол глаз: система с частицей на каждый «Eye …»-якорь.</summary>
        public const string GlowName = "Glow";
        public const string EyePrefix = "Eye ";

        public const string BoneBush = "bush", BoneCrownLeft = "crown_L", BoneCrownRight = "crown_R", BoneHead = "head", BoneChest = "chest";

        /// <summary>Карты эмиссии в Resources (копирует ThicketMasterBuilder из пакета).</summary>
        public const string EmissionF2Resource = "Characters/Forest_ThicketMaster/T_ThicketMaster_Emission_F2";
        public const string EmissionF3Resource = "Characters/Forest_ThicketMaster/T_ThicketMaster_Emission_F3";

        /// <summary>
        /// Карты цвета листвы (production/dressing/make_phase_colors.py): F2 — осень, F3 — цветение.
        /// Нет карты — листва Ф1 (как было до 02.10).
        /// </summary>
        public const string ColorF2Resource = "Characters/Forest_ThicketMaster/T_ThicketMaster_Color_F2";
        public const string ColorF3Resource = "Characters/Forest_ThicketMaster/T_ThicketMaster_Color_F3";

        /// <summary>Углей в ярости (рёв 50) больше во столько раз.</summary>
        private const float EmberRageGain = 1.6f;

        /// <summary>Рост: ягоды набухают за .6 с, цветы раскрываются за .5 с; всплеск живёт до 4 с.</summary>
        private const float BerrySeconds = .6f, FlowerSeconds = .5f, BurstLife = 4f;

        /// <summary>
        /// Ореол глаз: альфа на Ф2 и цвет (частицы 8-битные — свет за порог bloom даёт _HdrMultiply
        /// материала M_ThicketPhase_Ember); в Ф3 — розовое золото цветения.
        /// </summary>
        private const float EyeAlpha = .85f;
        private static readonly Color EyeColor = new Color(1f, .62f, .15f), EyeColorBloom = new Color(1f, .58f, .66f);

        private const int None = ThicketMasterPhaseRules.None;
        private const int ShapeSwell = 0, ShapeRise = 1;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int[] PhaseBitList = { Simulation.ThicketRoar66Bit, Simulation.ThicketRoar50Bit, Simulation.ThicketRoar33Bit };
        private static bool _warnedPrefab, _warnedKeyword;

        private sealed class Overlay
        {
            public Transform Root;
            public Transform[] Buds;
            public Vector3[] BudScale;
            public float[] BudDelay;
            public int Shape, MinLevel, GrowBit, BurstBit;
            /// <summary>Петли и аура — до этого уровня включительно (угли Ф2 гаснут в цветении Ф3).</summary>
            public int MaxLevel = 3;
            /// <summary>Множитель частоты петель в ярости (рёв 50).</summary>
            public float RageGain = 1f;
            /// <summary>Записанное состояние роста: −1 — свёрнута, 1 — выросла целиком, 0 — растёт.</summary>
            public int Written = 2;
            public RootSnarerCombatView.Fx Burst;
            public int BurstTick = None;
            /// <summary>Петли накладки (лепестки кроны, аура) и их частота из префаба.</summary>
            public ParticleSystem[] Loops = new ParticleSystem[0];
            public float[] LoopRate = new float[0];
            public bool LoopStarted;
            public ParticleSystem Glow;
            public Vector3[] Eyes;
            public float GlowSize = .22f;
        }

        private ThicketMasterAnimatorView _view;
        private ArenaView _arena;
        private TickDriver _driver;
        private Transform _body;
        private Renderer[] _slotRenderers = new Renderer[0];
        private int[] _slotIndices = new int[0];
        private MaterialPropertyBlock _block;
        private Texture _mapF2, _mapF3;
        /// <summary>Карта цвета тела: своя (листва Ф1) и фазовые; null — нет такой карты.</summary>
        private Texture _colorF1, _colorF2, _colorF3;
        private Overlay[] _overlays = new Overlay[0];
        private ParticleSystem.Particle[] _eyeParticles = new ParticleSystem.Particle[4];

        private Simulation _boundSim;
        private int _boundEntity = -1, _boundGeneration = -1;
        private int _bits;
        private readonly int[] _bitTick = { None, None, None };
        private int _deathTick = None;
        private float _lastTick;

        /// <summary>Уровень одежды этого кадра (1–3; 0 — не привязан). Для съёмки и стендов.</summary>
        public int Level { get; private set; }

        /// <summary>Множитель _EmissionColor этого кадра.</summary>
        public float Glow { get; private set; }

        private void Awake()
        {
            _view = GetComponent<ThicketMasterAnimatorView>();
            var animator = GetComponentInChildren<Animator>(true);
            _body = animator != null ? animator.transform : transform;
            _block = new MaterialPropertyBlock();
            CollectBodySlots();
            _mapF3 = Resources.Load<Texture2D>(EmissionF3Resource);
            if (_mapF2 == null) _mapF2 = Resources.Load<Texture2D>(EmissionF2Resource);
            if (_mapF3 == null) _mapF3 = _mapF2;
            // Листва фаз — только вместе со своей картой Ф1 (иначе нечем вернуть зелень в пул).
            if (_colorF1 != null)
            {
                _colorF2 = Resources.Load<Texture2D>(ColorF2Resource);
                _colorF3 = Resources.Load<Texture2D>(ColorF3Resource);
                if (_colorF3 == null) _colorF3 = _colorF2;
            }
            BuildOverlays();
            Collapse();
        }

        // ------------------------------------------------------------ сборка

        /// <summary>Слоты URP Lit тела (маску контура не трогаем); F2 — из материала, если он её держит.</summary>
        private void CollectBodySlots()
        {
            var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var slotRenderers = new List<Renderer>();
            var slotIndices = new List<int>();
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    // Тело — URP Lit или его копия с прозрачностью перед героем (ThicketMasterSeeThroughRules).
                    if (material == null || material.shader == null
                        || (material.shader.name != "Universal Render Pipeline/Lit" && material.shader.name != ThicketMasterSeeThroughRules.LitShader)) continue;
                    slotRenderers.Add(renderer);
                    slotIndices.Add(i);
                    if (_mapF2 == null && material.HasProperty(EmissionMapId)) _mapF2 = material.GetTexture(EmissionMapId);
                    if (_colorF1 == null && material.HasProperty(BaseMapId)) _colorF1 = material.GetTexture(BaseMapId);
                    if (!material.IsKeywordEnabled("_EMISSION") && !_warnedKeyword)
                    {
                        _warnedKeyword = true;
                        Debug.LogWarning("[thicketmaster] В материале тела нет _EMISSION — руны не загорятся. Собери «Разлом/Босс/Хозяин Чащи/Собрать представление» (маски эмиссии — production/dressing/make_emission.py).");
                    }
                }
            }
            _slotRenderers = slotRenderers.ToArray();
            _slotIndices = slotIndices.ToArray();
        }

        private void BuildOverlays()
        {
            Transform bush = null, crownLeft = null, crownRight = null, head = null, chest = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case BoneBush: bush = t; break;
                    case BoneCrownLeft: crownLeft = t; break;
                    case BoneCrownRight: crownRight = t; break;
                    case BoneHead: head = t; break;
                    case BoneChest: chest = t; break;
                }
            }
            var list = new List<Overlay>(7);
            Add(list, BerriesName, bush, ShapeSwell, 2, Simulation.ThicketRoar66Bit, Simulation.ThicketRoar66Bit);
            Add(list, BushBloomName, bush, ShapeRise, 3, Simulation.ThicketRoar33Bit, Simulation.ThicketRoar33Bit);
            Add(list, CrownBloomLeftName, crownLeft, ShapeRise, 3, Simulation.ThicketRoar33Bit, Simulation.ThicketRoar33Bit);
            Add(list, CrownBloomRightName, crownRight, ShapeRise, 3, Simulation.ThicketRoar33Bit, Simulation.ThicketRoar33Bit);
            // Глаза загораются с рёва 66; всплеск этой накладки (искры рун, листья) — на рёве 50.
            Add(list, EyeGlowName, head, ShapeRise, 2, Simulation.ThicketRoar66Bit, Simulation.ThicketRoar50Bit);
            // Ауры: угли — только осень Ф2 (в ярости гуще), розовый свет — цветение Ф3.
            var embers = Add(list, EmbersName, chest, ShapeRise, 2, Simulation.ThicketRoar66Bit, Simulation.ThicketRoar66Bit);
            if (embers != null) { embers.MaxLevel = 2; embers.RageGain = EmberRageGain; }
            Add(list, BloomAuraName, chest, ShapeRise, 3, Simulation.ThicketRoar33Bit, Simulation.ThicketRoar33Bit);
            _overlays = list.ToArray();
        }

        private Overlay Add(List<Overlay> list, string prefabName, Transform bone, int shape, int minLevel, int growBit, int burstBit)
        {
            var prefab = Resources.Load<GameObject>(PrefabFolder + prefabName);
            if (prefab == null || bone == null)
            {
                if (!_warnedPrefab)
                {
                    _warnedPrefab = true;
                    Debug.LogWarning($"[thicketmaster] Нет накладки фаз «{PrefabFolder}{prefabName}» или кости — собери «Разлом/Босс/Хозяин Чащи/Фазы: пересобрать». Руны светятся и без неё.");
                }
                return null;
            }
            var go = Instantiate(prefab, bone, false);
            go.name = prefabName;
            var root = go.transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            // Накладка авторится в метрах модели в осях кости: масштаб кости внутри модели снимается,
            // рост узла тела (TargetHeight сборщика) остаётся — накладка растёт вместе с телом.
            float bone01 = Mathf.Abs(bone.lossyScale.x), body01 = Mathf.Abs(_body.lossyScale.x);
            root.localScale = Vector3.one * (bone01 > 1e-6f && body01 > 1e-6f ? body01 / bone01 : 1f);

            var overlay = new Overlay { Root = root, Shape = shape, MinLevel = minLevel, GrowBit = growBit, BurstBit = burstBit };
            var buds = new List<Transform>();
            var eyes = new List<Vector3>();
            var loops = new List<ParticleSystem>();
            for (int c = 0; c < root.childCount; c++)
            {
                var child = root.GetChild(c);
                if (child.name.StartsWith(BudPrefix, System.StringComparison.Ordinal)) buds.Add(child);
                else if (child.name.StartsWith(EyePrefix, System.StringComparison.Ordinal)) eyes.Add(child.localPosition);
                else if (child.name == BurstsName) overlay.Burst = RootSnarerCombatView.Prepare(child.gameObject);
                else if (child.name == PetalLoopName || child.name == AuraName)
                {
                    var loop = child.GetComponent<ParticleSystem>();
                    if (loop != null) loops.Add(loop);
                }
                else if (child.name == GlowName) overlay.Glow = child.GetComponent<ParticleSystem>();
            }
            overlay.Buds = buds.ToArray();
            overlay.BudScale = new Vector3[buds.Count];
            overlay.BudDelay = new float[buds.Count];
            for (int i = 0; i < buds.Count; i++)
            {
                overlay.BudScale[i] = buds[i].localScale;
                string[] parts = buds[i].name.Split(BudSeparator);
                overlay.BudDelay[i] = parts.Length > 1 && int.TryParse(parts[1], out int ms) ? ms / 1000f : 0f;
            }
            overlay.Eyes = eyes.ToArray();
            if (overlay.Eyes.Length > _eyeParticles.Length) _eyeParticles = new ParticleSystem.Particle[overlay.Eyes.Length];
            if (overlay.Glow != null)
            {
                overlay.GlowSize = overlay.Glow.main.startSizeMultiplier;
                overlay.Glow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            overlay.Loops = loops.ToArray();
            overlay.LoopRate = new float[overlay.Loops.Length];
            for (int i = 0; i < overlay.Loops.Length; i++)
            {
                var loop = overlay.Loops[i];
                loop.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                overlay.LoopRate[i] = loop.emission.rateOverTimeMultiplier;
                // Лепестки ложатся на землю: плоскость — корень тела (ArenaView держит его на земле, только поворот вокруг Y).
                var collision = loop.collision;
                if (collision.enabled) collision.SetPlane(0, transform);
            }
            list.Add(overlay);
            return overlay;
        }

        // ------------------------------------------------------------ кадр

        private void LateUpdate()
        {
            if (_driver == null)
            {
                if (_arena == null) _arena = GetComponentInParent<ArenaView>();
                if (_arena != null) _driver = _arena.GetComponent<TickDriver>();
            }
            Simulation sim = _driver != null ? _driver.Sim : null;
            int entity = _view != null ? _view.Entity : -1;
            bool valid = sim != null && entity >= 1 && entity < sim.Entities.Count
                         && sim.Entities.Kind[entity] == EnemyKind.ForestThicketMaster;
            if (!valid)
            {
                if (_boundSim != null) Unbind();
                WriteBody(0f, _mapF2, _colorF1);
                return;
            }
            float tick = sim.Tick - 1 + _driver.Alpha;
            if (!ReferenceEquals(sim, _boundSim) || entity != _boundEntity || _driver.Generation != _boundGeneration)
                Bind(sim, entity, tick);

            if (sim.Entities.Alive[entity]) UpdateBits(sim, entity, tick);
            else if (_deathTick == None) _deathTick = DeathTick(entity, sim);

            int level = ThicketMasterPhaseRules.Level(_bits);
            bool enraged = ThicketMasterPhaseRules.Enraged(_bits);
            int change = LatestChange();
            float glow = ThicketMasterPhaseRules.Glow(level, enraged, tick, change, _deathTick);
            Level = level;
            Glow = glow;
            WriteBody(glow, ThicketMasterPhaseRules.UsesPhase3Map(level) ? _mapF3 : _mapF2, ColorFor(level));

            bool hidden = _view != null && _view.IsBurrowed;
            bool alive = _deathTick == None;
            float dt = Mathf.Clamp(tick - _lastTick, 0f, 8f) / Simulation.TicksPerSecond;
            _lastTick = tick;
            for (int i = 0; i < _overlays.Length; i++)
            {
                var overlay = _overlays[i];
                Grow(overlay, level, tick);
                StepBurst(overlay, tick);
                bool loopOn = level >= overlay.MinLevel && level <= overlay.MaxLevel && alive && !hidden;
                StepLoops(overlay, loopOn, enraged ? overlay.RageGain : 1f, dt);
                StepGlow(overlay, level, tick, glow);
            }
        }

        /// <summary>Карта цвета уровня: Ф2 — осень, Ф3 — цветение; нет карты — своя (Ф1).</summary>
        private Texture ColorFor(int level)
        {
            Texture map = level >= 3 ? _colorF3 : level == 2 ? _colorF2 : null;
            return map != null ? map : _colorF1;
        }

        /// <summary>Привязка к сущности: уровень сразу, без вспышки, роста и всплесков.</summary>
        private void Bind(Simulation sim, int entity, float tick)
        {
            _boundSim = sim;
            _boundEntity = entity;
            _boundGeneration = _driver.Generation;
            _bits = 0;
            if (sim.TryGetThicketMasterMemory(entity, out var m))
            {
                bool acting = sim.TryGetThicketMasterAction(entity, out var a);
                _bits = ThicketMasterPhaseRules.ShownBits(m, acting, a, tick);
            }
            for (int i = 0; i < _bitTick.Length; i++) _bitTick[i] = None;
            // Уже мёртв при привязке — руны погашены сразу.
            _deathTick = sim.Entities.Alive[entity] ? None : sim.Tick - 1 - ThicketMasterPhaseRules.DeathFadeTicks;
            _lastTick = tick;
            Collapse();
        }

        private void Unbind()
        {
            _boundSim = null;
            _boundEntity = -1;
            _boundGeneration = -1;
            _bits = 0;
            _deathTick = None;
            Level = 0;
            Glow = 0f;
            Collapse();
        }

        /// <summary>Новые биты порогов: тик смены — удар рёва (или этот тик), всплески накладок. Перемотка снимает биты.</summary>
        private void UpdateBits(Simulation sim, int entity, float tick)
        {
            if (!sim.TryGetThicketMasterMemory(entity, out var m)) return;
            bool acting = sim.TryGetThicketMasterAction(entity, out var a);
            int bits = ThicketMasterPhaseRules.ShownBits(m, acting, a, tick);
            if (bits == _bits) return;
            int change = ThicketMasterPhaseRules.ChangeTick(acting, a, tick);
            if (change == None) change = Mathf.FloorToInt(tick);
            for (int i = 0; i < PhaseBitList.Length; i++)
            {
                int bit = PhaseBitList[i];
                bool had = (_bits & bit) != 0, has = (bits & bit) != 0;
                if (has && !had)
                {
                    _bitTick[i] = change;
                    for (int k = 0; k < _overlays.Length; k++)
                        if (_overlays[k].BurstBit == bit) StartBurst(_overlays[k], change);
                }
                else if (had && !has) _bitTick[i] = None;
            }
            _bits = bits;
        }

        private int LatestChange()
        {
            int latest = None;
            for (int i = 0; i < _bitTick.Length; i++)
                if (_bitTick[i] != None && (latest == None || _bitTick[i] > latest) && (_bits & PhaseBitList[i]) != 0) latest = _bitTick[i];
            return latest;
        }

        private int BitTick(int bit)
        {
            for (int i = 0; i < PhaseBitList.Length; i++)
                if (PhaseBitList[i] == bit) return _bitTick[i];
            return None;
        }

        /// <summary>Тик смерти — из события Death этого кадра; нет его — последний тик Sim.</summary>
        private int DeathTick(int entity, Simulation sim)
        {
            IReadOnlyList<FrameEventContext> events = _driver.FrameEventContexts;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i].Event;
                if (e.Type == SimEventType.Death && e.Target == entity) return events[i].SimulationTick - 1;
            }
            return sim.Tick - 1;
        }

        // ------------------------------------------------------------ тело

        /// <summary>
        /// Слияние с блоком ArenaView этого кадра: его _HitFlash/_Outline* остаются, наше — эмиссия
        /// и карта цвета листвы. Карта цвета пишется всегда (и своя в Ф1): блок без неё оставил бы
        /// осень от прошлого владельца тела.
        /// </summary>
        private void WriteBody(float k, Texture map, Texture baseMap)
        {
            var color = new Vector4(k, k, k, 1f); // линейно: SetColor перевёл бы HDR-множитель из гаммы
            for (int i = 0; i < _slotRenderers.Length; i++)
            {
                var renderer = _slotRenderers[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(_block, _slotIndices[i]);
                _block.SetVector(EmissionColorId, color);
                if (map != null) _block.SetTexture(EmissionMapId, map);
                if (baseMap != null) _block.SetTexture(BaseMapId, baseMap);
                renderer.SetPropertyBlock(_block, _slotIndices[i]);
            }
        }

        // ------------------------------------------------------------ накладки

        private void Grow(Overlay overlay, int level, float tick)
        {
            if (overlay.Buds.Length == 0) return;
            if (level < overlay.MinLevel)
            {
                if (overlay.Written == -1) return;
                for (int i = 0; i < overlay.Buds.Length; i++) overlay.Buds[i].localScale = Vector3.zero;
                overlay.Written = -1;
                return;
            }
            int change = BitTick(overlay.GrowBit);
            float seconds = overlay.Shape == ShapeSwell ? BerrySeconds : FlowerSeconds;
            if (overlay.Written == 1 && (change == None || ThicketMasterPhaseRules.Bloom(tick, change, MaxDelay(overlay), seconds) >= 1f)) return;
            bool done = true;
            for (int i = 0; i < overlay.Buds.Length; i++)
            {
                float x = ThicketMasterPhaseRules.Bloom(tick, change, overlay.BudDelay[i], seconds);
                if (x < 1f) done = false;
                float s = overlay.Shape == ShapeSwell ? ThicketMasterPhaseRules.Swell(x) : RootSnarerCombatView.Rise(x);
                overlay.Buds[i].localScale = overlay.BudScale[i] * Mathf.Max(0f, s);
            }
            overlay.Written = done ? 1 : 0;
        }

        private static float MaxDelay(Overlay overlay)
        {
            float max = 0f;
            for (int i = 0; i < overlay.BudDelay.Length; i++) max = Mathf.Max(max, overlay.BudDelay[i]);
            return max;
        }

        private static void StartBurst(Overlay overlay, int tick)
        {
            var fx = overlay.Burst;
            if (fx == null) return;
            overlay.BurstTick = tick;
            fx.Tick = tick;
            for (int k = 0; k < fx.Particles.Length; k++)
            {
                var ps = fx.Particles[k];
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.randomSeed = fx.Seeds[k] + (uint)tick * 7919u;
                fx.Delays[k] = 0f;
                fx.Done[k] = -1f;
            }
        }

        private static void StopBurst(Overlay overlay)
        {
            overlay.BurstTick = None;
            var fx = overlay.Burst;
            if (fx == null) return;
            for (int k = 0; k < fx.Particles.Length; k++)
            {
                if (fx.Done[k] >= 0f || fx.Particles[k].particleCount > 0)
                    fx.Particles[k].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                fx.Done[k] = -1f;
            }
        }

        private static void StepBurst(Overlay overlay, float tick)
        {
            if (overlay.BurstTick == None || overlay.Burst == null) return;
            float age = (tick - overlay.BurstTick) / Simulation.TicksPerSecond;
            if (age < 0f || age > BurstLife) { StopBurst(overlay); return; }
            RootSnarerCombatView.StepParticles(overlay.Burst, age);
        }

        /// <summary>
        /// Петли (лепестки кроны, угли, розовый свет): шаг по тикам Sim (пауза держит), эмиссия —
        /// только на своих уровнях, живым и над землёй; rate — множитель частоты (ярость).
        /// </summary>
        private static void StepLoops(Overlay overlay, bool on, float rate, float dt)
        {
            var loops = overlay.Loops;
            if (loops.Length == 0) return;
            for (int i = 0; i < loops.Length; i++)
            {
                var emission = loops[i].emission;
                if (emission.enabled != on) emission.enabled = on;
                float want = overlay.LoopRate[i] * rate;
                if (on && emission.rateOverTimeMultiplier != want) emission.rateOverTimeMultiplier = want;
            }
            if (!on && !overlay.LoopStarted) return;
            bool first = !overlay.LoopStarted;
            overlay.LoopStarted = true;
            for (int i = 0; i < loops.Length; i++)
            {
                if (first) loops[i].Simulate(0f, true, true, false);
                if (dt > 0f) loops[i].Simulate(dt, true, false, false);
            }
        }

        /// <summary>Ореол глаз: частица на якорь, цвет — по яркости рун и проявлению с рёва 66.</summary>
        private void StepGlow(Overlay overlay, int level, float tick, float glow)
        {
            var ps = overlay.Glow;
            if (ps == null || overlay.Eyes.Length == 0) return;
            float appear = level >= 2 ? ThicketMasterPhaseRules.EyeAppear(tick, BitTick(Simulation.ThicketRoar66Bit)) : 0f;
            float alpha = Mathf.Clamp01(EyeAlpha * appear * Mathf.Min(1.4f, glow / ThicketMasterPhaseRules.Phase2Glow));
            int count = ps.particleCount;
            if (alpha <= 0f && count == 0) return;
            if (count < overlay.Eyes.Length)
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var emit = new ParticleSystem.EmitParams
                {
                    startLifetime = 1e6f, startSize = overlay.GlowSize, velocity = Vector3.zero,
                    startColor = new Color32(0, 0, 0, 0), applyShapeToPosition = false,
                };
                for (int i = 0; i < overlay.Eyes.Length; i++)
                {
                    emit.position = overlay.Eyes[i];
                    ps.Emit(emit, 1);
                }
                ps.Pause(false);
            }
            count = ps.GetParticles(_eyeParticles);
            var tint = level >= 3 ? EyeColorBloom : EyeColor;
            var color = new Color(tint.r, tint.g, tint.b, alpha);
            for (int i = 0; i < count; i++)
            {
                _eyeParticles[i].startColor = color;
                _eyeParticles[i].remainingLifetime = 1e6f;
                if (i < overlay.Eyes.Length) _eyeParticles[i].position = overlay.Eyes[i];
            }
            ps.SetParticles(_eyeParticles, count);
        }

        /// <summary>Всё свёрнуто: ягоды и цветы масштаба 0, всплески и лепестки сняты, глаза погашены.</summary>
        private void Collapse()
        {
            for (int i = 0; i < _overlays.Length; i++)
            {
                var overlay = _overlays[i];
                overlay.Written = 2; // перезаписать рост в следующем кадре
                for (int b = 0; b < overlay.Buds.Length; b++)
                    if (overlay.Buds[b] != null) overlay.Buds[b].localScale = Vector3.zero;
                StopBurst(overlay);
                for (int l = 0; l < overlay.Loops.Length; l++)
                    if (overlay.Loops[l] != null) overlay.Loops[l].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                overlay.LoopStarted = false;
                if (overlay.Glow != null) overlay.Glow.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void OnDisable()
        {
            // Возврат в пул: следующий владелец получает тёмные руны, зелёную листву и голый куст.
            WriteBody(0f, _mapF2, _colorF1);
            _boundSim = null;
            _boundEntity = -1;
            _boundGeneration = -1;
            _bits = 0;
            _deathTick = None;
            Level = 0;
            Glow = 0f;
            Collapse();
        }
    }
}
