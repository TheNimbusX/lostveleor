using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// «Масштаб интерфейса» из настроек для одного Canvas: делит эталонное
    /// разрешение CanvasScaler на масштаб. Эталон берётся из префаба при
    /// запуске, так что ручная правка CanvasScaler в Unity остаётся базой.
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class UiScaleFollower : MonoBehaviour
    {
        CanvasScaler _scaler;
        Vector2 _reference;

        void Awake()
        {
            _scaler = GetComponent<CanvasScaler>();
            _reference = _scaler.referenceResolution;
            GameUserSettings.UiScaleChanged += Apply;
            Apply();
        }

        void OnDestroy() => GameUserSettings.UiScaleChanged -= Apply;

        void Apply()
        {
            if (_scaler != null)
                _scaler.referenceResolution = _reference / Mathf.Clamp(GameUserSettings.UiScale, GameUserSettings.UiScaleMin, GameUserSettings.UiScaleMax);
        }
    }
}
