using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Звуки игровых событий: отказ способности, нехватка лавидия, новый уровень,
    /// зелье, добыча, портал, сумка, разбор. Клипы берутся по имени файла из
    /// Resources/Audio/Game/Prepared (tools/audio/prepare-sounds.ps1) — нет файла,
    /// значит тишина, без ошибок.
    ///
    /// Отличие от <see cref="UiSound"/>: эти звуки принадлежат миру, поэтому
    /// затихают вместе с игрой на паузе и идут через громкость «Эффекты».
    /// </summary>
    public static class GameSound
    {
        const string Folder = "Audio/Game/Prepared";

        static Dictionary<string, AudioClip> _clips;
        static AudioSource _source, _loopSource;
        static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();

        /// <summary>Один звук. <paramref name="minInterval"/> не даёт частому событию трещать.</summary>
        public static void Play(string name, float volume = 1f, float pitchJitter = .04f, float minInterval = .06f)
        {
            AudioClip clip = Find(name);
            if (clip == null) return;
            float now = Time.unscaledTime;
            if (LastPlayed.TryGetValue(name, out float last) && now - last < minInterval) return;
            LastPlayed[name] = now;
            AudioSource source = Source;
            source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            source.PlayOneShot(clip, Mathf.Clamp01(volume) * GameUserSettings.EffectsVolume);
        }

        /// <summary>Петля вроде сердцебиения на низком здоровье. Пустое имя — выключить.</summary>
        public static void Loop(string name, float volume = 1f)
        {
            AudioClip clip = string.IsNullOrEmpty(name) ? null : Find(name);
            AudioSource source = LoopSource;
            if (clip == null)
            {
                if (source.isPlaying) source.Stop();
                return;
            }
            if (source.clip != clip) { source.clip = clip; source.Play(); }
            else if (!source.isPlaying) source.Play();
            source.volume = Mathf.Clamp01(volume) * GameUserSettings.EffectsVolume;
        }

        static AudioClip Find(string name)
        {
            if (_clips == null)
            {
                _clips = new Dictionary<string, AudioClip>();
                foreach (AudioClip clip in Resources.LoadAll<AudioClip>(Folder))
                    if (clip != null) _clips[clip.name] = clip;
            }
            return !string.IsNullOrEmpty(name) && _clips.TryGetValue(name, out AudioClip found) ? found : null;
        }

        static AudioSource Source => _source != null ? _source : _source = Make("Game Sound", false);
        static AudioSource LoopSource => _loopSource != null ? _loopSource : _loopSource = Make("Game Sound Loop", true);

        static AudioSource Make(string name, bool loop)
        {
            var host = new GameObject(name) { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(host);
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            return source;
        }
    }
}
