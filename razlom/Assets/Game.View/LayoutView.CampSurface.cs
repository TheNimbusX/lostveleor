using UnityEngine;

namespace Game.View
{
    public sealed partial class LayoutView
    {
        private Texture2D _campSurfaceMap;
        private Color32[] _campSurfacePixels;
        private float[] _forestDistance;

        private Material CreateLocationGround(Color tint, Texture2D grass, Texture2D dirt, float tiling, float earth)
        {
            if (_style.CampSurfaceMaterial == null)
                return ViewMaterials.CreateMeadowGround(tint, grass, dirt, tiling, earth);
            // Общий ассет лагеря остаётся неизменным: у каждой карты собственный экземпляр.
            var material = new Material(_style.CampSurfaceMaterial);
            material.SetFloat("_IsSurface", 1);
            material.SetFloat("_IsPath", 0);
            material.SetFloat("_IsRiverBank", 0);
            material.SetColor("_BaseColor", material.GetColor("_BaseColor") * new Color(.88f, .95f, .89f, 1));
            if (material.HasProperty("_DetailSoftness")) material.SetFloat("_DetailSoftness", _style.GroundDetailSoftness);
            if (material.HasProperty("_TurfWeight")) material.SetFloat("_TurfWeight", _style.GroundTurfWeight);
            return material;
        }

        private void ApplyCampSurface()
        {
            if (_style.CampSurfaceMaterial == null || _trailMask == null) return;
            if (_campSurfaceMap == null)
            {
                _campSurfaceMap = new Texture2D(TrailResolution, TrailResolution, TextureFormat.RGBA32, false, true)
                { name = "Лагерная земля: маска разлома", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                _campSurfacePixels = new Color32[TrailResolution * TrailResolution];
            }
            for (int y = 0; y < TrailResolution; y++)
                for (int x = 0; x < TrailResolution; x++)
                {
                    int i = y * TrailResolution + x;
                    float px = _trailBounds.x + (x + .5f) / TrailResolution * _trailBounds.z;
                    float pz = _trailBounds.y + (y + .5f) / TrailResolution * _trailBounds.w;
                    float stones = _style.TrailStoneCoverage * Mathf.Lerp(.08f, 1, Mathf.SmoothStep(0, 1,
                        Mathf.InverseLerp(.3f, .7f, Mathf.PerlinNoise(px * .32f + 5, pz * .32f + 13))));
                    float turf = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.28f, .72f,
                        Mathf.PerlinNoise(px * .43f + 8, pz * .43f + 17) * .65f
                        + Mathf.PerlinNoise(px * 1.3f + 31, pz * 1.3f + 2) * .35f));
                    byte path = (byte)Mathf.Max(_trailPixels[i], _wearPixels[i] * .62f);
                    // Нулевой край не растягивает дорогу по всему фону при Clamp.
                    if (x == 0 || y == 0 || x == TrailResolution - 1 || y == TrailResolution - 1) path = 0;
                    _campSurfacePixels[i] = new Color32(path, (byte)(stones * 255), 255, (byte)(turf * 255));
                }
            StampForestFloor();
            // Земля связывает предметы с окружением; на проходах не появляется новая геометрия.
            for (int i = 0; i < _decorCount; i++)
            {
                int variant = _decorVariant[i];
                var kind = _style.DecorVariants[variant].Kind;
                if (kind == DecorKind.GrassTuft) continue;
                var item = _decor[i];
                float radius = _decorRadii[variant] * Mathf.Max(item.localScale.x, item.localScale.z)
                    / Mathf.Max(.01f, _style.DecorVariants[variant].ScaleRange.y);
                StampForestGround(new Vector2(item.position.x, item.position.z), Mathf.Clamp(radius * 1.2f, .8f, 5),
                    kind == DecorKind.Tree ? 1 : .6f);
            }
            for (int i = 0; i < _shownMap.ObstacleCount; i++)
            {
                var obstacle = _shownMap.GetObstacle(i);
                StampForestGround(TrailPoint(obstacle.Center), obstacle.Radius.ToFloat() + 1.1f, .85f);
            }
            _campSurfaceMap.SetPixels32(_campSurfacePixels); _campSurfaceMap.Apply(false, false);
            BindCampSurface(_roomMaterial); BindCampSurface(_entranceMaterial); BindCampSurface(_exitMaterial);
            if (_shore != null) BindCampSurface(_shore.GetComponent<MeshRenderer>().sharedMaterial);
            if (_banks != null) BindCampSurface(_banks.GetComponent<MeshRenderer>().sharedMaterial);
            if (_groundFill != null) BindCampSurface(_groundFill.GetComponent<MeshRenderer>().sharedMaterial);
        }

        // Лесная подстилка за краем боевого пола: чем дальше от поляны, тем больше земли
        // и тени, меньше плотного дёрна. Сам пол и тропы не меняются — светлая арена
        // в более тёмном лесу читается как поляна и не спорит с боем за внимание.
        private void StampForestFloor()
        {
            if (_shownMap.Outline == null || _style.ForestGroundWear <= 0) return;
            const int n = TrailResolution;
            if (_forestDistance == null) _forestDistance = new float[n * n];
            var dist = _forestDistance;
            float dx = _trailBounds.z / n, dz = _trailBounds.w / n, dd = Mathf.Sqrt(dx * dx + dz * dz);
            for (int y = 0; y < n; y++)
            {
                float pz = _trailBounds.y + (y + .5f) / n * _trailBounds.w;
                for (int x = 0; x < n; x++)
                {
                    float px = _trailBounds.x + (x + .5f) / n * _trailBounds.z;
                    dist[y * n + x] = _shownMap.Outline.ContainsCell(Mathf.FloorToInt(px * 2), Mathf.FloorToInt(pz * 2)) ? 0 : 1e6f;
                }
            }
            // Двухпроходная фаска: расстояние до пола в метрах с учётом неквадратного пикселя.
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x; float d = dist[i];
                    if (x > 0) d = Mathf.Min(d, dist[i - 1] + dx);
                    if (y > 0)
                    {
                        d = Mathf.Min(d, dist[i - n] + dz);
                        if (x > 0) d = Mathf.Min(d, dist[i - n - 1] + dd);
                        if (x < n - 1) d = Mathf.Min(d, dist[i - n + 1] + dd);
                    }
                    dist[i] = d;
                }
            for (int y = n - 1; y >= 0; y--)
                for (int x = n - 1; x >= 0; x--)
                {
                    int i = y * n + x; float d = dist[i];
                    if (x < n - 1) d = Mathf.Min(d, dist[i + 1] + dx);
                    if (y < n - 1)
                    {
                        d = Mathf.Min(d, dist[i + n] + dz);
                        if (x < n - 1) d = Mathf.Min(d, dist[i + n + 1] + dd);
                        if (x > 0) d = Mathf.Min(d, dist[i + n - 1] + dd);
                    }
                    dist[i] = d;
                }
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x;
                    if (dist[i] <= 0) continue;
                    float px = _trailBounds.x + (x + .5f) / n * _trailBounds.z;
                    float pz = _trailBounds.y + (y + .5f) / n * _trailBounds.w;
                    float wear = Mathf.Lerp(.4f, 1.1f, _style.ForestGroundWear);
                    float patchy = Mathf.Lerp(.55f, 1.15f, Mathf.PerlinNoise(px * .18f + 311, pz * .18f + 97));
                    // Тень и редкий дёрн начинаются сразу за краем, голая земля — только глубже:
                    // на солнце грунт светлее травы и крупным пятном отвлекал бы от боя.
                    float shade = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, 5, dist[i])) * wear;
                    float litter = Mathf.Clamp01(Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.5f, 9, dist[i])) * patchy) * wear;
                    if (shade <= 0) continue;
                    var pixel = _campSurfacePixels[i];
                    pixel.r = (byte)Mathf.Max(pixel.r, litter * 118);
                    pixel.b = (byte)Mathf.Min(pixel.b, (1 - shade * .45f) * 255);
                    pixel.a = (byte)(pixel.a * (1 - shade * .6f));
                    _campSurfacePixels[i] = pixel;
                }
        }

        private void StampForestGround(Vector2 center, float radius, float strength)
        {
            int x0 = Mathf.Max(1, Mathf.FloorToInt((center.x - radius - _trailBounds.x) / _trailBounds.z * TrailResolution));
            int x1 = Mathf.Min(TrailResolution - 2, Mathf.CeilToInt((center.x + radius - _trailBounds.x) / _trailBounds.z * TrailResolution));
            int y0 = Mathf.Max(1, Mathf.FloorToInt((center.y - radius - _trailBounds.y) / _trailBounds.w * TrailResolution));
            int y1 = Mathf.Min(TrailResolution - 2, Mathf.CeilToInt((center.y + radius - _trailBounds.y) / _trailBounds.w * TrailResolution));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(_trailBounds.x + (x + .5f) / TrailResolution * _trailBounds.z,
                        _trailBounds.y + (y + .5f) / TrailResolution * _trailBounds.w);
                    float edge = Vector2.Distance(p, center) / (radius * PatchWobble(p, center));
                    float wear = Mathf.SmoothStep(0, 1, Mathf.Clamp01(1 - edge)) * strength * _style.ForestGroundWear;
                    wear *= Mathf.Lerp(.65f, 1, Mathf.PerlinNoise(p.x * 1.7f, p.y * 1.7f));
                    int index = y * TrailResolution + x;
                    var pixel = _campSurfacePixels[index];
                    bool road = _trailPixels[index] > 90;
                    pixel.r = (byte)Mathf.Max(pixel.r, wear * 230);
                    pixel.b = (byte)Mathf.Min(pixel.b, (1 - wear * .22f) * 255);
                    if (!road) pixel.g = (byte)Mathf.Min(pixel.g, 25);
                    _campSurfacePixels[index] = pixel;
                }
        }

        private void BindCampSurface(Material material)
        {
            material.SetTexture("_SurfaceMap", _campSurfaceMap);
            material.SetVector("_SurfaceBounds", _trailBounds);
        }
    }
}
