using System.Collections;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class LayoutView
    {
        // Трава по всей карте рисуется инстансингом: тысяча пучков без игровых объектов и пулов.
        private const string GrassFieldPrefab = "CreatingGrassField";
        private readonly List<Matrix4x4> _grassField = new List<Matrix4x4>();
        private Mesh _grassMesh;
        private Matrix4x4 _grassPivot;
        private RenderParams _grassParams;
        private bool _grassLookedUp;

        // Камни, пни и стволы, сквозь которые трава не растёт: снимок на время травы (декор в ней не меняется),
        // в том же порядке и с теми же числами, что у InsideSolidDecor, — без чтения Transform на каждый пучок.
        private readonly List<Vector3> _solidScratch = new List<Vector3>();

        private IEnumerator ScatterGrassFieldSteps(LayoutMap map)
        {
            _grassField.Clear();
            if (map.Outline == null || map.PlacedCount == 0 || !FindGrassField()) yield break;
            _solidScratch.Clear();
            for (int i = 0; i < _decorCount; i++)
            {
                var kind = _style.DecorVariants[_decorVariant[i]].Kind;
                if (kind != DecorKind.Rock && kind != DecorKind.Tree) continue;
                float reach = _decorRadii[_decorVariant[i]] * (kind == DecorKind.Tree ? .25f : .6f);
                _solidScratch.Add(new Vector3(_decor[i].position.x, _decor[i].position.z, reach));
            }
            float cell = LayoutMap.CellSize.ToFloat();
            var first = map.GetPlaced(0);
            float minX = first.OriginX * cell, maxX = (first.OriginX + first.Width) * cell;
            float minZ = first.OriginY * cell, maxZ = (first.OriginY + first.Height) * cell;
            for (int m = 1; m < map.PlacedCount; m++)
            {
                var p = map.GetPlaced(m);
                minX = Mathf.Min(minX, p.OriginX * cell); maxX = Mathf.Max(maxX, (p.OriginX + p.Width) * cell);
                minZ = Mathf.Min(minZ, p.OriginY * cell); maxZ = Mathf.Max(maxZ, (p.OriginY + p.Height) * cell);
            }
            const float margin = 14, step = 1.3f;
            var rng = DecorRandom(0, 991);
            float patchX = (float)rng.NextDouble() * 1000, patchZ = (float)rng.NextDouble() * 1000;
            float lushness = map.IsArena ? CharacterOf(map, 0) == GladeCharacter.Sunny ? 1.15f
                : CharacterOf(map, 0) == GladeCharacter.Rocky ? .75f : 1 : 1;
            var portals = new List<Vector2> { TrailPoint(map.EntryPoint) };
            for (int e = 0; e < map.ExitCount; e++) portals.Add(TrailPoint(map.ExitPoint(e)));
            bool NearPortal(float px, float pz)
            {
                foreach (var portal in portals)
                    if ((portal - new Vector2(px, pz)).sqrMagnitude < 6.25f) return true;
                return false;
            }
            for (float z = minZ - margin; z < maxZ + margin; z += step)
            {
                yield return null;
                for (float x = minX - margin; x < maxX + margin; x += step)
                {
                    float px = x + ((float)rng.NextDouble() - .5f) * step;
                    float pz = z + ((float)rng.NextDouble() - .5f) * step;
                    double roll = rng.NextDouble();
                    float yaw = (float)rng.NextDouble() * 360, size = .85f + (float)rng.NextDouble() * .6f;
                    // Пятна гуще и реже, чем ровный ковёр: шум крупного масштаба задаёт поляны травы.
                    float patch = Mathf.PerlinNoise(px * .08f + patchX, pz * .08f + patchZ);
                    bool floor = map.Outline.ContainsCell(Mathf.FloorToInt(px * 2), Mathf.FloorToInt(pz * 2));
                    // На утоптанной земле поляны трава почти не растёт, в травяной кайме — как раньше.
                    float density = floor ? .16f * (1 - SurfaceEarth(px, pz) * .9f) : Mathf.Lerp(.25f, .95f, Mathf.SmoothStep(0, 1, patch));
                    if (roll > density * lushness) continue;
                    // Протоптанная тропа, вода, порталы и ориентиры остаются чистыми.
                    if (TrailWear(px, pz) > 60 || NearPond(px, pz, .5f) || NearLandmark(px, pz, .3f)) continue;
                    if (NearPortal(px, pz) || InsideSolidScratch(px, pz)) continue;
                    float y = floor ? FloorLevel(px, pz) : BackgroundHeight(map, px, pz) - .02f;
                    _grassField.Add(Matrix4x4.TRS(new Vector3(px, y, pz), Quaternion.Euler(0, yaw, 0),
                        new Vector3(size, size * (.85f + (float)rng.NextDouble() * .3f), size)) * _grassPivot);
                }
            }
            yield return null;
            // Кромка: густая высокая трава сразу за краем пола. Вместе с зарослями она отмечает весь
            // проходимый контур, а у точек появления врагов, где кусты не встают, край остаётся низким.
            var fringe = DecorRandom(0, 997);
            foreach (var (edge, normal) in OutlineEdges())
            {
                var tangent = new Vector2(normal.y, -normal.x);
                for (int k = 0; k < 2; k++)
                {
                    var p = edge + normal * (.1f + (float)fringe.NextDouble() * 1.1f)
                        + tangent * ((float)fringe.NextDouble() - .5f) * .5f;
                    float yaw = (float)fringe.NextDouble() * 360, size = 1.2f + (float)fringe.NextDouble() * .55f;
                    float stretch = 1.15f + (float)fringe.NextDouble() * .35f;
                    if (NearPond(p.x, p.y, .3f) || NearLandmark(p.x, p.y, .2f) || NearPortal(p.x, p.y) || InsideSolidScratch(p.x, p.y)) continue;
                    _grassField.Add(Matrix4x4.TRS(new Vector3(p.x, BackgroundHeight(map, p.x, p.y) - .02f, p.y),
                        Quaternion.Euler(0, yaw, 0), new Vector3(size, size * stretch, size)) * _grassPivot);
                }
            }
            var center = new Vector3((minX + maxX) * .5f, 0, (minZ + maxZ) * .5f);
            _grassParams.worldBounds = new Bounds(center, new Vector3(maxX - minX + margin * 2 + 4, 20, maxZ - minZ + margin * 2 + 4));
            _solidScratch.Clear();
        }

        private bool InsideSolidScratch(float x, float z)
        {
            for (int i = 0; i < _solidScratch.Count; i++)
            {
                var solid = _solidScratch[i];
                float dx = solid.x - x, dz = solid.y - z;
                if (dx * dx + dz * dz < solid.z * solid.z) return true;
            }
            return false;
        }

        private bool FindGrassField()
        {
            if (_grassLookedUp) return _grassMesh != null;
            _grassLookedUp = true;
            foreach (var variant in _style.DecorVariants)
            {
                if (variant.Prefab == null || variant.Prefab.name != GrassFieldPrefab) continue;
                var filter = variant.Prefab.GetComponentInChildren<MeshFilter>(true);
                var renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
                if (renderer == null || renderer.sharedMaterial == null) return false;
                _grassMesh = filter.sharedMesh;
                // Поворот и масштаб модели внутри префаба — часть каждого экземпляра.
                _grassPivot = variant.Prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                _grassParams = new RenderParams(renderer.sharedMaterial)
                {
                    shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                    receiveShadows = true,
                };
                return true;
            }
            return false;
        }

        private float TrailWear(float x, float z)
        {
            int px = Mathf.FloorToInt((x - _trailBounds.x) / _trailBounds.z * TrailResolution);
            int pz = Mathf.FloorToInt((z - _trailBounds.y) / _trailBounds.w * TrailResolution);
            if (px < 0 || pz < 0 || px >= TrailResolution || pz >= TrailResolution) return 0;
            return _trailPixels[pz * TrailResolution + px];
        }

        // Пучок не прорастает сквозь камни, пни и стволы.
        private bool InsideSolidDecor(float x, float z)
        {
            for (int i = 0; i < _decorCount; i++)
            {
                var kind = _style.DecorVariants[_decorVariant[i]].Kind;
                if (kind != DecorKind.Rock && kind != DecorKind.Tree) continue;
                float reach = _decorRadii[_decorVariant[i]] * (kind == DecorKind.Tree ? .25f : .6f);
                float dx = _decor[i].position.x - x, dz = _decor[i].position.z - z;
                if (dx * dx + dz * dz < reach * reach) return true;
            }
            return false;
        }

        private void DrawGrassField()
        {
            if (_grassField.Count == 0 || _grassMesh == null) return;
            for (int start = 0; start < _grassField.Count; start += 1023)
                Graphics.RenderMeshInstanced(_grassParams, _grassMesh, 0, _grassField,
                    Mathf.Min(1023, _grassField.Count - start), start);
        }
    }
}
