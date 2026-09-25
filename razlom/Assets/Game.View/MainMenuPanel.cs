using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Главное меню на паке «Ночная акварель» (владелец, 24 сентября): компоновка варианта 4
    /// (концепт 5-menu-arch-continue) на рисованной панораме лагеря с аркой и трещиной разлома
    /// (выбран фон 2, 6-menu-art-panorama). Лого сверху, слева «Продолжить» со строкой о сохранении,
    /// «Новая игра», «Настройки», «Выход»; «Новая игра» спрашивает подтверждение.
    /// Только ссылки и вид — поведение в MainMenuView. Свой файл обязателен: компонент стоит в
    /// префабе Resources/UI/Prefabs/MainMenuWc.
    /// </summary>
    public sealed class MainMenuPanel : MonoBehaviour
    {
        public CanvasGroup Group;
        public Button Continue;
        [Tooltip("Строка о сохранении под «Продолжить»: «Пелаг · уровень 5»")] public TMP_Text ContinueLine;
        public TMP_Text ContinueLabel;
        [Tooltip("Ромб перед «Продолжить»: без строки о сохранении встаёт по центру")] public RectTransform ContinueBullet;
        public Button NewGame;
        public Button Settings;
        public Button Exit;

        [Header("Подтверждение новой игры")]
        public CanvasGroup Confirm;
        public Button ConfirmYes;
        public Button ConfirmNo;

        /// <summary>Строка о сохранении под «Продолжить»; пустая — надпись и ромб встают по центру кнопки.</summary>
        public void SetContinueLine(string text)
        {
            bool shown = !string.IsNullOrEmpty(text);
            if (ContinueLine != null)
            {
                ContinueLine.text = shown ? text : "";
                ContinueLine.gameObject.SetActive(shown);
            }
            if (ContinueLabel != null)
            {
                RectTransform rect = ContinueLabel.rectTransform;
                rect.offsetMin = new Vector2(rect.offsetMin.x, shown ? 30f : 0f);
                rect.offsetMax = new Vector2(rect.offsetMax.x, shown ? -8f : 0f);
            }
            if (ContinueBullet != null)
            {
                ContinueBullet.anchorMin = ContinueBullet.anchorMax = new Vector2(0f, shown ? 1f : .5f);
                ContinueBullet.anchoredPosition = new Vector2(ContinueBullet.anchoredPosition.x, shown ? -39f : 0f);
            }
        }
    }
}
