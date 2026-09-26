using UnityEngine;
using UnityEngine.AI;

namespace Game.View
{
    /// <summary>
    /// Мягкая тень под ногами жителей лагеря (владелец 24 сентября выбрал вариант «А» из листа
    /// artifacts/camp-readability-20260924: фигуры стоят на земле, сами не меняются).
    /// Пятно — плоский квадрат с CampContactShadow (умножение, мягкий край) на уровне земли,
    /// под Эни, Веном и Лео ставится один раз. Под героем пятна нет — владелец его убрал.
    /// Создаётся после сборки навигации (добавляет CampPlayerView) и на проходимость не влияет:
    /// коллайдеров нет. Материалы персонажей не трогаются — высветление отвергнуто.
    /// </summary>
    public sealed class CampCharacterShadows : MonoBehaviour
    {
        [Tooltip("Размер пятна под жителем, метры")] public float NpcSize = 2f;
        [Tooltip("Пятно чуть сплюснуто по глубине, как тень от фигуры")] public float Flatten = .8f;
        [Range(0f, 1f)] public float Strength = .92f;
        [Range(.05f, 1f)] public float Softness = .7f;
        [Tooltip("Подъём над землёй против мерцания, метры")] public float Lift = .03f;

        Transform _root;
        GameObject _anchor;
        Material _material;
        Mesh _quad;

        void Start()
        {
            var source = Resources.Load<Material>("Environment/Camp/GroundDetails/Contact shadow");
            if (source == null) { enabled = false; return; }
            _material = new Material(source) { name = "Тень под ногами (лагерь)" };
            _material.SetFloat("_Strength", Strength);
            _material.SetFloat("_Softness", Softness);
            _quad = BuildQuad();
            _root = new GameObject("Тени под ногами жителей").transform;
            foreach (var npc in FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Exclude))
            {
                if (npc.Kind == CampServiceKind.Tent) continue;
                Bounds body = BodyBounds(npc);
                var blob = Blob("Под жителем — " + npc.Kind, NpcSize);
                blob.position = new Vector3(body.center.x, GroundAt(body.center, body.min.y) + Lift, body.center.z);
                if (_anchor == null) _anchor = npc.gameObject;
            }
        }

        // Корень пятен лежит вне иерархии лагеря: когда лагерь скрыт на время разлома,
        // пятна оставались на своих местах и проступали тёмными кляксами на траве арен.
        void LateUpdate()
        {
            if (_root != null) _root.gameObject.SetActive(_anchor != null && _anchor.activeInHierarchy);
        }

        void OnDisable()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
            if (_material != null) Destroy(_material);
            if (_quad != null) Destroy(_quad);
        }

        Transform Blob(string name, float size)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_root, false);
            go.transform.localScale = new Vector3(size, 1f, size * Flatten);
            go.GetComponent<MeshFilter>().sharedMesh = _quad;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        /// <summary>
        /// Земля под жителем по навигации: габариты скин-меша у Вена и Эни уходят ниже пола,
        /// и пятно пряталось под землю. Нижняя кромка тела — только если навигации рядом нет.
        /// </summary>
        static float GroundAt(Vector3 at, float fallback)
        {
            if (NavMesh.SamplePosition(at, out var hit, 3f, NavMesh.AllAreas)) return Mathf.Max(hit.position.y, fallback);
            return fallback;
        }

        /// <summary>Габариты тела жителя: скин-меши модели с аниматором, иначе все рендеры.</summary>
        static Bounds BodyBounds(CampServiceNpc npc)
        {
            var animator = npc.GetComponentInChildren<Animator>();
            Transform model = animator != null ? animator.transform : npc.transform;
            Renderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (renderers.Length == 0) renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(npc.transform.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        /// <summary>Плоский квадрат 1×1 в плоскости XZ, лицом вверх, UV 0..1.</summary>
        static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "Квадрат тени под ногами" };
            mesh.vertices = new[] { new Vector3(-.5f, 0f, -.5f), new Vector3(-.5f, 0f, .5f), new Vector3(.5f, 0f, .5f), new Vector3(.5f, 0f, -.5f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, .1f, 1f));
            return mesh;
        }
    }
}
