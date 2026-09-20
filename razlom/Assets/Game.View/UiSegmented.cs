using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Переключатель на несколько положений («Низкое · Среднее · Высокое»).
    ///
    /// Вид — в префабе: подложка, кнопки положений и один коралловый
    /// <see cref="Indicator"/>, который переезжает под выбранное положение.
    /// Без индикатора подсвечивается подложка самой кнопки (старые префабы).
    /// </summary>
    public sealed class UiSegmented : MonoBehaviour
    {
        public Button[] Options = Array.Empty<Button>();
        public TMP_Text[] Labels = Array.Empty<TMP_Text>();
        [Tooltip("Подложка выбранного положения; переезжает между положениями")]
        public RectTransform Indicator;
        [Tooltip("Спрайт выбранного положения для префабов без индикатора")]
        public Sprite Selected;
        public Color SelectedText = Color.white;
        public Color IdleText = new Color32(0xCF, 0xDD, 0xEB, 0xFF);
        [Tooltip("Секунд на переезд подложки")]
        public float MoveDuration = 0.18f;

        /// <summary>Нажато положение с индексом.</summary>
        public event Action<int> Chosen;

        int _selected = -1;
        bool _placed;

        void Awake()
        {
            for (int i = 0; i < Options.Length; i++)
            {
                int index = i;
                if (Options[i] != null) Options[i].onClick.AddListener(() => Chosen?.Invoke(index));
            }
        }

        void OnDisable() => _placed = false;

        public void SetLabels(string[] labels)
        {
            for (int i = 0; i < Options.Length; i++)
            {
                bool shown = i < labels.Length;
                if (Options[i] != null && Options[i].gameObject.activeSelf != shown) Options[i].gameObject.SetActive(shown);
                if (shown && i < Labels.Length && Labels[i] != null && Labels[i].text != labels[i]) Labels[i].text = labels[i];
            }
        }

        public void SetSelected(int index)
        {
            if (index == _selected && _placed) return;
            bool animate = _placed && index != _selected;
            _selected = index;

            for (int i = 0; i < Labels.Length; i++)
                if (Labels[i] != null)
                {
                    Color color = i == index ? SelectedText : IdleText;
                    if (animate) UiMotion.ColorTo(Labels[i], color, MoveDuration); else Labels[i].color = color;
                }

            if (Indicator == null)
            {
                for (int i = 0; i < Options.Length; i++)
                {
                    Image back = Options[i] != null ? Options[i].image : null;
                    if (back == null) continue;
                    back.sprite = Selected;
                    // Прозрачная, а не выключенная: иначе кнопка перестаёт ловить мышь.
                    back.color = i == index ? Color.white : new Color(1f, 1f, 1f, 0f);
                }
                _placed = true;
                return;
            }

            if ((uint)index >= (uint)Options.Length || Options[index] == null) return;
            // Раскладка группы должна быть посчитана, иначе у кнопок нулевые размеры.
            if (transform is RectTransform self) LayoutRebuilder.ForceRebuildLayoutImmediate(self);
            var option = (RectTransform)Options[index].transform;
            Vector2 center = (Vector2)option.localPosition + (new Vector2(0.5f, 0.5f) - option.pivot) * option.rect.size;
            Vector2 size = option.rect.size;
            Indicator.anchorMin = Indicator.anchorMax = Indicator.pivot = new Vector2(0.5f, 0.5f);
            if (!animate)
            {
                Indicator.localPosition = center;
                Indicator.sizeDelta = size;
                _placed = option.rect.width > 0f;
                return;
            }
            Vector2 fromPosition = Indicator.localPosition, fromSize = Indicator.sizeDelta;
            UiMotion.Play(Indicator, 10, MoveDuration, t =>
            {
                Indicator.localPosition = Vector2.LerpUnclamped(fromPosition, center, t);
                Indicator.sizeDelta = Vector2.LerpUnclamped(fromSize, size, t);
            });
        }
    }
}
