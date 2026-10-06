using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class LayoutView
    {
        private Texture2D _campSurfaceMap;
        private Color32[] _campSurfacePixels;
        private float[] _forestDistance;
        // Попиксельная часть маски земли: считается на рабочих потоках с конца сборки тропы,
        // пока главный поток расставляет декор; ApplyCampSurfaceSteps её дожидается.
        private Task _surfaceWork;

        private Material CreateLocationGround(Color tint, Texture2D grass, Texture2D dirt, float tiling, float earth)
        {
            if (_style.CampSurfaceMaterial == null)
                return ViewMaterials.CreateMeadowGround(tint, grass, dirt, tiling, earth);
            // Общий ассет лагеря остаётся неизменным: у каждой карты собственный экземпляр.
            var material = new Material(_style.CampSurfaceMaterial);
            material.SetFloat("_IsSurface", 1);
            material.SetFloat("_IsPath", 0);
            material.SetFloat("_IsRiverBank", 0);
            // Цвет травы — лагерный: прежний множитель (.86, 1, .84) вместе с закатом выжигал синий,
            // и поле арены уходило в жёлто-оранжевое (владелец, 29 сентября).
            if (material.HasProperty("_DetailSoftness")) material.SetFloat("_DetailSoftness", _style.GroundDetailSoftness);
            if (material.HasProperty("_TurfWeight")) material.SetFloat("_TurfWeight", _style.GroundTurfWeight);
            // Охристый грунт лагеря хорош под камнями его дорожек, а утоптанная середина арены
            // из него — сплошное оранжевое пятно. У разлома своя, природная земля.
            if (_style.EarthTexture != null && material.HasProperty("_DirtTex"))
            {
                material.SetTexture("_DirtTex", _style.EarthTexture);
                material.SetFloat("_TileMeters", _style.EarthTileMeters);
                if (material.HasProperty("_DirtGain")) material.SetFloat("_DirtGain", _style.EarthBrightness);
                if (material.HasProperty("_DirtTint")) material.SetColor("_DirtTint", _style.EarthTint);
                if (material.HasProperty("_EarthCracks"))
                {
                    material.SetFloat("_EarthCracks", _style.EarthCracks);
                    material.SetFloat("_CrackMeters", _style.CrackMeters);
                }
            }
            return material;
        }

        /// <summary>
        /// Попиксельная основа маски земли (камни, дёрн, тропа), лесная подстилка и земля поляны — на
        /// рабочих потоках. Зовётся, как только готовы маски тропы и вытоптанного грунта: до маски земли
        /// их никто не меняет, а саму маску до ApplyCampSurfaceSteps никто не читает. Те же условия, что
        /// и у прежнего ApplyCampSurface; порядок проходов прежний, каждый пиксель считается тем же кодом.
        /// </summary>
        private void StartSurfaceWork(LayoutMap map)
        {
            _surfaceWork = null;
            if (map.PlacedCount == 0 || _style.CampSurfaceMaterial == null || _trailMask == null) return;
            if (_campSurfaceMap == null)
            {
                _campSurfaceMap = new Texture2D(TrailResolution, TrailResolution, TextureFormat.RGBA32, false, true)
                { name = "Лагерная земля: маска разлома", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                _campSurfacePixels = new Color32[TrailResolution * TrailResolution];
            }
            const int n = TrailResolution;
            bool forest = map.Outline != null && _style.ForestGroundWear > 0;
            bool earth = map.Outline != null && map.IsArena;
            if (forest && _forestDistance == null) _forestDistance = new float[n * n];
            if (earth && _clearingDistance == null) _clearingDistance = new float[n * n];
            // Характер арены считается на главном потоке: он кэшируется в поле.
            float stones = earth ? CharacterOf(map, 0) == GladeCharacter.Rocky ? .7f : .45f : 0;
            var lakes = new Vector3[map.WaterCount];
            for (int w = 0; w < lakes.Length; w++)
            {
                var water = map.GetWater(w);
                lakes[w] = new Vector3(water.Center.X.ToFloat(), water.Center.Y.ToFloat(), water.Radius.ToFloat());
            }
            _surfaceWork = Task.Run(() =>
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                // Расстояния до края пола — двухпроходная фаска, строки зависят друг от друга: каждое
                // поле целиком на своём потоке, параллельно с основой.
                Task outside = forest ? Task.Run(() => FloorDistance(_forestDistance, false)) : null;
                Task inside = earth ? Task.Run(() => FloorDistance(_clearingDistance, true)) : null;
                Parallel.For(0, n, SurfaceBaseRow);
                if (forest)
                {
                    outside.Wait();
                    Parallel.For(0, n, ForestFloorRow);
                }
                if (earth)
                {
                    inside.Wait();
                    Parallel.For(1, n - 1, y => EarthClearingRow(y, stones, lakes));
                    FilletEarth(2.4f);
                }
                FrameCost.Worker("маска земли", start);
            });
        }

        private IEnumerator ApplyCampSurfaceSteps()
        {
            if (_style.CampSurfaceMaterial == null || _trailMask == null) yield break;
            if (_surfaceWork == null) StartSurfaceWork(_shownMap);
            yield return _surfaceWork;
            _surfaceWork = null;
            // Земля связывает предметы с окружением; на проходах не появляется новая геометрия.
            // Места и размеры снимаются с расставленного декора здесь, оттиски — на рабочих потоках.
            var stamps = new List<Vector4>(_decorCount + _shownMap.ObstacleCount);
            for (int i = 0; i < _decorCount; i++)
            {
                int variant = _decorVariant[i];
                var kind = _style.DecorVariants[variant].Kind;
                if (kind == DecorKind.GrassTuft) continue;
                var item = _decor[i];
                float radius = _decorRadii[variant] * Mathf.Max(item.localScale.x, item.localScale.z)
                    / Mathf.Max(.01f, _style.DecorVariants[variant].ScaleRange.y);
                stamps.Add(new Vector4(item.position.x, item.position.z, Mathf.Clamp(radius * 1.2f, .8f, 5),
                    kind == DecorKind.Tree ? 1 : .6f));
            }
            for (int i = 0; i < _shownMap.ObstacleCount; i++)
            {
                var obstacle = _shownMap.GetObstacle(i);
                var center = TrailPoint(obstacle.Center);
                stamps.Add(new Vector4(center.x, center.y, obstacle.Radius.ToFloat() + 1.1f, .85f));
            }
            var all = stamps.ToArray();
            yield return Rows(TrailResolution, y => StampForestGroundRow(all, y), "земля у предметов");
            _campSurfaceMap.SetPixels32(_campSurfacePixels); _campSurfaceMap.Apply(false, false);
            BindCampSurface(_roomMaterial); BindCampSurface(_entranceMaterial); BindCampSurface(_exitMaterial);
            if (_shore != null) BindCampSurface(_shore.GetComponent<MeshRenderer>().sharedMaterial);
            if (_banks != null) BindCampSurface(_banks.GetComponent<MeshRenderer>().sharedMaterial);
            if (_riverBanks != null) BindCampSurface(_riverBanks.GetComponent<MeshRenderer>().sharedMaterial);
            if (_groundFill != null) BindCampSurface(_groundFill.GetComponent<MeshRenderer>().sharedMaterial);
        }

        // Основа маски земли, одна строка: камни, дёрн и тропа по шуму.
        private void SurfaceBaseRow(int y)
        {
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
        }

        // Лесная подстилка за краем боевого пола: чем дальше от поляны, тем больше земли
        // и тени, меньше плотного дёрна. Сам пол и тропы не меняются — светлая арена
        // в более тёмном лесу читается как поляна и не спорит с боем за внимание.
        // Одна строка y ∈ [0, n − 1], край маски включён (глубокая подстилка у рамки, Костя 30.09);
        // поле _forestDistance уже посчитано (FloorDistance, снаружи пола).
        private void ForestFloorRow(int y)
        {
            const int n = TrailResolution;
            var dist = _forestDistance;
            float wear = Mathf.Lerp(.4f, 1.1f, _style.ForestGroundWear);
            // Маска кончается в двух метрах за модулями, дальше земля берёт её крайний пиксель.
            // Край маски — ровная глубокая подстилка: фон за лесом раньше тянул оттуда светлый
            // газон без тени на весь кадр, а пёстрый край растянулся бы полосами.
            var deep = new Color32((byte)(wear * 110), 0, (byte)((1 - wear * .45f) * 255), (byte)(128 * (1 - wear * .6f)));
            float blend = 48f / n;
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                if (dist[i] <= 0) continue;
                float px = _trailBounds.x + (x + .5f) / n * _trailBounds.z;
                float pz = _trailBounds.y + (y + .5f) / n * _trailBounds.w;
                float patchy = Mathf.Lerp(.55f, 1.15f, Mathf.PerlinNoise(px * .18f + 311, pz * .18f + 97));
                // Тень и редкий дёрн начинаются сразу за краем, голая земля — только глубже:
                // на солнце грунт светлее травы и крупным пятном отвлекал бы от боя.
                float shade = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, 5, dist[i])) * wear;
                float litter = Mathf.Clamp01(Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.5f, 9, dist[i])) * patchy) * wear;
                if (shade <= 0) continue;
                var pixel = _campSurfacePixels[i];
                pixel.r = (byte)Mathf.Max(pixel.r, litter * 118);
                pixel.b = (byte)Mathf.Min(pixel.b, Mathf.Max(0f, 1 - shade * .45f * ArenaMood.EdgeShade) * 255); // свет арены: 1 — как было
                pixel.a = (byte)(pixel.a * (1 - shade * .6f));
                float rim = Mathf.Min(Mathf.Min(x, n - 1 - x), Mathf.Min(y, n - 1 - y)) / (float)n;
                float toDeep = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(blend, 0, rim)) * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(3, 8, dist[i]));
                _campSurfacePixels[i] = Color32.Lerp(pixel, deep, toDeep);
            }
        }

        // Расстояние в метрах от каждого пикселя маски до края пола: снаружи пола (inside = false)
        // или внутри него (inside = true); по другую сторону края — ноль. Двухпроходная фаска
        // с учётом неквадратного пикселя.
        private void FloorDistance(float[] dist, bool inside)
        {
            const int n = TrailResolution;
            float dx = _trailBounds.z / n, dz = _trailBounds.w / n, dd = Mathf.Sqrt(dx * dx + dz * dz);
            for (int y = 0; y < n; y++)
            {
                float pz = _trailBounds.y + (y + .5f) / n * _trailBounds.w;
                for (int x = 0; x < n; x++)
                {
                    float px = _trailBounds.x + (x + .5f) / n * _trailBounds.z;
                    bool floor = _shownMap.Outline.ContainsCell(Mathf.FloorToInt(px * 2), Mathf.FloorToInt(pz * 2));
                    dist[y * n + x] = floor == inside ? 1e6f : 0;
                }
            }
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
        }

        // Оттиски земли у предметов, одна строка маски: все оттиски, задевающие строку, в прежнем порядке —
        // каждый пиксель получает те же операции в той же очереди, что при оттисках по одному. Оттиск:
        // x, z — центр, z — радиус, w — сила.
        private void StampForestGroundRow(Vector4[] stamps, int y)
        {
            for (int s = 0; s < stamps.Length; s++)
            {
                var stamp = stamps[s];
                var center = new Vector2(stamp.x, stamp.y);
                float radius = stamp.z;
                int y0 = Mathf.Max(1, Mathf.FloorToInt((center.y - radius - _trailBounds.y) / _trailBounds.w * TrailResolution));
                int y1 = Mathf.Min(TrailResolution - 2, Mathf.CeilToInt((center.y + radius - _trailBounds.y) / _trailBounds.w * TrailResolution));
                if (y < y0 || y > y1) continue;
                int x0 = Mathf.Max(1, Mathf.FloorToInt((center.x - radius - _trailBounds.x) / _trailBounds.z * TrailResolution));
                int x1 = Mathf.Min(TrailResolution - 2, Mathf.CeilToInt((center.x + radius - _trailBounds.x) / _trailBounds.z * TrailResolution));
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(_trailBounds.x + (x + .5f) / TrailResolution * _trailBounds.z,
                        _trailBounds.y + (y + .5f) / TrailResolution * _trailBounds.w);
                    float edge = Vector2.Distance(p, center) / (radius * PatchWobble(p, center));
                    float wear = Mathf.SmoothStep(0, 1, Mathf.Clamp01(1 - edge)) * stamp.w * _style.ForestGroundWear;
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
        }

        private void BindCampSurface(Material material)
        {
            material.SetTexture("_SurfaceMap", _campSurfaceMap);
            material.SetVector("_SurfaceBounds", _trailBounds);
        }
    }
}
