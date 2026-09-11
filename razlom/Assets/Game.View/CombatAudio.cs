using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using Sound = Game.View.CombatSound;

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

        public CombatAudioProfile Profile;
        private struct DelayedCue { public float Due; public Sound Sound; }
        private readonly DelayedCue[] _deathCues = new DelayedCue[64];
        private int _deathCueCount;
        private float _anchorImpactAt = -1f, _anchorLandAt = -1f;
        private CombatVoiceBudget _voiceBudget;
        private float _whirlwindEndAt = -1f;
        private bool _chainSoundActive;
        private int _generationShown = -1;
        private readonly CombatSoundEntry[] _entries = new CombatSoundEntry[(int)Sound.Count];

        private TickDriver _driver;
        private AudioSource[] _voices;
        private Sound[] _voiceSounds;
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
            if (Profile == null) Profile = Resources.Load<CombatAudioProfile>("Combat/CombatAudio");
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
            UpdateCycloneSound();
            ConsumeEvents();
            UpdateCleaveSound();
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
            if (_whirlwindEndAt >= 0f && Time.time >= _whirlwindEndAt)
            {
                _whirlwindEndAt = -1f;
                Play(Sound.WhirlwindEnd, AbilityVolume * .5f, 1f, .01f);
            }
            bool chain = _driver.Sim != null && _driver.Sim.ChainTargetId >= 0;
            if (_chainSoundActive && !chain)
            {
                StopKind(Sound.ChainStep);
                Play(Sound.ChainStepEnd, AbilityVolume * .5f, 1f, .01f);
            }
            _chainSoundActive = chain;
        }

        private int _cleaveSoundCast = -1;
        private bool _cleaveSoundPlayed;
        private void UpdateCleaveSound()
        {
            var sim = _driver.Sim;
            if (sim == null || !sim.CleaveActive) { _cleaveSoundCast = -1; return; }
            if (_cleaveSoundCast != sim.CleaveStartTick)
            { _cleaveSoundCast = sim.CleaveStartTick; _cleaveSoundPlayed = false; }
            if (!_cleaveSoundPlayed && sim.Tick - 1 + _driver.Alpha >= sim.CleaveSwingStartTick)
            {
                _cleaveSoundPlayed = true;
                Play(Sound.WhooshHeavy, WhooshVolume, .92f, .02f);
            }
        }

        private bool _cycloneSoundActive;
        private int _cycloneSoundTurn = -1;
        private void UpdateCycloneSound()
        {
            var sim = _driver.Sim;
            bool active = sim != null && sim.CycloneActive;
            if (active)
            {
                int turn = (sim.CycloneTravel / Fix64.TwoPi).ToInt();
                if (!_cycloneSoundActive || turn != _cycloneSoundTurn)
                {
                    float charge = Mathf.Clamp01(sim.CycloneElapsedTicks / 60f);
                    Play(Sound.CycloneTurn, WhooshVolume * .65f, Mathf.Lerp(1.03f, .72f, charge), .015f);
                    Play(Sound.HitMetal, MetalVolume * .16f, .78f, .015f);
                    _cycloneSoundTurn = turn;
                }
            }
            else if (_cycloneSoundActive)
            {
                StopKind(Sound.AnchorSweep);
                StopKind(Sound.CycloneTurn);
                Play(Sound.CycloneRelease, AbilityVolume * .5f, 1f, .01f);
                _cycloneSoundTurn = -1;
            }
            _cycloneSoundActive = active;
        }

        private void PlayModeChange()
        {
            GameSession session = _driver.Session;
            if (session == null || (session.Mode == _modeShown && session.Generation == _generationShown)) return;
            _generationShown = session.Generation;
            _modeShown = session.Mode;

            // Смена режима обрывает бой на середине. Осыпание, поставленное в
            // очередь за долю секунды до выхода из Разлома, прозвучало бы уже
            // в лагере — над пустой поляной, без тела.
            _deathCueCount = 0;
            _whirlwindEndAt = -1f;
            _chainSoundActive = false;
            _whooshDelay = -1f;
            _stepAnchorSet = false;
            _cycloneSoundActive = false;
            for (int i = 0; i < _voices.Length; i++) _voices[i].Stop();
            _voiceBudget.Clear();
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
                        else Play(Sound.EnemyWarning, 0.6f, 1f, 0f);
                        break;

                    case SimEventType.Damage:
                        PlayDamage(in e);
                        break;

                    case SimEventType.Death:
                        if (e.Target == Simulation.PlayerId) _anchorImpactAt = _anchorLandAt = -1f;
                        if (e.Target != Simulation.PlayerId)
                        {
                            var kind = _driver.Sim.Entities.Kind[e.Target];
                            Play(kind == EnemyKind.ForestRootSwarm ? Sound.RootSwarmKill : Sound.Kill,
                                KillVolume, 0.96f, 0.025f);
                            QueueDeathSounds(kind);
                        }
                        break;

                    case SimEventType.AbilityCast:
                        if (e.Source == Simulation.PlayerId)
                        {
                            _anchorImpactAt = _anchorLandAt = -1f;
                            _whirlwindEndAt = -1f;
                            StopKind(Sound.Whirlwind);
                            _whooshDelay = -1f;
                            _cleaveSoundCast = -1;
                            if (_driver.Sim.GetAbility(e.Amount)?.DefinitionId == AbilityDefinition.CleaveId) break;
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
                                _whirlwindEndAt = Time.time + CharacterAnimatorView.WhirlwindClipDuration;
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

            Play(Sound.Footstep, FootstepVolume, 1f, 0.02f, fixedVariant: (_stepPart - 1) % parts.Length);
        }

        private void UpdateWhoosh()
        {
            if (_whooshDelay < 0f) return;
            var sim = _driver.Sim;
            if (sim == null || !sim.Entities.Alive[Simulation.PlayerId]
                || sim.Entities.AttackImpactTick[Simulation.PlayerId] <= sim.Tick)
            { _whooshDelay = -1f; return; }
            _whooshDelay -= Time.deltaTime;
            if (_whooshDelay > 0f) return;

            _whooshDelay = -1f;
            bool heavy = _whooshAttackVariant == 1;
            Play(heavy ? Sound.WhooshHeavy : Sound.Whoosh, WhooshVolume * (heavy ? 1.12f : 1f),
                heavy ? 0.92f : 1.03f, 0.045f);
        }

        private void PlayDamage(in SimEvent e)
        {
            // Player damage keeps its visual flash/recoil but intentionally has
            // no one-shot until a dedicated, approved hurt cue exists.
            if (e.Target == Simulation.PlayerId)
            { Play(Sound.PlayerHurt, 0.65f, 1f, 0.02f); return; }

            if (e.Source != Simulation.PlayerId) return;

            Sound bodySound = _driver.Sim.Entities.Kind[e.Target] == EnemyKind.ForestRootSwarm
                ? Sound.RootSwarmHit : Sound.HitBody;
            bool ability = e.DamageOrigin == DamageOrigin.Ability;
            if (ability && _driver.Sim.GetAbility(e.ActionVariant)?.DefinitionId == AbilityDefinition.CleaveId
                && e.DamageKind != DamageType.Physical) return;
            bool whirlwind = ability && IsWhirlwindSlot(e.ActionVariant);
            bool heavy = e.Flag || e.ActionVariant == 1 || ability;
            if (ability && _driver.Sim.GetAbility(e.ActionVariant)?.DefinitionId == AbilityDefinition.ChainStepId)
                Play(Sound.ChainStepHop, AbilityVolume * .65f, 1f, .025f);

            // Blade definition and body weight are separate layers. The AoE cap
            // turns a whole Whirlwind contact into one large, clean event.
            if (whirlwind)
            {
                // The spin is the hero layer. Contact only adds definition and
                // weight; full-strength metal+body masked the sweep and could
                // sum into a clipped wall together with a same-frame kill.
                Play(Sound.HitMetal, MetalVolume * 0.70f, 0.96f, 0.025f);
                Play(bodySound, BodyVolume * 0.65f, 0.88f, 0.025f);
            }
            else
            {
                Play(Sound.HitMetal, MetalVolume * (heavy ? 1.24f : 1f),
                    heavy ? 0.92f : 1.02f, 0.05f);
                Play(bodySound, BodyVolume * (heavy ? 1.22f : 1f),
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

            if (build.DefinitionId == AbilityDefinition.ChainCycloneId) return Sound.AnchorSweep;
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
            if (build.DefinitionId == AbilityDefinition.ChainCycloneId) return 0.74f;
            if (build.DefinitionId == AbilityDefinition.ChainStepId) return 1.02f;
            return 0.95f;
        }

        private void QueueDeathSounds(EnemyKind kind)
        {
            var timing = EnemyPresentationProfile.Death(kind);
            bool swarm = kind == EnemyKind.ForestRootSwarm;
            Queue(swarm ? Sound.RootSwarmFall : Sound.GuardianFall, timing.FallSeconds);
            Queue(swarm ? Sound.RootSwarmDissolve : Sound.Dissolve, timing.DissolveAt);
        }

        private void Queue(Sound sound, float delay)
        {
            if (_deathCueCount >= _deathCues.Length) return;
            _deathCues[_deathCueCount++] = new DelayedCue { Sound = sound, Due = Time.time + delay };
        }

        private void FlushDissolves()
        {
            int write = 0;
            for (int i = 0; i < _deathCueCount; i++)
            {
                var cue = _deathCues[i];
                if (cue.Due > Time.time) { _deathCues[write++] = cue; continue; }
                bool dissolve = cue.Sound == Sound.Dissolve || cue.Sound == Sound.RootSwarmDissolve;
                Play(cue.Sound, dissolve ? DissolveVolume : BodyVolume * 0.7f, 1f, 0f);
            }
            _deathCueCount = write;
        }

        private int Play(Sound sound, float volume, float pitchCenter, float pitchSpread,
            float delay = 0f, int fixedVariant = -1)
        {
            int index = (int)sound;
            AudioClip[] clips = _variants[index];
            if (clips == null || clips.Length == 0) return -1;
            var entry = _entries[index];
            if (_playedThisFrame[index] >= (entry != null ? entry.MaxPerFrame : MaxPerKindPerFrame)) return -1;
            int variant = fixedVariant >= 0 ? fixedVariant % clips.Length : PickVariant(index, clips.Length);
            AudioClip clip = clips[variant];
            if (clip == null) return -1;
            float spread = entry != null ? entry.PitchVariation : pitchSpread;
            float pitch = Mathf.Clamp(pitchCenter * (entry != null ? entry.Pitch : 1f)
                + (Random01() - 0.5f) * spread * 2f, 0.5f, 2f);
            int priority = entry != null ? entry.Priority : CombatAudioProfile.DefaultPriority(sound);
            int slot = _voiceBudget.Acquire(AudioSettings.dspTime, delay + clip.length / pitch, priority);
            if (slot < 0) return -1;
            _playedThisFrame[index]++;
            AudioSource voice = _voices[slot];
            voice.Stop();
            _voiceSounds[slot] = sound;
            voice.clip = clip;
            voice.volume = Mathf.Clamp01(volume * Master * GameUserSettings.EffectsVolume
                * (Profile != null ? Profile.Gain : 0.8f) * (entry != null ? entry.Gain : 1f));
            voice.pitch = pitch;
            voice.priority = 256 - Mathf.Clamp(priority * 2, 0, 256);
            if (delay > 0f) voice.PlayDelayed(delay);
            else voice.Play();
            if (CombatAudioCapture.Recording)
                Debug.Log($"[capture-cue] {sound} clip={clip.name} load={clip.loadState} volume={voice.volume} playing={voice.isPlaying} dsp={AudioSettings.dspTime}");
            return slot;
        }

        private void OnDisable()
        {
            _deathCueCount = 0;
            _whooshDelay = _whirlwindEndAt = _anchorImpactAt = _anchorLandAt = -1f;
            _chainSoundActive = _cycloneSoundActive = false;
            if (_voices != null) foreach (var voice in _voices) if (voice != null) voice.Stop();
            _voiceBudget?.Clear();
        }

        private void StopKind(Sound sound)
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                if (_voiceSounds[i] != sound) continue;
                _voices[i].Stop();
                _voiceBudget.Release(i);
            }
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
            _voiceBudget = new CombatVoiceBudget(_voices.Length);
            _voiceSounds = new Sound[_voices.Length];

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
            // До получения новых записей используем прежние банки как временную основу.
            _variants[(int)Sound.WhooshHeavy] = _variants[(int)Sound.Whoosh];
            _variants[(int)Sound.CycloneTurn] = _variants[(int)Sound.Whoosh];
            _variants[(int)Sound.RootSwarmHit] = _variants[(int)Sound.HitBody];
            _variants[(int)Sound.RootSwarmKill] = _variants[(int)Sound.Kill];
            _variants[(int)Sound.RootSwarmDissolve] = _variants[(int)Sound.Dissolve];
            for (int i = 0; i < _entries.Length; i++)
            {
                _entries[i] = Profile != null ? Profile.Find((Sound)i) : null;
                if (_entries[i]?.Clips != null && _entries[i].Clips.Length > 0)
                    _variants[i] = _entries[i].Clips;
            }
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
