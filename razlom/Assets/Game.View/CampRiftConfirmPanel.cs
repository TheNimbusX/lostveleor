using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Вопрос «Отправиться в забег?» у арки на паке «Ночная акварель» (владелец 24 сентября:
    /// «плашка ещё без UI»). Префаб Resources/UI/Prefabs/CampRiftConfirmWc; показ и ответ —
    /// CampRiftEntrance. Появляется мягко: затемнение и карточка с лёгким подъёмом.
    /// Свой файл обязателен: компонент стоит в префабе.
    /// </summary>
    public sealed class CampRiftConfirmPanel : MonoBehaviour
    {
        public CanvasGroup Group;
        public RectTransform Card;
        public TMP_Text Title;
        [Tooltip("Необязательно: пояснение под вопросом (с 24 сентября его нет)")] public TMP_Text Text;
        public Button Enter;
        public Button Stay;
        [Tooltip("Секунд на появление")] public float Fade = .16f;

        float _shown;
        bool _open;

        public bool IsShown => _open;

        public void Show(bool open)
        {
            _open = open;
            if (open && !gameObject.activeSelf)
            {
                _shown = 0f;
                gameObject.SetActive(true);
                Apply();
            }
        }

        void Update()
        {
            _shown = Mathf.MoveTowards(_shown, _open ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(.01f, Fade));
            Apply();
            if (!_open && _shown <= 0f) gameObject.SetActive(false);
        }

        void Apply()
        {
            float t = 1f - (1f - _shown) * (1f - _shown);
            if (Group != null)
            {
                Group.alpha = t;
                Group.interactable = Group.blocksRaycasts = _open;
            }
            if (Card != null)
            {
                Card.localScale = Vector3.one * Mathf.Lerp(.96f, 1f, t);
                Card.anchoredPosition = new Vector2(0f, Mathf.Lerp(-18f, 0f, t));
            }
        }
    }
}
