using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>Значение со стрелками ‹ › по бокам («1920 × 1080»). Вид — в префабе.</summary>
    public sealed class UiStepper : MonoBehaviour
    {
        public Button Previous;
        public Button Next;
        public TMP_Text Value;

        /// <summary>−1 — назад, +1 — вперёд.</summary>
        public event Action<int> Stepped;

        void Awake()
        {
            if (Previous != null) Previous.onClick.AddListener(() => Stepped?.Invoke(-1));
            if (Next != null) Next.onClick.AddListener(() => Stepped?.Invoke(1));
        }

        public void SetValue(string text)
        {
            if (Value != null && Value.text != text) Value.text = text;
        }
    }
}
