using System;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — ВИД БОЯ: СКВОЗЬ БОССА ВИДНО ГЕРОЯ (владелец 02.10: «прозрачность должна быть,
    /// чтоб было видно»). Правило — <see cref="ThicketMasterSeeThroughRules"/>, сетка — шейдеры
    /// Razlom/Boss See-Through Lit / Simple Lit (Resources/Shaders/RazlomSeeThrough.hlsl).
    ///
    /// Каждый кадр: лучи от точек героя к камере против рамки тела (сетка тела в покое, в осях
    /// узла тела — едет, поворачивается и уходит в землю вместе с телом) → доля 0…1 с появлением за 0,15 с по тикам Sim →
    /// блок свойств на слоты тела и накладок фаз с шейдером прозрачности. Материалы не
    /// копируются. Блок вида тела ArenaView (порядок 0) каждый кадр заменяет блоки слотов
    /// целиком, поэтому вид дописывает свои значения поверх него (порядок 660), а одежда фаз
    /// (670) сливает эмиссию поверх обоих. Доля ноль — вид ничего не пишет: ArenaView сам
    /// снимает прошлое значение на следующем кадре (и один кадр нулей — на всякий случай).
    /// Маска контура (Texture Toon, UnitOutlineMask) остаётся целой намеренно: композит контура
    /// красит каждый пиксель вне маски рядом с маской, и сетка в маске залила бы дыру чернилами.
    /// </summary>
    public sealed partial class ThicketMasterCombatView
    {
        private static readonly int SeeThroughCenterId = Shader.PropertyToID("_RazlomSeeThroughCenter");
        private static readonly int SeeThroughRadiusId = Shader.PropertyToID("_RazlomSeeThroughRadius");
        private static readonly int SeeThroughDepthId = Shader.PropertyToID("_RazlomSeeThroughDepth");
        private static readonly int SeeThroughAmountId = Shader.PropertyToID("_RazlomSeeThroughAmount");

        /// <summary>Рамка тела, если у тела нет сетки скина: рост 4,14, след ≈ 4,9 × 6,5 м вокруг корня.</summary>
        private static readonly Vector3 FallbackBoxCentre = new Vector3(0f, 2.07f, 0f);
        private static readonly Vector3 FallbackBoxHalf = new Vector3(2.45f, 2.07f, 3.25f);

        private static bool _warnedSeeThrough;

        private Transform _seeBody;
        private Transform _seeFrame;
        private Simulation _seeSim;
        private Renderer[] _seeRenderers = Array.Empty<Renderer>();
        private int[] _seeSlots = Array.Empty<int>();
        private Vector3 _seeBoxMin, _seeBoxMax;
        private MaterialPropertyBlock _seeBlock;
        private Camera _seeCamera;
        private Vector3 _seeCentre;
        private float _seeFade;
        private float _seeTick = float.NaN;
        private bool _seeWritten;

        /// <summary>Прозрачность этого кадра, 0…1 (до плотности сетки). Для съёмки и стендов.</summary>
        public float SeeThrough => _seeFade;

        /// <summary>Доля прозрачности этого кадра: цель — герой за телом от камеры, шаг — по тикам Sim.</summary>
        private float SeeThroughFade(Simulation sim)
        {
            if (_bossBody != _seeBody || !ReferenceEquals(sim, _seeSim)) CollectSeeThrough(sim);
            float tick = sim.Tick - 1 + _driver.Alpha;
            float dt = float.IsNaN(_seeTick) ? 0f : Mathf.Clamp(tick - _seeTick, 0f, 8f) / Simulation.TicksPerSecond;
            _seeTick = tick;
            bool covered = _seeRenderers.Length > 0 && HeroBehindBoss(sim);
            _seeFade = ThicketMasterSeeThroughRules.Step(_seeFade, covered ? 1f : 0f, dt);
            return _seeFade;
        }

        /// <summary>Тело стоит между героем и камерой: хоть один луч от точек героя к камере проходит рамку тела.</summary>
        private bool HeroBehindBoss(Simulation sim)
        {
            if (Simulation.PlayerId >= sim.Entities.Count) return false;
            // Центр пишется и на сходе в ноль: круг не прыгает, пока сетка гаснет.
            Vector3 hero = _driver.GetRenderPosition(Simulation.PlayerId);
            _seeCentre = hero + Vector3.up * ThicketMasterSeeThroughRules.HeroCentreHeight;
            if (_seeFrame == null || BossBurrowed || !sim.Entities.Alive[Simulation.PlayerId]) return false;
            Camera camera = SeeThroughCamera();
            if (camera == null) return false;

            Transform view = camera.transform;
            Matrix4x4 toBody = _seeFrame.worldToLocalMatrix;
            bool orthographic = camera.orthographic;
            float[] heights = ThicketMasterSeeThroughRules.SampleHeights, sides = ThicketMasterSeeThroughRules.SampleSides;
            for (int k = 0; k < heights.Length; k++)
            {
                Vector3 point = hero + Vector3.up * heights[k] + view.right * sides[k];
                // Орто: луч вдоль взгляда до бесконечности; перспектива — отрезок до камеры.
                Vector3 toCamera = orthographic ? -view.forward : view.position - point;
                Vector3 o = toBody.MultiplyPoint3x4(point), d = toBody.MultiplyVector(toCamera);
                if (ThicketMasterSeeThroughRules.RayHitsBox(o.x, o.y, o.z, d.x, d.y, d.z,
                        _seeBoxMin.x, _seeBoxMin.y, _seeBoxMin.z, _seeBoxMax.x, _seeBoxMax.y, _seeBoxMax.z,
                        orthographic ? float.PositiveInfinity : 1f)) return true;
            }
            return false;
        }

        private Camera SeeThroughCamera()
        {
            if (_seeCamera == null || !_seeCamera.isActiveAndEnabled) _seeCamera = Camera.main;
            return _seeCamera;
        }

        /// <summary>
        /// Привязка к телу: слоты с шейдером прозрачности (тело, ягоды и цветы накладок фаз — они
        /// дети костей тела) и рамка тела (сетка скина в покое) в осях узла тела. Один раз на тело пула.
        /// </summary>
        private void CollectSeeThrough(Simulation sim)
        {
            _seeBody = _bossBody;
            // Рамка — в осях узла тела (он уходит в землю в нырке и при смерти): сдвиг вниз в миг
            // привязки не исказит рамку, и опустившееся тело не закрывает героя.
            _seeFrame = _bossView != null ? _bossView.Body : _bossBody;
            _seeSim = sim;
            _seeFade = 0f;
            _seeTick = float.NaN;
            _seeWritten = false;
            _seeRenderers = Array.Empty<Renderer>();
            _seeSlots = Array.Empty<int>();
            if (_bossBody == null) return;
            if (_seeBlock == null) _seeBlock = new MaterialPropertyBlock();

            var renderers = new List<Renderer>();
            var slots = new List<int>();
            Matrix4x4 toBody = _seeFrame.worldToLocalMatrix;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (Renderer renderer in _bossBody.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (IsSeeThrough(materials[i]))
                    {
                        renderers.Add(renderer);
                        slots.Add(i);
                    }
                if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                    Encapsulate(toBody * skin.transform.localToWorldMatrix, skin.sharedMesh.bounds, ref min, ref max);
            }
            // Метры мира → оси узла тела (они растянуты ростом сборщика).
            float scale = Mathf.Max(1e-4f, Mathf.Abs(_seeFrame.lossyScale.x));
            if (min.x > max.x)
            {
                min = (FallbackBoxCentre - FallbackBoxHalf) / scale;
                max = (FallbackBoxCentre + FallbackBoxHalf) / scale;
            }
            Vector3 margin = Vector3.one * (ThicketMasterSeeThroughRules.BoxMarginMetres / scale);
            _seeBoxMin = min - margin;
            _seeBoxMax = max + margin;
            _seeRenderers = renderers.ToArray();
            _seeSlots = slots.ToArray();

            // Серая заглушка (нет вида тела) прозрачности не ждёт; настоящее тело без шейдера — да.
            if (_seeRenderers.Length == 0 && _bossView != null && !_warnedSeeThrough)
            {
                _warnedSeeThrough = true;
                Debug.LogWarning("[thicketmaster] У тела босса нет материала «" + ThicketMasterSeeThroughRules.LitShader
                                 + "» — сквозь него героя не видно. Собери «Разлом/Босс/Хозяин Чащи/Прозрачность: подключить».");
            }
        }

        private static bool IsSeeThrough(Material material)
        {
            if (material == null || material.shader == null) return false;
            string name = material.shader.name;
            return name == ThicketMasterSeeThroughRules.LitShader || name == ThicketMasterSeeThroughRules.SimpleLitShader;
        }

        private static void Encapsulate(Matrix4x4 toBody, Bounds bounds, ref Vector3 min, ref Vector3 max)
        {
            Vector3 c = bounds.center, e = bounds.extents;
            for (int corner = 0; corner < 8; corner++)
            {
                var local = new Vector3((corner & 1) == 0 ? -e.x : e.x, (corner & 2) == 0 ? -e.y : e.y, (corner & 4) == 0 ? -e.z : e.z);
                Vector3 p = toBody.MultiplyPoint3x4(c + local);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        /// <summary>
        /// Прозрачность в шейдер: центр (середина героя в мире), радиус, глубина и доля — в блок
        /// каждого слота поверх блока ArenaView этого кадра. Невидимые рендеры (свёрнутые бутоны)
        /// пропускаются; сход в ноль пишется всем один раз.
        /// </summary>
        partial void ApplyCrownDither(int boss, ThicketMasterAnimatorView body, float amount)
        {
            float shown = ThicketMasterSeeThroughRules.Shown(amount);
            if (shown <= 0f && !_seeWritten) return;
            for (int i = 0; i < _seeRenderers.Length; i++)
            {
                Renderer renderer = _seeRenderers[i];
                if (renderer == null || (shown > 0f && !renderer.isVisible)) continue;
                int slot = _seeSlots[i];
                renderer.GetPropertyBlock(_seeBlock, slot);
                _seeBlock.SetVector(SeeThroughCenterId, _seeCentre);
                _seeBlock.SetFloat(SeeThroughRadiusId, ThicketMasterSeeThroughRules.RadiusMetres);
                _seeBlock.SetFloat(SeeThroughDepthId, ThicketMasterSeeThroughRules.DepthRampMetres);
                _seeBlock.SetFloat(SeeThroughAmountId, shown);
                renderer.SetPropertyBlock(_seeBlock, slot);
            }
            _seeWritten = shown > 0f;
        }
    }
}
