using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Студия портрета Пелага: настоящая модель героя вдали от игровых камер,
    /// своя камера на отдельном слое и материалы без сценового света.
    ///
    /// Один код на двух потребителей — живой портрет в палатке и снимок для
    /// HUD. Раньше студия жила только в палатке; второй копией она разошлась
    /// бы с первой при первой же правке материала.
    /// </summary>
    public static class PortraitStudio
    {
        public const int Layer = 31;

        public static GameObject Create(ArenaView arena, string name, Vector3 position, Vector3 cameraLocalPosition,
            float orthographicSize, RenderTexture target, List<Material> materials, out Camera camera)
        {
            var stage = new GameObject(name);
            stage.transform.position = position;

            var model = arena.CreateCampPlayer();
            model.name = name + " Pelag";
            model.transform.SetParent(stage.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0, -12, 0);
            foreach (var behavior in model.GetComponentsInChildren<MonoBehaviour>()) behavior.enabled = false;
            foreach (var child in model.GetComponentsInChildren<Transform>(true))
                if (child.name == "Pelag_AnchorGrip_Equipped") child.gameObject.SetActive(false);
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;

            var animator = model.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            var shader = Resources.Load<Shader>("Shaders/InventoryPortrait");
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    var source = shared[i];
                    if (source == null || shader == null) continue;
                    Texture texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.mainTexture;
                    var material = new Material(shader);
                    material.SetTexture("_BaseMap", texture);
                    material.SetColor("_BaseColor", new Color(1.35f, 1.35f, 1.35f, 1));
                    shared[i] = material;
                    materials.Add(material);
                }
                renderer.sharedMaterials = shared;
            }

            var cameraObject = new GameObject(name + " camera", typeof(Camera));
            cameraObject.transform.SetParent(stage.transform, false);
            camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = orthographicSize;
            cameraObject.transform.localPosition = cameraLocalPosition;
            cameraObject.transform.localRotation = Quaternion.Euler(0, 180, 0);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << Layer;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 10;
            camera.allowHDR = false;
            camera.targetTexture = target;
            return stage;
        }
    }

    /// <summary>
    /// Статичный портрет для HUD. Снимается ОДИН раз с настоящей модели и дальше
    /// рисуется как обычная текстура.
    ///
    /// Живая камера в HUD стоила бы прохода рендера каждый кадр ради картинки,
    /// которая не меняется: снаряжение меняет статы, а не модель героя.
    /// </summary>
    public static class HeroPortrait
    {
        private const int Size = 256;
        private static Texture2D _baked;

        public static Texture2D Texture => _baked;

        public static IEnumerator Bake(ArenaView arena)
        {
            if (_baked != null || arena == null) yield break;

            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            target.Create();
            var materials = new List<Material>();
            // Отдельная точка, не та, где стоит студия палатки: камеры видят
            // один и тот же слой, и две модели в одной точке попали бы в кадр обе.
            GameObject stage = PortraitStudio.Create(arena, "HUD portrait studio", new Vector3(-1000, 1000, 1000),
                new Vector3(0, 1.6f, 4), .38f, target, materials, out Camera camera);

            // Два кадра: аниматор успевает поставить позу покоя вместо позы
            // привязки, а камера — её отрисовать.
            yield return null;
            yield return new WaitForEndOfFrame();

            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var baked = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Pelag HUD portrait" };
            baked.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            baked.Apply();
            RenderTexture.active = previous;
            _baked = baked;

            if (camera != null) camera.targetTexture = null;
            Object.Destroy(stage);
            target.Release();
            Object.Destroy(target);
            foreach (var material in materials) Object.Destroy(material);
        }
    }
}
