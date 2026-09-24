using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>Карточка зелья в окне алхимика (префаб CampShopsWc). Только ссылки.</summary>
    public sealed class CampPotionCard : MonoBehaviour
    {
        public RawImage Art;
        public TMP_Text Name, Effect, Stock;
        public Button Buy;
        public TMP_Text BuyLabel;
        public Button Select;
        public TMP_Text SelectLabel;
        [Tooltip("Рамка выбранного для слота зелья")] public GameObject Chosen;
        [Tooltip("Закрыто до заказа алхимика")] public GameObject Locked;
        [Tooltip("Условие и ход заказа, который открывает зелье")] public TMP_Text LockedLabel;

        [Header("Заказ Лео (закрытые зелья)")]
        [Tooltip("Взять заказ или сдать выполненный")] public Button OrderMain;
        public TMP_Text OrderMainLabel;
        [Tooltip("Обмен вместо выполнения: редкая вещь или осколки")] public Button OrderAlt;
        public TMP_Text OrderAltLabel;
    }
}
