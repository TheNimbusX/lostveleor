using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>Плитка слота на экране замены способности (префаб RunHudWc, «Дым и свет»): медальон, имя, клавиша. Только ссылки.</summary>
    public sealed class RunSlotTile : MonoBehaviour
    {
        public Button Button;
        public RawImage Icon;
        public TMP_Text Name;
        [Tooltip("Что пропадёт: таланты заменённой способности")] public TMP_Text Note;
        public TMP_Text Key;
    }
}
