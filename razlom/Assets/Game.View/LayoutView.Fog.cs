using Game.Sim;
using UnityEngine;

namespace Game.View
{
    // Туман войны — чисто визуальный, растёт кругом от игрока, а не по
    // комнатам: каждый тик открывается всё в радиусе FogRevealRadius от
    // текущей позиции, навсегда. Simulation.Rng не трогается, проходимость
    // и спавны не меняются.
    public sealed partial class LayoutView
    {
        private bool[] _tileRevealed = System.Array.Empty<bool>();
        private bool[] _decorRevealed = System.Array.Empty<bool>();
        private bool _fogActive;

        private Texture2D _fogMask;
        private Color32[] _fogPixels;
        private Transform _fogPlane;
        private Material _fogPlaneMaterial;
        private MaterialPropertyBlock _fogBlock;
        private Vector2 _fogOrigin; // мировые X,Z левого нижнего угла маски
        private float _fogWorldSize;
        private int _fogResolution;

        private const float FogRevealRadius = 8f;
        private const float FogSoftEdge = 2.5f; // дополнительная мягкая кайма только у визуальной маски
        private const float FogPixelsPerMeter = 2f; // 0.5 м на пиксель
        private const float FogMargin = 20f; // захватывает пограничный лес/декор за пределами комнат
        private const float FogPlaneHeight = 1.65f;

        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void ResetFog(LayoutMap map)
        {
            _fogActive = map != null && map.PlacedCount > 0 && _style.FogOfWar && _driver != null;
            _tileRevealed = _fogActive ? new bool[map.PlacedCount] : System.Array.Empty<bool>();
            // _decor уже собран к этому моменту (ResetFog вызывается последним в Rebuild) —
            // размер массива открытости берём по его текущей ёмкости.
            _decorRevealed = _fogActive && _decor != null ? new bool[_decor.Length] : System.Array.Empty<bool>();
            ClearFogPlane();
            if (_fogActive) BuildFogMask(map);
            SyncFogVisibility();
        }

        // Пол и декор уже расставлены до сброса тумана: явно приводим их
        // активность в соответствие со свежим (пустым) состоянием открытости,
        // а не полагаемся на то, что было выставлено в момент расстановки.
        private void SyncFogVisibility()
        {
            for (int i = 0; i < _tileCount; i++)
                if (_tiles[i] != null)
                    _tiles[i].gameObject.SetActive(!_fogActive || _style.NaturalGround || (i < _tileRevealed.Length && _tileRevealed[i]));
            for (int i = 0; i < _decorCount; i++)
                if (_decor[i] != null)
                    _decor[i].gameObject.SetActive(!_fogActive || (i < _decorRevealed.Length && _decorRevealed[i]));
        }

        private void BuildFogMask(LayoutMap map)
        {
            float cell = LayoutMap.CellSize.ToFloat();
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < map.PlacedCount; i++)
            {
                PlacedModule room = map.GetPlaced(i);
                minX = Mathf.Min(minX, room.OriginX * cell); minZ = Mathf.Min(minZ, room.OriginY * cell);
                maxX = Mathf.Max(maxX, (room.OriginX + room.Width) * cell);
                maxZ = Mathf.Max(maxZ, (room.OriginY + room.Height) * cell);
            }
            minX -= FogMargin; minZ -= FogMargin; maxX += FogMargin; maxZ += FogMargin;
            _fogOrigin = new Vector2(minX, minZ);
            _fogWorldSize = Mathf.Max(maxX - minX, maxZ - minZ);
            _fogResolution = Mathf.Clamp(Mathf.CeilToInt(_fogWorldSize * FogPixelsPerMeter), 8, 512);

            EnsureFogResources();
            _fogPixels = new Color32[_fogResolution * _fogResolution];
            // Целые частоты синуса — бесшовный узор для видимой фактуры дымки,
            // не влияет на то, что уже открыто (это делает только альфа ниже).
            for (int y = 0; y < _fogResolution; y++)
                for (int x = 0; x < _fogResolution; x++)
                {
                    float u = x / (float)_fogResolution, v = y / (float)_fogResolution;
                    float wave = Mathf.Sin((u * 9f + v * 3f) * Mathf.PI * 2f) * .35f
                        + Mathf.Sin((v * 12f - u * 6f) * Mathf.PI * 2f) * .35f
                        + Mathf.Sin((u * 17f + v * 17f) * Mathf.PI * 2f) * .3f;
                    byte alpha = (byte)(Mathf.Lerp(.75f, 1f, Mathf.Clamp01(wave * .5f + .5f)) * 255);
                    _fogPixels[y * _fogResolution + x] = new Color32(255, 255, 255, alpha);
                }
            _fogMask.Reinitialize(_fogResolution, _fogResolution);
            _fogMask.SetPixels32(_fogPixels);
            _fogMask.Apply(false, false);

            if (_fogRoot == null) _fogRoot = CreateRoot("Пул: туман войны");
            var go = new GameObject("Пелена тумана войны");
            go.transform.SetParent(_fogRoot, false);
            go.transform.position = new Vector3(minX + _fogWorldSize * .5f, FogPlaneHeight, minZ + _fogWorldSize * .5f);
            go.transform.localScale = new Vector3(_fogWorldSize, 1f, _fogWorldSize);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = _fogVeilMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _fogPlaneMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _fogBlock.SetVector(MainTexStId, new Vector4(1f, 1f, 0f, 0f));
            _fogBlock.SetColor(ColorId, _style.FogColor);
            renderer.SetPropertyBlock(_fogBlock);
            _fogPlane = go.transform;
        }

        private Transform _fogRoot;
        private Mesh _fogVeilMesh;

        private void EnsureFogResources()
        {
            if (_fogVeilMesh == null) _fogVeilMesh = BuildFogQuadMesh();
            if (_fogMask == null)
                _fogMask = new Texture2D(_fogResolution, _fogResolution, TextureFormat.RGBA32, false)
                { name = "Разлом/Маска тумана войны", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            if (_fogPlaneMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                _fogPlaneMaterial = new Material(shader) { name = "Разлом/Туман войны", mainTexture = _fogMask };
                _ownedMaterials.Add(_fogPlaneMaterial);
            }
            else _fogPlaneMaterial.mainTexture = _fogMask;
            if (_fogBlock == null) _fogBlock = new MaterialPropertyBlock();
        }

        private static Mesh BuildFogQuadMesh()
        {
            var mesh = new Mesh { name = "Плита тумана" };
            mesh.vertices = new[]
            {
                new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 0, -.5f),
                new Vector3(.5f, 0, .5f), new Vector3(-.5f, 0, .5f),
            };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void ClearFogPlane()
        {
            if (_fogPlane != null) DestroyOwned(_fogPlane.gameObject);
            _fogPlane = null;
            _fogPixels = null;
        }

        // Растёт кругом от игрока каждый тик: открытое остаётся открытым,
        // никогда не гаснет обратно.
        private void UpdateFog(FixVec2 playerPosition)
        {
            if (!_fogActive) return;
            var player = new Vector2(playerPosition.X.ToFloat(), playerPosition.Y.ToFloat());
            float radiusSq = FogRevealRadius * FogRevealRadius;
            for (int i = 0; i < _tileCount && i < _tileRevealed.Length; i++)
            {
                if (_tileRevealed[i] || _tiles[i] == null) continue;
                Vector3 p = _tiles[i].position;
                if ((new Vector2(p.x, p.z) - player).sqrMagnitude > radiusSq) continue;
                _tileRevealed[i] = true;
                _tiles[i].gameObject.SetActive(true);
            }
            for (int i = 0; i < _decorCount && i < _decorRevealed.Length; i++)
            {
                if (_decorRevealed[i] || _decor[i] == null) continue;
                Vector3 p = _decor[i].position;
                // Крона входит в поле зрения раньше ствола; дальние деревья остаются скрытыми.
                float reach = FogRevealRadius + Mathf.Min(5, _decorRadii[_decorVariant[i]]);
                if ((new Vector2(p.x, p.z) - player).sqrMagnitude > reach * reach) continue;
                _decorRevealed[i] = true;
                _decor[i].gameObject.SetActive(true);
            }
            StampFogMask(player);
        }

        private void StampFogMask(Vector2 player)
        {
            if (_fogPixels == null) return;
            float outer = FogRevealRadius + FogSoftEdge;
            int px = Mathf.RoundToInt((player.x - _fogOrigin.x) * FogPixelsPerMeter);
            int pz = Mathf.RoundToInt((player.y - _fogOrigin.y) * FogPixelsPerMeter);
            int radiusPx = Mathf.CeilToInt(outer * FogPixelsPerMeter);
            int x0 = Mathf.Clamp(px - radiusPx, 0, _fogResolution - 1);
            int x1 = Mathf.Clamp(px + radiusPx, 0, _fogResolution - 1);
            int z0 = Mathf.Clamp(pz - radiusPx, 0, _fogResolution - 1);
            int z1 = Mathf.Clamp(pz + radiusPx, 0, _fogResolution - 1);
            if (x1 < x0 || z1 < z0) return;
            bool changed = false;
            for (int z = z0; z <= z1; z++)
            {
                float worldZ = _fogOrigin.y + z / FogPixelsPerMeter;
                int row = z * _fogResolution;
                for (int x = x0; x <= x1; x++)
                {
                    int index = row + x;
                    byte current = _fogPixels[index].a;
                    if (current == 0) continue;
                    float worldX = _fogOrigin.x + x / FogPixelsPerMeter;
                    float distance = Vector2.Distance(new Vector2(worldX, worldZ), player);
                    if (distance >= outer) continue;
                    float clear = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FogRevealRadius, outer, distance));
                    byte target = (byte)((1f - clear) * 255);
                    if (target >= current) continue;
                    var pixel = _fogPixels[index]; pixel.a = target; _fogPixels[index] = pixel;
                    changed = true;
                }
            }
            if (!changed) return;
            _fogMask.SetPixels32(x0, z0, x1 - x0 + 1, z1 - z0 + 1, SubRect(x0, z0, x1, z1));
            _fogMask.Apply(false, false);
        }

        private Color32[] SubRect(int x0, int z0, int x1, int z1)
        {
            int w = x1 - x0 + 1, h = z1 - z0 + 1;
            var block = new Color32[w * h];
            for (int z = 0; z < h; z++)
                System.Array.Copy(_fogPixels, (z0 + z) * _fogResolution + x0, block, z * w, w);
            return block;
        }

        /// <summary>Открыта ли мировая точка — та же маска, что рисует 3D-сцену.</summary>
        public bool IsRevealed(float x, float z)
        {
            if (!_fogActive || _fogPixels == null) return true;
            int px = Mathf.Clamp(Mathf.RoundToInt((x - _fogOrigin.x) * FogPixelsPerMeter), 0, _fogResolution - 1);
            int pz = Mathf.Clamp(Mathf.RoundToInt((z - _fogOrigin.y) * FogPixelsPerMeter), 0, _fogResolution - 1);
            return _fogPixels[pz * _fogResolution + px].a < 128;
        }

        /// <summary>Маска тумана для HUD-миникарты — те же данные, что видно в 3D.</summary>
        public Texture2D FogMask => _fogActive ? _fogMask : null;
        public Vector2 FogOrigin => _fogOrigin;
        public float FogWorldSize => _fogWorldSize;

        private void DisposeFog()
        {
            ClearFogPlane();
            DestroyOwned(_fogVeilMesh);
            _fogVeilMesh = null;
            DestroyOwned(_fogMask);
            _fogMask = null;
            _fogPlaneMaterial = null; // уничтожается вместе с _ownedMaterials
            _tileRevealed = System.Array.Empty<bool>();
            _decorRevealed = System.Array.Empty<bool>();
            _fogActive = false;
        }
    }
}
