using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.EditorTools
{
    /// <summary>
    /// Детали земли и берега лагеря (владелец 24 сентября: «большие одинаковые участки травы… вещи
    /// смотрятся поставленными на ковёр… река чистая, но пустая»).
    ///
    /// Кладёт: мягкие тёмные пятна под предметами и деревьями (Razlom/Camp Contact Shadow), кучки
    /// листвы у деревьев и изгороди, пятна сырой земли по краям дорожек и в траве, камешки, пену у
    /// опор моста и камней в воде (Razlom/Camp Water Foam), куртины камыша по кромке воды и кувшинки
    /// у берегов. Листья, земля и кувшинки — сгенерированный арт (ART/CAMP/ground-2026-09-24),
    /// камыш — сетка из кода. Всё в одной группе с CampSceneryDecoration: на проходимость не влияет.
    /// Перезапуск пересобирает группу; расстановка детерминирована.
    /// </summary>
    public static class CampGroundDetailAuthoring
    {
        public const string GroupName = "Земля и берег — детали";
        const string Folder = "Assets/Resources/Environment/Camp/GroundDetails";
        const float WaterY = -.2f;

        /// <summary>Поляна лагеря (как у стены леса) — где раскладывать детали травы.</summary>
        static readonly Vector2 Min = new Vector2(-27f, -20f), Max = new Vector2(20f, 23f);

        [MenuItem("Разлом/Лагерь/Детали земли и берега")]
        public static void BuildFromMenu() => Debug.Log(Build());

        public static string Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var camp = GameObject.Find("Authored World/CampRoot")?.transform;
            if (camp == null) return "Нет Authored World/CampRoot";
            var river = Object.FindAnyObjectByType<CampRiver>();
            var random = new System.Random(24092027);
            float R(float a, float b) => a + (float)random.NextDouble() * (b - a);

            Undo.SetCurrentGroupName("Детали земли и берега");
            int undo = Undo.GetCurrentGroup();
            var old = camp.Find(GroupName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject(GroupName).transform;
            root.SetParent(camp, false);
            Undo.RegisterCreatedObjectUndo(root.gameObject, GroupName);
            root.gameObject.AddComponent<CampSceneryDecoration>();
            // Лагерь в сцене масштабирован (×1,6): группа работает в мировых метрах.
            root.position = Vector3.zero;
            root.rotation = Quaternion.identity;
            Vector3 lossy = camp.lossyScale;
            root.localScale = new Vector3(1f / lossy.x, 1f / lossy.y, 1f / lossy.z);

            var heights = HeightGrid(camp);
            float Ground(Vector2 p)
            {
                var c = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
                if (heights.TryGetValue(c, out float h)) return h;
                return 0f;
            }
            var mask = PathMask();
            Mesh quad = Quad();

            GameObject Decal(Transform parent, string name, Material material, Vector2 p, float y, Vector2 size, float yaw)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(p.x, y, p.y);
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                go.transform.localScale = new Vector3(size.x, 1f, size.y);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                return go;
            }
            bool Water(Vector2 p, float margin)
            {
                Vector3 local = river.transform.InverseTransformPoint(new Vector3(p.x, 0f, p.y));
                return Mathf.Abs(local.z - river.CentreAt(local.x)) < river.Width * .5f + margin;
            }

            var shadows = Sub(root, "Тени под предметами");
            var leaves = Sub(root, "Листва");
            var dirt = Sub(root, "Сырая земля");
            var pebbles = Sub(root, "Камешки");
            var foam = Sub(root, "Пена");
            var reeds = Sub(root, "Камыш");
            var lilies = Sub(root, "Кувшинки");

            // ---------------------------------------------------------------- тени под предметами
            var shadowMat = ShaderMaterial("Contact shadow", "Razlom/Camp Contact Shadow");
            shadowMat.SetColor("_ShadowColor", new Color(.24f, .25f, .19f, 1f));
            shadowMat.SetFloat("_Strength", .7f);
            EditorUtility.SetDirty(shadowMat);
            var skipGroups = new HashSet<string> { "Ground Study - Campfire", "Художественный проход лагеря", "Река — нижняя граница лагеря",
                "Детали лагеря — гравий у оград", "Звуковое окружение лагеря", "Берег алхимика — трава", GroupName, "Ground" };
            var objects = new List<Transform>();
            foreach (Transform group in camp)
            {
                if (skipGroups.Contains(group.name)) continue;
                if (group.name == CampForestEdgeAuthoring.GroupName)
                {
                    foreach (Transform sub in group) if (sub.name != "Дымка по краю") foreach (Transform t in sub) objects.Add(t);
                    continue;
                }
                foreach (Transform t in group) objects.Add(t);
            }
            var frame = camp.Find("Ground/Frame");
            if (frame != null) foreach (Transform t in frame) objects.Add(t);
            var trees = new List<(Vector2 p, float r)>();
            int shadowCount = 0;
            foreach (var t in objects)
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("трава") || n.Contains("grass") || n.Contains("корень") || n.Contains("meadow") || n.Contains("petal") || n.Contains("flower light") || n.Contains("light")) continue;
                if (!Footprint(t, out Bounds b)) continue;
                if (b.size.y < .2f || b.min.y > .5f) continue;
                float wide = Mathf.Max(b.size.x, b.size.z);
                var p = new Vector2(b.center.x, b.center.z);
                if (Water(p, .1f)) continue;
                if (b.size.y > 2.5f && wide > 1.5f)
                {
                    float trunk = Mathf.Min(b.size.x, b.size.z) * .34f;
                    Decal(shadows, "Тень " + t.name, shadowMat, p, Ground(p) + .02f, new Vector2(trunk, trunk) * 2f, 0f);
                    trees.Add((p, Mathf.Min(b.size.x, b.size.z) * .5f));
                    shadowCount++;
                    continue;
                }
                if (wide > 9f) continue;
                var size = new Vector2(b.size.x * 1.25f + .3f, b.size.z * 1.25f + .3f);
                Decal(shadows, "Тень " + t.name, shadowMat, p, Mathf.Max(b.min.y, Ground(p)) + .02f, size, 0f);
                shadowCount++;
            }

            // ---------------------------------------------------------------- листва
            var leafMats = new List<Material>();
            for (int i = 0; i < 6; i++) leafMats.Add(LitMaterial("Leaves " + i, "leaves_" + i, cutout: true));
            int leafCount = 0;
            void Leaf(Vector2 p, float size)
            {
                if (Water(p, .6f) || mask(p) > .35f) return;
                Decal(leaves, "Листва " + (++leafCount), leafMats[random.Next(leafMats.Count)], p, Ground(p) + .025f, new Vector2(size, size), R(0f, 360f));
            }
            foreach (var (p, r) in trees)
            {
                if (p.x < Min.x - 14f || p.x > Max.x + 14f || p.y < Min.y - 14f || p.y > Max.y + 14f) continue;
                for (int i = 0; i < 2; i++)
                {
                    float a = R(0f, Mathf.PI * 2f), d = r * R(.3f, .9f);
                    Leaf(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d, R(1.2f, 2.3f));
                }
            }
            for (int i = 0; i < 40; i++) Leaf(new Vector2(R(Min.x, Max.x), R(Min.y, Max.y)), R(.9f, 1.7f));

            // ---------------------------------------------------------------- сырая земля и камешки
            var dirtMats = new List<Material>();
            for (int i = 0; i < 6; i++) dirtMats.Add(LitMaterial("Dirt " + i, "dirt_" + i, transparent: true));
            var gravel = FindGravel();
            var placedDirt = new List<Vector2>();
            int dirtCount = 0, pebbleCount = 0;
            for (int i = 0; i < 1400 && dirtCount < 60; i++)
            {
                var p = new Vector2(R(Min.x, Max.x), R(Min.y, Max.y));
                float m = mask(p);
                bool edge = m > .12f && m < .42f;
                bool meadow = m < .03f && random.NextDouble() < .12;
                if (!edge && !meadow) continue;
                if (Water(p, 1f)) continue;
                bool crowded = false;
                foreach (var q in placedDirt) if ((q - p).sqrMagnitude < 9f) { crowded = true; break; }
                if (crowded) continue;
                placedDirt.Add(p);
                float size = edge ? R(1.4f, 2.6f) : R(1f, 1.8f);
                var d = Decal(dirt, "Земля " + (++dirtCount), dirtMats[random.Next(dirtMats.Count)], p, Ground(p) + .012f, new Vector2(size, size * R(.75f, 1.1f)), R(0f, 360f));
                // Пятна — лёгкий налёт, а не заплатки: у дорожек плотнее, на лугу еле видно.
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", new Color(1f, 1f, 1f, edge ? .65f : .45f));
                d.GetComponent<MeshRenderer>().SetPropertyBlock(block);
                if (gravel != null && random.NextDouble() < .65)
                {
                    int k = random.Next(3, 7);
                    for (int s = 0; s < k; s++)
                    {
                        var at = p + new Vector2(R(-size, size), R(-size, size)) * .45f;
                        var go = Object.Instantiate(gravel.gameObject, pebbles);
                        go.name = "Камешек " + (++pebbleCount);
                        go.transform.position = new Vector3(at.x, Ground(at) + .01f, at.y);
                        go.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                        Vector3 source = gravel.lossyScale;
                        float scale = R(.8f, 2.2f);
                        go.transform.localScale = new Vector3(source.x, source.y, source.z) * scale;
                    }
                }
            }

            // ---------------------------------------------------------------- пена у опор моста и камней
            var foamMat = ShaderMaterial("Water foam", "Razlom/Camp Water Foam");
            Vector3 flow = river.transform.right;
            float flowYaw = Mathf.Atan2(flow.x, flow.z) * Mathf.Rad2Deg - 90f;
            int foamCount = 0;
            foreach (var point in BridgePosts(camp))
                Decal(foam, "Пена у опоры " + (++foamCount), foamMat, point, WaterY + .01f, new Vector2(1.7f, 1f), flowYaw);
            var bankStones = camp.Find("Река — нижняя граница лагеря/Берега — лёгкие камни и трава");
            if (bankStones != null)
                foreach (Transform stone in bankStones)
                {
                    if (!stone.name.Contains("камень")) continue;
                    var r = stone.GetComponent<Renderer>();
                    if (r == null || r.bounds.size.x < .4f) continue;
                    var p = new Vector2(r.bounds.center.x, r.bounds.center.z);
                    if (!Water(p, .15f)) continue;
                    Decal(foam, "Пена у камня " + (++foamCount), foamMat, p, WaterY + .01f, new Vector2(1.2f, .75f), flowYaw);
                }

            // ---------------------------------------------------------------- камыш и кувшинки у берегов
            var reedMat = LitMaterial("Reeds", "reed_atlas", twoSided: true);
            Mesh reedMesh = ReedClump();
            var lilyMats = new List<Material>();
            for (int i = 0; i < 8; i++) lilyMats.Add(LitMaterial("Lily " + i, "lily_" + i, cutout: true));
            Vector2 bridge = BridgeCentre(camp);
            int reedCount = 0, lilyCount = 0;
            for (float lx = -40f; lx <= 40f; lx += .9f)
            {
                float c = river.CentreAt(lx);
                foreach (int side in new[] { 1, -1 })
                {
                    float noise = Mathf.PerlinNoise(lx * .21f + (side > 0 ? 2.3f : 7.9f), side > 0 ? 1.7f : 4.4f);
                    // Камыш: куртины по самой кромке, часть — в воде по щиколотку.
                    if (noise > .52f && random.NextDouble() < .75)
                    {
                        float lz = c + side * (river.Width * .5f + R(-.45f, .2f));
                        Vector3 w = river.transform.TransformPoint(new Vector3(lx + R(-.3f, .3f), 0f, lz));
                        var p = new Vector2(w.x, w.z);
                        if (p.x > -30f && p.x < 24f && (p - bridge).sqrMagnitude > 16f)
                        {
                            var go = new GameObject("Камыш " + (++reedCount));
                            go.transform.SetParent(reeds, false);
                            go.transform.position = new Vector3(p.x, Mathf.Min(Ground(p), WaterY + .05f) - .05f, p.y);
                            go.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                            go.transform.localScale = Vector3.one * R(.8f, 1.35f);
                            go.AddComponent<MeshFilter>().sharedMesh = reedMesh;
                            var mr = go.AddComponent<MeshRenderer>();
                            mr.sharedMaterial = reedMat;
                            mr.shadowCastingMode = ShadowCastingMode.On;
                        }
                    }
                    // Кувшинки: тихие места у берега, подальше от моста.
                    if (noise < .38f && random.NextDouble() < .22)
                    {
                        float lz = c + side * (river.Width * .5f - R(.5f, 1.3f));
                        Vector3 w = river.transform.TransformPoint(new Vector3(lx, 0f, lz));
                        var centre = new Vector2(w.x, w.z);
                        if (centre.x < -28f || centre.x > 22f || (centre - bridge).sqrMagnitude < 25f) continue;
                        int k = random.Next(2, 6);
                        for (int s = 0; s < k; s++)
                        {
                            var p = centre + new Vector2(R(-.8f, .8f), R(-.8f, .8f));
                            int variant = random.NextDouble() < .18 ? (random.NextDouble() < .5 ? 1 : 4) : new[] { 0, 2, 3, 5, 6, 7 }[random.Next(6)];
                            float size = variant == 6 ? R(.7f, 1f) : R(.45f, .8f);
                            Decal(lilies, "Кувшинка " + (++lilyCount), lilyMats[variant], p, WaterY + .012f + s * .001f, new Vector2(size, size), R(0f, 360f));
                        }
                    }
                }
            }

            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(scene);
            return $"Детали: тени {shadowCount}, листва {leafCount}, земля {dirtCount}, камешки {pebbleCount}, пена {foamCount}, камыш {reedCount}, кувшинки {lilyCount}";
        }

        static Transform Sub(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>Габарит предмета по его рендерерам (у LOD — только ближний уровень, без частиц).</summary>
        static bool Footprint(Transform t, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            var lod = t.GetComponentInChildren<LODGroup>();
            var renderers = lod != null && lod.GetLODs().Length > 0 ? lod.GetLODs()[0].renderers : t.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                if (r == null || r is ParticleSystemRenderer || !r.enabled) continue;
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        /// <summary>Высота земли по сеткам «рисованной» земли, ячейка 1 м, берётся верхняя.</summary>
        static Dictionary<Vector2Int, float> HeightGrid(Transform camp)
        {
            var grid = new Dictionary<Vector2Int, float>();
            foreach (var filter in camp.GetComponentsInChildren<MeshFilter>())
            {
                var r = filter.GetComponent<Renderer>();
                if (r == null || r.sharedMaterial == null || !r.sharedMaterial.shader.name.Contains("Painterly Ground")) continue;
                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                var tr = filter.transform;
                foreach (var v in mesh.vertices)
                {
                    var w = tr.TransformPoint(v);
                    var c = new Vector2Int(Mathf.RoundToInt(w.x), Mathf.RoundToInt(w.z));
                    if (!grid.TryGetValue(c, out float h) || w.y > h) grid[c] = w.y;
                }
            }
            return grid;
        }

        /// <summary>Маска нарисованных дорожек (та же, что прячет траву в CampBreeze): 0 — трава, 1 — дорожка.</summary>
        static System.Func<Vector2, float> PathMask()
        {
            var tex = Shader.GetGlobalTexture("_CampPaintedPaths") as Texture2D;
            Vector4 b = Shader.GetGlobalVector("_CampPaintedPathBounds");
            if (tex == null || !tex.isReadable || b.z <= 0f) return _ => 0f;
            return p =>
            {
                float u = (p.x - b.x) / b.z, v = (p.y - b.y) / b.w;
                if (u < 0f || v < 0f || u > 1f || v > 1f) return 0f;
                return tex.GetPixelBilinear(u, v).r;
            };
        }

        static Transform FindGravel()
        {
            var group = GameObject.Find("Authored World/CampRoot/Детали лагеря — гравий у оград");
            if (group == null) return null;
            foreach (Transform t in group.transform) if (t.GetComponent<Renderer>() != null) return t;
            return null;
        }

        static Vector2 BridgeCentre(Transform camp)
        {
            foreach (var filter in camp.GetComponentsInChildren<MeshFilter>())
                if (filter.name.StartsWith("tripo_convert_85437eb2"))
                {
                    var b = filter.GetComponent<Renderer>().bounds;
                    return new Vector2(b.center.x, b.center.z);
                }
            return new Vector2(2.8f, -21.5f);
        }

        /// <summary>Где опоры моста входят в воду: вершины сетки моста ниже воды, собранные в пятна по 0,8 м.</summary>
        static List<Vector2> BridgePosts(Transform camp)
        {
            var points = new List<Vector2>();
            foreach (var filter in camp.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.name.StartsWith("tripo_convert_85437eb2") || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                var cells = new Dictionary<Vector2Int, (Vector2 sum, int n)>();
                foreach (var v in filter.sharedMesh.vertices)
                {
                    var w = filter.transform.TransformPoint(v);
                    if (w.y > WaterY + .06f || w.y < WaterY - .6f) continue;
                    var c = new Vector2Int(Mathf.FloorToInt(w.x / .8f), Mathf.FloorToInt(w.z / .8f));
                    cells.TryGetValue(c, out var acc);
                    cells[c] = (acc.sum + new Vector2(w.x, w.z), acc.n + 1);
                }
                foreach (var cell in cells.Values)
                {
                    if (cell.n < 6) continue;
                    var p = cell.sum / cell.n;
                    bool near = false;
                    foreach (var q in points) if ((q - p).sqrMagnitude < .9f) { near = true; break; }
                    if (!near) points.Add(p);
                }
            }
            return points;
        }

        static Mesh Quad()
        {
            string path = Folder + "/Ground detail quad.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = new Mesh { name = "Ground detail quad" };
            mesh.vertices = new[] { new Vector3(-.5f, 0f, -.5f), new Vector3(.5f, 0f, -.5f), new Vector3(.5f, 0f, .5f), new Vector3(-.5f, 0f, .5f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Куртина камыша: 13 стеблей-лезвий и два початка рогоза. Атлас: слева стебли, справа початки.</summary>
        static Mesh ReedClump()
        {
            string path = Folder + "/Reed clump.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = "Reed clump" }; AssetDatabase.CreateAsset(mesh, path); }
            else mesh.Clear();
            var random = new System.Random(77);
            float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            void Blade(Vector3 root, float height, float width, float yaw, float lean, float u0, float u1, bool tip)
            {
                var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                var bend = Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.right * lean;
                int i = v.Count;
                v.Add(root - dir * width * .5f); uv.Add(new Vector2(u0, 0f));
                v.Add(root + dir * width * .5f); uv.Add(new Vector2(u1, 0f));
                v.Add(root + Vector3.up * height * .55f + bend * .35f - dir * width * .35f); uv.Add(new Vector2(u0, .55f));
                v.Add(root + Vector3.up * height * .55f + bend * .35f + dir * width * .35f); uv.Add(new Vector2(u1, .55f));
                if (tip) { v.Add(root + Vector3.up * height + bend); uv.Add(new Vector2((u0 + u1) * .5f, 1f)); }
                else { v.Add(root + Vector3.up * height + bend - dir * width * .3f); uv.Add(new Vector2(u0, 1f)); v.Add(root + Vector3.up * height + bend + dir * width * .3f); uv.Add(new Vector2(u1, 1f)); }
                tri.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
                if (tip) tri.AddRange(new[] { i + 2, i + 4, i + 3 });
                else tri.AddRange(new[] { i + 2, i + 4, i + 3, i + 3, i + 4, i + 5 });
            }
            // Густая куртина: 22 широких стебля (первый вариант с 13 узкими читался сухими палками).
            for (int b = 0; b < 22; b++)
            {
                float a = R(0f, Mathf.PI * 2f), d = R(0f, .34f);
                var root = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                Blade(root, R(.5f, 1.1f), R(.08f, .13f), R(0f, 360f), R(.08f, .3f), .02f, .46f, true);
            }
            for (int c = 0; c < 2; c++)
            {
                var root = new Vector3(R(-.12f, .12f), 0f, R(-.12f, .12f));
                float h = R(1.05f, 1.3f);
                Blade(root, h, .025f, R(0f, 180f), .04f, .2f, .26f, false);
                // Початок: два скрещённых узких квада на верхушке стебля.
                for (int k = 0; k < 2; k++)
                {
                    float yaw = k * 90f;
                    var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                    var top = root + Vector3.up * (h - .22f) + Quaternion.Euler(0f, 90f, 0f) * Vector3.right * .02f;
                    int i = v.Count;
                    v.Add(top - dir * .03f); uv.Add(new Vector2(.55f, 0f));
                    v.Add(top + dir * .03f); uv.Add(new Vector2(.95f, 0f));
                    v.Add(top - dir * .03f + Vector3.up * .2f); uv.Add(new Vector2(.55f, 1f));
                    v.Add(top + dir * .03f + Vector3.up * .2f); uv.Add(new Vector2(.95f, 1f));
                    tri.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
                }
            }
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            // Нормали вверх, как у травы лагеря: обе стороны стебля освещены одинаково, без чёрной изнанки.
            var normals = new Vector3[v.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        static Material ShaderMaterial(string name, string shaderName)
        {
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find(shaderName)) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>URP/Lit без блеска: листва и кувшинки — с отсечением по альфе, земля — полупрозрачная, камыш — двусторонний.</summary>
        static Material LitMaterial(string name, string texture, bool cutout = false, bool transparent = false, bool twoSided = false)
        {
            string texPath = Folder + "/" + texture + ".png";
            var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (importer != null && (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp))
            {
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            string path = Folder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            if (cutout)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", .5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent - 70;
            }
            if (twoSided) m.SetFloat("_Cull", 0f);
            m.SetFloat("_ReceiveShadows", 1f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
