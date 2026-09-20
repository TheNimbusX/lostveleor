using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Проигрывает звуки интерфейса из <see cref="UiSoundBank"/>. Работает в паузе
    /// (игнорирует паузу слушателя и Time.timeScale), громкость — «Эффекты» из
    /// настроек. Нет банка или клипов у события — тишина, без ошибок.
    /// </summary>
    public static class UiSound
    {
        const string BankPath = "UI/Sounds/UiSoundBank";

        static UiSoundBank _bank;
        static bool _loaded;
        static AudioSource _source;
        static readonly Dictionary<UiSoundEvent, float> LastPlayed = new Dictionary<UiSoundEvent, float>();

        public static void Play(UiSoundEvent sound)
        {
            if (!_loaded)
            {
                _loaded = true;
                _bank = Resources.Load<UiSoundBank>(BankPath);
            }
            UiSoundBank.Entry entry = _bank != null ? _bank.Find(sound) : null;
            if (entry == null || entry.Clips == null || entry.Clips.Length == 0) return;
            AudioClip clip = entry.Clips[Random.Range(0, entry.Clips.Length)];
            if (clip == null) return;

            float now = Time.unscaledTime;
            if (LastPlayed.TryGetValue(sound, out float last) && now - last < entry.MinInterval) return;
            LastPlayed[sound] = now;

            AudioSource source = Source;
            source.pitch = 1f + Random.Range(-entry.PitchJitter, entry.PitchJitter);
            source.PlayOneShot(clip, entry.Volume * GameUserSettings.EffectsVolume);
        }

        static AudioSource Source
        {
            get
            {
                if (_source != null) return _source;
                var host = new GameObject("UI Sound") { hideFlags = HideFlags.HideInHierarchy };
                Object.DontDestroyOnLoad(host);
                _source = host.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.spatialBlend = 0f;
                _source.ignoreListenerPause = true;
                return _source;
            }
        }
    }
}
