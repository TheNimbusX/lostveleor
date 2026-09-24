using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Одна плитка способности в префабе боевого HUD. Только ссылки на части:
    /// что показывать, решает <see cref="CombatHudView"/>, а как это выглядит —
    /// правится в префабе руками.
    /// </summary>
    public sealed class HudSlotWidget : MonoBehaviour
    {
        [Tooltip("Область, по которой ловится мышь")] public RectTransform Hit;
        [Tooltip("Рамка плитки; она же маска для арта")] public Image Frame;
        public RawImage Art;
        [Tooltip("Затемнение перезарядки, Image типа Filled")] public Image Cooldown;
        [Tooltip("Необязательно: кольцо делений перезарядки (Image типа Filled), доля оставшегося времени")] public Image CooldownRing;
        public TMP_Text CooldownText;
        [Tooltip("Подсветка при наведении")] public Image Highlight;
        public TMP_Text Key;
        [Tooltip("Плашка нехватки лавидия")] public GameObject ResourceBadge;
        public TMP_Text ResourceText;
        [Tooltip("Что тянуть при нажатии")] public RectTransform Body;
        [Tooltip("Необязательно: свечение и рамка «готово» (включается, когда способность доступна)")] public GameObject ReadyGlow;
        [Tooltip("Необязательно: вспышка нажатия")] public GameObject PressGlow;
        [Tooltip("Необязательно: камень готовности на верхней кромке (вместо свечения ReadyGlow)")] public HudReadyGem ReadyGem;
    }
}
