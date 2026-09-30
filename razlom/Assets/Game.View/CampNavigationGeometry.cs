using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Game.View
{
    /// <summary>Простые основания препятствий. Авторская роль имеет приоритет над каталогом старых ассетов.</summary>
    public static class CampNavigationGeometry
    {
        public struct Footprint
        {
            public Transform Root;
            public CampObstacleRole Role;
            public Bounds LocalBounds;
            public Vector3 LocalCenter;
            public float Radius;
            public float Scale;
        }

        public static CampObstacleRole LegacyRole(string name, float height, float groundClearance)
            => CampNavigationRules.LegacyRole(name, height, groundClearance);

        public static bool TryDescribe(MeshFilter mesh, Transform campRoot, float ground, out Footprint footprint)
        {
            footprint = default;
            if (mesh == null || mesh.sharedMesh == null || IsVisualOnly(mesh.transform)
                || mesh.sharedMesh.name == "Shadow Quad" || mesh.GetComponentInParent<CampFlameProView>() != null
                || mesh.GetComponentInParent<CampDummyView>() != null || mesh.GetComponentInParent<CampMagicDecoration>() != null
                || mesh.GetComponentInParent<CampRiver>() != null || mesh.GetComponentInParent<CampGroundStudy>() != null
                || mesh.sharedMesh.name == "Объём луча арки") return false;

            var explicitRole = mesh.GetComponentInParent<CampNavigationObstacle>();
            Transform model = explicitRole != null ? explicitRole.transform : FindModelRoot(mesh.transform, campRoot);
            var lod = mesh.GetComponentInParent<LODGroup>();
            if (lod != null && explicitRole == null) model = lod.transform;
            if (lod != null && !IsNearLod(mesh, lod)) return false;

            // Постоянное основание не читает габариты сменной видимой модели.
            Bounds bounds = explicitRole != null && !explicitRole.FitVisualFootprint
                ? new Bounds(explicitRole.Center, explicitRole.Size) : LocalBounds(model);
            Vector3 bottom = model.TransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            float worldHeight = bounds.size.y * Mathf.Abs(model.lossyScale.y);
            string identity = model.name + " " + mesh.name + " " + mesh.sharedMesh.name;
            CampObstacleRole role = explicitRole != null ? explicitRole.Role : LegacyRole(identity, worldHeight, bottom.y - ground);
            // В окружении бывает настоящая граница леса; обычный декор остаётся проходимым.
            if (explicitRole == null && mesh.GetComponentInParent<CampSceneryDecoration>() != null
                && role != CampObstacleRole.Boundary && role != CampObstacleRole.Trunk) return false;
            if (role == CampObstacleRole.Passable) return false;

            float worldScale = Mathf.Max(Mathf.Abs(model.lossyScale.x), Mathf.Abs(model.lossyScale.z));
            // Старый CapsuleCollider импортированной ели охватывал крону, а не основание ствола.
            // Явный авторский Radius сохраняется; старым моделям задаём небольшой мировой радиус.
            float trunkRadius = CampNavigationRules.LegacyTrunkRadius(Mathf.Min(bounds.extents.x, bounds.extents.z), worldScale);
            footprint = new Footprint { Root = model, Role = role, LocalBounds = bounds,
                LocalCenter = explicitRole != null && !explicitRole.FitVisualFootprint ? explicitRole.Center
                    : Vector3.zero,
                Radius = explicitRole != null ? explicitRole.Radius : role == CampObstacleRole.Trunk
                    ? trunkRadius : 0,
                Scale = explicitRole != null ? explicitRole.FootprintScale : .9f };
            return true;
        }

        static Transform FindModelRoot(Transform child, Transform campRoot)
        {
            for (Transform at = child; at != null && at != campRoot; at = at.parent)
            {
                string name = at.name.ToLowerInvariant();
                if (CampNavigationRules.IsModelRootName(name, at.GetComponent<MeshFilter>() != null
                    || at.GetComponent<Renderer>() != null)) return at;
            }
            return child;
        }

        static bool IsVisualOnly(Transform node)
        {
            for (Transform at = node; at != null; at = at.parent)
                if (CampNavigationRules.IsVisualOnly(at.name)) return true;
            return false;
        }

        static bool IsNearLod(MeshFilter mesh, LODGroup group)
        {
            LOD[] lods = group.GetLODs();
            if (lods.Length == 0) return true;
            var own = mesh.GetComponent<Renderer>();
            foreach (Renderer renderer in lods[0].renderers) if (renderer == own) return true;
            return false;
        }

        static Bounds LocalBounds(Transform model)
        {
            bool found = false; Bounds combined = default;
            foreach (MeshFilter mesh in model.GetComponentsInChildren<MeshFilter>())
            {
                if (mesh.sharedMesh == null) continue;
                var lod = mesh.GetComponentInParent<LODGroup>();
                if (lod != null && !IsNearLod(mesh, lod)) continue;
                Bounds bounds = mesh.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = model.InverseTransformPoint(mesh.transform.TransformPoint(corner));
                    if (!found) { combined = new Bounds(local, Vector3.zero); found = true; }
                    else combined.Encapsulate(local);
                }
            }
            return found ? combined : new Bounds(Vector3.up, Vector3.one);
        }

        public static List<Footprint> Collect(Transform campRoot, Transform bridge, float ground)
        {
            var result = new List<Footprint>(); var seen = new HashSet<Transform>();
            foreach (MeshFilter mesh in campRoot.GetComponentsInChildren<MeshFilter>())
            {
                if (bridge != null && mesh.transform.IsChildOf(bridge)) continue;
                if (!TryDescribe(mesh, campRoot, ground, out var footprint) || !seen.Add(footprint.Root)) continue;
                result.Add(footprint);
            }
            // Явный объём границы может существовать и без видимого меша.
            foreach (var proxy in campRoot.GetComponentsInChildren<CampNavigationObstacle>())
            {
                if (proxy.Role == CampObstacleRole.Passable || IsVisualOnly(proxy.transform) || !seen.Add(proxy.transform)) continue;
                result.Add(new Footprint { Root = proxy.transform, Role = proxy.Role, Radius = proxy.Radius,
                    LocalBounds = new Bounds(proxy.Center, proxy.Size), LocalCenter = proxy.Center, Scale = proxy.FootprintScale });
            }
            return result;
        }

        public static void AddSources(List<NavMeshBuildSource> sources, List<Footprint> footprints, float ground)
        {
            foreach (Footprint footprint in footprints) sources.Add(Source(footprint, ground));
        }

        public static NavMeshBuildSource Source(Footprint footprint, float ground)
        {
            Transform root = footprint.Root; Bounds bounds = footprint.LocalBounds;
            bool round = footprint.Role == CampObstacleRole.Trunk || footprint.Role == CampObstacleRole.FirePit;
            if (round)
            {
                float radius = footprint.Role == CampObstacleRole.FirePit
                        ? Mathf.Max(bounds.extents.x * Mathf.Abs(root.lossyScale.x), bounds.extents.z * Mathf.Abs(root.lossyScale.z)) * footprint.Scale
                        : footprint.Radius * Mathf.Max(Mathf.Abs(root.lossyScale.x), Mathf.Abs(root.lossyScale.z));
                Vector3 at = root.TransformPoint(footprint.Role == CampObstacleRole.Trunk ? footprint.LocalCenter : bounds.center);
                at.y = ground + 1;
                return new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Capsule, area = 1,
                    transform = Matrix4x4.TRS(at, Quaternion.identity, Vector3.one), size = new Vector3(radius * 2, 2, radius * 2) };
            }
            Vector3 center = root.TransformPoint(bounds.center); center.y = ground + 1;
            Vector3 size = Vector3.Scale(bounds.size, new Vector3(Mathf.Abs(root.lossyScale.x), 1, Mathf.Abs(root.lossyScale.z)));
            size.x = Mathf.Max(.08f, size.x * footprint.Scale); size.z = Mathf.Max(.08f, size.z * footprint.Scale); size.y = 2;
            return new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, area = 1,
                transform = Matrix4x4.TRS(center, Quaternion.Euler(0, root.eulerAngles.y, 0), Vector3.one), size = size };
        }

        // Диагностика использует те же основания и запас тела, что построение карты.
        // Декоративные коллайдеры больше не закрывают ходьбу и не попадают в объяснение.
        public static bool Covers(Footprint footprint, Vector3 point, float clearance)
        {
            var source = Source(footprint, point.y);
            Vector3 local = source.transform.inverse.MultiplyPoint3x4(point);
            if (source.shape == NavMeshBuildSourceShape.Capsule)
                return new Vector2(local.x, local.z).sqrMagnitude <= Mathf.Pow(source.size.x * .5f + clearance, 2);
            float dx = Mathf.Max(0, Mathf.Abs(local.x) - source.size.x * .5f);
            float dz = Mathf.Max(0, Mathf.Abs(local.z) - source.size.z * .5f);
            return dx * dx + dz * dz <= clearance * clearance;
        }
    }
}
