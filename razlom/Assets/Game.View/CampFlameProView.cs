using UnityEngine;

namespace Game.View
{
    /// <summary>Авторский Flame Pro из Arcadia вместо прежнего эмиттера костра.</summary>
    public sealed class CampFlameProView : MonoBehaviour
    {
        private const float Width = 2.5875f;
        private const float Height = 3.36f;
        private const float FlameBaseY = .12f;
        [System.NonSerialized] private Mesh _mesh;
        private Camera _camera;

        public static void Install(Transform campfire)
        {
            if (campfire == null || campfire.GetComponentInChildren<CampFlameProView>(true) != null) return;
            var material = Resources.Load<Material>("Environment/Camp/FlamePro/M_FlamePro");
            var oldFire = campfire.Find("Campfire_VFX/Fire_Core");
            if (material == null || oldFire == null) return;
            // Источники света и дрова остаются в авторской сцене; заменяется только пламя.
            oldFire.gameObject.SetActive(false);
            var obj = new GameObject("Arcadia Flame Pro");
            obj.transform.SetParent(oldFire.parent, false);
            // Масштабируем от основания огня в атласе, чтобы рост не уводил его с дров.
            obj.transform.localPosition = new Vector3(oldFire.localPosition.x,
                FlameBaseY + Height * .34f, oldFire.localPosition.z);
            var view = obj.AddComponent<CampFlameProView>();
            view._mesh = new Mesh { name = "Camp Flame Pro billboard" };
            view._mesh.vertices = new[] { new Vector3(-Width/2,-Height/2,0), new Vector3(Width/2,-Height/2,0),
                new Vector3(-Width/2,Height/2,0), new Vector3(Width/2,Height/2,0) };
            view._mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            view._mesh.triangles = new[] { 0,2,1,1,2,3 };
            view._mesh.RecalculateBounds();
            obj.AddComponent<MeshFilter>().sharedMesh = view._mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            view._camera = Camera.main;
            view.LateUpdate();
        }

        private void LateUpdate()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;
            // Вертикальная ось сохраняет основание огня на дровах при наклоне камеры.
            Vector3 facing = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up);
            if (facing.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
        }

        private void OnDestroy() { if (_mesh != null) { if(Application.isPlaying)Destroy(_mesh);else DestroyImmediate(_mesh); } }
    }
}
