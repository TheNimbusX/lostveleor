using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    // Все ссылки задаёт префаб: окно не ищет подписи или кнопки по именам.
    public sealed class CampTraderProgressionPanel : MonoBehaviour
    {
        public CanvasGroup Group;
        public TMP_Text Title, SelectedStock, ReservedStock, ReserveReason, CategoryReason, Status;
        public Button Reserve, Back;
        public TMP_Text ReserveLabel;
        public Button[] Categories;
        public TMP_Text[] CategoryLabels;
    }
}
