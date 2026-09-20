using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Одна строка параметров в подсказке способности.
    ///
    /// Отдельный файл обязателен: Unity связывает компонент со скриптом по
    /// имени файла. Класс, лежавший в HudSlotWidget.cs, в префабе числился
    /// «missing script», и префаб отказывался сохраняться.
    /// </summary>
    public sealed class HudTooltipMetric : MonoBehaviour
    {
        public Image Icon;
        public TMP_Text Value;
        [Tooltip("Черта слева от столбца; у первого видимого столбца прячется")]
        public GameObject Separator;
    }
}
