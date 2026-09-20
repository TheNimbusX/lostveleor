using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    // Scoped to the displayed rift. Preview scenes never change global lighting.
    public sealed class MeadowLighting
    {
        private bool _active, _fog;
        private FogMode _fogMode;
        private AmbientMode _ambientMode;
        private float _start, _end, _sunIntensity, _ambientIntensity;
        private Color _fogColor, _sky, _equator, _ground, _sunColor, _background;
        private Light _sun;
        private Camera _camera;
        private CameraClearFlags _clearFlags;
        private Quaternion _rotation;
        private Volume _campVolume;
        private Light _fill;
        private float _fillIntensity, _shadowStrength;
        private bool _usingCamp;
        public void Apply(LayoutStyle style)
        {
            if (_active) return;
            if (style.UseCampLighting && ApplyCamp()) return;
            _active = true;
            _fog = RenderSettings.fog; _fogMode = RenderSettings.fogMode;
            _fogColor = RenderSettings.fogColor; _start = RenderSettings.fogStartDistance; _end = RenderSettings.fogEndDistance;
            _ambientMode = RenderSettings.ambientMode; _ambientIntensity = RenderSettings.ambientIntensity;
            _sky = RenderSettings.ambientSkyColor; _equator = RenderSettings.ambientEquatorColor; _ground = RenderSettings.ambientGroundColor;
            _sun = RenderSettings.sun;
            if (_sun == null)
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.isActiveAndEnabled && light.type == LightType.Directional && (_sun == null || light.intensity > _sun.intensity)) _sun = light;
            if (_sun != null)
            {
                _sunColor = _sun.color; _sunIntensity = _sun.intensity; _rotation = _sun.transform.rotation;
                _sun.color = style.SunColor; _sun.intensity = style.SunIntensity; _sun.transform.rotation = Quaternion.Euler(style.SunAngles);
            }
            _camera = Camera.main;
            if (_camera != null)
            {
                _background = _camera.backgroundColor; _clearFlags = _camera.clearFlags;
                _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = style.SkyColor;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight; RenderSettings.ambientIntensity = 1;
            RenderSettings.ambientSkyColor = style.SkyColor;
            RenderSettings.ambientEquatorColor = style.AmbientColor;
            RenderSettings.ambientGroundColor = style.AmbientColor * .45f;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = style.FogColor; RenderSettings.fogStartDistance = style.FogStart; RenderSettings.fogEndDistance = style.FogEnd;
        }
        public void Restore()
        {
            if (!_active) return;
            _active = false;
            if (_usingCamp)
            {
                _usingCamp = false;
                if (_campVolume != null) _campVolume.gameObject.SetActive(false);
                if (_sun != null) { _sun.color = _sunColor; _sun.shadowStrength = _shadowStrength; }
                if (_fill != null) _fill.intensity = _fillIntensity;
                return;
            }
            RenderSettings.fog = _fog; RenderSettings.fogMode = _fogMode; RenderSettings.fogColor = _fogColor;
            RenderSettings.fogStartDistance = _start; RenderSettings.fogEndDistance = _end;
            RenderSettings.ambientMode = _ambientMode; RenderSettings.ambientIntensity = _ambientIntensity;
            RenderSettings.ambientSkyColor = _sky; RenderSettings.ambientEquatorColor = _equator; RenderSettings.ambientGroundColor = _ground;
            if (_sun != null) { _sun.color = _sunColor; _sun.intensity = _sunIntensity; _sun.transform.rotation = _rotation; }
            if (_camera != null) { _camera.backgroundColor = _background; _camera.clearFlags = _clearFlags; }
        }

        private bool ApplyCamp()
        {
            var world = Object.FindAnyObjectByType<SceneWorldView>();
            var look = world != null && world.CampRoot != null
                ? world.CampRoot.GetComponentInChildren<CampLookController>(true) : null;
            if (look == null || look.Sun == null || look.Fill == null || look.Volume == null || look.SelectedProfile == null)
                return false;
            _sun = look.Sun; _fill = look.Fill;
            _sunColor = _sun.color; _shadowStrength = _sun.shadowStrength; _fillIntensity = _fill.intensity;
            if (look.Style != CampLookStyle.Original)
            {
                _sun.color = look.SunColor; _sun.shadowStrength = look.ShadowStrength;
                _fill.intensity = look.FillIntensity;
            }
            // Объекты лагеря остаются выключенными; переиспользуется только его профиль Volume.
            if (_campVolume == null)
            {
                var root = new GameObject("Освещение разлома — профиль лагеря") { hideFlags = HideFlags.DontSave };
                _campVolume = root.AddComponent<Volume>();
            }
            _campVolume.gameObject.layer = look.Volume.gameObject.layer;
            _campVolume.isGlobal = true;
            _campVolume.priority = look.Volume.priority;
            _campVolume.weight = look.Volume.weight;
            _campVolume.sharedProfile = look.SelectedProfile;
            _campVolume.gameObject.SetActive(true);
            _active = _usingCamp = true;
            if (Application.isPlaying)
                Debug.Log($"[rift-light] camp={look.Style} profile={look.SelectedProfile.name} sun={_sun.intensity} fill={_fill.intensity}");
            return true;
        }

        public void Dispose()
        {
            Restore();
            if (_campVolume == null) return;
            if (Application.isPlaying) Object.Destroy(_campVolume.gameObject);
            else Object.DestroyImmediate(_campVolume.gameObject);
            _campVolume = null;
        }
    }
}
