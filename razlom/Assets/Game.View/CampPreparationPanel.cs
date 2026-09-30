using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    public sealed class CampPreparationPanel : MonoBehaviour
    {
        public CanvasGroup Group;
        public Button[] Starters, Gifts, Potions, Slots;
        public TMP_Text[] StarterLabels, GiftLabels, PotionLabels, SlotLabels;
        public TMP_Text GiftDescription, PotionDescription, Status;
        public Button Depart, Back;
    }
}
