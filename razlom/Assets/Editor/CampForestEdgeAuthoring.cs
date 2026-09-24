using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.EditorTools
{
    /// <summary>
    /// Стена леса и дымка по краю лагеря (владелец 24 сентября: «сверху видно, что лагерь стоит на
    /// ровной плоскости… камера и миникарта не должны показывать, где кончается карта»).
    ///
    /// Проходимым был весь луг до x ±40 и z +35; теперь поляну лагеря обходит живая изгородь
    /// (кусты в лагере — препятствие), за ней три ряда елей и изредка лиственных деревьев, а над
    /// ними — кольцо дымки (Razlom/Camp Edge Haze). Деревьям, чья вечерняя тень (солнце 21°,
    /// тень ~2,5 высоты) упала бы на поляну, тени выключены: иначе стена погасила бы лагерь.
    ///
    /// Перезапуск пересобирает группу целиком; расстановка детерминирована (зерно).
    /// </summary>
    public static class CampForestEdgeAuthoring
    {
        public const string GroupName = "Стена леса и дымка по краю";

        /// <summary>
        /// Край поляны лагеря по часовой стрелке. Река идёт наискось (на западе поднимается к северу),
        /// поэтому край начинается у её ближнего берега на западе, обходит север и кончается на востоке
        /// у края проходимой земли (карта проходимости 24 сентября).
        /// </summary>
        static readonly Vector2[] Clearing =
        {
            new Vector2(-27f, 1f), new Vector2(-26f, 8f), new Vector2(-23.5f, 14f), new Vector2(-19.5f, 19f),
            new Vector2(-15f, 21.5f), new Vector2(-8f, 22.5f), new Vector2(-2f, 21f), new Vector2(2f, 18.5f), new Vector2(7.5f, 18.5f),
            new Vector2(11.5f, 20f), new Vector2(16f, 19f), new Vector2(18.5f, 13f), new Vector2(19f, 5f), new Vector2(19f, -3f),
            new Vector2(19.5f, -11f),
        };

        /// <summary>Пустые места за рекой по сторонам от поляны алхимика (там лес уже густой, кроме них).</summary>
        static readonly Vector2[] FarBankGaps = { new Vector2(11f, -22f), new Vector2(15f, -23.5f), new Vector2(13f, -27f), new Vector2(-11.5f, -21.5f), new Vector2(18.5f, -28.5f) };

        [MenuItem("Разлом/Лагерь/Стена леса и дымка по краю")]
        public static void BuildFromMenu() => Debug.Log(Build());

        public static string Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var camp = GameObject.Find("Authored World/CampRoot")?.transform;
            if (camp == null) return "Нет Authored World/CampRoot";
            var river = Object.FindAnyObjectByType<CampRiver>();
            Transform Source(string path) { var t = camp.Find(path); if (t == null) throw new System.InvalidOperationException("Нет образца " + path); return t; }
            Transform spruce = Source("Ground/Frame/UNS_Spruce_01 (5)");
            Transform broad = Source("Ground/Frame/stylized+tree+3d+model (4)");
            Transform small = Source("Ground/Frame/UNS_Spruce_01_LOD0 (2)");
            Transform bush = Source("Ground/Frame/UNS_Bush_LOD0 (10)");

            Undo.SetCurrentGroupName("Стена леса по краю лагеря");
            int undo = Undo.GetCurrentGroup();
            var old = camp.Find(GroupName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject(GroupName).transform;
            root.SetParent(camp, false);
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Стена леса");
            var hedgeGroup = Group(root, "Живая изгородь");
            var treeGroup = Group(root, "Деревья");
            var farGroup = Group(root, "За рекой");

            // Занятые места: стволы существующих деревьев.
            var trunks = new List<Vector2>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                if (r.bounds.size.y > 2.5f && !(r is ParticleSystemRenderer) && !r.transform.IsChildOf(root))
                    trunks.Add(new Vector2(r.bounds.center.x, r.bounds.center.z));

            var random = new System.Random(24092026);
            float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
            Vector2 sun = SunGround();
            int trees = 0, shadowless = 0, bushes = 0, smalls = 0;

            bool InRiver(Vector2 p, float margin)
            {
                if (river == null) return false;
                Vector3 local = river.transform.InverseTransformPoint(new Vector3(p.x, 0f, p.y));
                return Mathf.Abs(local.z - river.CentreAt(local.x)) < river.Width * .5f + river.BankWidth + margin;
            }
            // Сторона лагеря — где ближний берег (у края земли); дальний берег — отдельная группа.
            bool CampSide(Vector2 p)
            {
                if (river == null) return true;
                Vector3 local = river.transform.InverseTransformPoint(new Vector3(p.x, 0f, p.y));
                return local.z > river.CentreAt(local.x);
            }
            bool Free(Vector2 p, float gap)
            {
                foreach (var t in trunks) if ((t - p).sqrMagnitude < gap * gap) return false;
                return true;
            }
            GameObject Place(Transform source, Transform parent, Vector2 p, float scale, string name)
            {
                var go = Object.Instantiate(source.gameObject, parent);
                go.name = name;
                go.transform.position = new Vector3(p.x, source.position.y, p.y);
                go.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f) * Quaternion.Euler(source.eulerAngles.x, 0f, source.eulerAngles.z);
                go.transform.localScale = source.localScale * scale;
                return go;
            }
            void Tree(Vector2 p, Transform parent)
            {
                bool leafy = random.NextDouble() < .16;
                float scale = leafy ? R(.7f, .95f) : R(.72f, 1.18f);
                var go = Place(leafy ? broad : spruce, parent, p, scale, (leafy ? "Лиственное дерево " : "Ель ") + (++trees));
                float height = 0f;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) height = Mathf.Max(height, r.bounds.size.y);
                if (ShadowFallsOnClearing(p, height, sun))
                {
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
                    shadowless++;
                }
                trunks.Add(p);
            }

            // Изгородь по краю поляны и её продолжение к воде на обоих концах.
            foreach (var (p, n) in Along(Clearing, 1.25f, .2f, random))
            {
                var at = p + n * R(.6f, 1.2f);
                if (!CampSide(at) || InRiver(at, .2f)) continue;
                Place(bush, hedgeGroup, at, R(1f, 1.35f), "Куст изгороди " + (++bushes));
                if (bushes % 3 == 0 && Free(p + n * 2.4f, 1.6f))
                {
                    Place(small, hedgeGroup, p + n * R(2f, 2.8f), R(.9f, 1.3f), "Молодая ель " + (++smalls));
                }
            }
            // Западный конец доводится до воды; восточный упирается в край проходимой земли сам.
            foreach (int end in new[] { 0 })
            {
                Vector2 dir = (Clearing[end] - Clearing[end == 0 ? 1 : end - 1]).normalized;
                Vector2 p = Clearing[end];
                for (int i = 0; i < 12 && !InRiver(p, -river.BankWidth + .1f); i++)
                {
                    p += dir * 1.1f;
                    Place(bush, hedgeGroup, p, R(1.05f, 1.35f), "Куст изгороди у воды " + (++bushes));
                }
            }

            // Три ряда деревьев за изгородью.
            float[] rows = { 3.8f, 8.3f, 12.8f };
            float[] spacing = { 4.3f, 4.9f, 5.6f };
            for (int row = 0; row < rows.Length; row++)
                foreach (var (p, n) in Along(Clearing, spacing[row], 1.1f, random))
                {
                    var at = p + n * (rows[row] + R(-1.2f, 1.2f));
                    if (!CampSide(at) || InRiver(at, 1.5f) || !Free(at, 2.6f)) continue;
                    Tree(at, treeGroup);
                }

            // За рекой: пустые места по сторонам поляны алхимика.
            foreach (var gap in FarBankGaps)
                for (int i = 0; i < 3; i++)
                {
                    var at = gap + new Vector2(R(-2.5f, 2.5f), R(-2.5f, 2.5f));
                    if (InRiver(at, 1.5f) || !Free(at, 3f)) continue;
                    Tree(at, farGroup);
                }

            BuildHaze(root);
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(scene);
            return "Стена леса: деревьев " + trees + " (без тени " + shadowless + "), кустов изгороди " + bushes + ", молодых елей " + smalls;
        }

        static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>Точки вдоль ломаной с шагом и наружной нормалью (обход по часовой — наружу слева).</summary>
        static IEnumerable<(Vector2, Vector2)> Along(Vector2[] line, float step, float jitter, System.Random random)
        {
            float carry = 0f;
            for (int i = 0; i < line.Length - 1; i++)
            {
                Vector2 a = line[i], b = line[i + 1];
                Vector2 d = b - a;
                float length = d.magnitude;
                d /= length;
                var normal = new Vector2(-d.y, d.x);
                float s = carry;
                for (; s < length; s += step)
                {
                    float j = ((float)random.NextDouble() * 2f - 1f) * jitter;
                    yield return (a + d * Mathf.Clamp(s + j, 0f, length), normal);
                }
                carry = s - length;
            }
        }

        static Vector2 SunGround()
        {
            var key = GameObject.Find("Environment/Lighting/Key Light");
            float yaw = key != null ? key.transform.eulerAngles.y : 35f;
            Vector3 forward = Quaternion.Euler(21.4f, yaw, 0f) * Vector3.forward;
            return new Vector2(forward.x, forward.z).normalized;
        }

        static bool ShadowFallsOnClearing(Vector2 p, float height, Vector2 sun)
        {
            float length = height / Mathf.Tan(21.4f * Mathf.Deg2Rad);
            for (float s = 0f; s <= length; s += 1.5f)
                if (InsideClearing(p + sun * s)) return true;
            return false;
        }

        static bool InsideClearing(Vector2 p)
        {
            // Поляна замыкается по реке: от восточного конца обратно к западному.
            bool inside = false;
            int n = Clearing.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 a = Clearing[i], b = Clearing[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Кольцо дымки: три слоя над лесом. Прозрачность в цвете вершин — 0 у внутренней кромки
        /// (над изгородью её нет), плотнее к краю мира. Форма — «квадратный эллипс» вокруг лагеря и берега алхимика.
        /// </summary>
        static void BuildHaze(Transform root)
        {
            var shader = Shader.Find("Razlom/Camp Edge Haze");
            if (shader == null) { Debug.LogWarning("[лагерь] Нет шейдера Razlom/Camp Edge Haze — дымка не построена"); return; }
            const string folder = "Assets/Resources/Environment/Camp/Edge";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Resources/Environment/Camp", "Edge");
            string matPath = folder + "/Camp edge haze.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                material = new Material(shader) { name = "Camp edge haze" };
                material.SetColor("_Color", new Color(.12f, .17f, .15f, 1f));
                material.SetFloat("_Density", .5f);
                AssetDatabase.CreateAsset(material, matPath);
            }
            string meshPath = folder + "/Camp edge haze.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null) { mesh = new Mesh { name = "Camp edge haze" }; AssetDatabase.CreateAsset(mesh, meshPath); }
            else mesh.Clear();

            var centre = new Vector2(-3.5f, -6.5f);
            const float a = 33f, b = 36f;
            float[] rings = { 1f, 1.12f, 1.28f, 1.5f, 1.8f, 2.3f };
            float[] alpha = { 0f, .3f, .55f, .75f, .9f, 1f };
            const int segments = 144;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            for (int s = 0; s <= segments; s++)
            {
                float angle = s * Mathf.PI * 2f / segments;
                float c = Mathf.Cos(angle), si = Mathf.Sin(angle);
                // Сверхэллипс степени 3: к углам поляны кольцо не подходит близко.
                float k = Mathf.Pow(Mathf.Pow(Mathf.Abs(c) / a, 3f) + Mathf.Pow(Mathf.Abs(si) / b, 3f), -1f / 3f);
                for (int r = 0; r < rings.Length; r++)
                {
                    vertices.Add(new Vector3(centre.x + c * k * rings[r], 0f, centre.y + si * k * rings[r]));
                    colors.Add(new Color(1f, 1f, 1f, alpha[r]));
                }
            }
            for (int s = 0; s < segments; s++)
                for (int r = 0; r < rings.Length - 1; r++)
                {
                    int i0 = s * rings.Length + r, i1 = i0 + 1, j0 = i0 + rings.Length, j1 = j0 + 1;
                    triangles.AddRange(new[] { i0, j0, i1, i1, j0, j1 });
                }
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            var haze = Group(root, "Дымка по краю");
            haze.gameObject.AddComponent<CampSceneryDecoration>();
            // Лагерь в сцене масштабирован (CampRoot ×1,6): сетка дымки построена в мировых метрах.
            haze.position = Vector3.zero;
            haze.rotation = Quaternion.identity;
            Vector3 lossy = haze.parent.lossyScale;
            haze.localScale = new Vector3(1f / lossy.x, 1f / lossy.y, 1f / lossy.z);
            foreach (float height in new[] { 1.2f, 3.6f, 7f })
            {
                var layer = new GameObject("Слой дымки " + height.ToString("0.0"));
                layer.transform.SetParent(haze, false);
                layer.transform.localPosition = new Vector3(0f, height, 0f);
                layer.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = layer.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
            }
            AssetDatabase.SaveAssets();
        }
    }
}
