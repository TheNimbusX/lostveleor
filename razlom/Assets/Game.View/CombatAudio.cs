using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Звук боя.
    ///
    /// ПОЧЕМУ ЭТО ВАЖНЕЕ, ЧЕМ ВЫГЛЯДИТ. Ощущение «сочно бьёт» на девять десятых
    /// собирается ушами: звук удара воспроизводится десятки тысяч раз за сессию,
    /// музыкальная тема — десятки. Пока удар молчит, никакая картинка его
    /// не спасёт.
    ///
    /// ВСЕ КЛИПЫ СИНТЕЗИРУЮТСЯ КОДОМ. Ни одного файла, ни одного импорта:
    /// на прототипе важно, чтобы иерархия звуков существовала и её можно было
    /// крутить числами, а не чтобы она была красивой. Настоящие сэмплы встанут
    /// на те же места — меняется LoadClips, остальное нет.
    ///
    /// Читает подтверждённые SimEvent, как и весь презентационный слой,
    /// и ничего не решает сам.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(1100)]
    public sealed class CombatAudio : MonoBehaviour
    {
        [Header("Громкость")]
        [Range(0f, 1f)] public float Master = 0.75f;
        [Range(0f, 1f)] public float HitVolume = 0.42f;
        [Range(0f, 1f)] public float CritVolume = 0.70f;
        [Range(0f, 1f)] public float KillVolume = 0.62f;
        [Range(0f, 1f)] public float PlayerHurtVolume = 0.80f;
        [Range(0f, 1f)] public float CastVolume = 0.55f;
        [Range(0f, 1f)] public float RewardVolume = 0.70f;

        [Header("Агрегация")]
        [Tooltip("Сколько одинаковых звуков разрешено за кадр. Двадцать попаданий " +
                 "по площади должны звучать как один сочный удар, а не как помехи.")]
        public int MaxPerKindPerFrame = 3;

        [Tooltip("Голосов в пуле. Больше — гуще каша, меньше — глотаются удары.")]
        public int Voices = 12;

        private const int SampleRate = 44100;

        private enum Sound : byte
        {
            Hit = 0,
            Crit = 1,
            Kill = 2,
            PlayerHurt = 3,
            Cast = 4,
            Reward = 5,

            Count = 6,
        }

        private TickDriver _driver;
        private AudioSource[] _voices;
        private int _voiceCursor;

        private readonly AudioClip[] _clips = new AudioClip[(int)Sound.Count];
        private readonly int[] _playedThisFrame = new int[(int)Sound.Count];

        private uint _random = 0x2545F491u;
        private GameMode _modeShown = GameMode.Camp;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
        }

        private void Start()
        {
            BuildClips();
            BuildVoices();
        }

        private void LateUpdate()
        {
            if (_voices == null) return;

            for (int i = 0; i < _playedThisFrame.Length; i++) _playedThisFrame[i] = 0;

            PlayModeChange();

            if (_driver.Sim == null) return;
            ConsumeEvents();
        }

        /// <summary>
        /// Звук на смену режима: взятая награда и конец забега — это события,
        /// которые игрок обязан услышать, даже если смотрит в другую часть экрана.
        /// </summary>
        private void PlayModeChange()
        {
            GameSession session = _driver.Session;
            if (session == null) return;

            if (session.Mode == _modeShown) return;
            _modeShown = session.Mode;

            if (_modeShown == GameMode.Summary) Play(Sound.Reward, RewardVolume, 0.92f);
        }

        private void ConsumeEvents()
        {
            IReadOnlyList<SimEvent> events = _driver.FrameEvents;

            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];

                switch (e.Type)
                {
                    case SimEventType.Damage:
                        PlayDamage(in e);
                        break;

                    case SimEventType.Death:
                        // Смерть игрока не звучит добиванием: её уже озвучил
                        // удар, который её нанёс, а «кульминация» на своей же
                        // гибели читалась бы как награда.
                        if (e.Target != Simulation.PlayerId) Play(Sound.Kill, KillVolume, 1f);
                        break;

                    case SimEventType.AbilityCast:
                        if (e.Source == Simulation.PlayerId) Play(Sound.Cast, CastVolume, 1f);
                        break;
                }
            }
        }

        /// <summary>
        /// Иерархия силы, слышимая ухом: удар по герою тревожнее любого своего,
        /// крит заметнее обычного, обычный — короткий и ясный. Удар, в котором
        /// не участвует игрок, не звучит вообще: в кадре и так сорок врагов.
        /// </summary>
        private void PlayDamage(in SimEvent e)
        {
            if (e.Target == Simulation.PlayerId)
            {
                Play(Sound.PlayerHurt, PlayerHurtVolume, 1f);
                return;
            }

            if (e.Source != Simulation.PlayerId) return;

            if (e.Flag) Play(Sound.Crit, CritVolume, 1f);
            else Play(Sound.Hit, HitVolume, 1f);
        }

        /// <summary>
        /// Проигрывает звук с разбросом высоты и агрегацией по кадру.
        ///
        /// Разброс обязателен: без него сотня одинаковых ударов складывается
        /// в фазовую гребёнку и начинает звучать механически — это слышно
        /// даже тем, кто не знает слова «фаза».
        /// </summary>
        private void Play(Sound sound, float volume, float volumeScale)
        {
            int index = (int)sound;
            AudioClip clip = _clips[index];
            if (clip == null) return;

            if (_playedThisFrame[index] >= MaxPerKindPerFrame) return;

            // Каждый следующий одинаковый звук в кадре тише предыдущего:
            // залп по площади должен читаться как один плотный удар.
            float crowding = 1f / (1f + _playedThisFrame[index]);
            _playedThisFrame[index]++;

            AudioSource voice = _voices[_voiceCursor];
            _voiceCursor = (_voiceCursor + 1) % _voices.Length;

            voice.clip = clip;
            voice.volume = Mathf.Clamp01(volume * volumeScale * crowding * Master);
            voice.pitch = 1f + (Random01() - 0.5f) * 0.12f;
            voice.Play();
        }

        // ---- синтез ----

        private void BuildVoices()
        {
            Transform root = new GameObject("Пул: голоса боя").transform;
            root.SetParent(transform, false);

            _voices = new AudioSource[Mathf.Max(1, Voices)];
            for (int i = 0; i < _voices.Length; i++)
            {
                var go = new GameObject($"Голос {i}");
                go.transform.SetParent(root, false);

                AudioSource source = go.AddComponent<AudioSource>();

                // 2D-звук намеренно: бой идёт вокруг персонажа, и панорама
                // по мировым координатам только размазывала бы удар.
                source.spatialBlend = 0f;
                source.playOnAwake = false;
                source.loop = false;
                source.bypassReverbZones = true;
                _voices[i] = source;
            }
        }

        private void BuildClips()
        {
            _clips[(int)Sound.Hit] = MakeHit();
            _clips[(int)Sound.Crit] = MakeCrit();
            _clips[(int)Sound.Kill] = MakeKill();
            _clips[(int)Sound.PlayerHurt] = MakePlayerHurt();
            _clips[(int)Sound.Cast] = MakeCast();
            _clips[(int)Sound.Reward] = MakeReward();
        }

        /// <summary>
        /// Обычное попадание: короткий шумовой щелчок плюс низкий толчок.
        /// Восемьдесят миллисекунд — верхняя граница того, что ухо ещё слышит
        /// как один удар, а не как звук с длительностью.
        /// </summary>
        private AudioClip MakeHit()
        {
            float[] data = NewBuffer(0.085f);
            AddNoise(data, 0.90f, 0.016f, 0.35f);
            AddSine(data, 150f, 0.55f, 0.045f);
            AddSine(data, 320f, 0.25f, 0.022f);
            return Finish("hit", data);
        }

        /// <summary>
        /// Крит: тот же удар, но ярче и с металлическим призвуком, который
        /// тянется дольше. Отличать крит на слух — требование иерархии силы.
        /// </summary>
        private AudioClip MakeCrit()
        {
            float[] data = NewBuffer(0.24f);
            AddNoise(data, 1.00f, 0.020f, 0.62f);
            AddSine(data, 170f, 0.60f, 0.060f);
            AddSine(data, 940f, 0.30f, 0.140f);
            AddSine(data, 1410f, 0.20f, 0.110f);
            return Finish("crit", data);
        }

        /// <summary>
        /// Добивание: низкий провал вниз. Кульминация цепочки — единственный
        /// звук боя, которому позволено быть длинным.
        /// </summary>
        private AudioClip MakeKill()
        {
            float[] data = NewBuffer(0.36f);
            AddNoise(data, 0.55f, 0.055f, 0.22f);
            AddSweep(data, 210f, 70f, 0.75f, 0.150f);
            AddSine(data, 95f, 0.45f, 0.190f);
            return Finish("kill", data);
        }

        /// <summary>
        /// Удар по герою: глухой, тревожный, заметно ниже своих. Игрок обязан
        /// понять, что бьют его, не глядя на полоску здоровья.
        /// </summary>
        private AudioClip MakePlayerHurt()
        {
            float[] data = NewBuffer(0.26f);
            AddNoise(data, 0.45f, 0.030f, 0.14f);
            AddSweep(data, 260f, 120f, 0.85f, 0.120f);
            AddSine(data, 62f, 0.50f, 0.150f);
            return Finish("player_hurt", data);
        }

        /// <summary>Каст: короткий подъём с воздухом. Отличается от удара по направлению движения тона.</summary>
        private AudioClip MakeCast()
        {
            float[] data = NewBuffer(0.30f);
            AddSweep(data, 320f, 1180f, 0.42f, 0.120f);
            AddNoise(data, 0.30f, 0.090f, 0.85f);
            return Finish("cast", data);
        }

        /// <summary>
        /// Награда: чистый колокол на трёх обертонах. Второй приоритет звука
        /// по документу — звон, узнаваемый из соседней комнаты.
        /// </summary>
        private AudioClip MakeReward()
        {
            float[] data = NewBuffer(0.85f);
            AddSine(data, 784f, 0.50f, 0.320f);
            AddSine(data, 1176f, 0.28f, 0.240f);
            AddSine(data, 1568f, 0.16f, 0.180f);
            return Finish("reward", data);
        }

        private static float[] NewBuffer(float seconds)
            => new float[Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate))];

        private void AddNoise(float[] data, float amplitude, float decay, float brightness)
        {
            // Однополюсный фильтр: brightness = 1 это чистый шум, меньше — глуше.
            float previous = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float raw = Random01() * 2f - 1f;
                previous += (raw - previous) * brightness;
                data[i] += previous * amplitude * Decay(i, decay);
            }
        }

        private static void AddSine(float[] data, float frequency, float amplitude, float decay)
        {
            float step = 2f * Mathf.PI * frequency / SampleRate;
            for (int i = 0; i < data.Length; i++)
                data[i] += Mathf.Sin(step * i) * amplitude * Decay(i, decay);
        }

        /// <summary>Тон, едущий от одной частоты к другой. Фаза копится, иначе будет щелчок.</summary>
        private static void AddSweep(float[] data, float from, float to, float amplitude, float decay)
        {
            float phase = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / data.Length;
                float frequency = Mathf.Lerp(from, to, t);
                phase += 2f * Mathf.PI * frequency / SampleRate;
                data[i] += Mathf.Sin(phase) * amplitude * Decay(i, decay);
            }
        }

        private static float Decay(int sample, float tau)
            => Mathf.Exp(-(sample / (float)SampleRate) / Mathf.Max(0.0005f, tau));

        /// <summary>
        /// Нормализует и сглаживает края. Без короткого фейда в начале и конце
        /// клип щёлкает на старте и обрыве — это слышно на каждом ударе.
        /// </summary>
        private static AudioClip Finish(string name, float[] data)
        {
            float peak = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float a = data[i] < 0f ? -data[i] : data[i];
                if (a > peak) peak = a;
            }
            if (peak > 0.0001f)
            {
                float gain = 0.92f / peak;
                for (int i = 0; i < data.Length; i++) data[i] *= gain;
            }

            int fade = Mathf.Min(48, data.Length / 4);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                data[i] *= k;
                data[data.Length - 1 - i] *= k;
            }

            AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// Свой генератор случайности. Не Unity Random намеренно: он общий
        /// на всю игру, и звук не должен сдвигать чужие броски — даже те,
        /// что живут в представлении.
        /// </summary>
        private float Random01()
        {
            _random ^= _random << 13;
            _random ^= _random >> 17;
            _random ^= _random << 5;
            return (_random & 0xFFFFFFu) / 16777215f;
        }
    }
}
