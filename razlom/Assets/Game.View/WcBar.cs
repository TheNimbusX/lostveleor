using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Полоса пака (здоровье, лавидий, опыт, загрузка): заполнение — правый край Fill
    /// по доле Value. Капсула заполнения растягивается 9-slice, поэтому скругление
    /// конца сохраняется при любом значении, в отличие от Image.Filled.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class WcBar : MonoBehaviour
    {
        [Range(0f, 1f)] public float Value = .7f;
        public RectTransform Fill;
        [Tooltip("Необязательная «голова» на конце заполнения (камень у полосы загрузки)")] public RectTransform Head;
        [Tooltip("След недавнего урона: светлый участок от Value до TrailValue")] public RectTransform Trail;
        [Range(0f, 1f)] public float TrailValue;

        public void Set(float value)
        {
            Value = Mathf.Clamp01(value);
            Apply();
        }

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public void Apply()
        {
            if (this == null || Fill == null) return;
            Fill.anchorMin = new Vector2(0f, 0f);
            Fill.anchorMax = new Vector2(Value, 1f);
            // Короче высоты капсула сминается в эллипс: при малом значении — просто прячем.
            Fill.gameObject.SetActive(Value > .001f);
            if (Trail != null)
            {
                bool show = TrailValue > Value + .001f;
                Trail.gameObject.SetActive(show);
                Trail.anchorMin = new Vector2(Value, 0f);
                Trail.anchorMax = new Vector2(Mathf.Max(Value, TrailValue), 1f);
                Trail.offsetMin = Trail.offsetMax = Vector2.zero;
            }
            if (Head != null)
            {
                Head.anchorMin = new Vector2(Value, .5f);
                Head.anchorMax = new Vector2(Value, .5f);
                Head.anchoredPosition = new Vector2(0f, Head.anchoredPosition.y);
            }
        }
    }
}
