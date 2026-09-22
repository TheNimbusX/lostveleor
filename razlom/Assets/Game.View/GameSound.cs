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

        /// <summary>
        /// Один звук. <paramref name="minInterval"/> не даёт частому событию трещать.
        /// Имя без номера («smith_hammer») берёт один из вариантов smith_hammer_01, _02 …,
        /// не повторяя подряд тот же.
        /// </summary>
        public static void Play(string name, float volume = 1f, float pitchJitter = .04f, float minInterval = .06f)
        {
            AudioClip clip = Find(name) ?? Variant(name);
            if (clip == null) return;
            float now = Time.unscaledTime;
            if (LastPlayed.TryGetValue(name, out float last) && now - last < minInterval) return;
            LastPlayed[name] = now;
            AudioSource source = Source;
            source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            source.PlayOneShot(clip, Mathf.Clamp01(volume) * GameUserSettings.EffectsVolume);
        }

        /// <summary>Готовый клип из другой папки (шаги владельца из боя) через тот же источник и громкость.</summary>
        public static void PlayClip(AudioClip clip, float volume = 1f, float pitchJitter = .04f)
        {
            if (clip == null) return;
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

        static readonly Dictionary<string, List<AudioClip>> Variants = new Dictionary<string, List<AudioClip>>();
        static readonly Dictionary<string, AudioClip> LastVariant = new Dictionary<string, AudioClip>();

        static AudioClip Variant(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Find(name);
            if (!Variants.TryGetValue(name, out List<AudioClip> list))
            {
                list = new List<AudioClip>();
                foreach (var pair in _clips)
                {
                    string key = pair.Key;
                    if (key.Length > name.Length + 1 && key.StartsWith(name + "_") && int.TryParse(key.Substring(name.Length + 1), out _)) list.Add(pair.Value);
                }
                list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                Variants[name] = list;
            }
            if (list.Count == 0) return null;
            LastVariant.TryGetValue(name, out AudioClip last);
            AudioClip pick = list[Random.Range(0, list.Count)];
            if (list.Count > 1 && pick == last) pick = list[(list.IndexOf(pick) + 1 + Random.Range(0, list.Count - 1)) % list.Count];
            LastVariant[name] = pick;
            return pick;
        }

        /// <summary>
        /// Короткая цепочка: каждый звук со своей задержкой в секундах реального времени
        /// (молот, затем пар, затем звон). Паузу игры не ждёт: цепочки звучат в окнах лагеря.
        /// </summary>
        public static void Sequence(params (string name, float delay, float volume)[] steps)
        {
            foreach (var step in steps)
            {
                if (step.delay <= 0f) Play(step.name, step.volume, .04f, 0f);
                else Runner.Queue(step.name, Time.unscaledTime + step.delay, step.volume);
            }
        }

        static SequenceRunner _runner;
        static SequenceRunner Runner
        {
            get
            {
                if (_runner != null) return _runner;
                var host = new GameObject("Game Sound Sequence") { hideFlags = HideFlags.HideInHierarchy };
                Object.DontDestroyOnLoad(host);
                return _runner = host.AddComponent<SequenceRunner>();
            }
        }

        sealed class SequenceRunner : MonoBehaviour
        {
            readonly List<(string name, float at, float volume)> _pending = new List<(string, float, float)>();
            public void Queue(string name, float at, float volume) => _pending.Add((name, at, volume));
            void Update()
            {
                float now = Time.unscaledTime;
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    if (_pending[i].at > now) continue;
                    var step = _pending[i];
                    _pending.RemoveAt(i);
                    Play(step.name, step.volume, .04f, 0f);
                }
            }
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
