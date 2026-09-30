using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Game.View
{
    public sealed class CampForgePanel : MonoBehaviour
    {
        [HideInInspector] public int LayoutVersion;
        public CanvasGroup Group;
        public TMP_Text Before, After, Changes, Price, Status, DonorCaption;
        public TMP_Text BeforeProperties, AfterProperties, DonorInfo, TargetAffixCaption;
        public CampShopCell BeforeItem, AfterItem, DonorItem;
        public Button[] Operations, Affixes, Options, Donors, DonorAffixes;
        public TMP_Text[] OperationLabels, AffixLabels, OptionLabels, DonorLabels, DonorAffixLabels;
        public Button Confirm, Back, PreviousDonors, NextDonors;
    }
}
