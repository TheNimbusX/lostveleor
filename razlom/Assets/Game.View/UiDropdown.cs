using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Выпадающий список (разрешение экрана). Поле, раскрывающийся список с
    /// прокруткой и пункты по шаблону <see cref="OptionTemplate"/> — всё в
    /// префабе. Список открывается и закрывается с проявлением и лёгким
    /// масштабом; выбранный пункт подсвечен.
    /// </summary>
    public sealed class UiDropdown : MonoBehaviour
    {
        public Button Field;
        public TMP_Text Value;
        [Tooltip("Корень раскрывающегося списка (с CanvasGroup)")]
        public RectTransform List;
        [Tooltip("Контейнер пунктов внутри прокрутки")]
        public RectTransform Content;
        [Tooltip("Шаблон пункта: кнопка с TMP-подписью; сам остаётся выключенным")]
        public Button OptionTemplate;
        public ScrollRect Scroll;
        public Color OptionSelected = new Color32(0xCB, 0x51, 0x58, 0xFF);
        public Color OptionIdle = new Color(1f, 1f, 1f, 0f);
        public float Duration = 0.15f;

        /// <summary>Выбран пункт с индексом.</summary>
        public event Action<int> Chosen;

        readonly List<Button> _options = new List<Button>();
        string[] _labels = Array.Empty<string>();
        int _selected = -1;
        CanvasGroup _group;

        public bool IsOpen => List != null && List.gameObject.activeSelf;

        void Awake()
        {
            if (OptionTemplate != null) OptionTemplate.gameObject.SetActive(false);
            if (List != null)
            {
                _group = List.GetComponent<CanvasGroup>();
                if (_group == null) _group = List.gameObject.AddComponent<CanvasGroup>();
                List.gameObject.SetActive(false);
            }
            if (Field != null) Field.onClick.AddListener(() => { if (IsOpen) Close(); else Open(); });
        }

        void OnDisable()
        {
            if (List != null) List.gameObject.SetActive(false);
        }

        public bool Interactable
        {
            set { if (Field != null) Field.interactable = value; if (!value && IsOpen) Close(); }
        }

        public void SetOptions(string[] labels)
        {
            bool same = labels.Length == _labels.Length;
            for (int i = 0; same && i < labels.Length; i++) same = labels[i] == _labels[i];
            if (same || OptionTemplate == null || Content == null) return;
            _labels = (string[])labels.Clone();
            foreach (Button old in _options) if (old != null) Destroy(old.gameObject);
            _options.Clear();
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                Button option = Instantiate(OptionTemplate, Content);
                option.gameObject.SetActive(true);
                option.name = "Option " + labels[i];
                var label = option.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = labels[i];
                option.onClick.AddListener(() => { Chosen?.Invoke(index); Close(); });
                _options.Add(option);
            }
            _selected = -1;
        }

        public void SetSelected(int index, string text)
        {
            if (Value != null && Value.text != text) Value.text = text;
            if (index == _selected) return;
            _selected = index;
            for (int i = 0; i < _options.Count; i++)
                if (_options[i] != null && _options[i].image != null)
                    _options[i].image.color = i == index ? OptionSelected : OptionIdle;
        }

        public void Open()
        {
            if (List == null) return;
            UiSound.Play(UiSoundEvent.ListOpen);
            List.gameObject.SetActive(true);
            List.localScale = new Vector3(1f, 0.92f, 1f);
            _group.alpha = 0f;
            UiMotion.FadeTo(_group, 1f, Duration);
            UiMotion.ScaleTo(List, 1f, Duration);
            // Выбранный пункт — в видимой части списка.
            if (Scroll != null && _options.Count > 1 && _selected >= 0)
                Scroll.verticalNormalizedPosition = 1f - _selected / (float)(_options.Count - 1);
        }

        public void Close()
        {
            if (List == null || !List.gameObject.activeSelf) return;
            UiSound.Play(UiSoundEvent.ListClose);
            UiMotion.FadeTo(_group, 0f, Duration * 0.8f, () => { if (List != null) List.gameObject.SetActive(false); });
        }
    }
}
