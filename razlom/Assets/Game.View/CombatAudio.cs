using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Presentation-only combat mix built from imported CC0 one-shots.
    /// It consumes confirmed simulation events and never decides damage itself.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(1100)]
    public sealed class CombatAudio : MonoBehaviour
    {
        private static readonly float AttackContactTime =
            Simulation.AttackWindupTicks / (float)Simulation.TicksPerSecond;
        [Header("Mix")]
        [Range(0f, 1f)] public float Master = 0.72f;
        [Range(0f, 1f)] public float WhooshVolume = 0.40f;
        [Range(0f, 1f)] public float MetalVolume = 0.34f;
        [Range(0f, 1f)] public float BodyVolume = 0.52f;
        [Range(0f, 1f)] public float KillVolume = 0.56f;
        [Range(0f, 1f)] public float DissolveVolume = 0.44f;
        [Range(0f, 1f)] public float WhirlwindVolume = 0.60f;
        [Range(0f, 1f)] public float CastVolume = 0.50f;
        [Range(0f, 1f)] public float RewardVolume = 0.60f;
        [Range(0f, 1f)] public float AbilityVolume = 0.58f;
        [Range(0f, 1f)] public float FootstepVolume = 0.30f;

        [Header("Шаги")]
        [Tooltip("Сколько метров проходит герой между шагами.")]
        [Min(0.4f)] public float FootstepDistance = 1.35f;

        [Header("Density")]
        [Tooltip("AoE contacts in one frame are mixed into one readable impact.")]
        [Min(1)] public int MaxPerKindPerFrame = 1;
        [Min(4)] public int Voices = 14;

        private enum Sound : byte
        {
            Whoosh = 0,
            HitMetal = 1,
            HitBody = 2,
            Kill = 3,
            Whirlwind = 4,
            Cast = 5,
            Reward = 6,
            AnchorSweep = 7,
            ChainStep = 8,

            /// <summary>
            /// Шаг. Три части подряд, а не случайная из трёх.
            ///
            /// Владелец сдал их как ОДИН трёхкомпонентный звук ходьбы: части
            /// продолжают друг друга, и случайный выбор превратил бы связную
            /// поступь в дробь из одинаковых щелчков.
            /// </summary>
            Footstep = 9,

            /// <summary>
            /// Осыпание трупа. Отдельный звук от Kill, а не замена ему: Kill —
            /// это удар, который убил, а этот — то, как тело перестаёт быть.
            /// Они и звучат в разное время, см. DissolveDelay.
            /// </summary>
            Dissolve = 10,

            Count = 11,
        }

        /// <summary>
        /// Когда осыпание вступает после смерти.
        ///
        /// ЧИСЛО НЕ ПОДОБРАНО, А ВЗЯТО ИЗ ПОКАЗА. Порядок в ArenaView такой:
        /// падение (OrvillDeathAnimationDuration = 0.73) → лежит
        /// (OrvillDeathPoseHoldDuration = 0.14) → растворение
        /// (OrvillDeathFadeDuration = 0.50). Сумма первых двух и есть момент,
        /// когда _DeathFade трогается с нуля, — здесь и вступает звук.
        ///
        /// Сам клип обрезан ровно под 0.50 с растворения: владелец просил,
        /// чтобы звук совпадал со временем осыпания, а не догорал после него.
        ///
        /// ЗАВИСИТ ОТ ArenaView. Меняешь окно растворения — правь здесь и
        /// перережь dissolve_sand_00.ogg под новую длину.
        /// </summary>
        private const float DissolveDelay = ArenaView.OrvillDeathDissolveStartDelay;

        // The visual dissolve window is fixed by ArenaView. Imported clips may
        // contain a small encoder tail, so the dissolve voice is rate-matched
        // to the same 0.50 s window instead of audibly outliving the body.
        private const float DissolveDuration = 0.50f;

        /// <summary>
        /// Отложенные осыпания: время, когда каждое должно прозвучать.
        ///
        /// ПОЧЕМУ НЕ PlayDelayed. Он занимает голос из пула с момента вызова,
        /// а ждать теперь почти секунду: голос простаивал бы всю смерть и его
        /// успел бы отобрать следующий удар — звук пропал бы молча. Здесь
        /// голос берётся в тот момент, когда пора играть.
        /// </summary>
        private readonly float[] _dissolveDue = new float[16];
        private int _dissolveDueCount;
        private float _anchorImpactAt = -1f, _anchorLandAt = -1f;

        private TickDriver _driver;
        private AudioSource[] _voices;
        private int _voiceCursor;
        private readonly AudioClip[][] _variants = new AudioClip[(int)Sound.Count][];
        private readonly int[] _lastVariant = new int[(int)Sound.Count];
        private readonly int[] _playedThisFrame = new int[(int)Sound.Count];
        private uint _random = 0x2545F491u;
        private GameMode _modeShown = GameMode.Camp;
        private float _whooshDelay = -1f;
        private int _whooshAttackVariant;

        // ---- шаги ----
        //
        // Считаются по ПРОЙДЕННОМУ ПУТИ, а не по таймеру. Таймер отвязан от
        // скорости: замедленный герой продолжал бы частить, ускоренный —
        // скользить беззвучно. Путь даёт постоянную длину шага при любой
        // скорости, и это ровно то, что слышно как походка.
        private Vector2 _stepAnchor;
        private bool _stepAnchorSet;
        private int _stepPart;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            for (int i = 0; i < _lastVariant.Length; i++) _lastVariant[i] = -1;
        }

        private void Start()
        {
            LoadClips();
            BuildVoices();
        }

        private void LateUpdate()
        {
            if (_voices == null) return;
            for (int i = 0; i < _playedThisFrame.Length; i++) _playedThisFrame[i] = 0;

            PlayModeChange();
            UpdateWhoosh();
            if (_driver.Sim != null)
            {
            ConsumeEvents();
            if (_anchorImpactAt >= 0f && Time.time >= _anchorImpactAt)
            {
                _anchorImpactAt = -1f;
                Play(Sound.HitMetal, MetalVolume * .85f, .78f, .02f);
                Play(Sound.HitBody, BodyVolume * .55f, .82f, .02f);
            }
            if (_anchorLandAt >= 0f && Time.time >= _anchorLandAt)
            {
                _anchorLandAt = -1f;
                PlayFootstepPart();
            }
                UpdateFootsteps();
            }

            // ПОСЛЕ ConsumeEvents. Смерть этого кадра ставится в очередь на
            // без малого секунду вперёд, так что раньше следующего кадра
            // сработать всё равно не может, — а вот вчерашние очереди должны
            // успеть прозвучать до того, как кадр закончится.
            FlushDissolves();
        }

        private void PlayModeChange()
        {
            GameSession session = _driver.Session;
            if (session == null || session.Mode == _modeShown) return;
            _modeShown = session.Mode;

            // Смена режима обрывает бой на середине. Осыпание, поставленное в
            // очередь за долю секунды до выхода из Разлома, прозвучало бы уже
            // в лагере — над пустой поляной, без тела.
            _dissolveDueCount = 0;
            _anchorImpactAt = _anchorLandAt = -1f;

            if (_modeShown == GameMode.Summary)
                Play(Sound.Reward, RewardVolume, 0.96f, 0.03f);
        }

        private void ConsumeEvents()
        {
            IReadOnlyList<SimEvent> events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                switch (e.Type)
                {
                    case SimEventType.Attack:
                        if (e.Source == Simulation.PlayerId)
                        {
                            // Whoosh leads the shared contact tick; keeping the
                            // lead relative to Simulation avoids drift when the
                            // attack windup is tuned.
                            _whooshDelay = AttackContactTime *
                                (e.ActionVariant == 1 ? 0.50f : 0.55f);
                            _whooshAttackVariant = e.ActionVariant;
                        }
                        break;

                    case SimEventType.Damage:
                        PlayDamage(in e);
                        break;

                    case SimEventType.Death:
                        if (e.Target == Simulation.PlayerId) _anchorImpactAt = _anchorLandAt = -1f;
                        if (e.Target != Simulation.PlayerId)
                        {
                            Play(Sound.Kill, KillVolume, 0.90f, 0.05f);
                            QueueDissolve(_driver.Sim.Entities.Kind[e.Target]);
                        }
                        break;

                    case SimEventType.AbilityCast:
                        if (e.Source == Simulation.PlayerId)
                        {
                            _anchorImpactAt = _anchorLandAt = -1f;
                            _whooshDelay = -1f;
                            if (_driver.Sim.GetAbility(e.Amount)?.DefinitionId == AbilityDefinition.AnchorLeapId)
                            {
                                // Даже промах имеет контакт с землёй; попадания во врагов звучат по Damage.
                                _anchorImpactAt = Time.time + PelagAbilityTiming.LeapWindup;
                                _anchorLandAt = Time.time + PelagAbilityTiming.LeapArrival;
                            }
                            if (IsWhirlwindSlot(e.Amount))
                            {
                                // Whirlwind cancels a primed basic attack in
                                // Sim. Cancel its delayed whoosh too, otherwise
                                // the old swing lands acoustically inside the
                                // ability and makes the next combo feel late.
                                _whooshDelay = -1f;
                                // Curated sweep peak is at ~0.30 s and the
                                // deterministic Whirlwind contact is at 0.333 s.
                                // A fixed identity keeps that signature aligned
                                // on every cast instead of randomising two cues
                                // whose peaks land on different combat phases.
                                Play(Sound.Whirlwind, WhirlwindVolume, 0.96f, 0.015f);
                            }
                            else
                            {
                                // У Подсечки и Шага по цепи теперь свои записи.
                                // У Броска якоря записи нет — он берёт общий
                                // Cast, поднятый по тону: рывок должен звучать
                                // суше и выше волока, иначе две способности на
                                // одной цепи сливаются на слух.
                                Sound sound = AbilitySound(e.Amount);
                                Play(sound,
                                    sound == Sound.Cast ? CastVolume : AbilityVolume,
                                    sound == Sound.Cast ? AbilityCastPitch(e.Amount) : 0.98f,
                                    0.03f);
                            }
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Трёхкомпонентная поступь: pt1 → pt2 → pt3 → pt1.
        ///
        /// Части идут ПОДРЯД, а не случайно. Владелец сдал их как один звук
        /// ходьбы, где части продолжают друг друга; случайный выбор из трёх
        /// превратил бы связную поступь в дробь.
        /// </summary>
        private void UpdateFootsteps()
        {
            Simulation sim = _driver.Sim;
            GameSession session = _driver.Session;
            EntityStore e = sim.Entities;
            int player = Simulation.PlayerId;

            bool walking = session != null
                           && session.Mode == GameMode.Rift
                           && e.Alive[player]
                           && e.Velocity[player].LengthSq.Raw != 0
                           && e.ForcedTicksLeft[player] <= 0;

            if (!walking)
            {
                // Якорь сбрасывается на остановке, чтобы первый шаг после
                // паузы звучал сразу, а не через полтора метра.
                _stepAnchorSet = false;
                return;
            }

            Vector2 here = new Vector2(
                e.Position[player].X.ToFloat(), e.Position[player].Y.ToFloat());

            if (!_stepAnchorSet)
            {
                _stepAnchor = here;
                _stepAnchorSet = true;
                return;
            }

            float step = Mathf.Max(0.4f, FootstepDistance);
            if ((here - _stepAnchor).sqrMagnitude < step * step) return;

            _stepAnchor = here;
            PlayFootstepPart();
        }

        private void PlayFootstepPart()
        {
            AudioClip[] parts = _variants[(int)Sound.Footstep];
            if (parts == null || parts.Length == 0) return;

            AudioClip clip = parts[_stepPart % parts.Length];
            _stepPart++;
            if (clip == null) return;

            // Шаг играется мимо общего Play: тот выбирает вариант случайно,
            // а здесь порядок и есть содержание звука. Голос берётся из того же
            // кольца — иначе шаги заняли бы собственный источник и перестали
            // вытесняться в общей давке.
            if (_voices == null || _voices.Length == 0) return;
            AudioSource voice = _voices[_voiceCursor];
            _voiceCursor = (_voiceCursor + 1) % _voices.Length;
            if (voice == null) return;
            voice.clip = clip;
            voice.volume = Master * FootstepVolume;
            voice.pitch = 0.99f + (Random01() - 0.5f) * 0.04f;
            voice.Play();
        }

        private void UpdateWhoosh()
        {
            if (_whooshDelay < 0f) return;
            _whooshDelay -= Time.deltaTime;
            if (_whooshDelay > 0f) return;

            _whooshDelay = -1f;
            bool heavy = _whooshAttackVariant == 1;
            Play(Sound.Whoosh, WhooshVolume * (heavy ? 1.12f : 1f),
                heavy ? 0.92f : 1.03f, 0.045f);
        }

        private void PlayDamage(in SimEvent e)
        {
            // Player damage keeps its visual flash/recoil but intentionally has
            // no one-shot until a dedicated, approved hurt cue exists.
            if (e.Target == Simulation.PlayerId) return;

            if (e.Source != Simulation.PlayerId) return;

            bool ability = e.DamageOrigin == DamageOrigin.Ability;
            bool whirlwind = ability && IsWhirlwindSlot(e.ActionVariant);
            bool heavy = e.Flag || e.ActionVariant == 1 || ability;

            // Blade definition and body weight are separate layers. The AoE cap
            // turns a whole Whirlwind contact into one large, clean event.
            if (whirlwind)
            {
                // The spin is the hero layer. Contact only adds definition and
                // weight; full-strength metal+body masked the sweep and could
                // sum into a clipped wall together with a same-frame kill.
                Play(Sound.HitMetal, MetalVolume * 0.70f, 0.96f, 0.025f);
                Play(Sound.HitBody, BodyVolume * 0.65f, 0.88f, 0.025f);
            }
            else
            {
                Play(Sound.HitMetal, MetalVolume * (heavy ? 1.24f : 1f),
                    heavy ? 0.92f : 1.02f, 0.05f);
                Play(Sound.HitBody, BodyVolume * (heavy ? 1.22f : 1f),
                    ability ? 0.76f : (heavy ? 0.86f : 0.94f), 0.045f);
            }
        }

        private bool IsWhirlwindSlot(int slot)
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null || (uint)slot >= Simulation.AbilitySlots) return false;
            AbilityBuild build = sim.GetAbility(slot);
            return build != null && build.DefinitionId == AbilityDefinition.WhirlwindId;
        }

        /// <summary>
        /// Высота общего звука каста под конкретную способность.
        ///
        /// Спрашивается по DefinitionId, а не по номеру слота: слот — это
        /// позиция на панели, и она уже один раз переезжала.
        /// </summary>
        /// <summary>
        /// Своя запись способности или общий каст, если записи нет.
        ///
        /// Разбор по DefinitionId, а не по номеру слота: слот — это позиция
        /// на панели, и она уже дважды переезжала.
        /// </summary>
        private Sound AbilitySound(int slot)
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null || (uint)slot >= Simulation.AbilitySlots) return Sound.Cast;

            AbilityBuild build = sim.GetAbility(slot);
            if (build == null) return Sound.Cast;

            if (build.DefinitionId == AbilityDefinition.AnchorSweepId) return Sound.AnchorSweep;
            if (build.DefinitionId == AbilityDefinition.ChainStepId) return Sound.ChainStep;
            return Sound.Cast;
        }

        private float AbilityCastPitch(int slot)
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null || (uint)slot >= Simulation.AbilitySlots) return 0.95f;

            AbilityBuild build = sim.GetAbility(slot);
            if (build == null) return 0.95f;

            if (build.DefinitionId == AbilityDefinition.AnchorLeapId) return 1.22f;
            if (build.DefinitionId == AbilityDefinition.AnchorSweepId) return 0.74f;
            if (build.DefinitionId == AbilityDefinition.ChainStepId) return 1.02f;
            return 0.95f;
        }

        private void QueueDissolve(EnemyKind kind)
        {
            // Переполнение — не ошибка, а решение: если за одну секунду умерло
            // больше шестнадцати, шестнадцатый шелест всё равно неразличим.
            if (_dissolveDueCount >= _dissolveDue.Length) return;
            _dissolveDue[_dissolveDueCount++] = Time.time + ArenaView.DeathDissolveStartDelay(kind);
        }

        private void FlushDissolves()
        {
            float now = Time.time;
            int write = 0;
            for (int i = 0; i < _dissolveDueCount; i++)
            {
                if (_dissolveDue[i] > now)
                {
                    _dissolveDue[write++] = _dissolveDue[i];
                    continue;
                }

                // Pitch здесь намеренно фиксирован. Этот голос подгоняется к
                // ровно 0.50 с визуального dissolve; случайный разброс pitch
                // снова менял бы его длину до 0.46..0.55 с и возвращал
                // рассинхрон. Вариативность толпы даёт crowding микса.
                Play(Sound.Dissolve, DissolveVolume, 1.00f, 0f,
                    playbackDuration: DissolveDuration);
            }
            _dissolveDueCount = write;
        }

        private void Play(Sound sound, float volume, float pitchCenter, float pitchSpread,
            float delay = 0f, float playbackDuration = 0f)
        {
            int soundIndex = (int)sound;
            AudioClip[] clips = _variants[soundIndex];
            if (clips == null || clips.Length == 0) return;
            if (_playedThisFrame[soundIndex] >= MaxPerKindPerFrame) return;

            float crowding = 1f / (1f + _playedThisFrame[soundIndex]);
            _playedThisFrame[soundIndex]++;

            int variant = PickVariant(soundIndex, clips.Length);
            AudioSource voice = _voices[_voiceCursor];
            _voiceCursor = (_voiceCursor + 1) % _voices.Length;
            voice.clip = clips[variant];
            voice.volume = Mathf.Clamp01(volume * crowding * Master
                                         * GameUserSettings.EffectsVolume);
            float pitch = pitchCenter + (Random01() - 0.5f) * pitchSpread * 2f;
            if (playbackDuration > 0.001f && voice.clip.length > 0.001f)
                pitch *= voice.clip.length / playbackDuration;
            voice.pitch = Mathf.Clamp(pitch, 0.70f, 1.18f);
            // PlayDelayed, а не корутина: задержка отсчитывается по звуковым
            // часам, и осыпание не съезжает от растворения на просадке кадров.
            // Голос при этом занят с этой секунды — то есть отложенный звук
            // нельзя перебить, не остановив его же.
            if (delay > 0f) voice.PlayDelayed(delay);
            else voice.Play();
        }

        private int PickVariant(int soundIndex, int count)
        {
            if (count <= 1) return 0;
            int pick = Mathf.Min(count - 1, (int)(Random01() * count));
            if (pick == _lastVariant[soundIndex]) pick = (pick + 1) % count;
            _lastVariant[soundIndex] = pick;
            return pick;
        }

        private void BuildVoices()
        {
            Transform root = new GameObject("Combat audio voices").transform;
            root.SetParent(transform, false);
            _voices = new AudioSource[Mathf.Max(4, Voices)];

            for (int i = 0; i < _voices.Length; i++)
            {
                var go = new GameObject($"Voice {i}");
                go.transform.SetParent(root, false);
                AudioSource source = go.AddComponent<AudioSource>();
                source.spatialBlend = 0f;
                source.playOnAwake = false;
                source.loop = false;
                source.bypassReverbZones = true;
                _voices[i] = source;
            }
        }

        private void LoadClips()
        {
            _variants[(int)Sound.Whoosh] = Resources.LoadAll<AudioClip>("Audio/Combat/Whoosh");
            _variants[(int)Sound.HitMetal] = Resources.LoadAll<AudioClip>("Audio/Combat/HitMetal");
            _variants[(int)Sound.HitBody] = Resources.LoadAll<AudioClip>("Audio/Combat/HitBody");
            _variants[(int)Sound.Kill] = Resources.LoadAll<AudioClip>("Audio/Combat/Kill");
            _variants[(int)Sound.Dissolve] = Resources.LoadAll<AudioClip>("Audio/Combat/Dissolve");
            AudioClip whirlwind = Resources.Load<AudioClip>("Audio/Combat/whirlwind_pelag_pcm");
            _variants[(int)Sound.Whirlwind] = whirlwind != null
                ? new[] { whirlwind }
                : new AudioClip[0];
            if (whirlwind == null)
                Debug.LogWarning("[CombatAudio] Missing Resources/Audio/Combat/whirlwind_pelag_pcm.", this);
            _variants[(int)Sound.Cast] = Resources.LoadAll<AudioClip>("Audio/Combat/Cast");
            _variants[(int)Sound.Reward] = Resources.LoadAll<AudioClip>("Audio/Combat/Reward");
            _variants[(int)Sound.AnchorSweep] = Resources.LoadAll<AudioClip>("Audio/Combat/AnchorSweep");
            _variants[(int)Sound.ChainStep] = Resources.LoadAll<AudioClip>("Audio/Combat/ChainStep");

            // Шаги сортируются по имени: LoadAll порядок не гарантирует, а
            // здесь он и есть смысл — pt1, pt2, pt3 идут подряд.
            AudioClip[] steps = Resources.LoadAll<AudioClip>("Audio/Combat/Footstep");
            if (steps != null && steps.Length > 1)
                System.Array.Sort(steps, (a, b) => string.CompareOrdinal(a.name, b.name));
            _variants[(int)Sound.Footstep] = steps;
        }

        // Audio presentation must not touch UnityEngine.Random: keeping a private
        // generator prevents sound variation from perturbing any other system.
        private float Random01()
        {
            _random ^= _random << 13;
            _random ^= _random >> 17;
            _random ^= _random << 5;
            return (_random & 0xFFFFFFu) / 16777215f;
        }
    }
}
