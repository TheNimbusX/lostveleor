using System;
using UnityEngine;
// System.Random и UnityEngine.Random в одном файле неоднозначны; здесь нужен игровой.
using Random = UnityEngine.Random;

namespace Game.View
{
    /// <summary>
    /// Живой звук лагеря поверх ровных петель <see cref="CampSoundscape"/>: редкие
    /// одиночные звуки вокруг героя (дятел, вороны, стайка птиц, хлопки ткани) и
    /// порывы ветра, от которых качается листва.
    ///
    /// Клипы берутся по имени файла из Resources/Audio/Camp/Prepared
    /// (tools/audio/prepare-sounds.ps1), источники создаются в игре — авторская
    /// сцена не меняется. Нет файла — группа молчит, без ошибок.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Разлом/Лагерь/Живой звук")]
    public sealed class CampAmbientSounds : MonoBehaviour
    {
        const string Folder = "Audio/Camp/Prepared";

        [Serializable]
        public sealed class Group
        {
            [Tooltip("Начало имени файла: берутся все клипы, что с него начинаются")]
            public string Prefix;
            [Tooltip("Пауза между звуками: от и до, секунд")]
            public Vector2 Interval = new Vector2(20f, 50f);
            [Range(0f, 1f)] public float Gain = .5f;
            [Tooltip("Разброс высоты тона")]
            [Range(0f, .3f)] public float PitchJitter = .06f;
            [Tooltip("Насколько далеко вокруг героя ставится звук, метры")]
            public float Spread = 7f;
            [NonSerialized] public AudioClip[] Clips;
            [NonSerialized] public float Next;
        }

        [Tooltip("Ветер и огни: порыв качает листву вместе со звуком")]
        public CampAmbience Ambience;
        [Range(0f, 1f)] public float MasterGain = .85f;
        [Tooltip("Приглушение, пока открыто окно или пауза")]
        [Range(0f, 1f)] public float MenuDuck = .55f;

        [Header("Петли")]
        [Tooltip("Вечерний лес поверх дневных птиц")]
        public string ForestLoop = "camp_evening_forest";
        [Range(0f, 1f)] public float ForestGain = .38f;
        [Tooltip("Далёкий гул Разлома")]
        public string DroneLoop = "camp_rift_drone";
        [Range(0f, 1f)] public float DroneGain = .12f;

        [Header("Порывы ветра")]
        public string GustPrefix = "camp_wind_gust";
        public string LeavesPrefix = "camp_leaves";
        public Vector2 GustInterval = new Vector2(16f, 38f);
        [Range(0f, 1f)] public float GustGain = .5f;
        [Tooltip("Во сколько раз усиливается качание листвы на порыве")]
        [Range(1f, 3f)] public float GustBreeze = 1.8f;
        [Tooltip("Длительность порыва, секунд")]
        public float GustDuration = 2.6f;

        [Header("Одиночные звуки")]
        public Group[] Groups =
        {
            new Group { Prefix = "camp_woodpecker",   Interval = new Vector2(26f, 60f), Gain = .45f, Spread = 9f },
            new Group { Prefix = "camp_crow",         Interval = new Vector2(34f, 80f), Gain = .40f, Spread = 11f },
            new Group { Prefix = "camp_birds_takeoff",Interval = new Vector2(40f, 95f), Gain = .45f, Spread = 8f },
            new Group { Prefix = "camp_cloth",        Interval = new Vector2(18f, 44f), Gain = .35f, Spread = 5f },
        };

        AudioSource _forest, _drone;
        AudioSource[] _shots;
        int _shot;
        AudioClip[] _gusts, _leaves;
        float _clock, _nextGust, _gustStarted = -99f, _baseBreeze = 1f;
        bool _loaded;

        void OnEnable()
        {
            if (!_loaded) Load();
            _clock = 0f;
            _nextGust = Random.Range(GustInterval.x, GustInterval.y) * .5f;
            foreach (Group group in Groups)
                group.Next = Random.Range(group.Interval.x, group.Interval.y) * .6f;
        }

        void OnDisable()
        {
            if (_forest != null) _forest.Stop();
            if (_drone != null) _drone.Stop();
            if (Ambience != null) Ambience.BreezeStrength = _baseBreeze;
        }

        void Load()
        {
            _loaded = true;
            AudioClip[] all = Resources.LoadAll<AudioClip>(Folder);
            AudioClip[] Pick(string prefix) => string.IsNullOrEmpty(prefix)
                ? Array.Empty<AudioClip>()
                : Array.FindAll(all, c => c != null && c.name.StartsWith(prefix, StringComparison.Ordinal));

            _forest = MakeSource("Вечерний лес", true);
            AudioClip[] forest = Pick(ForestLoop);
            if (forest.Length > 0) _forest.clip = forest[0];
            _drone = MakeSource("Гул Разлома", true);
            AudioClip[] drone = Pick(DroneLoop);
            if (drone.Length > 0) _drone.clip = drone[0];

            _gusts = Pick(GustPrefix);
            _leaves = Pick(LeavesPrefix);
            foreach (Group group in Groups) group.Clips = Pick(group.Prefix);

            // Три источника по кругу: редкие звуки не обрывают друг друга.
            _shots = new AudioSource[3];
            for (int i = 0; i < _shots.Length; i++) _shots[i] = MakeSource("Одиночный звук " + (i + 1), false);
            if (Ambience != null) _baseBreeze = Ambience.BreezeStrength;
        }

        AudioSource MakeSource(string name, bool loop)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            return source;
        }

        void Update()
        {
            // При записи звука съёмка идёт по игровому времени кадра, иначе — по реальному.
            float dt = CombatAudioCapture.Recording ? Time.deltaTime : Time.unscaledDeltaTime;
            _clock += dt;

            CampPlayerView player = CampPlayerView.Instance;
            bool audible = player != null && player.Active && !MainMenuView.IsOpen;
            bool panel = player != null && (player.InventoryOpen || player.EntranceOpen);
            float duck = panel ? MenuDuck : 1f;
            float gain = MasterGain * GameUserSettings.EffectsVolume * duck * (audible ? 1f : 0f);

            Loop(_forest, ForestGain * gain, audible, dt);
            Loop(_drone, DroneGain * gain, audible, dt);
            if (!audible) { ReleaseGust(); return; }

            if (_clock >= _nextGust) Gust(gain);
            UpdateGust();

            foreach (Group group in Groups)
            {
                if (group.Clips == null || group.Clips.Length == 0) continue;
                if (_clock < group.Next) continue;
                group.Next = _clock + Random.Range(group.Interval.x, group.Interval.y);
                PlayAround(group.Clips[Random.Range(0, group.Clips.Length)], group.Gain * gain, group.PitchJitter, group.Spread);
            }
        }

        void Loop(AudioSource source, float wanted, bool audible, float dt)
        {
            if (source == null || source.clip == null) return;
            if (audible && !source.isPlaying) { source.time = Random.Range(0f, Mathf.Max(0f, source.clip.length - 1f)); source.Play(); }
            source.volume = Mathf.Lerp(source.volume, wanted, 1f - Mathf.Exp(-dt * 2f));
            if (!audible && source.volume < .001f && source.isPlaying) source.Stop();
        }

        void Gust(float gain)
        {
            _nextGust = _clock + Random.Range(GustInterval.x, GustInterval.y);
            _gustStarted = _clock;
            if (_gusts != null && _gusts.Length > 0) PlayAround(_gusts[Random.Range(0, _gusts.Length)], GustGain * gain, .05f, 6f);
            if (_leaves != null && _leaves.Length > 0) PlayAround(_leaves[Random.Range(0, _leaves.Length)], GustGain * .8f * gain, .07f, 8f);
        }

        /// <summary>Листва качается вместе со звуком: порыв нарастает и стихает, а не включается ступенькой.</summary>
        void UpdateGust()
        {
            if (Ambience == null) return;
            float since = _clock - _gustStarted;
            if (since < 0f || since > GustDuration) { Ambience.BreezeStrength = _baseBreeze; return; }
            float wave = Mathf.Sin(since / GustDuration * Mathf.PI);
            Ambience.BreezeStrength = Mathf.Lerp(_baseBreeze, _baseBreeze * GustBreeze, wave);
        }

        void ReleaseGust()
        {
            if (Ambience != null) Ambience.BreezeStrength = _baseBreeze;
        }

        void PlayAround(AudioClip clip, float volume, float jitter, float spread)
        {
            if (clip == null || _shots == null || volume <= .0005f) return;
            AudioSource source = _shots[_shot];
            _shot = (_shot + 1) % _shots.Length;
            source.clip = clip;
            source.volume = volume;
            source.pitch = 1f + Random.Range(-jitter, jitter);
            // Панорама вместо трёхмерного звука: камера лагеря стоит высоко, и расстояние считается от героя.
            source.panStereo = Mathf.Clamp(Random.Range(-spread, spread) / 12f, -.6f, .6f);
            source.Play();
        }
    }
}
