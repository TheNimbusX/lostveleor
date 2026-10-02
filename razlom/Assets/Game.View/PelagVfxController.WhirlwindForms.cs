using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Формы Вихря (владелец 02.10) — вид поверх принятой «морской пены».
    /// Целевые кадры выбраны владельцем (ART/characters/pelag/whirlwind-forms-2026-10-02/chatgpt-results):
    ///  • Буря (storm-2) — водяной столб из витков-полос вокруг героя, ломающийся
    ///    гребень сверху, брызги с кромки; у земли стенки прозрачнее, чтобы враги
    ///    внутри читались. Живёт от WhirlwindStormStarted до WhirlwindStormEnded,
    ///    на каждый оборот — выброс брызг, в конце — всплеск;
    ///  • Водоворот (vortex-1) — шесть плоских рукавов-спиралей на земле на 4 м,
    ///    закручиваются внутрь, пока Sim тянет (WhirlwindMaelstromPull); струи пены
    ///    к центру; на каждом оглушённом в контакт — корона брызг;
    ///  • Пенные волны (waves-2) — два кольца пены; фронт кольца ведёт событие
    ///    WhirlwindFoamRing и числа Sim (PelagWhirlwindFormRules.FoamRingFront),
    ///    на каждом задетом враге — белая корона брызг (WhirlwindFoamRingHit).
    /// Префабы — Editor/PelagWhirlwindFoamVfxSetup.Forms.cs. Вид ничего не решает
    /// за Sim: всё рождается от событий, обычный Вихрь без формы не меняется.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Сколько витков столба Бури ещё доживает после конца удержания, с.</summary>
        private const float StormColumnLinger = .55f;
        /// <summary>Страховочный срок столба: удержание дольше 3 с не бывает.</summary>
        private const float StormColumnMaxSeconds = 4.5f;
        /// <summary>Брызги на оборот Бури: основа и прибавка за каждого врага в круге (до пяти).</summary>
        private const int StormPulseDrops = 10, StormPulseDropsPerEnemy = 2;
        /// <summary>Водоворот рисуется чуть шире радиуса тяги: тела на краю стоят в рукавах, а не за ними.</summary>
        private const float MaelstromVisualMargin = .2f;
        private const float CrownSplashScale = 1.1f, MaelstromCrownScale = .9f;
        private const int FoamWaveSlots = 4;
        /// <summary>Пока брызги кольца ещё летят после конца жизни кольца, с.</summary>
        private const float FoamWaveSprayTail = .45f;

        private int _stormFx = -1;
        private GameObject _stormObject;
        private ParticleSystem[] _stormLoops;
        private ParticleSystem _stormPulse;
        private bool _stormEnding;

        private int _maelstromContactTick = -1;
        private Vector3 _maelstromCentre;

        private struct FoamWaveRun
        {
            public bool Active;
            public int Fx;
            public GameObject Object;
            public int Ring, StartTick, Travel;
            public Vector3 Centre;
            public ParticleSystem Spray, Foam;
            public bool Stopped;
        }

        private readonly FoamWaveRun[] _foamWaves = new FoamWaveRun[FoamWaveSlots];
        private readonly Vector3[] _foamRingCentres = new Vector3[Simulation.FoamRingCount];
        /// <summary>Враг, которого только что задело кольцо: следующий его Damage — от кольца, не от клинка.</summary>
        private int _foamRingDamageTarget = -1;

        /// <summary>Событие форм Вихря. True — разобрано здесь (оглушение — нет: его видят и другие).</summary>
        private bool ConsumeWhirlwindFormEvent(in SimEvent e, int eventTick)
        {
            switch (e.Type)
            {
                case SimEventType.WhirlwindStormStarted: BeginStormColumn(); return true;
                case SimEventType.WhirlwindStormPulse: PulseStormColumn(e.Amount); return true;
                case SimEventType.WhirlwindStormEnded: EndStormColumn(true); return true;
                case SimEventType.WhirlwindMaelstromPull: PlayMaelstrom(e, eventTick); return true;
                case SimEventType.WhirlwindFoamRing: BeginFoamWave(e, eventTick); return true;
                case SimEventType.WhirlwindFoamRingHit: PlayFoamRingHit(e); return true;
                case SimEventType.Stun:
                    if (PelagWhirlwindFormRules.IsMaelstromStagger(eventTick, e.Amount, _maelstromContactTick))
                        PlayCrownSplash(e.Target, e.Position, _maelstromCentre, MaelstromCrownScale, true);
                    return false;
                default: return false;
            }
        }

        /// <summary>
        /// Удар кольца Пенных волн: Damage сразу за WhirlwindFoamRingHit по тому же
        /// врагу. Его брызги — корона кольца, а не всплеск клинка, стоп-кадра и света героя нет.
        /// </summary>
        private bool TakeFoamRingDamage(in SimEvent e)
        {
            if (_foamRingDamageTarget < 0 || e.Target != _foamRingDamageTarget || !IsWhirlwindSlot(e.ActionVariant)) return false;
            _foamRingDamageTarget = -1;
            return true;
        }

        /// <summary>Тик Sim, на котором родилось событие кадра (FrameEventContext ставит тик после шага).</summary>
        private int EventTick(int index)
        {
            var contexts = _driver.FrameEventContexts;
            if (contexts != null && index < contexts.Count) return contexts[index].SimulationTick - 1;
            return _driver.Sim != null ? _driver.Sim.Tick - 1 : 0;
        }

        private void UpdateWhirlwindForms()
        {
            Simulation sim = _driver.Sim;
            if (_stormFx >= 0)
            {
                if (!StillActive(_stormFx, _stormObject)) ForgetStormColumn();
                else
                {
                    _active[_stormFx].Object.transform.position = PlayerPosition();
                    // Страховка: Sim сбросили (смена комнаты, новый бой) без события конца.
                    if (!_stormEnding && (sim == null || !sim.WhirlwindStorming)) EndStormColumn(false);
                }
            }
            if (sim == null) return;
            float now = sim.Tick - 1 + _driver.Alpha;
            for (int i = 0; i < _foamWaves.Length; i++)
            {
                ref FoamWaveRun run = ref _foamWaves[i];
                if (!run.Active) continue;
                if (!StillActive(run.Fx, run.Object)) { run = default; continue; }
                float radius = PelagWhirlwindFormRules.FoamRingFront(run.Ring, run.StartTick, run.Travel, now);
                run.Object.transform.localScale = Vector3.one * radius;
                if (!run.Stopped && !PelagWhirlwindFormRules.FoamRingTravelling(run.StartTick, run.Travel, now))
                {
                    run.Stopped = true;
                    if (run.Spray != null) run.Spray.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    if (run.Foam != null) run.Foam.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        /// <summary>Смерть героя и сброс: все объекты форм уже отпущены вместе с остальными.</summary>
        private void StopWhirlwindForms()
        {
            ForgetStormColumn();
            for (int i = 0; i < _foamWaves.Length; i++) _foamWaves[i] = default;
            _foamRingDamageTarget = -1;
            _maelstromContactTick = -1;
        }

        private bool StillActive(int fx, GameObject go)
            => fx >= 0 && fx < _active.Length && _active[fx].Active && _active[fx].Object == go;

        // ---- Буря ----

        private void BeginStormColumn()
        {
            if (CaptureRig.NoVfx) return;
            // Новое удержание сменяет недогоревший столб прежнего.
            if (_stormFx >= 0 && StillActive(_stormFx, _stormObject)) Release(_stormFx);
            ForgetStormColumn();
            if (!TryAcquire(PelagVfxId.WhirlwindStormColumn, out GameObject go, out PelagVfxElement element)) return;
            int index = ReserveActive();
            Vector3 feet = PlayerPosition();
            float scale = element.AuthoredRadius > 0f ? WhirlwindRadius() / element.AuthoredRadius : 1f;
            // Корень как у кольца Вихря: X90 — местная +Z вниз, витки крутятся вокруг неё.
            element.Begin(feet, Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = Vector3.one * scale;
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WhirlwindStormColumn, Object = go, Element = element,
                Duration = StormColumnMaxSeconds, Start = feet, End = feet, Motion = Motion.Static, FollowIndex = -1
            };
            _stormFx = index;
            _stormObject = go;
            _stormEnding = false;
            Transform root = go.transform;
            Transform pulse = root.Find("PulseSpray");
            _stormPulse = pulse != null ? pulse.GetComponent<ParticleSystem>() : null;
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            int loops = 0;
            foreach (var ps in systems) if (ps.main.loop) loops++;
            _stormLoops = new ParticleSystem[loops];
            loops = 0;
            foreach (var ps in systems) if (ps.main.loop) _stormLoops[loops++] = ps;
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[whirlwind-storm] column scale={scale:F2} loops={_stormLoops.Length}");
        }

        private void PulseStormColumn(int enemies)
        {
            if (_stormFx < 0 || _stormEnding || !StillActive(_stormFx, _stormObject)) return;
            if (_stormPulse != null)
                _stormPulse.Emit(StormPulseDrops + StormPulseDropsPerEnemy * Mathf.Clamp(enemies, 0, 5));
            PulseCombatLight(.30f);
        }

        /// <summary>Конец Бури: витки больше не рождаются и доживают, по земле — всплеск.</summary>
        private void EndStormColumn(bool splash)
        {
            if (_stormFx < 0 || _stormEnding) return;
            if (!StillActive(_stormFx, _stormObject)) { ForgetStormColumn(); return; }
            _stormEnding = true;
            if (_stormLoops != null)
                foreach (var ps in _stormLoops) if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            ref ActiveFx fx = ref _active[_stormFx];
            fx.Duration = Mathf.Min(fx.Duration, fx.Age + StormColumnLinger);
            if (!splash || CaptureRig.NoVfx) return;
            Vector3 feet = PlayerPosition();
            float radius = WhirlwindRadius();
            if (!TryAcquire(PelagVfxId.WhirlwindStormSplash, out GameObject go, out PelagVfxElement element)) return;
            int index = ReserveActive();
            element.Begin(feet, Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = Vector3.one * (element.AuthoredRadius > 0f ? radius / element.AuthoredRadius : 1f);
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WhirlwindStormSplash, Object = go, Element = element,
                Duration = Mathf.Max(.05f, element.DefaultLifetime), Start = feet, End = feet,
                Motion = Motion.Static, FollowIndex = -1
            };
            PulseCombatLight(.45f);
        }

        private void ForgetStormColumn()
        {
            _stormFx = -1;
            _stormObject = null;
            _stormLoops = null;
            _stormPulse = null;
            _stormEnding = false;
        }

        // ---- Водоворот ----

        private void PlayMaelstrom(in SimEvent e, int eventTick)
        {
            float ground = PlayerPosition().y;
            _maelstromCentre = new Vector3(e.Position.X.ToFloat(), ground, e.Position.Y.ToFloat());
            _maelstromContactTick = PelagWhirlwindFormRules.MaelstromContactTick(eventTick, e.ActionVariant);
            if (CaptureRig.NoVfx || !TryAcquire(PelagVfxId.WhirlwindMaelstrom, out GameObject go, out PelagVfxElement element)) return;
            int index = ReserveActive();
            Vector3 at = _maelstromCentre + Vector3.up * .03f;
            element.Begin(at, Quaternion.Euler(90f, 0f, 0f));
            float radius = Simulation.MaelstromRadius.ToFloat() + MaelstromVisualMargin;
            go.transform.localScale = Vector3.one * (element.AuthoredRadius > 0f ? radius / element.AuthoredRadius : 1f);
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WhirlwindMaelstrom, Object = go, Element = element,
                Duration = Mathf.Max(.05f, element.DefaultLifetime), Start = at, End = at,
                Motion = Motion.Static, FollowIndex = -1
            };
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[whirlwind-maelstrom] pulled={e.Amount} ticks={e.ActionVariant} contact={_maelstromContactTick} radius={radius:F2}");
        }

        // ---- Пенные волны ----

        private void BeginFoamWave(in SimEvent e, int eventTick)
        {
            int ring = Mathf.Clamp(e.Amount, 0, Simulation.FoamRingCount - 1);
            var centre = new Vector3(e.Position.X.ToFloat(), PlayerPosition().y, e.Position.Y.ToFloat());
            _foamRingCentres[ring] = centre;
            if (CaptureRig.NoVfx || !TryAcquire(PelagVfxId.WhirlwindFoamWave, out GameObject go, out PelagVfxElement element)) return;
            int travel = e.ActionVariant > 0 ? e.ActionVariant : Simulation.FoamRingTravelTicks(ring);
            float life = PelagWhirlwindFormRules.FoamRingLifeSeconds(travel);
            Transform root = go.transform;
            // Жизнь кольца — от хода этого кольца: шейдер считает время в долях жизни.
            Transform ringLayer = root.Find("Ring");
            if (ringLayer != null && ringLayer.TryGetComponent(out ParticleSystem ringSystem))
            {
                ringSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ringSystem.main;
                main.startLifetime = life;
                main.duration = life + .05f;
            }
            int index = ReserveActive();
            Vector3 at = centre + Vector3.up * .04f;
            element.Begin(at, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
            float now = _driver.Sim != null ? _driver.Sim.Tick - 1 + _driver.Alpha : eventTick;
            root.localScale = Vector3.one * PelagWhirlwindFormRules.FoamRingFront(ring, eventTick, travel, now);
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WhirlwindFoamWave, Object = go, Element = element,
                Duration = life + FoamWaveSprayTail, Start = at, End = at, Motion = Motion.Static, FollowIndex = -1
            };
            int slot = 0;
            for (int i = 0; i < _foamWaves.Length; i++)
                if (!_foamWaves[i].Active || !StillActive(_foamWaves[i].Fx, _foamWaves[i].Object)) { slot = i; break; }
            Transform spray = root.Find("Spray"), foam = root.Find("Foam");
            _foamWaves[slot] = new FoamWaveRun
            {
                Active = true, Fx = index, Object = go, Ring = ring, StartTick = eventTick, Travel = travel, Centre = centre,
                Spray = spray != null ? spray.GetComponent<ParticleSystem>() : null,
                Foam = foam != null ? foam.GetComponent<ParticleSystem>() : null
            };
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[whirlwind-foam-wave] ring={ring} tick={eventTick} travel={travel} life={life:F2}");
        }

        private void PlayFoamRingHit(in SimEvent e)
        {
            _foamRingDamageTarget = e.Target;
            int ring = Mathf.Clamp(e.Amount, 0, Simulation.FoamRingCount - 1);
            PlayCrownSplash(e.Target, e.Position, _foamRingCentres[ring], CrownSplashScale, false);
        }

        /// <summary>
        /// Корона брызг у ног врага: белые языки и вырезанные капли, наклон — от
        /// центра наружу (кольцо толкает), у Водоворота — к центру (тянет).
        /// </summary>
        private void PlayCrownSplash(int target, FixVec2 fallback, Vector3 centre, float scale, bool inward)
        {
            if (CaptureRig.NoVfx) return;
            Vector3 body = EntityPosition(target, fallback);
            Vector3 at = new Vector3(body.x, PlayerPosition().y + .02f, body.z);
            Vector3 outward = FlatDirection(centre, at);
            Spawn(PelagVfxId.WhirlwindCrownSplash, at, Quaternion.LookRotation(inward ? -outward : outward, Vector3.up),
                .6f, scale, scale, Motion.Static);
        }

        /// <summary>Радиус Вихря из сборки (как у кольца контакта); без Вихря — базовые 2,3 м.</summary>
        private float WhirlwindRadius()
        {
            Simulation sim = _driver.Sim;
            if (sim != null)
                for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
                {
                    AbilityBuild ability = sim.GetAbility(slot);
                    if (ability != null && ability.DefinitionId == AbilityDefinition.WhirlwindId)
                        return ability.Get(AbilityStatType.Radius).ToFloat();
                }
            return 2.3f;
        }
    }
}
