using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    // Обрыв с видом вдаль (владелец, 12 октября: «красивый бэкграунд»): у части арен лес с одной стороны
    // кончается открытым уступом, земля уходит вниз, а внизу — долина в дымке. Камера ортографическая,
    // перспективы нет, поэтому даль читается не размером, а содержанием: долина нарисована шейдером с
    // кронами вчетверо мельче настоящих, рекой и дымкой, густеющей от края (Razlom/Valley Vista).
    // Обрыв — сбоку от оси входа и выхода (тропы в него не упираются) и с той стороны, что дальше от
    // камеры: стена смотрит от неё и не видна, виден край и долина за ним. Всё — только вид: пол
    // симуляции не меняется, за край не ходят.
    public sealed partial class LayoutView
    {
        public const string VistaCaptureFlag = "-capture-vista";
        private const float VistaDrop = 12f, VistaMargin = 5f;

        private bool _vista;
        private Vector2 _vistaNormal, _vistaTangent;
        private float _vistaLine, _vistaFloor, _vistaPhase;
        private GameObject _valley;
        private Mesh _valleyMesh;
        private Material _valleyMaterial;
        private readonly List<Vector3> _vistaFalls = new List<Vector3>();
        private ParticleSystem _fallSpray;
        private Material _fallSprayMaterial;

        /// <summary>Сколько метров от точки до края обрыва: меньше нуля — на плато, больше — за краем.</summary>
        private float VistaOffset(float x, float z)
        {
            if (!_vista) return -1000f;
            float along = x * _vistaTangent.x + z * _vistaTangent.y;
            // Край — не прямая: два плавных изгиба, свои у каждой арены.
            return x * _vistaNormal.x + z * _vistaNormal.y - _vistaLine - VistaBend(along);
        }

        private float VistaBend(float along)
            => 2.2f * Mathf.Sin(along * .09f + _vistaPhase) + 1.1f * Mathf.Sin(along * .23f + _vistaPhase * 1.7f);

        // До рельефа: высоты, лес и декор этой карты уже считаются с обрывом.
        private void SetupVista(LayoutMap map)
        {
            _vista = false;
            if (_valley != null) _valley.SetActive(false);
            if (!Application.isPlaying || !map.IsArena || map.GladeCount == 0 || IsBossClearing(map)) return;
            var rng = DecorRandom(0, 1319);
            bool forced = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), VistaCaptureFlag) >= 0;
            if (rng.NextDouble() > .5 && !forced) return;
            _vistaPhase = (float)rng.NextDouble() * 6.28f;

            // Ось арены — от входа к первой поляне, по ближайшей оси карты; обрыв поперёк неё.
            var first = TrailPoint(map.GetGlade(0).Center) - TrailPoint(map.EntryPoint);
            _vistaTangent = Mathf.Abs(first.x) > Mathf.Abs(first.y) ? new Vector2(Mathf.Sign(first.x), 0) : new Vector2(0, Mathf.Sign(first.y));
            _vistaNormal = new Vector2(-_vistaTangent.y, _vistaTangent.x);
            if (Vector2.Dot(_vistaNormal, _hillAxis) < 0) _vistaNormal = -_vistaNormal;

            float cell = LayoutMap.CellSize.ToFloat();
            float line = float.MinValue, minAlong = float.MaxValue, maxAlong = float.MinValue;
            for (int m = 0; m < map.PlacedCount; m++)
            {
                var room = map.GetPlaced(m);
                for (int corner = 0; corner < 4; corner++)
                {
                    var point = new Vector2((room.OriginX + (corner & 1) * room.Width) * cell, (room.OriginY + (corner >> 1) * room.Height) * cell);
                    line = Mathf.Max(line, Vector2.Dot(point, _vistaNormal) + VistaMargin);
                    float along = Vector2.Dot(point, _vistaTangent);
                    minAlong = Mathf.Min(minAlong, along); maxAlong = Mathf.Max(maxAlong, along);
                }
            }
            _vistaLine = line;
            float level = float.MaxValue;
            for (int g = 0; g < map.GladeCount; g++)
            {
                var center = TrailPoint(map.GetGlade(g).Center);
                level = Mathf.Min(level, FloorLevel(center.x, center.y));
            }
            _vistaFloor = level - VistaDrop;
            _vista = true;
            // Река арены доходит до края и срывается водопадом (LayoutView.Rivers обрезает ленту): пруд на её
            // конце за краем не нужен. Озёра карты лежат внутри поляны и остаются.
            for (int i = _ponds.Count - 1; i >= map.WaterCount; i--)
                if (VistaOffset(_ponds[i].x, _ponds[i].y) > -(Mathf.Max(_ponds[i].z, _ponds[i].w) + 2)) _ponds.RemoveAt(i);
            _vistaFalls.Clear();

            // Нижняя земля сдвинута на экране к краю: первая её полоса скрыта за плато.
            var camera = Camera.main;
            float pitch = camera != null ? camera.transform.eulerAngles.x * Mathf.Deg2Rad : .84f;
            float hidden = VistaDrop / Mathf.Max(.3f, Mathf.Tan(pitch)) * Mathf.Max(.2f, Vector2.Dot(_vistaNormal, _hillAxis));
            BuildValley(minAlong - 150, maxAlong + 150, hidden);
            Debug.Log($"[Луга] обрыв: край {_vistaLine:0.0} по ({_vistaNormal.x:0},{_vistaNormal.y:0}), долина {_vistaFloor:0.0}, скрыто {hidden:0.0} м");
        }

        private void BuildValley(float fromAlong, float toAlong, float hidden)
        {
            if (_valley == null)
            {
                var shader = Resources.Load<Shader>("Shaders/RazlomValleyVista");
                if (shader == null) { _vista = false; return; }
                _valleyMaterial = new Material(shader) { name = "Долина под обрывом", hideFlags = HideFlags.DontSave };
                _valleyMesh = new Mesh { name = "Долина под обрывом" };
                _valley = new GameObject("Долина под обрывом");
                _valley.transform.SetParent(_groundFill.transform.parent, false);
                _valley.AddComponent<MeshFilter>().sharedMesh = _valleyMesh;
                var renderer = _valley.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _valleyMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Vector3 Corner(float along, float across)
            {
                var point = _vistaTangent * along + _vistaNormal * (_vistaLine + across);
                return new Vector3(point.x, _vistaFloor, point.y);
            }
            _valleyMesh.Clear();
            _valleyMesh.vertices = new[] { Corner(fromAlong, -10), Corner(toAlong, -10), Corner(toAlong, 160), Corner(fromAlong, 160) };
            _valleyMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _valleyMesh.RecalculateBounds();
            _valleyMaterial.SetVector("_Edge", new Vector4(_vistaNormal.x, _vistaNormal.y, _vistaLine, hidden));
            // Озеро долины — напротив арены, со сдвигом вдоль обрыва, своим у каждой карты.
            float lake = (fromAlong + toAlong) * .5f + (_vistaPhase / 6.28f - .5f) * 24;
            _valleyMaterial.SetVector("_Along", new Vector4(_vistaTangent.x, _vistaTangent.y, _vistaPhase, lake));
            _birdLane = new Vector2((fromAlong + toAlong) * .5f, Mathf.Max(30, (toAlong - fromAlong) * .5f - 150));
            _valley.SetActive(true);
        }

        // Кромка обрыва: камни и низкая зелень группами, между ними чистый край с видом. Высоких деревьев
        // у края нет (TouchesLandmark, ScatterForestSteps): крона закрыла бы долину.
        private void PlaceVistaRim(LayoutMap map)
        {
            if (!_vista) return;
            var rocks = new List<int>(); var green = new List<int>();
            foreach (string name in new[] { "CreatingRock", "ArenaCreatingRockA", "ArenaCreatingRockB" })
            {
                int variant = VariantNamed(name);
                if (variant >= 0) rocks.Add(variant);
            }
            int fern = VariantNamed(FernPrefab);
            if (fern >= 0) green.Add(fern);
            for (int i = 0; i < _style.DecorVariants.Length; i++)
                if (_style.DecorVariants[i].Kind == DecorKind.Bush && _style.DecorVariants[i].Weight > 0 && _style.DecorVariants[i].Prefab != null) green.Add(i);
            var rng = DecorRandom(0, 1321);
            float cell = LayoutMap.CellSize.ToFloat(), from = float.MaxValue, to = float.MinValue;
            for (int m = 0; m < map.PlacedCount; m++)
            {
                var room = map.GetPlaced(m);
                float a = Vector2.Dot(new Vector2(room.OriginX * cell, room.OriginY * cell), _vistaTangent);
                float b = Vector2.Dot(new Vector2((room.OriginX + room.Width) * cell, (room.OriginY + room.Height) * cell), _vistaTangent);
                from = Mathf.Min(from, Mathf.Min(a, b)); to = Mathf.Max(to, Mathf.Max(a, b));
            }
            for (float along = from - 28; along < to + 28; along += 1.7f)
            {
                float clump = Mathf.PerlinNoise(along * .09f + _vistaPhase * 3, 4.7f);
                if (clump < .42f || rng.NextDouble() > Mathf.InverseLerp(.42f, .7f, clump) + .2f) continue;
                // Точка на самой кромке: смещение по нормали подбирается под изгиб края.
                var point = _vistaTangent * (along + ((float)rng.NextDouble() - .5f) * 1.4f) + _vistaNormal * _vistaLine;
                point -= _vistaNormal * (VistaOffset(point.x, point.y) + .2f + (float)rng.NextDouble() * 1.1f);
                if (rocks.Count > 0 && rng.NextDouble() < .6)
                {
                    SpawnDecor(rocks[rng.Next(rocks.Count)], point.x, point.y, rng);
                    var rock = _decor[_decorCount - 1];
                    rock.localScale *= 1.1f + (float)rng.NextDouble() * 1.3f;
                    rock.position = new Vector3(point.x, BackgroundHeight(map, point.x, point.y) - .35f - (float)rng.NextDouble() * .3f, point.y);
                }
                if (green.Count == 0) continue;
                for (int item = rng.Next(1, 4); item > 0; item--)
                {
                    int detail = green[rng.Next(green.Count)];
                    var at = point - _vistaNormal * (1.2f + (float)rng.NextDouble() * 2.6f) + _vistaTangent * ((float)rng.NextDouble() - .5f) * 3;
                    TryForestDetail(map, detail, at, rng, .6f + (float)rng.NextDouble() * .5f, .5f, detail == fern ? .45f : 1f);
                }
            }
        }

        // Водопад: сама струя за краем с камеры не видна (стена смотрит от неё), поэтому падение читается
        // по водяной пыли, которая поднимается над кромкой там, где обрывается река.
        private void BuildFallSpray()
        {
            if (_fallSpray != null) _fallSpray.gameObject.SetActive(false);
            if (!_vista || _vistaFalls.Count == 0) return;
            if (_fallSpray == null)
            {
                var shader = Resources.Load<Shader>("Shaders/CampBiomeParticles");
                if (shader == null) return;
                _fallSprayMaterial = new Material(shader) { name = "Пыль водопада", hideFlags = HideFlags.DontSave };
                var host = new GameObject("Пыль водопада");
                host.transform.SetParent(transform, false);
                _fallSpray = host.AddComponent<ParticleSystem>();
                _fallSpray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = _fallSpray.main;
                main.loop = true; main.prewarm = true;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(.5f, 1.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(.8f, 1.9f);
                main.startColor = new Color(.93f, .97f, .95f, .13f);
                main.maxParticles = 220;
                var emission = _fallSpray.emission;
                emission.rateOverTime = 60f;
                var shape = _fallSpray.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(3.4f, .6f, 2.6f);
                shape.rotation = new Vector3(-90, 0, 0);
                var grow = _fallSpray.sizeOverLifetime;
                grow.enabled = true; grow.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .5f, 1, 1.5f));
                var fade = _fallSpray.colorOverLifetime;
                fade.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .25f), new GradientAlphaKey(0, 1) });
                fade.color = gradient;
                var renderer = host.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = _fallSprayMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // Чуть за кромкой и ниже неё: пыль встаёт из-за края, а не лежит на плато.
            var fall = _vistaFalls[0];
            _fallSpray.transform.position = fall + new Vector3(_vistaNormal.x, 0, _vistaNormal.y) * 1.6f + Vector3.down * 1.5f;
            _fallSpray.gameObject.SetActive(true);
            _fallSpray.Clear(); _fallSpray.Play();
        }

        /// <summary>Точка пола у обрыва — для съёмки (-capture-vista): край поляны со стороны долины.</summary>
        public bool VistaViewpoint(out FixVec2 point)
        {
            point = default;
            if (!_vista || _shownMap == null) return false;
            var glade = _shownMap.GetGlade(_shownMap.GladeCount / 2);
            var step = new FixVec2(Fix64.FromDouble(_vistaNormal.x), Fix64.FromDouble(_vistaNormal.y));
            point = glade.Center;
            for (int i = 0; i < 60 && _shownMap.IsWalkable(point + step, Fix64.One); i++) point += step;
            return true;
        }

        // Птицы над долиной: небольшая стая летит вдоль обрыва, машет крыльями и уходит за кадр; через
        // паузу — новый пролёт на другой высоте и удалении. Только вид, без теней и коллизий.
        private const int BirdCount = 6;
        private Transform _birds;
        private readonly Transform[] _birdWings = new Transform[BirdCount * 2];
        private readonly Vector3[] _birdOffsets = new Vector3[BirdCount];
        private Mesh _wingMesh;
        private Material _birdMaterial;
        private Vector2 _birdLane;
        private float _birdAlong, _birdAcross, _birdHeight, _birdSpeed, _birdPause;
        private System.Random _birdRandom;

        private void UpdateVistaBirds()
        {
            if (!_vista || !Application.isPlaying)
            {
                if (_birds != null) _birds.gameObject.SetActive(false);
                return;
            }
            if (_birds == null) BuildBirds();
            if (!_birds.gameObject.activeSelf)
            {
                _birds.gameObject.SetActive(true);
                _birdRandom = new System.Random(unchecked((int)_layoutSeed * 31 + 7));
                _birdPause = 0; _birdSpeed = 0;
            }
            float reach = _birdLane.y + 32;
            if (_birdSpeed == 0 || Mathf.Abs(_birdAlong - _birdLane.x) > reach)
            {
                // Новый пролёт: с любой стороны, над ближней полосой долины.
                _birdPause -= Time.deltaTime;
                if (_birdSpeed != 0 && _birdPause > 0) return;
                float side = _birdRandom.NextDouble() < .5 ? -1 : 1;
                _birdSpeed = -side * (5.5f + (float)_birdRandom.NextDouble() * 2.5f);
                _birdAlong = _birdLane.x + side * (reach - .5f);
                _birdAcross = 5 + (float)_birdRandom.NextDouble() * 11;
                _birdHeight = 3 + (float)_birdRandom.NextDouble() * 4;
                _birdPause = 6 + (float)_birdRandom.NextDouble() * 10;
                for (int b = 0; b < BirdCount; b++)
                    _birdOffsets[b] = new Vector3(-b * 1.1f - (float)_birdRandom.NextDouble() * .8f,
                        ((float)_birdRandom.NextDouble() - .5f) * 1.2f, (b % 2 == 0 ? 1 : -1) * (b + 1) * .5f + ((float)_birdRandom.NextDouble() - .5f) * .6f);
            }
            _birdAlong += _birdSpeed * Time.deltaTime;
            var forward = new Vector3(_vistaTangent.x, 0, _vistaTangent.y) * Mathf.Sign(_birdSpeed);
            var right = new Vector3(_vistaNormal.x, 0, _vistaNormal.y);
            float drift = Mathf.Sin(Time.time * .35f) * 1.5f;
            var lead = new Vector3(_vistaTangent.x, 0, _vistaTangent.y) * _birdAlong + right * (_vistaLine + _birdAcross + drift);
            lead.y = _vistaFloor + VistaDrop + _birdHeight;
            for (int b = 0; b < BirdCount; b++)
            {
                var bird = _birds.GetChild(b);
                var offset = _birdOffsets[b];
                bird.position = lead + forward * offset.x + Vector3.up * (offset.y + Mathf.Sin(Time.time * 1.3f + b * 1.7f) * .25f) + right * offset.z;
                bird.rotation = Quaternion.LookRotation(forward);
                bird.localScale = Vector3.one * 1.5f;
                // Взмах: крылья идут вверх и вниз, у каждой птицы свой такт.
                float flap = Mathf.Sin(Time.time * (7.5f + b * .6f) + b * 2.1f) * 38;
                _birdWings[b * 2].localRotation = Quaternion.Euler(0, 0, flap);
                _birdWings[b * 2 + 1].localRotation = Quaternion.Euler(0, 0, -flap);
            }
        }

        private void BuildBirds()
        {
            // Крыло двустороннее; птица — два крыла на общей оси.
            _wingMesh = new Mesh { name = "Крыло птицы" };
            _wingMesh.vertices = new[] { new Vector3(0, 0, .1f), new Vector3(0, 0, -.1f), new Vector3(.3f, 0, .12f), new Vector3(.3f, 0, -.02f), new Vector3(.62f, 0, -.2f) };
            // Стреловидное крыло из двух частей: силуэт стаи — «галочки», а не ромбы.
            _wingMesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 2, 4, 3, 0, 1, 2, 1, 3, 2, 2, 3, 4 };
            _wingMesh.RecalculateNormals(); _wingMesh.RecalculateBounds();
            _birdMaterial = ViewMaterials.CreateLit(new Color(.3f, .26f, .23f));
            _birds = new GameObject("Птицы над долиной").transform;
            _birds.SetParent(transform, false);
            for (int b = 0; b < BirdCount; b++)
            {
                var bird = new GameObject("Птица " + b).transform;
                bird.SetParent(_birds, false);
                for (int w = 0; w < 2; w++)
                {
                    var wing = new GameObject(w == 0 ? "Правое крыло" : "Левое крыло");
                    wing.transform.SetParent(bird, false);
                    wing.transform.localScale = new Vector3(w == 0 ? 1 : -1, 1, 1);
                    wing.AddComponent<MeshFilter>().sharedMesh = _wingMesh;
                    var renderer = wing.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = _birdMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _birdWings[b * 2 + w] = wing.transform;
                }
            }
            _birds.gameObject.SetActive(false);
        }

        private void DisposeVista()
        {
            _vista = false;
            if (_birds != null) DestroyOwned(_birds.gameObject);
            DestroyOwned(_wingMesh); DestroyOwned(_birdMaterial); _birds = null; _wingMesh = null; _birdMaterial = null;
            if (_fallSpray != null) DestroyOwned(_fallSpray.gameObject);
            DestroyOwned(_fallSprayMaterial); _fallSpray = null; _fallSprayMaterial = null;
            if (_valley != null) DestroyOwned(_valley);
            DestroyOwned(_valleyMesh); DestroyOwned(_valleyMaterial);
            _valley = null; _valleyMesh = null; _valleyMaterial = null;
        }
    }
}
