using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class PelagOrdnanceView
    {
        private sealed class FireEffect
        {
            public Transform Root;
            public ParticleSystem[] Systems;
            public float Age;
            public bool Active;
        }
        private readonly FireEffect[] _explosions = new FireEffect[12];
        private readonly FireEffect[] _oilFire = new FireEffect[40];
        private readonly Mesh[] _oilMeshes = new Mesh[8];
        private const int OilGrid = 13;
        private readonly Vector3[][] _oilVertices = new Vector3[8][];
        private readonly Vector2[] _oilUv = new Vector2[OilGrid * OilGrid];
        private readonly int[] _oilTriangles = new int[(OilGrid - 1) * (OilGrid - 1) * 6];
        private readonly Vector3[] _oilCenters = new Vector3[8];
        private readonly bool[] _oilActive = new bool[8];
        private float _fxTick = -1;
        private int _explosionCursor, _fireSeed;

        private void PrepareAuthoredFire()
        {
            var burst = Resources.Load<GameObject>("VFX/Pelag/Ordnance/BottleExplosion");
            var fire = Resources.Load<GameObject>("VFX/Pelag/Ordnance/BurningOil");
            if (burst == null || fire == null)
            {
                Debug.LogError("[Pelag Ordnance] Missing BottleExplosion or BurningOil prefab.");
                return;
            }
            for (int i = 0; i < _explosions.Length; i++) _explosions[i] = MakeFire(burst);
            for (int i = 0; i < _oilFire.Length; i++) _oilFire[i] = MakeFire(fire);
            int triangle = 0;
            for (int y = 0; y < OilGrid; y++) for (int x = 0; x < OilGrid; x++)
            {
                int v = y * OilGrid + x; _oilUv[v] = new Vector2(x / (float)(OilGrid-1), y / (float)(OilGrid-1));
                if (x == OilGrid-1 || y == OilGrid-1) continue;
                _oilTriangles[triangle++] = v; _oilTriangles[triangle++] = v+OilGrid; _oilTriangles[triangle++] = v+1;
                _oilTriangles[triangle++] = v+1; _oilTriangles[triangle++] = v+OilGrid; _oilTriangles[triangle++] = v+OilGrid+1;
            }
            for (int i = 0; i < _pools.Length; i++)
            {
                _oilMeshes[i] = new Mesh { name = "Burning oil ground " + i };
                _oilVertices[i] = new Vector3[OilGrid*OilGrid];
                _oilMeshes[i].vertices = _oilVertices[i]; _oilMeshes[i].uv = _oilUv; _oilMeshes[i].triangles = _oilTriangles;
                _pools[i].GetComponent<MeshFilter>().sharedMesh = _oilMeshes[i];
            }
        }
        private FireEffect MakeFire(GameObject prefab)
        {
            if (prefab == null) return null;
            var root = Instantiate(prefab, _worldFireRoot).transform;
            root.gameObject.SetActive(false);
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems) ps.randomSeed = (uint)(5731 + ++_fireSeed * 3719);
            return new FireEffect { Root = root, Systems = systems };
        }
        private static void StartFire(FireEffect fx, Vector3 position, float scale, float yaw, float warmup = 0)
        {
            if (fx == null) return;
            fx.Root.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            fx.Root.localScale = Vector3.one * scale / fx.Root.parent.lossyScale.x;
            fx.Root.gameObject.SetActive(true); fx.Age = 0; fx.Active = true;
            foreach (var ps in fx.Systems)
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(false);
                // Ненулевой первый шаг выпускает authored burst на времени 0.
                ps.Simulate(.001f + warmup, false, true, false);
                ps.Pause(false);
            }
        }
        private void BurstAuthoredFire(Vector3 position, float radius, int tick)
            => StartFire(_explosions[_explosionCursor++ % _explosions.Length], position + Vector3.up*.12f, radius * .95f, tick * 137.5f);

        private void UpdateAuthoredFire(Simulation sim, float tick)
        {
            float dt = _fxTick < 0 ? 0 : Mathf.Clamp((tick - _fxTick) / Simulation.TicksPerSecond, 0, .15f);
            _fxTick = tick;
            foreach (var fx in _explosions) AdvanceFire(fx, dt, 1.15f);
            for (int i = 0; i < _pools.Length; i++)
            {
                bool active = sim.FirePoolActive(i);
                var center = active ? Ground(sim.FirePoolAt(i)) : Vector3.zero;
                if (active && (!_oilActive[i] || (center - _oilCenters[i]).sqrMagnitude > .001f))
                {
                    float radius = sim.FirePoolRadius(i).ToFloat();
                    FitOilToGround(i, center, radius);
                    for (int j = 0; j < 5; j++)
                    {
                        float angle = j * 2.39996f + i * .73f;
                        float distance = j == 0 ? 0 : radius * .61f;
                        Vector3 point = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
                        if (_layout != null) point.y = _layout.WeaponGroundHeight(point.x, point.z) + .035f;
                        StartFire(_oilFire[i * 5 + j], point, radius * (.31f + .025f * (j % 3)), angle * Mathf.Rad2Deg, .2f + j * .137f);
                    }
                    _oilCenters[i] = center;
                }
                for (int j = 0; j < 5; j++)
                {
                    var fx = _oilFire[i * 5 + j];
                    if (!active) StopFire(fx); else AdvanceFire(fx, dt, float.PositiveInfinity);
                }
                _oilActive[i] = active;
            }
        }
        private static void AdvanceFire(FireEffect fx, float dt, float lifetime)
        {
            if (fx == null || !fx.Active) return;
            fx.Age += dt;
            if (fx.Age > lifetime) { StopFire(fx); return; }
            if (dt > 0)
                foreach (var ps in fx.Systems) ps.Simulate(dt, false, false, false);
            if (CaptureRig.LiveSkill && fx.Age >= .04f && fx.Age - dt < .04f && lifetime < 2f)
            {
                int particles = 0; foreach (var ps in fx.Systems) particles += ps.particleCount;
                Debug.Log("[ordnance-burst] particles=" + particles + " systems=" + fx.Systems.Length);
            }
        }
        private static void StopFire(FireEffect fx)
        {
            if (fx == null || !fx.Active) return;
            foreach (var ps in fx.Systems) if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (fx.Root != null) fx.Root.gameObject.SetActive(false); fx.Active = false;
        }
        private void FitOilToGround(int index, Vector3 center, float radius)
        {
            // Пятно повторяет рельеф: плоская карточка наполовину исчезала в траве и склонах.
            var vertices = _oilVertices[index];
            var root = _pools[index].transform;
            root.SetPositionAndRotation(center, Quaternion.identity); root.localScale = Vector3.one;
            for (int v = 0; v < vertices.Length; v++)
            {
                Vector3 p = center + new Vector3(_oilUv[v].x * 2 - 1, 0, _oilUv[v].y * 2 - 1) * radius;
                if (_layout != null) p.y = _layout.WeaponGroundHeight(p.x, p.z) + .055f;
                vertices[v] = root.InverseTransformPoint(p);
            }
            var mesh = _oilMeshes[index]; mesh.vertices = vertices; mesh.RecalculateBounds();
        }
        private void ClearAuthoredFire()
        {
            _fxTick = -1;
            foreach (var fx in _explosions) StopFire(fx);
            foreach (var fx in _oilFire) StopFire(fx);
            for (int i = 0; i < _oilActive.Length; i++) _oilActive[i] = false;
        }
    }
}
