using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    public enum CampLookStyle
    {
        [InspectorName("Исходный вид")] Original,
        [InspectorName("Чистый атмосферный")] Clean,
        [InspectorName("Мягкий живописный")] Painterly,
        [InspectorName("Лёгкий плёночный")] Film,
        [InspectorName("Сравнение ACES")] Aces
    }

    // Корень лагеря выключается при выходе: общие источники возвращаются к состоянию до входа.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class CampLookController : MonoBehaviour
    {
        public CampLookStyle Style = CampLookStyle.Clean;
        public Volume Volume;
        public VolumeProfile Original, Clean, Painterly, Film, Aces;
        public Light Sun, Fill;
        public Color SunColor = new Color(1f, .93f, .83f);
        [Range(0, 1)] public float ShadowStrength = .82f;
        [Min(0)] public float FillIntensity = .085f;

        Color _sunColor;
        float _shadowStrength, _fillIntensity;
        VolumeProfile _previousProfile;
        CampLookStyle _applied;
        bool _captured;
        public bool HasCaptured => _captured;

        void Capture()
        {
            if (Volume == null || Sun == null || Fill == null) return;
            _sunColor = Sun.color;
            _shadowStrength = Sun.shadowStrength;
            _fillIntensity = Fill.intensity;
            _previousProfile = Volume.sharedProfile;
            _captured = true;
            Apply();
        }

        void LateUpdate()
        {
            // При возвращении сначала LayoutView восстанавливает прежний свет Разлома.
            if (!_captured) Capture();
            else if (_applied != Style) Apply();
        }

        public void SetStyle(CampLookStyle style)
        {
            Style = style;
            if (_captured && isActiveAndEnabled) Apply();
        }

        void Apply()
        {
            VolumeProfile profile = Style switch
            {
                CampLookStyle.Original => Original,
                CampLookStyle.Painterly => Painterly,
                CampLookStyle.Film => Film,
                CampLookStyle.Aces => Aces,
                _ => Clean
            };
            if (profile == null) return;
            bool original = Style == CampLookStyle.Original;
            Sun.color = original ? _sunColor : SunColor;
            Sun.shadowStrength = original ? _shadowStrength : ShadowStrength;
            Fill.intensity = original ? _fillIntensity : FillIntensity;
            Volume.sharedProfile = profile;
            _applied = Style;
        }

        void OnDisable() => Restore();

        // Редактор применяет настройки только на время отрисовки камеры, не сохраняя их в сцену.
        public void BeginEditorPreview() { if (!Application.isPlaying) { Capture(); } }
        public void EndEditorPreview() { if (!Application.isPlaying) Restore(); }

        void Restore()
        {
            if (!_captured) return;
            if (Sun != null) { Sun.color = _sunColor; Sun.shadowStrength = _shadowStrength; }
            if (Fill != null) Fill.intensity = _fillIntensity;
            if (Volume != null) Volume.sharedProfile = _previousProfile;
            _captured = false;
        }
    }
}
