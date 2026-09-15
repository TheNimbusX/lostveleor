using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    // Камера готовит фон миникарты при смене локации и выключается после кадра.
    // Это часть HUD при запуске игры, не редакторская съёмка или проверка.
    internal sealed class HudMapBackdrop
    {
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
                var root = new GameObject("HUD map backdrop") { hideFlags = HideFlags.HideAndDontSave };
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
            if (_camera != null) Object.Destroy(_camera.gameObject);
            if (_texture != null) { _texture.Release(); Object.Destroy(_texture); }
        }
    }
}
