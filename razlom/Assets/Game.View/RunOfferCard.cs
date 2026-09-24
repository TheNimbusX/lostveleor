using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Карточка награды на экране выбора (префаб RunHudWc). Только ссылки на части
    /// карточки пака: что писать, решает <see cref="RunHud"/>, как это выглядит —
    /// правится в префабе. Свой файл обязателен: компонент стоит в префабе.
    /// </summary>
    public sealed class RunOfferCard : MonoBehaviour
    {
        public Button Button;
        public RawImage Icon;
        public TMP_Text Title;
        [Tooltip("Плашка под названием: способность, талант, предмет")] public TMP_Text Kind;
        [Tooltip("Рисованный значок вида награды в плашке")] public RawImage KindIcon;
        public TMP_Text Description;
        public TMP_Text ValueLabel;
        public TMP_Text Value;
        [Tooltip("Клавиша выбора в углу карточки")] public TMP_Text Key;
        public WcRarity Rarity;
    }
}
