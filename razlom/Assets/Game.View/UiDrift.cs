using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Медленный дрейф элемента туда-обратно (туман главного меню): смещение по синусу от места
    /// в префабе. Время интерфейса — меню стоит на паузе.
    /// </summary>
    public sealed class UiDrift : MonoBehaviour
    {
        [Tooltip("Размах, единицы Canvas")] public Vector2 Amplitude = new Vector2(80f, 10f);
        [Tooltip("Секунд на полный ход туда и обратно")] public float Period = 46f;
        [Tooltip("Сдвиг фазы 0..1: соседние клубы плывут вразнобой")] [Range(0f, 1f)] public float Phase;

        Vector2 _rest;
        bool _known;

        void OnEnable()
        {
            if (_known) return;
            _rest = ((RectTransform)transform).anchoredPosition;
            _known = true;
        }

        void LateUpdate()
        {
            float a = (UiMotion.Now / Mathf.Max(1f, Period) + Phase) * Mathf.PI * 2f;
            ((RectTransform)transform).anchoredPosition = _rest + new Vector2(Mathf.Sin(a) * Amplitude.x, Mathf.Sin(a * 1.37f) * Amplitude.y);
        }
    }
}
