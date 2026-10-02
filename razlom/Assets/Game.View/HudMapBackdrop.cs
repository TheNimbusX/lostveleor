using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    // Камера готовит фон миникарты при смене локации и выключается после кадра.
    // Это часть HUD при запуске игры, не редакторская съёмка или проверка.
    internal sealed class HudMapBackdrop
    {
        /// <summary>Имя корня камеры: по нему находятся камеры, оставшиеся без владельца.</summary>
        public const string RootName = "HUD map backdrop";

        // Камеры живых владельцев. Перезагрузка домена посреди Play (правка скрипта при «Recompile And
        // Continue Playing») теряет C#-владельца, а камера с HideAndDontSave её переживает — и выход из
        // Play тоже (02.10 в редакторе нашлось три таких). Статика при перезагрузке обнуляется, так что
        // всё, чего здесь нет, — сироты.
        static readonly List<GameObject> Live = new List<GameObject>();

        Camera _camera;
        RenderTexture _texture;
        bool _ready;
        bool _heroHidden;
        Renderer[] _heroRenderers;
        bool[] _heroVisibility;
        public Texture Texture => _ready ? _texture : null;

        public void Request(Rect world, float floor)
        {
            if (_camera == null)
            {
                DestroyLeftovers();
                var root = new GameObject(RootName) { hideFlags = HideFlags.HideAndDontSave };
                Live.Add(root);
                _camera = root.AddComponent<Camera>();
                _camera.enabled = false;
                _camera.orthographic = true;
                _camera.depth = -100f;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(.28f, .32f, .18f);
                _camera.allowHDR = false;
                _camera.allowMSAA = false;
                _camera.useOcclusionCulling = false;
                _texture = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32)
                { name = "HUD map scenery", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                _texture.Create();
                _camera.targetTexture = _texture;
                var data = _camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.requiresColorTexture = false;
                data.requiresDepthTexture = false;
                data.volumeLayerMask = 0;
                RenderPipelineManager.beginCameraRendering += BeforeCamera;
                RenderPipelineManager.endCameraRendering += AfterCamera;
            }
            RestoreHero();
            var body = CampPlayerView.Instance?.Body;
            _heroRenderers = body != null ? body.GetComponentsInChildren<Renderer>() : null;
            _heroVisibility = _heroRenderers != null ? new bool[_heroRenderers.Length] : null;
            float elevation = Mathf.Max(60f, world.width + 30f);
            _camera.transform.SetPositionAndRotation(new Vector3(world.center.x, floor + elevation, world.center.y), Quaternion.Euler(90f, 0f, 0f));
            _camera.orthographicSize = world.height * .5f;
            _camera.aspect = 1f;
            _camera.nearClipPlane = .1f;
            _camera.farClipPlane = elevation + 30f;
            _camera.cullingMask = (Camera.main != null ? Camera.main.cullingMask : ~0) & ~LayerMask.GetMask("UI", "EnemyOutline");
            _ready = false;
            _camera.enabled = true;
        }

        void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera || _heroRenderers == null) return;
            _heroHidden = true;
            for (int i = 0; i < _heroRenderers.Length; i++)
            {
                if (_heroRenderers[i] == null) continue;
                _heroVisibility[i] = _heroRenderers[i].forceRenderingOff;
                _heroRenderers[i].forceRenderingOff = true;
            }
        }
        void AfterCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera) return;
            RestoreHero();
            _ready = true;
            _camera.enabled = false;
        }
        void RestoreHero()
        {
            if (!_heroHidden || _heroRenderers == null || _heroVisibility == null) return;
            for (int i = 0; i < _heroRenderers.Length; i++)
                if (_heroRenderers[i] != null) _heroRenderers[i].forceRenderingOff = _heroVisibility[i];
            _heroHidden = false;
        }
        public void Invalidate()
        {
            RestoreHero();
            _ready = false;
            if (_camera != null) _camera.enabled = false;
        }
        public void Dispose()
        {
            Invalidate();
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RenderPipelineManager.endCameraRendering -= AfterCamera;
            if (_camera != null) { Live.Remove(_camera.gameObject); Object.Destroy(_camera.gameObject); }
            if (_texture != null) { _texture.Release(); Object.Destroy(_texture); }
        }

        /// <summary>Снести камеры фона без живого владельца (вместе с их картинкой), как MainMenuScene.DestroyLeftovers.</summary>
        internal static void DestroyLeftovers()
        {
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.name != RootName || go.transform.parent != null
                    || (go.hideFlags & HideFlags.DontSaveInEditor) == 0 || Live.Contains(go)) continue;
                Camera camera = go.GetComponent<Camera>();
                RenderTexture texture = camera != null ? camera.targetTexture : null;
                if (texture != null)
                {
                    camera.targetTexture = null;
                    texture.Release();
                    Remove(texture);
                }
                Remove(go);
            }
        }

        static void Remove(Object target)
        {
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }

#if UNITY_EDITOR
        // После выхода из Play камер фона в редакторе быть не должно: сносятся все, с владельцем и без
        // (свою владелец обычно уже убрал в OnDestroy PlayerHud). Подписка — заново после каждой
        // перезагрузки домена, поэтому ровно одна.
        [UnityEditor.InitializeOnLoadMethod]
        static void DestroyLeftoversOnPlayExit()
        {
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state != UnityEditor.PlayModeStateChange.EnteredEditMode) return;
                Live.Clear();
                DestroyLeftovers();
            };
        }
#endif
    }
}
