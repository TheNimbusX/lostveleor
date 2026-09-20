using System;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// События звуков интерфейса. ТОЛЬКО ДОПИСЫВАТЬ В КОНЕЦ: номер события
    /// лежит в банке звуков.
    /// </summary>
    public enum UiSoundEvent
    {
        Hover = 0,
        Click = 1,
        Back = 2,
        Tab = 3,
        Toggle = 4,
        SliderStep = 5,
        ListOpen = 6,
        ListClose = 7,
        WindowOpen = 8,
        WindowClose = 9,
        PauseOpen = 10,
        PauseClose = 11,
        Denied = 12,
        KeyWaiting = 13,
        KeyAssigned = 14,
        Apply = 15,
        Reset = 16,
    }

    /// <summary>
    /// Банк звуков интерфейса: Resources/UI/Sounds/UiSoundBank.asset.
    /// На событие — несколько вариантов клипа (выбирается случайный) и
    /// громкость. Пустое событие молчит. Заполняется владельцем в инспекторе.
    /// </summary>
    [CreateAssetMenu(menuName = "Разлом/UI/Банк звуков интерфейса", fileName = "UiSoundBank")]
    public sealed class UiSoundBank : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public UiSoundEvent Event;
            [Tooltip("Варианты: каждый раз играет случайный")] public AudioClip[] Clips = Array.Empty<AudioClip>();
            [Range(0f, 1f)] public float Volume = 0.8f;
            [Tooltip("Разброс высоты тона, чтобы частые звуки не надоедали")] [Range(0f, 0.2f)] public float PitchJitter = 0.04f;
            [Tooltip("Не чаще, секунд (наведение, шаг слайдера)")] public float MinInterval = 0.03f;
            [Tooltip("Клипы поставлены автоматически из Resources/Audio/UI/Prepared по имени файла. " +
                     "Снимите галочку, если меняете клипы руками, — иначе сборщик вернёт свои.")]
            public bool AutoFilled;
        }

        public Entry[] Entries = Array.Empty<Entry>();

        public Entry Find(UiSoundEvent sound)
        {
            foreach (Entry entry in Entries)
                if (entry != null && entry.Event == sound) return entry;
            return null;
        }
    }
}
