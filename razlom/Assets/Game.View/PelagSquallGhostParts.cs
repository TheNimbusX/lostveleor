using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Пенный двойник Неуловимого (кадр elusive-return-3, выбор владельца 02.10):
    /// полупрозрачный Пелаг из бирюзовой воды и белой пены стоит на точке каста —
    /// туда последний прыжок вернёт героя; с него капает пена. Плюс короткие
    /// двойники на каждом отрыве («его здесь уже нет»).
    ///
    /// Это ОТДЕЛЬНАЯ копия позы, а не правка героя: материалы и яркость самого
    /// Пелага не трогаются (владелец 24.09 отверг высветление героя — «ужасно»).
    /// Поза снимается в миг события Sim: скиннед-меши героя — BakeMesh в свои
    /// меши (по умолчанию, без useScale; часть ставится в позицию и поворот
    /// скина с масштабом 1 — проверено на рендере поз босса 02.10), снаряжение
    /// (сабля, якорь) — тот же меш с мировым масштабом. Прозрачное (тени, VFX,
    /// огонь клинка) не копируется.
    ///
    /// Компонент стоит на корне префаба VFX_Pelag_Squall_Ghost
    /// (Editor/PelagSquallFoamVfxSetup): слоты Part0…Part11 (MeshFilter + MeshRenderer
    /// с M_Squall_Ghost) и частицы капель/лужи/всплеска. Свои меши создаются раз на
    /// объект пула и дальше переписываются; чужие меши никогда не перезаписываются.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PelagSquallGhostParts : MonoBehaviour
    {
        public const int MaxParts = 12;
        public const string MeshName = "Шквал: двойник";

        [SerializeField] private MeshFilter[] _filters = new MeshFilter[0];
        [SerializeField] private MeshRenderer[] _renderers = new MeshRenderer[0];
        [SerializeField] private ParticleSystem _drips, _pool, _burst;

        private Mesh[] _owned;
        private Material _ghost;
        private int _used;
        private MaterialPropertyBlock _block;
        private static readonly List<Renderer> Sources = new List<Renderer>(24);
        private static readonly Dictionary<int, Material[]> Slots = new Dictionary<int, Material[]>();
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        private static readonly int ClockId = Shader.PropertyToID("_Clock");
        private static readonly int BaseId = Shader.PropertyToID("_Base");

        public ParticleSystem Drips => _drips;
        public ParticleSystem Pool => _pool;
        public ParticleSystem Burst => _burst;
        public int Used => _used;
        /// <summary>Мировой охват снятой позы (для капель и всплеска).</summary>
        public Bounds Area { get; private set; }

#if UNITY_EDITOR
        /// <summary>Только сборка префаба (PelagSquallFoamVfxSetup).</summary>
        public void Configure(MeshFilter[] filters, MeshRenderer[] renderers, ParticleSystem drips, ParticleSystem pool, ParticleSystem burst)
        {
            _filters = filters;
            _renderers = renderers;
            _drips = drips;
            _pool = pool;
            _burst = burst;
        }
#endif

        private void Awake()
        {
            if (_filters == null || _filters.Length == 0)
            {
                // Страховка: префаб без ссылок — слоты по именам.
                var filters = new List<MeshFilter>();
                var renderers = new List<MeshRenderer>();
                for (int i = 0; i < MaxParts; i++)
                {
                    Transform part = transform.Find("Part" + i);
                    if (part == null) break;
                    filters.Add(part.GetComponent<MeshFilter>());
                    renderers.Add(part.GetComponent<MeshRenderer>());
                }
                _filters = filters.ToArray();
                _renderers = renderers.ToArray();
            }
            _owned = new Mesh[_filters.Length];
            _ghost = _renderers.Length > 0 && _renderers[0] != null ? _renderers[0].sharedMaterial : null;
        }

        /// <summary>
        /// Снять позу героя <paramref name="body"/> в слоты. Корень двойника уже
        /// поставлен (Begin: позиция героя, без поворота, масштаб 1). Возвращает
        /// число занятых слотов (0 — снимать нечего, двойника не будет).
        /// </summary>
        public int Capture(Transform body)
        {
            _used = 0;
            if (_owned == null || _owned.Length != _filters.Length) _owned = new Mesh[_filters.Length];
            if (body != null && _ghost != null)
            {
                Sources.Clear();
                body.GetComponentsInChildren(false, Sources);
                bool first = true;
                var area = new Bounds();
                foreach (Renderer source in Sources)
                {
                    if (_used >= _filters.Length) break;
                    if (!source.enabled || source.GetComponentInParent<PelagVfxElement>() != null) continue;
                    Material material = source.sharedMaterial;
                    // Прозрачное — тени, вспышки, огонь клинка: двойник из них не собирается.
                    if (material == null || material.renderQueue >= 2500) continue;
                    MeshFilter filter = _filters[_used];
                    MeshRenderer target = _renderers[_used];
                    if (filter == null || target == null) break;
                    Transform part = filter.transform;
                    Mesh mesh;
                    if (source is SkinnedMeshRenderer skin)
                    {
                        if (skin.sharedMesh == null) continue;
                        mesh = Owned(_used);
                        skin.BakeMesh(mesh);
                        part.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                        part.localScale = Vector3.one;
                    }
                    else if (source is MeshRenderer)
                    {
                        MeshFilter from = source.GetComponent<MeshFilter>();
                        if (from == null || from.sharedMesh == null) continue;
                        mesh = from.sharedMesh;
                        part.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                        part.localScale = source.transform.lossyScale;
                    }
                    else continue;
                    filter.sharedMesh = mesh;
                    target.sharedMaterials = SlotMaterials(mesh.subMeshCount);
                    target.enabled = true;
                    if (first) { area = source.bounds; first = false; }
                    else area.Encapsulate(source.bounds);
                    _used++;
                }
                Area = area;
            }
            for (int i = _used; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = false;
            return _used;
        }

        /// <summary>
        /// Вид двойника: <paramref name="opacity"/> 0…1, <paramref name="dissolve"/> 0…1
        /// (растворение пеной сверху вниз), <paramref name="clock"/> — секунды жизни
        /// (пена стекает), <paramref name="ground"/> — земля под ним, цвет формы.
        /// </summary>
        public void Look(float opacity, float dissolve, float clock, float ground, PelagSquallFormLook.Palette palette)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            for (int i = 0; i < _used; i++)
            {
                MeshRenderer r = _renderers[i];
                if (r == null) continue;
                _block.Clear();
                _block.SetFloat(OpacityId, Mathf.Clamp01(opacity));
                _block.SetFloat(DissolveId, Mathf.Clamp01(dissolve));
                _block.SetFloat(ClockId, clock);
                _block.SetFloat(BaseId, ground);
                PelagSquallFormLook.Write(_block, _ghost, palette);
                r.SetPropertyBlock(_block);
                r.enabled = opacity > .002f;
            }
        }

        /// <summary>Случайная точка внутри снятой позы ниже плеч — откуда капает пена.</summary>
        public Vector3 DripPoint()
        {
            Bounds area = Area;
            float y = Mathf.Lerp(area.min.y + .25f, area.max.y - .35f, Random.value);
            Vector2 offset = Random.insideUnitCircle * Mathf.Min(.24f, Mathf.Min(area.extents.x, area.extents.z));
            return new Vector3(area.center.x + offset.x, y, area.center.z + offset.y);
        }

        private Mesh Owned(int i)
        {
            if (_owned[i] == null) _owned[i] = new Mesh { name = MeshName };
            return _owned[i];
        }

        private Material[] SlotMaterials(int count)
        {
            count = Mathf.Max(1, count);
            if (Slots.TryGetValue(count, out Material[] materials) && materials[0] == _ghost) return materials;
            materials = new Material[count];
            for (int i = 0; i < count; i++) materials[i] = _ghost;
            Slots[count] = materials;
            return materials;
        }

        private void OnDestroy()
        {
            if (_owned == null) return;
            foreach (Mesh mesh in _owned) if (mesh != null) Destroy(mesh);
        }
    }
}
